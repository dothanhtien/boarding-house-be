using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Entities;

public class TenantProfileTests
{
    private static readonly TenantProfileDetails WithDocument = TenantProfileDetails.Empty with
    {
        IdentityType = IdentityType.CitizenIdentityCard,
        IdentityNumber = "001095012345",
        IdentityIssuedDate = new DateOnly(2020, 1, 1),
        IdentityExpiryDate = new DateOnly(2035, 1, 1)
    };

    [Fact]
    public void Create_NormalizesText()
    {
        var profile = TenantProfile.Create(
            Guid.NewGuid(),
            WithDocument with { IdentityNumber = " ab123 ", IdentityIssuedPlace = "  ", PermanentAddress = " Hanoi " },
            Guid.NewGuid());

        Assert.Equal(("AB123", (string?)null, "Hanoi"),
            (profile.IdentityNumber, profile.IdentityIssuedPlace, profile.PermanentAddress));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Create_NoFieldSet_Throws(string? blank)
    {
        var ex = Assert.Throws<DomainException>(() =>
            TenantProfile.Create(Guid.NewGuid(), TenantProfileDetails.Empty with { PermanentAddress = blank }, Guid.NewGuid()));

        Assert.Equal("Tenant profile must contain at least one field", ex.Message);
    }

    [Fact]
    public void Update_DocumentDetailsWithoutNumber_Throws()
    {
        var profile = TenantProfile.Create(Guid.NewGuid(), WithDocument, Guid.NewGuid());

        var ex = Assert.Throws<DomainException>(() => profile.Update(WithDocument with { IdentityNumber = null }));

        Assert.Equal("Identity issued date, issued place and expiry date require an identity document", ex.Message);
        Assert.Equal("001095012345", profile.IdentityNumber);
    }

    [Fact]
    public void Update_ExpiryNotAfterIssued_Throws()
    {
        var profile = TenantProfile.Create(Guid.NewGuid(), WithDocument, Guid.NewGuid());

        var ex = Assert.Throws<DomainException>(() =>
            profile.Update(WithDocument with { IdentityExpiryDate = WithDocument.IdentityIssuedDate }));

        Assert.Equal("Identity expiry date must be after the identity issued date", ex.Message);
    }

    [Fact]
    public void Update_ReplacesEveryField_ClearingThoseSetToNull()
    {
        var profile = TenantProfile.Create(Guid.NewGuid(), WithDocument with { PermanentAddress = "Hanoi" }, Guid.NewGuid());

        profile.Update(TenantProfileDetails.Empty with { EmergencyContactName = "Mom" });

        Assert.Equal(TenantProfileDetails.Empty with { EmergencyContactName = "Mom" }, profile.Details);
    }

    [Fact]
    public void Details_RoundTripsEveryField()
    {
        var details = new TenantProfileDetails(
            IdentityType.Passport, "P123", new DateOnly(2020, 1, 1), "Hanoi", new DateOnly(2030, 1, 1),
            new DateOnly(1995, 5, 5), Gender.Female, "Address", "Contact", "0900000000");

        Assert.Equal(details, TenantProfile.Create(Guid.NewGuid(), details, Guid.NewGuid()).Details);
    }
}
