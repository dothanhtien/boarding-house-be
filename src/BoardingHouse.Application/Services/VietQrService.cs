using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.VietQr;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Queries;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.VietQr;

namespace BoardingHouse.Application.Services;

public class VietQrService(
    IInvoiceRepository invoiceRepository,
    IOrganizationSettingsRepository organizationSettingsRepository,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ITenantPortalQueries tenantPortalQueries,
    ICurrentUserAccessor currentUserAccessor) : IVietQrService
{
    public List<VietQrBankResponse> GetBanks() =>
        VietQrBanks.All
            .Select(b => new VietQrBankResponse { Bin = b.Bin, Code = b.Code, ShortName = b.ShortName })
            .ToList();

    public async Task<InvoicePaymentQrResponse> GetInvoicePaymentQrAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await invoiceRepository.GetByIdAsync(invoiceId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Invoice(invoiceId));

        await organizationScopeAccessor.EnsureScopeAsync(
            invoice.OrganizationId,
            PermissionResources.Invoice,
            PermissionActions.Read,
            NotFoundMessages.Invoice(invoiceId),
            cancellationToken);

        return await BuildResponseAsync(invoice, cancellationToken);
    }

    public async Task<InvoicePaymentQrResponse> GetMyInvoicePaymentQrAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        if (!await tenantPortalQueries.IsInvoiceVisibleAsync(currentUserAccessor.RequiredUser.Id, invoiceId, cancellationToken))
        {
            throw new AppNotFoundException(NotFoundMessages.Invoice(invoiceId));
        }

        var invoice = await invoiceRepository.GetByIdAsync(invoiceId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Invoice(invoiceId));

        return await BuildResponseAsync(invoice, cancellationToken);
    }

    private async Task<InvoicePaymentQrResponse> BuildResponseAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var amount = invoice.GetAmountToRequest();

        var settings = await organizationSettingsRepository.GetByOrganizationIdAsync(invoice.OrganizationId, cancellationToken)
            ?? throw new AppConflictException(OrganizationSettings.BankAccountNotConfiguredMessage);
        var account = settings.GetVietQrAccount();
        var transferContent = VietQrPayload.TransferContentFor(invoice.InvoiceNumber);

        return new InvoicePaymentQrResponse
        {
            InvoiceId = invoice.Id,
            InvoiceNumber = invoice.InvoiceNumber,
            Payload = VietQrPayload.Build(account.Bank.Bin, account.AccountNumber, amount, transferContent),
            BankBin = account.Bank.Bin,
            BankCode = account.Bank.Code,
            BankShortName = account.Bank.ShortName,
            AccountNumber = account.AccountNumber,
            AccountName = account.AccountName,
            Amount = amount,
            TransferContent = transferContent
        };
    }
}
