using Microsoft.EntityFrameworkCore;
using BoardingHouse.IntegrationTests.Fixtures;
using BoardingHouse.Infrastructure.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.IntegrationTests.Repositories;

public class LeaseRepositoryIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetAggregateForUpdateAsync_LoadsRoomCurrentTenantsWithUsersAndUtilityServices()
    {
        var leaseId = await SeedAsync();

        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var lease = await new LeaseRepository(context).GetAggregateForUpdateAsync(leaseId);

        Assert.NotNull(lease);
        Assert.NotNull(lease.Room);
        // The tenant removed from the draft is soft-deleted and must not come back
        var tenant = Assert.Single(lease.Tenants);
        Assert.Equal("Representative", tenant.User!.FullName);
        var service = Assert.Single(lease.UtilityServices);
        Assert.Equal("Internet", service.Service!.Name);
    }

    [Fact]
    public async Task GetAggregateForUpdateAsync_UnknownLease_ReturnsNull()
    {
        await using var context = fixture.CreateContext();

        Assert.Null(await new LeaseRepository(context).GetAggregateForUpdateAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetByIdWithTenantsForDeleteAsync_SoftDelete_CascadesToTenantsAndKeepsUtilityServices()
    {
        var leaseId = await SeedAsync();

        await using (var context = fixture.CreateContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            var repository = new LeaseRepository(context);
            var lease = await repository.GetByIdWithTenantsForDeleteAsync(leaseId);

            Assert.NotNull(lease);
            Assert.Single(lease.Tenants);
            Assert.Empty(lease.UtilityServices);

            repository.SoftDelete(lease);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verify = fixture.CreateContext();
        Assert.All(
            await verify.LeaseTenants.IgnoreQueryFilters().Where(t => t.LeaseId == leaseId).ToListAsync(),
            t => Assert.NotNull(t.DeletedAt));
        Assert.Single(await verify.LeaseUtilityServices.Where(s => s.LeaseId == leaseId).ToListAsync());
    }

    // Draft lease with a representative, a second tenant removed again (soft-deleted row), and one utility service
    private async Task<Guid> SeedAsync()
    {
        await using var context = fixture.CreateContext();

        // lease_status_histories.changed_by references users, so the actor must be a real user
        var staff = TestEntities.New<User>(new { FullName = "Staff", PasswordHash = "hash", CreatedBy = SentinelActors.System });
        context.Users.Add(staff);
        var actor = staff.Id;

        var organization = TestEntities.New<Organization>(new { Name = "Org", CreatedBy = actor });
        var property = TestEntities.New<Property>(new { OrganizationId = organization.Id, Name = "Property", CreatedBy = actor });
        var room = TestRooms.New(propertyId: property.Id, roomNumber: "101", capacity: 4, createdBy: actor);
        var representative = TestEntities.New<User>(new { FullName = "Representative", PasswordHash = "hash", CreatedBy = actor });
        var removed = TestEntities.New<User>(new { FullName = "Removed", PasswordHash = "hash", CreatedBy = actor });
        var internet = TestEntities.New<UtilityService>(new
        {
            PropertyId = property.Id,
            Name = "Internet",
            Unit = "month",
            BillingMethod = UtilityBillingMethod.Fixed,
            DefaultUnitPrice = 100_000,
            CreatedBy = actor
        });
        context.AddRange(organization, property, room, representative, removed, internet);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var users = new Dictionary<Guid, User> { [representative.Id] = representative, [removed.Id] = removed };
        var lease = Lease.CreateDraft(
            organization.Id, room, previousLeaseId: null, "HD-001", today, today.AddYears(1), billingDay: null,
            monthlyRent: 1_000_000, depositAmount: 1_000_000,
            [
                new TenantChange { UserId = representative.Id, IsRepresentative = true },
                new TenantChange { UserId = removed.Id }
            ],
            users, [new LeaseUtilityServiceSelection(internet, UnitPriceOverride: null, Quantity: 1)], DateTimeOffset.UtcNow, actor);
        context.Add(lease);
        await context.SaveChangesAsync();

        var removedTenant = lease.Tenants.Single(t => t.UserId == removed.Id);
        lease.Update(lease.CurrentTerms, [new TenantChange { TenantId = removedTenant.Id, IsDeleted = true }], users, today, actor);
        await context.SaveChangesAsync();

        return lease.Id;
    }
}
