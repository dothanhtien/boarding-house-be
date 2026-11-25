using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class TenantPortalControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string Password = "password123";

    private Guid _staffId;
    private Guid _organizationId;
    private Guid _propertyId;
    private Room _room = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();

        // lease_status_histories.changed_by references users, so the actor must be a real user
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staff = TestEntities.New<User>(new { FullName = "Staff", PasswordHash = "hash", CreatedBy = SentinelActors.System });
        var organization = TestEntities.New<Organization>(new { Name = "Org A", CreatedBy = staff.Id });
        var property = TestEntities.New<Property>(new { OrganizationId = organization.Id, Name = "Property A", CreatedBy = staff.Id });
        _room = TestRooms.New(propertyId: property.Id, roomNumber: "101", capacity: 4, createdBy: staff.Id);
        context.AddRange(staff, organization, property, _room);
        await context.SaveChangesAsync();

        _staffId = staff.Id;
        _organizationId = organization.Id;
        _propertyId = property.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string ExtractAccessTokenCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("accessToken=", StringComparison.Ordinal))
            .Split(';')[0]["accessToken=".Length..];

    private async Task<(HttpClient Client, Guid UserId)> RegisterAndLoginAsync(string fullName)
    {
        var email = $"user-{Guid.NewGuid():N}@test.com";
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = fullName
        });
        var user = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        return (client, user.Id);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    // A lease in _room with the given tenants (first one is the representative), moved to `status`
    private async Task<Guid> SeedLeaseAsync(
        LeaseStatus status, string leaseCode, DateOnly startDate, params Guid[] tenantUserIds)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await context.Users.Where(u => tenantUserIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        var room = await context.Rooms.SingleAsync(r => r.Id == _room.Id);

        var lease = Lease.CreateDraft(
            _organizationId, room, previousLeaseId: null, leaseCode, startDate, startDate.AddYears(1), billingDay: null,
            monthlyRent: 3_000_000, depositAmount: 3_000_000,
            tenantUserIds.Select((id, index) => new TenantChange { UserId = id, IsRepresentative = index == 0 }).ToList(),
            users, [], DateTimeOffset.UtcNow, _staffId);
        TestEntities.Set(lease, new { Status = status });
        context.Leases.Add(lease);
        await context.SaveChangesAsync();
        return lease.Id;
    }

    private async Task SetTenantDatesAsync(Guid leaseId, Guid userId, DateOnly? joinedDate, DateOnly? leftDate)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await context.LeaseTenants.SingleAsync(t => t.LeaseId == leaseId && t.UserId == userId);
        TestEntities.Set(tenant, new { JoinedDate = joinedDate, LeftDate = leftDate });
        await context.SaveChangesAsync();
    }

    // The tenant moves out on `leftDate`, then comes back to the same lease on `rejoinedDate`: a second lease_tenants row
    private async Task RejoinAsync(Guid leaseId, Guid userId, DateOnly leftDate, DateOnly rejoinedDate)
    {
        await SetTenantDatesAsync(leaseId, userId, joinedDate: null, leftDate);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.LeaseTenants.Add(TestEntities.New<LeaseTenant>(new
        {
            LeaseId = leaseId,
            UserId = userId,
            IsRepresentative = false,
            JoinedDate = rejoinedDate,
            CreatedBy = _staffId
        }));
        await context.SaveChangesAsync();
    }

    private async Task SoftDeleteLeaseAsync(Guid leaseId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lease = await context.Leases.SingleAsync(l => l.Id == leaseId);
        context.Leases.Remove(lease);
        await context.SaveChangesAsync();
    }

    private async Task SoftDeleteTenantRowAsync(Guid leaseId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await context.LeaseTenants.SingleAsync(t => t.LeaseId == leaseId && t.UserId == userId);
        context.LeaseTenants.Remove(tenant);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetTenancy_UserWithoutLeases_IsZero()
    {
        var (client, _) = await RegisterAndLoginAsync("Staff");

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        Assert.Equal(0, tenancy.TotalLeaseCount);
        Assert.Equal(0, tenancy.ActiveLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_ActiveAndPastLeases_CountsThem_IgnoringDraft()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        await SeedLeaseAsync(LeaseStatus.Active, "HD-ACTIVE", new DateOnly(2026, 1, 1), tenantA);
        await SeedLeaseAsync(LeaseStatus.Expired, "HD-EXPIRED", new DateOnly(2025, 1, 1), tenantA);
        await SeedLeaseAsync(LeaseStatus.Draft, "HD-DRAFT", new DateOnly(2027, 1, 1), tenantA);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        // no tenant_profiles row is seeded: tenancy comes from lease_tenants only
        Assert.Equal(1, tenancy.ActiveLeaseCount);
        Assert.Equal(2, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_LeasesOfOtherUsers_NotCounted()
    {
        var (client, _) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        await SeedLeaseAsync(LeaseStatus.Active, "HD-B", new DateOnly(2026, 1, 1), tenantB);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        Assert.Equal(0, tenancy.ActiveLeaseCount);
        Assert.Equal(0, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_TenantWhoLeftActiveLease_IsNotActive()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await SetTenantDatesAsync(leaseId, tenantA, joinedDate: null, leftDate: yesterday);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        Assert.Equal(0, tenancy.ActiveLeaseCount);
        Assert.Equal(1, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_TenantLeavingToday_IsStillActive()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SetTenantDatesAsync(leaseId, tenantA, joinedDate: null, leftDate: today);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        // left_date is the last day of the stay
        Assert.Equal(1, tenancy.ActiveLeaseCount);
        Assert.Equal(1, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_TenantWhoRejoinedSameLease_CountedOnce()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // the old stay ends in the future too, so a row-based count would give ActiveLeaseCount 2
        await RejoinAsync(leaseId, tenantA, leftDate: today.AddDays(1), rejoinedDate: today.AddDays(2));

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        Assert.Equal(1, tenancy.ActiveLeaseCount);
        Assert.Equal(1, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_SoftDeletedLeaseOrTenantRow_NotCounted()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var deletedLeaseId = await SeedLeaseAsync(LeaseStatus.Expired, "HD-DELETED", new DateOnly(2024, 1, 1), tenantA);
        var removedFromLeaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-REMOVED", new DateOnly(2026, 1, 1), tenantB, tenantA);
        await SoftDeleteLeaseAsync(deletedLeaseId);
        await SoftDeleteTenantRowAsync(removedFromLeaseId, tenantA);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        // StaysOf uses IgnoreQueryFilters: this fails if its explicit DeletedAt checks are dropped
        Assert.Equal(0, tenancy.ActiveLeaseCount);
        Assert.Equal(0, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task AdminEndpoint_CalledByTenantOnly_Returns403()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);

        var response = await client.GetAsync("/api/leases");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTenancy_Anonymous_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/me/tenancy");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
