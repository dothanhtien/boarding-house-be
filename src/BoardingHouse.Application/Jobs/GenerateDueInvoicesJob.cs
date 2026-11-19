using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.Application.Jobs;

/// <summary>
/// Creates this month's invoice of every lease whose billing day has come (on or before today, UTC) in organizations
/// with <c>AutoGenerateInvoices</c>, through <c>IInvoiceGenerator</c>, and records one generation run per organization
/// that had due leases. Scheduled daily by <c>RecurringJobRegistry</c> (Infrastructure); safe to re-run — invoiced
/// leases are no longer due, each lease has its own transaction and one organization failing doesn't stop the others.
/// </summary>
public class GenerateDueInvoicesJob(
    IOrganizationSettingsRepository organizationSettingsRepository,
    ILeaseRepository leaseRepository,
    IInvoiceGenerator invoiceGenerator,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<GenerateDueInvoicesJob> logger)
{
    public Task RunAsync(CancellationToken cancellationToken = default) =>
        RunAsync(DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);

    // today is a parameter so integration tests can pick the day of the month
    public async Task RunAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        currentUserAccessor.IsSystemActor = true;

        var month = BillingMonth.Of(today);
        var organizationIds = await organizationSettingsRepository.ListOrganizationIdsWithAutoGenerateInvoicesAsync(cancellationToken);

        var created = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var organizationId in organizationIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var leases = await leaseRepository.ListDueForBillingAsync(organizationId, today, cancellationToken);
                if (leases.Count == 0) continue;

                var run = await invoiceGenerator.GenerateAllAsync(
                    organizationId, propertyId: null, month, today, leases, triggeredBy: null, cancellationToken);

                created += run.CreatedCount;
                skipped += run.SkippedCount;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Generate due invoices failed ({OrganizationId})", organizationId);
            }
            finally
            {
                unitOfWork.DiscardChanges();
            }
        }

        logger.LogInformation(
            "Generate due invoices finished: {Organizations} organizations, {Created} created, {Skipped} skipped, {Failed} organizations failed",
            organizationIds.Count, created, skipped, failed);
    }
}
