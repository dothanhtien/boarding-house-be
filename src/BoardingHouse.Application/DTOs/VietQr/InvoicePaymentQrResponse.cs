namespace BoardingHouse.Application.DTOs.VietQr;

public record InvoicePaymentQrResponse
{
    public required Guid InvoiceId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string Payload { get; init; }
    public required string BankBin { get; init; }
    public required string BankCode { get; init; }
    public required string BankShortName { get; init; }
    public required string AccountNumber { get; init; }
    public string? AccountName { get; init; }
    public required decimal Amount { get; init; }
    public required string TransferContent { get; init; }
}
