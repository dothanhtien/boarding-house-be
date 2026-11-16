using Microsoft.EntityFrameworkCore;
using BoardingHouse.IntegrationTests.Fixtures;
using BoardingHouse.Application.Services;
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

    [Fact]
    public async Task LateFeeResolver_TerminatedLeaseInASoftDeletedRoom_UsesThePropertyLateFee()
    {
        var (leaseId, organizationId) = await SeedTerminatedLeaseInDeletedRoomAsync();

        await using var context = fixture.CreateContext();
        var resolver = new InvoiceLateFeeResolver(new PropertyRepository(context), new OrganizationSettingsRepository(context));
        var invoice = TestEntities.New<Invoice>(new { LeaseId = leaseId, OrganizationId = organizationId });

        var lateFee = await resolver.ResolveAsync(invoice, CancellationToken.None);

        Assert.Equal(new LateFeePolicy(LateFeeType.Percent, 5, graceDays: 3), lateFee);
    }

    // The SeedAsync lease, activated then terminated, its room soft-deleted, and an organization late fee of 50,000
    private async Task<(Guid LeaseId, Guid OrganizationId)> SeedTerminatedLeaseInDeletedRoomAsync()
    {
        var (leaseId, _) = await SeedAsync();

        await using var context = fixture.CreateContext();
        var lease = await context.Leases
            .Include(l => l.Room)
            .Include(l => l.Tenants).ThenInclude(t => t.User)
            .SingleAsync(l => l.Id == leaseId);
        var actor = lease.CreatedBy;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        lease.Activate(DateTimeOffset.UtcNow, today, actor);
        lease.Terminate(DateTimeOffset.UtcNow, today, actor, leftDate: today, "Moving out");
        TestEntities.Set(lease.Room!, new { DeletedAt = (DateTimeOffset?)DateTimeOffset.UtcNow, DeletedBy = (Guid?)actor });

        var settings = OrganizationSettings.DefaultsFor(lease.OrganizationId);
        TestEntities.Set(settings, new { LateFeeType = (LateFeeType?)LateFeeType.Fixed, LateFeeValue = (decimal?)50_000 });
        context.OrganizationSettings.Add(settings);
        await context.SaveChangesAsync();

        return (leaseId, lease.OrganizationId);
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
        var representative = TestEntities.New<User>(new { FullName = "Representative", PasswordHash = "hash", IsActive = true, CreatedBy = actor });
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
