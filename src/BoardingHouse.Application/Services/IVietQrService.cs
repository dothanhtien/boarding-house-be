using BoardingHouse.Application.DTOs.VietQr;

namespace BoardingHouse.Application.Services;

public interface IVietQrService
{
    List<VietQrBankResponse> GetBanks();
    Task<InvoicePaymentQrResponse> GetInvoicePaymentQrAsync(Guid invoiceId, CancellationToken cancellationToken = default);
    Task<InvoicePaymentQrResponse> GetMyInvoicePaymentQrAsync(Guid invoiceId, CancellationToken cancellationToken = default);
}
