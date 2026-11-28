using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class OrganizationPaymentGatewayTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 3, 0, 0, TimeSpan.Zero);

    private static OrganizationPaymentGateway NewAccount() =>
        OrganizationPaymentGateway.Create(OrganizationId, PaymentGateway.VnPay, " TESTCODE ", "cipher-1", isActive: true, ActorId, Now);

    [Fact]
    public void Create_SetsTheValuesAndTrimsTheMerchantCode()
    {
        var account = NewAccount();

        Assert.Equal(OrganizationId, account.OrganizationId);
        Assert.Equal(PaymentGateway.VnPay, account.Gateway);
        Assert.Equal("TESTCODE", account.MerchantCode);
        Assert.Equal("cipher-1", account.SecretEncrypted);
        Assert.True(account.IsActive);
        Assert.Equal(ActorId, account.CreatedBy);
    }

    [Fact]
    public void Create_Inactive_IsSavedInactive()
    {
        var account = OrganizationPaymentGateway.Create(
            OrganizationId, PaymentGateway.VnPay, "TESTCODE", "cipher-1", isActive: false, ActorId, Now);

        Assert.False(account.IsActive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_WithoutSecret_Throws(string? secretEncrypted)
    {
        var ex = Assert.Throws<DomainException>(() => OrganizationPaymentGateway.Create(
            OrganizationId, PaymentGateway.VnPay, "TESTCODE", secretEncrypted, isActive: true, ActorId, Now));

        Assert.Equal(OrganizationPaymentGateway.SecretRequiredMessage, ex.Message);
        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutMerchantCode_Throws(string? merchantCode)
    {
        var ex = Assert.Throws<DomainException>(() => OrganizationPaymentGateway.Create(
            OrganizationId, PaymentGateway.VnPay, merchantCode!, "cipher-1", isActive: true, ActorId, Now));

        Assert.Equal("MerchantCode is required", ex.Message);
        Assert.Equal(DomainErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public void Create_TooLongMerchantCode_Throws()
    {
        var ex = Assert.Throws<DomainException>(() => OrganizationPaymentGateway.Create(
            OrganizationId, PaymentGateway.VnPay, new string('A', OrganizationPaymentGateway.MerchantCodeMaxLength + 1),
            "cipher-1", isActive: true, ActorId, Now));

        Assert.Equal("MerchantCode must not exceed 50 characters", ex.Message);
    }

    [Fact]
    public void Create_MerchantCodeAtMaxLengthAfterTrim_IsAccepted()
    {
        var code = new string('A', OrganizationPaymentGateway.MerchantCodeMaxLength);

        var account = OrganizationPaymentGateway.Create(
            OrganizationId, PaymentGateway.VnPay, $"  {code}  ", "cipher-1", isActive: true, ActorId, Now);

        Assert.Equal(code, account.MerchantCode);
    }

    [Fact]
    public void Update_WithoutSecret_KeepsTheSavedOne()
    {
        var account = NewAccount();

        account.Update("OTHERCODE", secretEncrypted: null, isActive: false, ActorId, Now);

        Assert.Equal("OTHERCODE", account.MerchantCode);
        Assert.Equal("cipher-1", account.SecretEncrypted);
        Assert.False(account.IsActive);
    }

    [Fact]
    public void Update_WithEmptySecret_KeepsTheSavedOne()
    {
        var account = NewAccount();

        account.Update("TESTCODE", secretEncrypted: "", isActive: true, ActorId, Now);

        Assert.Equal("cipher-1", account.SecretEncrypted);
    }

    [Fact]
    public void Update_WithSecret_ReplacesIt()
    {
        var account = NewAccount();

        account.Update("TESTCODE", "cipher-2", isActive: true, ActorId, Now);

        Assert.Equal("cipher-2", account.SecretEncrypted);
    }

    [Fact]
    public void Update_InvalidValue_ChangesNothing()
    {
        var account = NewAccount();

        Assert.Throws<DomainException>(() => account.Update("", "cipher-2", isActive: false, ActorId, Now));

        Assert.Equal(("TESTCODE", "cipher-1", true), (account.MerchantCode, account.SecretEncrypted, account.IsActive));
    }

    [Fact]
    public void Create_StartsAtTheFirstVersionWithoutHistory()
    {
        var account = NewAccount();

        Assert.Equal(OrganizationPaymentGateway.FirstCredentialsVersion, account.CredentialsVersion);
        Assert.Empty(account.CredentialHistories);
    }

    [Fact]
    public void Update_WithSecret_RetiresTheCurrentCredentialsAndBumpsTheVersion()
    {
        var account = NewAccount();
        var changedAt = Now.AddMinutes(10);

        account.Update("OTHERCODE", "cipher-2", isActive: true, ActorId, changedAt);

        Assert.Equal(2, account.CredentialsVersion);
        var retired = Assert.Single(account.CredentialHistories);
        Assert.Equal((account.Id, 1, "TESTCODE", "cipher-1"),
            (retired.OrganizationPaymentGatewayId, retired.Version, retired.MerchantCode, retired.SecretEncrypted));
        Assert.Equal((ActorId, changedAt), (retired.ReplacedBy, retired.ReplacedAt));
    }

    [Fact]
    public void Update_OnlyMerchantCode_RetiresTheCurrentCredentials()
    {
        var account = NewAccount();

        account.Update("OTHERCODE", secretEncrypted: null, isActive: true, ActorId, Now.AddMinutes(10));

        var retired = Assert.Single(account.CredentialHistories);
        Assert.Equal(("TESTCODE", "cipher-1"), (retired.MerchantCode, retired.SecretEncrypted));
        Assert.Equal(("OTHERCODE", "cipher-1", 2), (account.MerchantCode, account.SecretEncrypted, account.CredentialsVersion));
    }

    [Fact]
    public void Update_SameCredentials_KeepsTheVersion()
    {
        var account = NewAccount();

        account.Update("TESTCODE", secretEncrypted: null, isActive: false, ActorId, Now.AddMinutes(10));

        Assert.Equal(OrganizationPaymentGateway.FirstCredentialsVersion, account.CredentialsVersion);
        Assert.Empty(account.CredentialHistories);
    }

    [Fact]
    public void Update_SeveralChanges_KeepsEveryRetiredVersion()
    {
        var account = NewAccount();

        account.Update("TESTCODE", "cipher-2", isActive: true, ActorId, Now.AddMinutes(1));
        account.Update("OTHERCODE", "cipher-3", isActive: true, ActorId, Now.AddMinutes(2));

        Assert.Equal(3, account.CredentialsVersion);
        Assert.Equal(
            [(1, "TESTCODE", "cipher-1"), (2, "TESTCODE", "cipher-2")],
            account.CredentialHistories.Select(h => (h.Version, h.MerchantCode, h.SecretEncrypted)));
    }

    [Fact]
    public void Update_InvalidValue_RetiresNothing()
    {
        var account = NewAccount();

        Assert.Throws<DomainException>(() => account.Update("", "cipher-2", isActive: true, ActorId, Now));

        Assert.Equal(OrganizationPaymentGateway.FirstCredentialsVersion, account.CredentialsVersion);
        Assert.Empty(account.CredentialHistories);
    }

    [Fact]
    public void EnsureActive_Active_DoesNotThrow()
    {
        var account = NewAccount();

        var ex = Record.Exception(account.EnsureActive);

        Assert.Null(ex);
    }

    [Fact]
    public void EnsureActive_Inactive_ThrowsConflict()
    {
        var account = NewAccount();
        account.Update("TESTCODE", secretEncrypted: null, isActive: false, ActorId, Now);

        var ex = Assert.Throws<DomainException>(account.EnsureActive);

        Assert.Equal(DomainErrorKind.Conflict, ex.Kind);
        Assert.Equal(OrganizationPaymentGateway.NotAvailableMessage, ex.Message);
    }
}
