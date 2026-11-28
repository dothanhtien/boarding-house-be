using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using BoardingHouse.Infrastructure.Services.Security;

namespace BoardingHouse.UnitTests.Services.Security;

public class DataProtectionSecretProtectorTests
{
    private const string Secret = "TESTSECRET0123456789";

    [Fact]
    public void Protect_ThenUnprotect_ReturnsTheSecret()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        var cipher = protector.Protect(Secret);

        Assert.NotEqual(Secret, cipher);
        Assert.DoesNotContain("TESTSECRET", cipher);
        Assert.Equal(Secret, protector.Unprotect(cipher));
    }

    [Fact]
    public void Protect_SameSecretTwice_GivesDifferentCiphers()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        Assert.NotEqual(protector.Protect(Secret), protector.Protect(Secret));
    }

    [Fact]
    public void Unprotect_CipherOfAnotherPurpose_Throws()
    {
        var provider = new EphemeralDataProtectionProvider();
        var cipher = provider.CreateProtector("SomethingElse").Protect(Secret);

        Assert.ThrowsAny<CryptographicException>(() => new DataProtectionSecretProtector(provider).Unprotect(cipher));
    }

    [Fact]
    public void Unprotect_CipherFromAnotherKeyRing_Throws()
    {
        var cipher = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider()).Protect(Secret);

        Assert.ThrowsAny<CryptographicException>(
            () => new DataProtectionSecretProtector(new EphemeralDataProtectionProvider()).Unprotect(cipher));
    }
}
