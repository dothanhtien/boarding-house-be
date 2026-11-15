using Moq;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.UnitTests.Common;

namespace BoardingHouse.UnitTests.Services;

public class InvoiceLateFeeResolverTests
{
    private readonly Mock<IPropertyRepository> _propertyRepository = new();
    private readonly Mock<IOrganizationSettingsRepository> _organizationSettingsRepository = new();
    private readonly InvoiceLateFeeResolver _resolver;
    private readonly Invoice _invoice = TestEntities.New<Invoice>(new { OrganizationId = Guid.NewGuid(), LeaseId = Guid.NewGuid() });

    public InvoiceLateFeeResolverTests()
    {
        _resolver = new InvoiceLateFeeResolver(_propertyRepository.Object, _organizationSettingsRepository.Object);
    }

    private static Property PropertyWith(LateFeePolicy lateFee) =>
        Property.Create(
            Guid.NewGuid(),
            new PropertyDetails("Property", null, null, null, null, null, IsActive: true, DefaultBillingDay: null, lateFee),
            SentinelActors.System);

    private static OrganizationSettings SettingsWith(LateFeePolicy lateFee) =>
        TestEntities.Set(
            OrganizationSettings.DefaultsFor(Guid.NewGuid()),
            new { LateFeeType = lateFee.Type, LateFeeValue = lateFee.Value, LateFeeGraceDays = lateFee.GraceDays });

    private void Setup(Property? property, OrganizationSettings? settings)
    {
        _propertyRepository
            .Setup(r => r.GetByLeaseIdAsync(_invoice.LeaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);
        _organizationSettingsRepository
            .Setup(r => r.GetByOrganizationIdAsync(_invoice.OrganizationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);
    }

    [Fact]
    public async Task ResolveAsync_PropertyHasALateFee_ReturnsTheProperty()
    {
        var propertyFee = new LateFeePolicy(LateFeeType.Fixed, 100_000, graceDays: 2);
        Setup(PropertyWith(propertyFee), SettingsWith(new LateFeePolicy(LateFeeType.Percent, 5, graceDays: null)));

        Assert.Equal(propertyFee, await _resolver.ResolveAsync(_invoice, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_PropertyWithoutALateFee_ReturnsTheOrganization()
    {
        var organizationFee = new LateFeePolicy(LateFeeType.Percent, 5, graceDays: null);
        Setup(PropertyWith(LateFeePolicy.None), SettingsWith(organizationFee));

        Assert.Equal(organizationFee, await _resolver.ResolveAsync(_invoice, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_NothingFound_ReturnsNone()
    {
        Setup(property: null, settings: null);

        Assert.Equal(LateFeePolicy.None, await _resolver.ResolveAsync(_invoice, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_PassesTheCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        Setup(property: null, settings: null);

        await _resolver.ResolveAsync(_invoice, cts.Token);

        _propertyRepository.Verify(r => r.GetByLeaseIdAsync(_invoice.LeaseId, cts.Token), Times.Once);
        _organizationSettingsRepository.Verify(r => r.GetByOrganizationIdAsync(_invoice.OrganizationId, cts.Token), Times.Once);
    }
}
