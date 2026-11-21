namespace BoardingHouse.Application.DTOs.VietQr;

public record VietQrBankResponse
{
    public required string Bin { get; init; }
    public required string Code { get; init; }
    public required string ShortName { get; init; }
}
