namespace BoardingHouse.Domain.VietQr;

public sealed record VietQrAccount(VietQrBank Bank, string AccountNumber, string? AccountName);
