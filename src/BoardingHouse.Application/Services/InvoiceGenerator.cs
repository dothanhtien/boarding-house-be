using BoardingHouse.Application.Common;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Services;

public class InvoiceGenerator(
    ILeaseRepository leaseRepository,
    IRoomRepository roomRepository,
    IInvoiceRepository invoiceRepository,
    IInvoiceGenerationRunRepository invoiceGenerationRunRepository,
    InvoiceBillingInputLoader billingInputLoader,
    IUnitOfWork unitOfWork,
    ILogger<InvoiceGenerator> logger) : IInvoiceGenerator
{
    public const string UnexpectedErrorMessage = "Unexpected error, see the server logs";

    public async Task<InvoiceCalculation> CalculateAsync(
        Lease lease, InvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var input = await billingInputLoader.LoadAsync(
            lease, draft.BillingMonth, draft.Today, draft.CustomLines, draft.DiscountAmount, cancellationToken);

        return InvoiceCalculator.Calculate(input);
    }

    public async Task<InvoiceGenerationResult> GenerateAsync(
        InvoiceDraft draft, Guid? createdBy, CancellationToken cancellationToken = default)
    {
        InvoiceGenerationResult result;

        try
        {
            result = await unitOfWork.ExecuteInTransactionAsync<InvoiceGenerationResult>(async ct =>
            {
                var roomId = await leaseRepository.GetRoomIdAsync(draft.LeaseId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(draft.LeaseId));

                _ = await roomRepository.GetByIdWithOrganizationIdForShareAsync(roomId, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(draft.LeaseId));

                var lease = await leaseRepository.GetByIdForBillingAsync(draft.LeaseId, forUpdate: true, ct)
                    ?? throw new AppNotFoundException(NotFoundMessages.Lease(draft.LeaseId));

                if (lease.RoomId != roomId)
                {
                    throw new AppConflictException("The lease was modified concurrently, please retry");
                }

                var calculation = await CalculateAsync(lease, draft, ct);

                if (!calculation.CanCreate)
                {
                    return new InvoiceGenerationResult.Skipped(calculation.Errors);
                }

                var number = await invoiceRepository.NextNumberAsync(lease.OrganizationId, draft.BillingMonth, ct);

                var invoice = Invoice.Create(
                    calculation,
                    lease.OrganizationId,
                    lease.Id,
                    Invoice.FormatNumber(draft.BillingMonth, number),
                    draft.DiscountReason,
                    draft.Notes,
                    DateTimeOffset.UtcNow,
                    createdBy);

                await invoiceRepository.AddAsync(invoice, ct);

                return new InvoiceGenerationResult.Created(invoice.Id);
            }, cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.InvoiceLeaseIdBillingMonth)
        {
            unitOfWork.DiscardChanges();
            logger.LogWarning(
                "Create invoice failed: lease {LeaseId} already has an invoice for {BillingMonth}",
                draft.LeaseId, draft.BillingMonth);

            return new InvoiceGenerationResult.Skipped(
                [new InvoiceIssue(InvoiceIssueCode.AlreadyInvoiced, $"The lease already has an invoice for {draft.BillingMonth}")]);
        }

        if (result is InvoiceGenerationResult.Created created)
        {
            logger.LogInformation(
                "Invoice created ({InvoiceId}, lease {LeaseId}, month {BillingMonth})",
                created.InvoiceId, draft.LeaseId, draft.BillingMonth);
        }

        return result;
    }

    public async Task<InvoiceGenerationRun> GenerateAllAsync(
        Guid organizationId,
        Guid? propertyId,
        BillingMonth billingMonth,
        DateOnly today,
        IReadOnlyList<Lease> leases,
        Guid? triggeredBy,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var createdCount = 0;
        var skipped = new List<InvoiceGenerationSkip>();

        foreach (var lease in leases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = await GenerateAsync(
                    InvoiceDraft.Monthly(lease.Id, billingMonth, today), triggeredBy, cancellationToken);

                if (result is InvoiceGenerationResult.Created)
                {
                    createdCount++;
                }
                else if (result is InvoiceGenerationResult.Skipped { AlreadyInvoiced: false } skip)
                {
                    skipped.Add(ToSkip(lease, skip.Errors
                        .Select(e => new InvoiceGenerationSkipReason(InvoiceIssueKeys.Of(e.Code), e.Message))));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Generate invoice failed (lease {LeaseId}, month {BillingMonth})", lease.Id, billingMonth);

                var message = ex is AppException or DomainException ? ex.Message : UnexpectedErrorMessage;
                skipped.Add(ToSkip(lease, [new InvoiceGenerationSkipReason(InvoiceIssueKeys.Failed, message)]));
            }
            finally
            {
                unitOfWork.DiscardChanges();
            }
        }

        var run = InvoiceGenerationRun.Record(
            organizationId, propertyId, billingMonth, triggeredBy, startedAt, DateTimeOffset.UtcNow, createdCount, skipped);

        await invoiceGenerationRunRepository.AddAsync(run, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Invoice generation run recorded ({RunId}, organization {OrganizationId}, property {PropertyId}, month {BillingMonth}): {Created} created, {Skipped} skipped",
            run.Id, organizationId, propertyId, billingMonth, createdCount, skipped.Count);

        return run;
    }

    private static InvoiceGenerationSkip ToSkip(Lease lease, IEnumerable<InvoiceGenerationSkipReason> reasons) =>
        new(lease.Id, lease.LeaseCode, lease.Room!.RoomNumber, reasons.ToList());
}
