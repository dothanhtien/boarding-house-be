using BoardingHouse.Application.Services.Security;
using Microsoft.AspNetCore.DataProtection;

namespace BoardingHouse.Infrastructure.Services.Security;

public class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    public const string Purpose = "PaymentGatewaySecrets";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
