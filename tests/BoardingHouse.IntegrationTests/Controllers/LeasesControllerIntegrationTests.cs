using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.DTOs.Properties;
using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.Services.Caching;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.IntegrationTests.Controllers;

public class LeasesControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "lease-actor@test.com";
    private const string Password = "password123";
    private const string GeneratedLeaseCodePattern = @"^HD-\d{18}$";

    private static readonly DateOnly StartDate = new(2026, 11, 1);

    private Guid _actorId;
    private Guid _organizationId;
    private Guid _propertyId;
    private Guid _roomId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        _organizationId = await CreateOrganizationAsync("Lease Org");
        _propertyId = await CreatePropertyAsync(_client, _organizationId, "Lease Property");
        _roomId = await CreateRoomAsync(_client, _propertyId, "101", monthlyRent: 3_000_000, depositAmount: 2_000_000, capacity: 3);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string ExtractAccessTokenCookie(HttpResponseMessage response)
    {
        var setCookieHeader = response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("accessToken=", StringComparison.Ordinal));

        return setCookieHeader.Split(';')[0]["accessToken=".Length..];
    }

    private async Task AuthenticateAsync()
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = ActorEmail,
            Phone = "0900000094",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Lease Actor"
        });
        var actor = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        await factory.GrantPlatformAdminRoleAsync(actor!.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = ActorEmail, Password = Password });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        _actorId = actor.Id;
    }

    private async Task<Guid> CreateOrganizationAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/organizations", new CreateOrganizationRequest { Name = name, OwnerId = _actorId });
        var organization = (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())?.Data;
        return organization!.Id;
    }

    private static async Task<Guid> CreatePropertyAsync(HttpClient client, Guid organizationId, string name)
    {
        var response = await client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest { OrganizationId = organizationId, Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())!.Data!.Id;
    }

    private static async Task<Guid> CreateRoomAsync(
        HttpClient client, Guid propertyId, string roomNumber,
        decimal? monthlyRent = null, decimal? depositAmount = null, int? capacity = null)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest
        {
            PropertyId = propertyId,
            RoomNumber = roomNumber,
            MonthlyRent = monthlyRent,
            DepositAmount = depositAmount,
            Capacity = capacity
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!.Id;
    }

    // Tenants are plain self-registered users (no role); returns their id.
    private async Task<Guid> CreateTenantUserAsync(string fullName = "Tenant")
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"tenant-{Guid.NewGuid():N}@test.com",
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = fullName
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!.Id;
    }

    private CreateLeaseRequest ValidCreateRequest() => new() { RoomId = _roomId, StartDate = StartDate };

    private static async Task<LeaseResponse> ReadLeaseAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<LeaseResponse>>(JsonOptions))!.Data!;

    private async Task<LeaseResponse> CreateLeaseAsync(CreateLeaseRequest request, HttpClient? client = null)
    {
        var response = await (client ?? _client).PostAsJsonAsync("/api/leases", request, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadLeaseAsync(response);
    }

    private Task<HttpResponseMessage> PatchLeaseAsync(Guid id, UpdateLeaseRequest request) =>
        _client.PatchAsJsonAsync($"/api/leases/{id}", request, JsonOptions);

    private async Task<PagedResult<LeaseResponse>> GetLeasesAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<LeaseResponse>>>(JsonOptions))!.Data!;
    }

    private async Task WithContextAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task SetLeaseStatusAsync(Guid leaseId, LeaseStatus status) =>
        WithContextAsync(context => context.Leases
            .Where(l => l.Id == leaseId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, status)));

    private async Task<(HttpClient Client, Guid OrganizationId)> CreateOrganizationScopedMemberAsync(string roleSlug)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RbacSeeder.SeedAsync(context);

        var organization = new Organization { Name = $"Member Org {Guid.NewGuid()}", CreatedBy = SentinelActors.System };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var memberClient = factory.CreateClient();
        var email = $"member-{Guid.NewGuid():N}@test.com";
        var registerResponse = await memberClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Org Member"
        });
        var member = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        var role = await context.Roles.SingleAsync(r => r.Slug == roleSlug);
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = member!.Id,
            RoleId = role.Id,
            CreatedBy = SentinelActors.System
        });
        await context.SaveChangesAsync();

        var loginResponse = await memberClient.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        return (memberClient, organization.Id);
    }

    [Fact]
    public async Task GetAll_Unauthenticated_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/leases");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_UserWithoutAnyRole_Returns403()
    {
        var client = factory.CreateClient();
        var email = $"norole-{Guid.NewGuid():N}@test.com";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = "0900000093",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "No Role"
        });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        var response = await client.PostAsJsonAsync("/api/leases", ValidCreateRequest(), JsonOptions);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutPrices_SnapshotsRoomPricesAndGeneratesCode()
    {
        var response = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with
        {
            EndDate = StartDate.AddYears(1),
            BillingDay = 5
        }, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = await ReadLeaseAsync(response);
        Assert.Equal(LeaseStatus.Draft, created.Status);
        Assert.Equal(_organizationId, created.OrganizationId);
        Assert.Equal(_propertyId, created.PropertyId);
        Assert.Equal(_roomId, created.RoomId);
        Assert.Equal("101", created.RoomNumber);
        Assert.Matches(GeneratedLeaseCodePattern, created.LeaseCode);
        Assert.Equal(3_000_000, created.MonthlyRent);
        Assert.Equal(2_000_000, created.DepositAmount);
        Assert.Empty(created.Tenants);

        var fetched = await ReadLeaseAsync(await _client.GetAsync($"/api/leases/{created.Id}"));
        Assert.Equal(created.LeaseCode, fetched.LeaseCode);
        Assert.Equal(StartDate.AddYears(1), fetched.EndDate);
        Assert.Equal(5, fetched.BillingDay);
    }

    [Fact]
    public async Task Create_WithPrices_UsesRequestPrices()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest() with { MonthlyRent = 3_500_000, DepositAmount = 0 });

        Assert.Equal(3_500_000, created.MonthlyRent);
        Assert.Equal(0, created.DepositAmount);
    }

    [Fact]
    public async Task Create_RoomWithoutPricesAndNoneSent_Returns400()
    {
        var roomId = await CreateRoomAsync(_client, _propertyId, "102");

        var response = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with { RoomId = roomId }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_RoomPriceChangedAfterwards_LeaseKeepsSnapshot()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest());

        var roomResponse = await _client.PatchAsJsonAsync($"/api/rooms/{_roomId}", new UpdateRoomRequest { MonthlyRent = 4_000_000m }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, roomResponse.StatusCode);

        var fetched = await ReadLeaseAsync(await _client.GetAsync($"/api/leases/{created.Id}"));
        Assert.Equal(3_000_000, fetched.MonthlyRent);
    }

    [Fact]
    public async Task Create_LeaseCodeUniquePerOrganizationAmongLiveLeases()
    {
        var first = await CreateLeaseAsync(ValidCreateRequest() with { LeaseCode = "HD-001" });

        var sameOrg = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with { LeaseCode = "HD-001" }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, sameOrg.StatusCode);

        var otherOrganizationId = await CreateOrganizationAsync("Other Org");
        var otherPropertyId = await CreatePropertyAsync(_client, otherOrganizationId, "Other Property");
        var otherRoomId = await CreateRoomAsync(_client, otherPropertyId, "201", monthlyRent: 1_000_000, depositAmount: 500_000);
        var otherOrg = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with { RoomId = otherRoomId, LeaseCode = "HD-001" }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, otherOrg.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/leases/{first.Id}")).StatusCode);
        var afterDelete = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with { LeaseCode = "HD-001" }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Create_RoomNotFound_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with { RoomId = Guid.NewGuid() }, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_PreviousLeaseOfAnotherOrganization_Returns404()
    {
        var otherOrganizationId = await CreateOrganizationAsync("Other Org");
        var otherPropertyId = await CreatePropertyAsync(_client, otherOrganizationId, "Other Property");
        var otherRoomId = await CreateRoomAsync(_client, otherPropertyId, "201", monthlyRent: 1_000_000, depositAmount: 500_000);
        var otherLease = await CreateLeaseAsync(ValidCreateRequest() with { RoomId = otherRoomId });

        var response = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with { PreviousLeaseId = otherLease.Id }, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_PreviousLeaseOfSameOrganization_IsLinked()
    {
        var previous = await CreateLeaseAsync(ValidCreateRequest());
        await SetLeaseStatusAsync(previous.Id, LeaseStatus.Terminated);

        var created = await CreateLeaseAsync(ValidCreateRequest() with { PreviousLeaseId = previous.Id, StartDate = StartDate.AddYears(1) });

        Assert.Equal(previous.Id, created.PreviousLeaseId);
    }

    [Fact]
    public async Task Create_WithTenants_ReturnsRepresentativeFirstWithNameAndPhone()
    {
        var memberId = await CreateTenantUserAsync("Member Tenant");
        var representativeId = await CreateTenantUserAsync("Representative Tenant");

        var created = await CreateLeaseAsync(ValidCreateRequest() with
        {
            Tenants =
            [
                new CreateLeaseTenantRequest { UserId = memberId },
                new CreateLeaseTenantRequest { UserId = representativeId, IsRepresentative = true, JoinedDate = StartDate.AddDays(3) }
            ]
        });

        Assert.Equal(2, created.Tenants.Count);
        Assert.Equal(representativeId, created.Tenants[0].UserId);
        Assert.True(created.Tenants[0].IsRepresentative);
        Assert.Equal("Representative Tenant", created.Tenants[0].FullName);
        Assert.NotNull(created.Tenants[0].Phone);
        Assert.Equal(StartDate.AddDays(3), created.Tenants[0].JoinedDate);
        Assert.Equal(memberId, created.Tenants[1].UserId);

        var fetched = await ReadLeaseAsync(await _client.GetAsync($"/api/leases/{created.Id}"));
        Assert.Equal(created.Tenants.Select(t => t.Id), fetched.Tenants.Select(t => t.Id));
    }

    [Fact]
    public async Task Create_TenantUserNotFound_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with
        {
            Tenants = [new CreateLeaseTenantRequest { UserId = Guid.NewGuid() }]
        }, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_InactiveTenantUser_Returns400()
    {
        var userId = await CreateTenantUserAsync();
        await WithContextAsync(context => context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));

        var response = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with
        {
            Tenants = [new CreateLeaseTenantRequest { UserId = userId }]
        }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_MoreTenantsThanRoomCapacity_Returns400()
    {
        var tenants = new List<CreateLeaseTenantRequest>();
        for (var i = 0; i < 4; i++)
        {
            tenants.Add(new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync() });
        }

        var response = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with { Tenants = tenants }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_OnlyEndDate_KeepsOtherFields()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest() with { BillingDay = 10, LeaseCode = "HD-KEEP" });

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest { EndDate = StartDate.AddMonths(6) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadLeaseAsync(response);
        Assert.Equal(StartDate.AddMonths(6), updated.EndDate);
        Assert.Equal(10, updated.BillingDay);
        Assert.Equal("HD-KEEP", updated.LeaseCode);
        Assert.Equal(StartDate, updated.StartDate);
        Assert.Equal(3_000_000, updated.MonthlyRent);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Update_StartDateNotBeforeStoredEndDate_Returns400()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest() with { EndDate = StartDate.AddMonths(6) });

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest { StartDate = StartDate.AddMonths(6) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_BillingDayNull_ClearsIt()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest() with { BillingDay = 10 });

        var updated = await ReadLeaseAsync(await PatchLeaseAsync(created.Id, new UpdateLeaseRequest { BillingDay = null }));

        Assert.Null(updated.BillingDay);
    }

    [Fact]
    public async Task Update_MonthlyRentNull_Returns400()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest());

        var response = await _client.PatchAsync(
            $"/api/leases/{created.Id}", new StringContent("""{"monthlyRent":null}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_PreviousLeaseIsItself_Returns400()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest());

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest { PreviousLeaseId = created.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_TenantsAddEditDeleteInOneRequest_AppliesAll()
    {
        var keptId = await CreateTenantUserAsync("Kept");
        var removedId = await CreateTenantUserAsync("Removed");
        var addedId = await CreateTenantUserAsync("Added");
        var created = await CreateLeaseAsync(ValidCreateRequest() with
        {
            Tenants =
            [
                new CreateLeaseTenantRequest { UserId = keptId, IsRepresentative = true },
                new CreateLeaseTenantRequest { UserId = removedId }
            ]
        });
        var kept = created.Tenants.Single(t => t.UserId == keptId);
        var removed = created.Tenants.Single(t => t.UserId == removedId);

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest>
            {
                new() { Id = kept.Id, JoinedDate = StartDate.AddDays(2) },
                new() { Id = removed.Id, IsDeleted = true },
                new() { UserId = addedId }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadLeaseAsync(response);
        Assert.Equal([keptId, addedId], updated.Tenants.Select(t => t.UserId));
        Assert.Equal(StartDate.AddDays(2), updated.Tenants[0].JoinedDate);
        Assert.True(updated.Tenants[0].IsRepresentative);
        Assert.Equal("Added", updated.Tenants[1].FullName);

        await WithContextAsync(async context =>
        {
            var row = await context.LeaseTenants.IgnoreQueryFilters().SingleAsync(t => t.Id == removed.Id);
            Assert.NotNull(row.DeletedAt);
        });
    }

    [Fact]
    public async Task Update_TenantIdOfAnotherLease_Returns404()
    {
        var other = await CreateLeaseAsync(ValidCreateRequest() with
        {
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync() }]
        });
        var created = await CreateLeaseAsync(ValidCreateRequest());

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { Id = other.Tenants[0].Id, IsDeleted = true } }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_AddUserAlreadyOnLease_Returns409()
    {
        var userId = await CreateTenantUserAsync();
        var created = await CreateLeaseAsync(ValidCreateRequest() with { Tenants = [new CreateLeaseTenantRequest { UserId = userId }] });

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { UserId = userId } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_RemoveAndReAddSameUserInOneRequest_Returns200()
    {
        var userId = await CreateTenantUserAsync();
        var created = await CreateLeaseAsync(ValidCreateRequest() with { Tenants = [new CreateLeaseTenantRequest { UserId = userId }] });

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest>
            {
                new() { Id = created.Tenants[0].Id, IsDeleted = true },
                new() { UserId = userId, IsRepresentative = true }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadLeaseAsync(response);
        var tenant = Assert.Single(updated.Tenants);
        Assert.NotEqual(created.Tenants[0].Id, tenant.Id);
        Assert.True(tenant.IsRepresentative);
    }

    [Fact]
    public async Task Update_SwapRepresentativeBackAndForth_Returns200()
    {
        var firstId = await CreateTenantUserAsync("First");
        var secondId = await CreateTenantUserAsync("Second");
        var created = await CreateLeaseAsync(ValidCreateRequest() with
        {
            Tenants =
            [
                new CreateLeaseTenantRequest { UserId = firstId, IsRepresentative = true },
                new CreateLeaseTenantRequest { UserId = secondId }
            ]
        });
        var first = created.Tenants.Single(t => t.UserId == firstId);
        var second = created.Tenants.Single(t => t.UserId == secondId);

        // EF orders the two UPDATEs by key, not by intent. Swapping both ways guarantees one direction issues the
        // promotion before the demotion, which only passes because demotions are flushed first.
        foreach (var (promote, demote) in new[] { (second, first), (first, second) })
        {
            var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest
            {
                Tenants = new List<UpdateLeaseTenantRequest>
                {
                    new() { Id = promote.Id, IsRepresentative = true },
                    new() { Id = demote.Id, IsRepresentative = false }
                }
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = await ReadLeaseAsync(response);
            Assert.Equal(promote.Id, updated.Tenants[0].Id);
            Assert.True(updated.Tenants[0].IsRepresentative);
            Assert.False(updated.Tenants[1].IsRepresentative);
        }
    }

    [Fact]
    public async Task Update_SecondRepresentative_Returns400()
    {
        var firstId = await CreateTenantUserAsync();
        var created = await CreateLeaseAsync(ValidCreateRequest() with
        {
            Tenants = [new CreateLeaseTenantRequest { UserId = firstId, IsRepresentative = true }]
        });

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { UserId = await CreateTenantUserAsync(), IsRepresentative = true } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_TenantsBeyondCapacity_Returns400()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest() with
        {
            Tenants =
            [
                new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync() },
                new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync() },
                new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync() }
            ]
        });

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { UserId = await CreateTenantUserAsync() } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAndDelete_NonDraftLease_Returns409()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest());
        await SetLeaseStatusAsync(created.Id, LeaseStatus.Active);

        var patch = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest { BillingDay = 3 });
        var delete = await _client.DeleteAsync($"/api/leases/{created.Id}");

        Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_DraftLease_SoftDeletesLeaseAndTenants()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest() with
        {
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync() }]
        });

        var response = await _client.DeleteAsync($"/api/leases/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/leases/{created.Id}")).StatusCode);
        await WithContextAsync(async context =>
        {
            var tenant = await context.LeaseTenants.IgnoreQueryFilters().SingleAsync(t => t.LeaseId == created.Id);
            Assert.NotNull(tenant.DeletedAt);
            Assert.Equal(_actorId, tenant.DeletedBy);
        });
    }

    [Fact]
    public async Task GetAll_FiltersSearchSortAndPaging()
    {
        var tenantId = await CreateTenantUserAsync();
        var otherRoomId = await CreateRoomAsync(_client, _propertyId, "102", monthlyRent: 1_000_000, depositAmount: 500_000);
        var early = await CreateLeaseAsync(ValidCreateRequest() with { LeaseCode = "HD-EARLY" });
        var late = await CreateLeaseAsync(ValidCreateRequest() with
        {
            LeaseCode = "HD-LATE",
            StartDate = StartDate.AddMonths(2),
            Tenants = [new CreateLeaseTenantRequest { UserId = tenantId }]
        });
        var otherRoom = await CreateLeaseAsync(ValidCreateRequest() with { RoomId = otherRoomId, LeaseCode = "HD-OTHER", StartDate = StartDate.AddMonths(1) });
        var deleted = await CreateLeaseAsync(ValidCreateRequest() with { LeaseCode = "HD-DELETED" });
        await _client.DeleteAsync($"/api/leases/{deleted.Id}");
        await SetLeaseStatusAsync(otherRoom.Id, LeaseStatus.Active);

        var all = await GetLeasesAsync(_client, "/api/leases");
        Assert.Equal([late.Id, otherRoom.Id, early.Id], all.Items.Select(l => l.Id));

        var byRoom = await GetLeasesAsync(_client, $"/api/leases?roomId={_roomId}");
        Assert.Equal([late.Id, early.Id], byRoom.Items.Select(l => l.Id));

        var byStatus = await GetLeasesAsync(_client, "/api/leases?status=active");
        Assert.Equal([otherRoom.Id], byStatus.Items.Select(l => l.Id));

        var byTenant = await GetLeasesAsync(_client, $"/api/leases?tenantUserId={tenantId}");
        Assert.Equal([late.Id], byTenant.Items.Select(l => l.Id));

        var byProperty = await GetLeasesAsync(_client, $"/api/leases?propertyId={_propertyId}");
        Assert.Equal(3, byProperty.TotalItems);

        var bySearch = await GetLeasesAsync(_client, "/api/leases?search=early");
        Assert.Equal([early.Id], bySearch.Items.Select(l => l.Id));

        var byCodeAsc = await GetLeasesAsync(_client, "/api/leases?sortBy=leaseCode&sortOrder=asc");
        Assert.Equal(["HD-EARLY", "HD-LATE", "HD-OTHER"], byCodeAsc.Items.Select(l => l.LeaseCode));

        var page = await GetLeasesAsync(_client, "/api/leases?page=2&pageSize=2");
        Assert.Equal(3, page.TotalItems);
        Assert.Equal([early.Id], page.Items.Select(l => l.Id));
    }

    [Fact]
    public async Task OrganizationStaff_OnlySeesAndManagesOwnOrganizationLeases()
    {
        var (memberClient, memberOrganizationId) = await CreateOrganizationScopedMemberAsync(RoleSlugs.OrganizationStaff);
        var memberPropertyId = await CreatePropertyAsync(_client, memberOrganizationId, "Member Property");
        var memberRoomId = await CreateRoomAsync(_client, memberPropertyId, "301", monthlyRent: 2_000_000, depositAmount: 500_000);
        var otherLease = await CreateLeaseAsync(ValidCreateRequest());

        var own = await CreateLeaseAsync(ValidCreateRequest() with { RoomId = memberRoomId }, memberClient);
        Assert.Equal(memberOrganizationId, own.OrganizationId);

        var otherRoom = await memberClient.PostAsJsonAsync("/api/leases", ValidCreateRequest(), JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, otherRoom.StatusCode);

        var list = await GetLeasesAsync(memberClient, "/api/leases");
        Assert.Equal([own.Id], list.Items.Select(l => l.Id));

        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/leases/{otherLease.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PatchAsJsonAsync(
            $"/api/leases/{otherLease.Id}", new UpdateLeaseRequest { BillingDay = 3 }, JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"/api/leases/{otherLease.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await memberClient.DeleteAsync($"/api/leases/{own.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteRoom_WithDraftLease_Returns409()
    {
        await CreateLeaseAsync(ValidCreateRequest());

        var response = await _client.DeleteAsync($"/api/rooms/{_roomId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoom_WithOnlyTerminatedLease_Returns204AndLeaseStaysVisible()
    {
        var lease = await CreateLeaseAsync(ValidCreateRequest());
        await SetLeaseStatusAsync(lease.Id, LeaseStatus.Terminated);

        var response = await _client.DeleteAsync($"/api/rooms/{_roomId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var fetched = await ReadLeaseAsync(await _client.GetAsync($"/api/leases/{lease.Id}"));
        Assert.Equal("101", fetched.RoomNumber);
        var list = await GetLeasesAsync(_client, "/api/leases");
        Assert.Equal([lease.Id], list.Items.Select(l => l.Id));
    }

    [Fact]
    public async Task TenantWithSoftDeletedUser_StaysVisibleAndCanBeRemoved()
    {
        var deletedUserId = await CreateTenantUserAsync("Gone");
        var otherUserId = await CreateTenantUserAsync("Other");
        var created = await CreateLeaseAsync(ValidCreateRequest() with
        {
            Tenants =
            [
                new CreateLeaseTenantRequest { UserId = deletedUserId, IsRepresentative = true },
                new CreateLeaseTenantRequest { UserId = otherUserId }
            ]
        });
        await WithContextAsync(context => context.Users
            .Where(u => u.Id == deletedUserId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, DateTimeOffset.UtcNow)));

        var fetched = await ReadLeaseAsync(await _client.GetAsync($"/api/leases/{created.Id}"));
        Assert.Equal([deletedUserId, otherUserId], fetched.Tenants.Select(t => t.UserId));
        var listed = await GetLeasesAsync(_client, $"/api/leases?tenantUserId={deletedUserId}");
        Assert.Equal(2, listed.Items.Single().Tenants.Count);

        // The hidden representative still counts, so promoting another tenant alone is rejected...
        var secondRepresentative = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new() { Id = fetched.Tenants[1].Id, IsRepresentative = true } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, secondRepresentative.StatusCode);

        // ...and the tenant can be removed.
        var remove = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest>
            {
                new() { Id = fetched.Tenants[0].Id, IsDeleted = true },
                new() { Id = fetched.Tenants[1].Id, IsRepresentative = true }
            }
        });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal([otherUserId], (await ReadLeaseAsync(remove)).Tenants.Select(t => t.UserId));
    }

    [Fact]
    public async Task Create_PreviousLeaseOfAnotherRoom_Returns400()
    {
        var otherRoomId = await CreateRoomAsync(_client, _propertyId, "102", monthlyRent: 1_000_000, depositAmount: 500_000);
        var otherRoomLease = await CreateLeaseAsync(ValidCreateRequest() with { RoomId = otherRoomId });

        var response = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with { PreviousLeaseId = otherRoomLease.Id }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_PreviousLeaseCreatesCycle_Returns400()
    {
        // A previous lease must not be a draft, so each link is made while its target is terminated;
        // first is then put back to draft so it can be edited.
        var first = await CreateLeaseAsync(ValidCreateRequest());
        await SetLeaseStatusAsync(first.Id, LeaseStatus.Terminated);
        var second = await CreateLeaseAsync(ValidCreateRequest() with { PreviousLeaseId = first.Id, StartDate = StartDate.AddYears(1) });
        await SetLeaseStatusAsync(second.Id, LeaseStatus.Terminated);
        var third = await CreateLeaseAsync(ValidCreateRequest() with { PreviousLeaseId = second.Id, StartDate = StartDate.AddYears(2) });
        await SetLeaseStatusAsync(third.Id, LeaseStatus.Terminated);
        await SetLeaseStatusAsync(first.Id, LeaseStatus.Draft);

        var response = await PatchLeaseAsync(first.Id, new UpdateLeaseRequest { PreviousLeaseId = third.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAndUpdate_PreviousLeaseAlreadyRenewed_Returns409()
    {
        var previous = await CreateLeaseAsync(ValidCreateRequest());
        await SetLeaseStatusAsync(previous.Id, LeaseStatus.Terminated);
        var renewal = await CreateLeaseAsync(ValidCreateRequest() with { PreviousLeaseId = previous.Id, StartDate = StartDate.AddYears(1) });
        var other = await CreateLeaseAsync(ValidCreateRequest() with { StartDate = StartDate.AddYears(2) });

        var createResponse = await _client.PostAsJsonAsync(
            "/api/leases", ValidCreateRequest() with { PreviousLeaseId = previous.Id, StartDate = StartDate.AddYears(2) }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PatchLeaseAsync(other.Id, new UpdateLeaseRequest { PreviousLeaseId = previous.Id })).StatusCode);

        // Re-sending its own link is not a second renewal, and a deleted renewal frees the previous lease.
        Assert.Equal(HttpStatusCode.OK, (await PatchLeaseAsync(renewal.Id, new UpdateLeaseRequest { PreviousLeaseId = previous.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/leases/{renewal.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PatchLeaseAsync(other.Id, new UpdateLeaseRequest { PreviousLeaseId = previous.Id })).StatusCode);
    }

    [Fact]
    public async Task Delete_LeaseThatIsPreviousOfAnother_Returns409()
    {
        var previous = await CreateLeaseAsync(ValidCreateRequest());
        await SetLeaseStatusAsync(previous.Id, LeaseStatus.Terminated);
        var next = await CreateLeaseAsync(ValidCreateRequest() with { PreviousLeaseId = previous.Id, StartDate = StartDate.AddYears(1) });
        // Back to draft so only the renewal link blocks the delete.
        await SetLeaseStatusAsync(previous.Id, LeaseStatus.Draft);

        Assert.Equal(HttpStatusCode.Conflict, (await _client.DeleteAsync($"/api/leases/{previous.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PatchLeaseAsync(next.Id, new UpdateLeaseRequest { PreviousLeaseId = null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/leases/{previous.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(365)]
    public async Task Create_TenantJoinedDateOutsideLease_Returns400(int daysFromStart)
    {
        var response = await _client.PostAsJsonAsync("/api/leases", ValidCreateRequest() with
        {
            EndDate = StartDate.AddDays(365),
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync(), JoinedDate = StartDate.AddDays(daysFromStart) }]
        }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_StartDateAfterTenantJoinedDate_Returns400()
    {
        var created = await CreateLeaseAsync(ValidCreateRequest() with
        {
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync(), JoinedDate = StartDate.AddDays(5) }]
        });

        var response = await PatchLeaseAsync(created.Id, new UpdateLeaseRequest { StartDate = StartDate.AddDays(10) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteRoom_AfterItsOnlyLeaseIsDeleted_Returns204()
    {
        var lease = await CreateLeaseAsync(ValidCreateRequest());
        await _client.DeleteAsync($"/api/leases/{lease.Id}");

        var response = await _client.DeleteAsync($"/api/rooms/{_roomId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private Task<HttpResponseMessage> ActivateAsync(Guid id, HttpClient? client = null) =>
        (client ?? _client).PostAsync($"/api/leases/{id}/activate", content: null);

    private Task<HttpResponseMessage> TerminateAsync(Guid id, TerminateLeaseRequest request, HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync($"/api/leases/{id}/terminate", request, JsonOptions);

    private async Task<List<LeaseStatusHistoryResponse>> GetStatusHistoriesAsync(Guid id)
    {
        var response = await _client.GetAsync($"/api/leases/{id}/status-histories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<List<LeaseStatusHistoryResponse>>>(JsonOptions))!.Data!;
    }

    private async Task<RoomStatus> GetRoomStatusAsync(Guid roomId)
    {
        var status = default(RoomStatus);
        await WithContextAsync(async context =>
            status = await context.Rooms.Where(r => r.Id == roomId).Select(r => r.RoomStatus).SingleAsync());
        return status;
    }

    // A draft that can be activated right away: starts today, the first tenant is the representative.
    private async Task<LeaseResponse> CreateActivatableLeaseAsync(int tenantCount = 1)
    {
        var tenants = new List<CreateLeaseTenantRequest>();
        for (var i = 0; i < tenantCount; i++)
        {
            tenants.Add(new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync($"Tenant {i}"), IsRepresentative = i == 0 });
        }

        return await CreateLeaseAsync(ValidCreateRequest() with { StartDate = Today, Tenants = tenants });
    }

    private async Task<LeaseResponse> CreateActiveLeaseAsync(int tenantCount = 1)
    {
        var lease = await CreateActivatableLeaseAsync(tenantCount);
        var response = await ActivateAsync(lease.Id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadLeaseAsync(response);
    }

    [Fact]
    public async Task Create_WritesInitialDraftStatusHistory()
    {
        var lease = await CreateLeaseAsync(ValidCreateRequest());

        var history = Assert.Single(await GetStatusHistoriesAsync(lease.Id));
        Assert.Null(history.OldStatus);
        Assert.Equal(LeaseStatus.Draft, history.NewStatus);
        Assert.Equal(_actorId, history.ChangedBy);
    }

    [Fact]
    public async Task Activate_DraftWithRepresentative_ActivatesAndOccupiesRoom()
    {
        var draft = await CreateActivatableLeaseAsync();

        var response = await ActivateAsync(draft.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lease = await ReadLeaseAsync(response);
        Assert.Equal(LeaseStatus.Active, lease.Status);
        Assert.NotNull(lease.SignedAt);
        Assert.All(lease.Tenants, t => Assert.Equal(lease.StartDate, t.JoinedDate));
        Assert.Equal(RoomStatus.Occupied, await GetRoomStatusAsync(_roomId));

        var histories = await GetStatusHistoriesAsync(draft.Id);
        Assert.Collection(histories,
            h => Assert.Equal(((LeaseStatus?)null, LeaseStatus.Draft), (h.OldStatus, h.NewStatus)),
            h => Assert.Equal(((LeaseStatus?)LeaseStatus.Draft, LeaseStatus.Active), (h.OldStatus, h.NewStatus)));
    }

    [Fact]
    public async Task Activate_WithoutTenants_Returns400()
    {
        var draft = await CreateLeaseAsync(ValidCreateRequest() with { StartDate = Today });

        Assert.Equal(HttpStatusCode.BadRequest, (await ActivateAsync(draft.Id)).StatusCode);
    }

    [Fact]
    public async Task Activate_WithoutRepresentative_Returns400()
    {
        var draft = await CreateLeaseAsync(ValidCreateRequest() with
        {
            StartDate = Today,
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync() }]
        });

        Assert.Equal(HttpStatusCode.BadRequest, (await ActivateAsync(draft.Id)).StatusCode);
    }

    [Fact]
    public async Task Activate_RoomCapacityLoweredAfterDraft_Returns400()
    {
        var draft = await CreateActivatableLeaseAsync(tenantCount: 2);
        var patchRoom = await _client.PatchAsJsonAsync($"/api/rooms/{_roomId}", new UpdateRoomRequest { Capacity = 1 }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, patchRoom.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await ActivateAsync(draft.Id)).StatusCode);
    }

    [Fact]
    public async Task Activate_EndDateAlreadyPassed_Returns400()
    {
        var draft = await CreateActivatableLeaseAsync();
        await WithContextAsync(context => context.Leases
            .Where(l => l.Id == draft.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(l => l.StartDate, Today.AddMonths(-2))
                .SetProperty(l => l.EndDate, Today)));

        Assert.Equal(HttpStatusCode.BadRequest, (await ActivateAsync(draft.Id)).StatusCode);
    }

    [Fact]
    public async Task Activate_RoomUnderMaintenance_Returns409()
    {
        var draft = await CreateActivatableLeaseAsync();
        var patchRoom = await _client.PatchAsJsonAsync(
            $"/api/rooms/{_roomId}", new UpdateRoomRequest { RoomStatus = RoomStatus.Maintenance }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, patchRoom.StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await ActivateAsync(draft.Id)).StatusCode);
    }

    [Fact]
    public async Task Activate_RoomAlreadyHasActiveLease_Returns409()
    {
        await CreateActiveLeaseAsync();
        var second = await CreateActivatableLeaseAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await ActivateAsync(second.Id)).StatusCode);
    }

    [Fact]
    public async Task Activate_AlreadyActiveLease_Returns409()
    {
        var active = await CreateActiveLeaseAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await ActivateAsync(active.Id)).StatusCode);
    }

    [Fact]
    public async Task Activate_TenantUserDeactivatedAfterDraft_Returns400()
    {
        var draft = await CreateActivatableLeaseAsync();
        var userId = draft.Tenants.Single().UserId;
        await WithContextAsync(context => context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false)));

        Assert.Equal(HttpStatusCode.BadRequest, (await ActivateAsync(draft.Id)).StatusCode);
    }

    [Fact]
    public async Task Activate_TwoDraftsOfSameRoomConcurrently_OnlyOneSucceeds()
    {
        var first = await CreateActivatableLeaseAsync();
        var second = await CreateActivatableLeaseAsync();

        var responses = await Task.WhenAll(ActivateAsync(first.Id), ActivateAsync(second.Id));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Terminate_ActiveLease_FreesRoomAndMovesTenantsOut()
    {
        var active = await CreateActiveLeaseAsync(tenantCount: 2);

        var response = await TerminateAsync(active.Id, new TerminateLeaseRequest { Reason = "Contract broken" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lease = await ReadLeaseAsync(response);
        Assert.Equal(LeaseStatus.Terminated, lease.Status);
        Assert.NotNull(lease.TerminatedAt);
        Assert.Equal("Contract broken", lease.TerminationReason);
        Assert.All(lease.Tenants, t => Assert.Equal(Today, t.LeftDate));
        Assert.Equal(RoomStatus.Available, await GetRoomStatusAsync(_roomId));

        var last = (await GetStatusHistoriesAsync(active.Id)).Last();
        Assert.Equal(((LeaseStatus?)LeaseStatus.Active, LeaseStatus.Terminated), (last.OldStatus, last.NewStatus));
        Assert.Equal("Contract broken", last.Reason);
    }

    [Fact]
    public async Task Terminate_DraftLease_Returns409()
    {
        var draft = await CreateActivatableLeaseAsync();

        var response = await TerminateAsync(draft.Id, new TerminateLeaseRequest { Reason = "Never signed" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Terminate_WithoutReason_Returns400()
    {
        var active = await CreateActiveLeaseAsync();

        var response = await TerminateAsync(active.Id, new TerminateLeaseRequest { Reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task Terminate_LeftDateOutsideStartDateAndToday_Returns400(int daysFromToday)
    {
        var active = await CreateActiveLeaseAsync();

        var response = await TerminateAsync(
            active.Id, new TerminateLeaseRequest { Reason = "Moved out", LeftDate = Today.AddDays(daysFromToday) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ActiveLeaseContractTerms_Returns409()
    {
        var active = await CreateActiveLeaseAsync();

        var response = await PatchLeaseAsync(active.Id, new UpdateLeaseRequest { MonthlyRent = 5_000_000 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ActiveLeaseAddTenant_JoinsToday()
    {
        var active = await CreateActiveLeaseAsync();
        var newUserId = await CreateTenantUserAsync("Newcomer");

        var response = await PatchLeaseAsync(active.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { UserId = newUserId } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newcomer = (await ReadLeaseAsync(response)).Tenants.Single(t => t.UserId == newUserId);
        Assert.Equal(Today, newcomer.JoinedDate);
    }

    [Fact]
    public async Task LeaseActivatedBeforeStartDate_NewTenantJoinsAndTerminationEndsOnStartDate()
    {
        var startDate = Today.AddDays(10);
        var draft = await CreateLeaseAsync(ValidCreateRequest() with
        {
            StartDate = startDate,
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync(), IsRepresentative = true }]
        });
        Assert.Equal(HttpStatusCode.OK, (await ActivateAsync(draft.Id)).StatusCode);
        var newUserId = await CreateTenantUserAsync("Newcomer");

        var patch = await PatchLeaseAsync(draft.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { UserId = newUserId } }
        });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        Assert.Equal(startDate, (await ReadLeaseAsync(patch)).Tenants.Single(t => t.UserId == newUserId).JoinedDate);

        var terminate = await TerminateAsync(draft.Id, new TerminateLeaseRequest { Reason = "Backed out" });

        Assert.Equal(HttpStatusCode.OK, terminate.StatusCode);
        Assert.All((await ReadLeaseAsync(terminate)).Tenants, t => Assert.Equal(startDate, t.LeftDate));
        Assert.Equal(RoomStatus.Available, await GetRoomStatusAsync(_roomId));
    }

    [Fact]
    public async Task Terminate_TenantMovedOutAfterLeftDate_IsClampedToLeftDate()
    {
        var active = await CreateActiveLeaseAsync(tenantCount: 2);
        var roommate = active.Tenants.Single(t => !t.IsRepresentative);
        await WithContextAsync(async context =>
        {
            await context.Leases.Where(l => l.Id == active.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.StartDate, Today.AddDays(-10)));
            await context.LeaseTenants.Where(t => t.LeaseId == active.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.JoinedDate, Today.AddDays(-10)));
            await context.LeaseTenants.Where(t => t.Id == roommate.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.LeftDate, Today.AddDays(-2)));
        });

        var response = await TerminateAsync(
            active.Id, new TerminateLeaseRequest { Reason = "Ended early", LeftDate = Today.AddDays(-5) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All((await ReadLeaseAsync(response)).Tenants, t => Assert.Equal(Today.AddDays(-5), t.LeftDate));
    }

    [Fact]
    public async Task UpdateRoom_CapacityBelowCurrentTenantsOfActiveLease_Returns409()
    {
        await CreateActiveLeaseAsync(tenantCount: 2);

        var tooLow = await _client.PatchAsJsonAsync($"/api/rooms/{_roomId}", new UpdateRoomRequest { Capacity = 1 }, JsonOptions);
        var enough = await _client.PatchAsJsonAsync($"/api/rooms/{_roomId}", new UpdateRoomRequest { Capacity = 2 }, JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, tooLow.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enough.StatusCode);
    }

    [Fact]
    public async Task Update_ActiveLeaseUnchangedContractWithTenantChange_Returns200()
    {
        var active = await CreateActiveLeaseAsync();

        var response = await PatchLeaseAsync(active.Id, new UpdateLeaseRequest
        {
            LeaseCode = active.LeaseCode,
            StartDate = active.StartDate,
            EndDate = active.EndDate,
            MonthlyRent = active.MonthlyRent,
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { UserId = await CreateTenantUserAsync() } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_ActiveLeaseMovedOutTenantRejoins_CreatesNewStay()
    {
        var active = await CreateActiveLeaseAsync(tenantCount: 2);
        var roommate = active.Tenants.Single(t => !t.IsRepresentative);
        var moveOut = await PatchLeaseAsync(active.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { Id = roommate.Id, LeftDate = Today } }
        });
        Assert.Equal(HttpStatusCode.OK, moveOut.StatusCode);

        var rejoin = await PatchLeaseAsync(active.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { UserId = roommate.UserId } }
        });

        Assert.Equal(HttpStatusCode.OK, rejoin.StatusCode);
        var stays = (await ReadLeaseAsync(rejoin)).Tenants.Where(t => t.UserId == roommate.UserId).ToList();
        Assert.Equal(2, stays.Count);
        Assert.Single(stays, t => t.LeftDate is null);
    }

    [Fact]
    public async Task Update_ActiveLeaseTenantMovesOut_Returns200()
    {
        var active = await CreateActiveLeaseAsync(tenantCount: 2);
        var roommate = active.Tenants.Single(t => !t.IsRepresentative);

        var response = await PatchLeaseAsync(active.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { Id = roommate.Id, LeftDate = Today } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Today, (await ReadLeaseAsync(response)).Tenants.Single(t => t.Id == roommate.Id).LeftDate);
    }

    [Fact]
    public async Task Update_ActiveLeaseDeleteTenant_Returns409()
    {
        var active = await CreateActiveLeaseAsync(tenantCount: 2);
        var roommate = active.Tenants.Single(t => !t.IsRepresentative);

        var response = await PatchLeaseAsync(active.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { Id = roommate.Id, IsDeleted = true } }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ActiveLeaseRepresentativeMovesOutWithoutReplacement_Returns400()
    {
        var active = await CreateActiveLeaseAsync(tenantCount: 2);
        var representative = active.Tenants.Single(t => t.IsRepresentative);

        var response = await PatchLeaseAsync(active.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { Id = representative.Id, IsRepresentative = false, LeftDate = Today } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_ActiveLeaseRepresentativeMovesOutWithReplacement_Returns200()
    {
        var active = await CreateActiveLeaseAsync(tenantCount: 2);
        var representative = active.Tenants.Single(t => t.IsRepresentative);
        var roommate = active.Tenants.Single(t => !t.IsRepresentative);

        var response = await PatchLeaseAsync(active.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest>
            {
                new UpdateLeaseTenantRequest { Id = representative.Id, IsRepresentative = false, LeftDate = Today },
                new UpdateLeaseTenantRequest { Id = roommate.Id, IsRepresentative = true }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tenants = (await ReadLeaseAsync(response)).Tenants;
        Assert.True(tenants.Single(t => t.Id == roommate.Id).IsRepresentative);
        Assert.Equal(Today, tenants.Single(t => t.Id == representative.Id).LeftDate);
    }

    [Fact]
    public async Task Update_DraftLeaseTenantLeftDate_Returns400()
    {
        var draft = await CreateActivatableLeaseAsync();

        var response = await PatchLeaseAsync(draft.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { Id = draft.Tenants.Single().Id, LeftDate = Today } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TerminatedLease_CannotBeActivatedTerminatedOrEdited()
    {
        var active = await CreateActiveLeaseAsync();
        await TerminateAsync(active.Id, new TerminateLeaseRequest { Reason = "Done" });

        Assert.Equal(HttpStatusCode.Conflict, (await ActivateAsync(active.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await TerminateAsync(active.Id, new TerminateLeaseRequest { Reason = "Again" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PatchLeaseAsync(active.Id, new UpdateLeaseRequest
        {
            Tenants = new List<UpdateLeaseTenantRequest> { new UpdateLeaseTenantRequest { UserId = await CreateTenantUserAsync() } }
        })).StatusCode);
    }

    [Fact]
    public async Task Create_PreviousLeaseIsDraft_Returns400()
    {
        var previous = await CreateLeaseAsync(ValidCreateRequest());

        var response = await _client.PostAsJsonAsync(
            "/api/leases", ValidCreateRequest() with { PreviousLeaseId = previous.Id, StartDate = StartDate.AddYears(1) }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_RecordsStatusHistoriesInOrder()
    {
        var active = await CreateActiveLeaseAsync();
        await TerminateAsync(active.Id, new TerminateLeaseRequest { Reason = "Moved out" });

        var histories = await GetStatusHistoriesAsync(active.Id);

        Assert.Equal(
            [LeaseStatus.Draft, LeaseStatus.Active, LeaseStatus.Terminated],
            histories.Select(h => h.NewStatus));
    }

    [Fact]
    public async Task OrganizationStaffOfAnotherOrganization_CannotActivateTerminateOrReadHistories()
    {
        var (memberClient, _) = await CreateOrganizationScopedMemberAsync(RoleSlugs.OrganizationStaff);
        var draft = await CreateActivatableLeaseAsync();
        var active = await CreateLeaseAsync(ValidCreateRequest() with { StartDate = Today });
        await SetLeaseStatusAsync(active.Id, LeaseStatus.Active);

        Assert.Equal(HttpStatusCode.NotFound, (await ActivateAsync(draft.Id, memberClient)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await TerminateAsync(
            active.Id, new TerminateLeaseRequest { Reason = "Not mine" }, memberClient)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/leases/{draft.Id}/status-histories")).StatusCode);
    }

    [Fact]
    public async Task MemberWithoutActivatePermission_Activate_Returns403()
    {
        var (memberClient, memberOrganizationId) = await CreateOrganizationScopedMemberAsync(RoleSlugs.OrganizationStaff);
        var memberPropertyId = await CreatePropertyAsync(_client, memberOrganizationId, "Member Property");
        var memberRoomId = await CreateRoomAsync(_client, memberPropertyId, "301", monthlyRent: 2_000_000, depositAmount: 500_000);
        var draft = await CreateLeaseAsync(ValidCreateRequest() with
        {
            RoomId = memberRoomId,
            StartDate = Today,
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync(), IsRepresentative = true }]
        });

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = await context.Roles.SingleAsync(r => r.Slug == RoleSlugs.OrganizationStaff);
            await context.RolePermissions
                .Where(rp => rp.RoleId == role.Id
                    && rp.Permission!.Resource == PermissionResources.Lease
                    && rp.Permission.Action == PermissionActions.Activate)
                .ExecuteDeleteAsync();
            await scope.ServiceProvider.GetRequiredService<IRolePermissionCache>().InvalidateAsync(role.Id);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await ActivateAsync(draft.Id, memberClient)).StatusCode);
    }
}
