using BoardingHouse.IntegrationTests.Fixtures;
using BoardingHouse.Infrastructure.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.IntegrationTests.Repositories;

public class PropertyRepositoryIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetByLeaseIdAsync_ReturnsThePropertyOfTheLeasesRoomUntracked()
    {
        var (leaseId, propertyId) = await SeedAsync();

        await using var context = fixture.CreateContext();
        var property = await new PropertyRepository(context).GetByLeaseIdAsync(leaseId);

        Assert.NotNull(property);
        Assert.Equal(propertyId, property.Id);
        Assert.Equal(new LateFeePolicy(LateFeeType.Percent, 5, graceDays: 3), property.LateFee);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetByLeaseIdAsync_UnknownLease_ReturnsNull()
    {
        await SeedAsync();

        await using var context = fixture.CreateContext();

        Assert.Null(await new PropertyRepository(context).GetByLeaseIdAsync(Guid.NewGuid()));
    }

    // A draft lease in room 101 of a property with a 5% late fee, plus a second property that must not be returned
    private async Task<(Guid LeaseId, Guid PropertyId)> SeedAsync()
    {
        await using var context = fixture.CreateContext();

        // lease_status_histories.changed_by references users, so the actor must be a real user
        var staff = TestEntities.New<User>(new { FullName = "Staff", PasswordHash = "hash", CreatedBy = SentinelActors.System });
        context.Users.Add(staff);
        var actor = staff.Id;

        var organization = TestEntities.New<Organization>(new { Name = "Org", CreatedBy = actor });
        var property = TestEntities.New<Property>(new
        {
            OrganizationId = organization.Id,
            Name = "Property",
            LateFeeType = (LateFeeType?)LateFeeType.Percent,
            LateFeeValue = (decimal?)5,
            LateFeeGraceDays = (int?)3,
            CreatedBy = actor
        });
        var other = TestEntities.New<Property>(new { OrganizationId = organization.Id, Name = "Other", CreatedBy = actor });
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101", capacity: 2, createdBy: actor);
        var otherRoom = TestRooms.New(propertyId: other.Id, roomNumber: "101", capacity: 2, createdBy: actor);
        var representative = TestEntities.New<User>(new { FullName = "Representative", PasswordHash = "hash", CreatedBy = actor });
        context.AddRange(organization, property, other, room, otherRoom, representative);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lease = Lease.CreateDraft(
            organization.Id, room, previousLeaseId: null, "HD-001", today, today.AddYears(1), billingDay: null,
            monthlyRent: 1_000_000, depositAmount: 1_000_000,
            [new TenantChange { UserId = representative.Id, IsRepresentative = true }],
            new Dictionary<Guid, User> { [representative.Id] = representative }, [], DateTimeOffset.UtcNow, actor);
        context.Add(lease);
        await context.SaveChangesAsync();

        return (lease.Id, property.Id);
    }
}
