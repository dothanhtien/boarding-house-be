using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Leases;
using BoardingHouse.Api.DTOs.Properties;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.DTOs.TenantProfiles;
using BoardingHouse.Api.DTOs.Tenants;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.Api.Services;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardingHouse.IntegrationTests.Controllers;

public class TenantsControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private const string Url = "/api/tenants";
    private const string Password = "password123";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly HttpClient _admin = factory.CreateClient();

    private Guid _organizationA;
    private Guid _organizationB;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();

        var (adminId, _) = await RegisterAsync(_admin, "tenant-admin@test.com", "0900000090");
        await factory.GrantPlatformAdminRoleAsync(adminId);
        await LoginAsync(_admin, new LoginRequest { Identifier = "tenant-admin@test.com", Password = Password });

        _organizationA = await CreateOrganizationAsync("Org A");
        _organizationB = await CreateOrganizationAsync("Org B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string ExtractAccessTokenCookie(HttpResponseMessage response)
    {
        var setCookieHeader = response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("accessToken=", StringComparison.Ordinal));

        return setCookieHeader.Split(';')[0].Substring("accessToken=".Length);
    }

    private static async Task<(Guid Id, string Email)> RegisterAsync(HttpClient client, string email, string phone)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = phone,
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Registered User"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!.Id, email);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, LoginRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", request);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(response));
        }

        return response;
    }

    private async Task WithContextAsync(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Guid> CreateOrganizationAsync(string name)
    {
        var organization = new Organization { Name = name, CreatedBy = SentinelActors.System };
        await WithContextAsync(async context =>
        {
            context.Organizations.Add(organization);
            await context.SaveChangesAsync();
        });
        return organization.Id;
    }

    // Logged-in member of `organizationId` holding the given organization-scoped role.
    private async Task<HttpClient> CreateStaffClientAsync(Guid organizationId, string roleSlug = RoleSlugs.OrganizationStaff)
    {
        var client = factory.CreateClient();
        var email = $"staff-{Guid.NewGuid():N}@test.com";
        var (userId, _) = await RegisterAsync(client, email, $"09{Random.Shared.Next(10000000, 99999999)}");

        await WithContextAsync(async context =>
        {
            await RbacSeeder.SeedAsync(context);
            var role = await context.Roles.SingleAsync(r => r.Slug == roleSlug);
            context.OrganizationMembers.Add(new OrganizationMember
            {
                OrganizationId = organizationId,
                UserId = userId,
                RoleId = role.Id,
                CreatedBy = SentinelActors.System
            });
            await context.SaveChangesAsync();
        });

        await LoginAsync(client, new LoginRequest { Identifier = email, Password = Password });
        return client;
    }

    private static CreateTenantRequest ValidCreateRequest(Guid organizationId, string phone = "0912345678") => new()
    {
        OrganizationId = organizationId,
        FullName = "Nguyen Van A",
        Phone = phone,
        Password = Password,
        PasswordConfirmation = Password
    };

    private static Task<HttpResponseMessage> PostTenantAsync(HttpClient client, CreateTenantRequest request) =>
        client.PostAsJsonAsync(Url, request, JsonOptions);

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    private static async Task<TenantResponse> CreateTenantAsync(HttpClient client, CreateTenantRequest request)
    {
        var response = await PostTenantAsync(client, request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadDataAsync<TenantResponse>(response);
    }

    private async Task<Guid> CreateRoomAsync(Guid organizationId, string roomNumber)
    {
        var propertyResponse = await _admin.PostAsJsonAsync("/api/properties", new CreatePropertyRequest
        {
            OrganizationId = organizationId,
            Name = $"Property {Guid.NewGuid():N}"
        });
        Assert.Equal(HttpStatusCode.Created, propertyResponse.StatusCode);
        var propertyId = (await ReadDataAsync<PropertyResponse>(propertyResponse)).Id;

        var roomResponse = await _admin.PostAsJsonAsync("/api/rooms", new CreateRoomRequest
        {
            PropertyId = propertyId,
            RoomNumber = roomNumber,
            MonthlyRent = 3_000_000,
            DepositAmount = 2_000_000
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, roomResponse.StatusCode);
        return (await ReadDataAsync<RoomResponse>(roomResponse)).Id;
    }

    private static async Task<HttpResponseMessage> CreateDraftLeaseAsync(HttpClient client, Guid roomId, Guid tenantUserId) =>
        await client.PostAsJsonAsync("/api/leases", new CreateLeaseRequest
        {
            RoomId = roomId,
            StartDate = new DateOnly(2026, 11, 1),
            Tenants = [new CreateLeaseTenantRequest { UserId = tenantUserId, IsRepresentative = true }]
        }, JsonOptions);

    [Fact]
    public async Task Create_WithoutEmail_Returns201_AndTenantCanLogInByPhone()
    {
        var staff = await CreateStaffClientAsync(_organizationA);

        var response = await PostTenantAsync(staff, ValidCreateRequest(_organizationA, phone: "+84 912.345.678") with
        {
            Profile = new UpdateTenantProfileRequest
            {
                IdentityType = IdentityType.CitizenIdentityCard,
                IdentityNumber = "001095012345",
                DateOfBirth = new DateOnly(1995, 6, 20)
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tenant = await ReadDataAsync<TenantResponse>(response);
        Assert.Equal($"/api/tenants/{tenant.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Null(tenant.Email);
        Assert.Equal("0912345678", tenant.Phone);
        Assert.Equal("001095012345", tenant.Profile!.IdentityNumber);
        Assert.Empty(tenant.Leases);

        await WithContextAsync(async context =>
        {
            Assert.False(await context.UserRoles.AnyAsync(ur => ur.UserId == tenant.Id));
            Assert.False(await context.OrganizationMembers.AnyAsync(m => m.UserId == tenant.Id));
        });

        var tenantClient = factory.CreateClient();
        var loginResponse = await LoginAsync(tenantClient, new LoginRequest { Identifier = "0912 345 678", Password = Password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var me = await ReadDataAsync<UserResponse>(await tenantClient.GetAsync("/api/auth/me"));
        Assert.Equal(tenant.Id, me.Id);
        Assert.Null(me.Email);
    }

    [Fact]
    public async Task Create_WithEmail_StoresItLowercased()
    {
        var tenant = await CreateTenantAsync(_admin, ValidCreateRequest(_organizationA) with { Email = " Tenant@Test.COM " });

        Assert.Equal("tenant@test.com", tenant.Email);
    }

    [Fact]
    public async Task Create_PhoneAlreadyUsed_Returns409()
    {
        await CreateTenantAsync(_admin, ValidCreateRequest(_organizationA, phone: "0912345678"));

        var response = await PostTenantAsync(_admin, ValidCreateRequest(_organizationB, phone: "+84912345678"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_EmailAlreadyUsedBySelfRegisteredUser_Returns409()
    {
        await RegisterAsync(factory.CreateClient(), "taken@test.com", "0900000001");

        var response = await PostTenantAsync(_admin, ValidCreateRequest(_organizationA) with { Email = "TAKEN@test.com" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ProfileIdentityTaken_Returns409_AndDoesNotCreateUser()
    {
        var profile = new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.CitizenIdentityCard,
            IdentityNumber = "001095012345"
        };
        await CreateTenantAsync(_admin, ValidCreateRequest(_organizationA, phone: "0911111111") with { Profile = profile });

        var response = await PostTenantAsync(_admin, ValidCreateRequest(_organizationA, phone: "0922222222") with { Profile = profile });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await WithContextAsync(async context =>
            Assert.False(await context.Users.AnyAsync(u => u.Phone == "0922222222")));
    }

    [Fact]
    public async Task Create_EmptyProfile_Returns400_AndDoesNotCreateUser()
    {
        var response = await PostTenantAsync(_admin, ValidCreateRequest(_organizationA) with { Profile = new UpdateTenantProfileRequest() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await WithContextAsync(async context =>
            Assert.False(await context.Users.AnyAsync(u => u.Phone == "0912345678")));
    }

    [Fact]
    public async Task Create_OrganizationOutsideScope_Returns404()
    {
        var staffOfA = await CreateStaffClientAsync(_organizationA);

        var response = await PostTenantAsync(staffOfA, ValidCreateRequest(_organizationB));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_OrganizationFromHeader_UsesIt()
    {
        var staffOfA = await CreateStaffClientAsync(_organizationA);
        var request = new HttpRequestMessage(HttpMethod.Post, Url)
        {
            Content = JsonContent.Create(ValidCreateRequest(_organizationA) with { OrganizationId = null }, options: JsonOptions)
        };
        request.Headers.Add("X-Organization-Id", _organizationA.ToString());

        var response = await staffOfA.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutOrganization_Returns400()
    {
        var response = await PostTenantAsync(_admin, ValidCreateRequest(_organizationA) with { OrganizationId = null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("0912345678")]
    [InlineData("%2B84912345678")]
    [InlineData("%2B84%20912.345.678")]
    public async Task Lookup_ByPhoneInAnyFormat_ReturnsMaskedTenant(string phoneQuery)
    {
        var tenant = await CreateTenantAsync(_admin, ValidCreateRequest(_organizationA) with { Email = "nguyenvana@gmail.com" });
        var staffOfB = await CreateStaffClientAsync(_organizationB);

        var response = await staffOfB.GetAsync($"{Url}/lookup?phone={phoneQuery}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var found = await ReadDataAsync<TenantLookupResponse>(response);
        Assert.Equal(tenant.Id, found.Id);
        Assert.Equal("Nguyen Van A", found.FullName);
        Assert.Equal("091****678", found.MaskedPhone);
        Assert.Equal("ng***@gmail.com", found.MaskedEmail);
        Assert.False(found.HasTenantProfile);
        Assert.True(found.IsActive);

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("0912345678", json);
        Assert.DoesNotContain("nguyenvana", json);
    }

    [Fact]
    public async Task Lookup_ByEmailIgnoringCase_ReturnsTenantWithProfileFlag()
    {
        await CreateTenantAsync(_admin, ValidCreateRequest(_organizationA) with
        {
            Email = "tenant@test.com",
            Profile = new UpdateTenantProfileRequest { PermanentAddress = "12 Nguyen Trai" }
        });

        var response = await _admin.GetAsync($"{Url}/lookup?email=TENANT@test.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ReadDataAsync<TenantLookupResponse>(response)).HasTenantProfile);
    }

    [Fact]
    public async Task Lookup_UnknownPhone_Returns404()
    {
        var response = await _admin.GetAsync($"{Url}/lookup?phone=0999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Lookup_PartialPhone_Returns404()
    {
        await CreateTenantAsync(_admin, ValidCreateRequest(_organizationA));

        var response = await _admin.GetAsync($"{Url}/lookup?phone=0912345");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?phone=0912345678&email=tenant@test.com")]
    [InlineData("?phone=abc")]
    public async Task Lookup_InvalidQuery_Returns400(string queryString)
    {
        var response = await _admin.GetAsync($"{Url}/lookup{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_TenantOnlyRentingInAnotherOrganization_Returns404()
    {
        var tenant = await CreateTenantAsync(_admin, ValidCreateRequest(_organizationB));
        var roomInB = await CreateRoomAsync(_organizationB, "B-101");
        Assert.Equal(HttpStatusCode.Created, (await CreateDraftLeaseAsync(_admin, roomInB, tenant.Id)).StatusCode);
        var staffOfA = await CreateStaffClientAsync(_organizationA);

        var getResponse = await staffOfA.GetAsync($"{Url}/{tenant.Id}");
        var patchResponse = await staffOfA.PatchAsJsonAsync(
            $"{Url}/{tenant.Id}/profile", new UpdateTenantProfileRequest { PermanentAddress = "Hacked" }, JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, patchResponse.StatusCode);
    }

    [Fact]
    public async Task Get_TenantCreatedByOwnOrganization_Returns404UntilAddedToLease()
    {
        var staffOfA = await CreateStaffClientAsync(_organizationA);
        var tenant = await CreateTenantAsync(staffOfA, ValidCreateRequest(_organizationA));

        Assert.Equal(HttpStatusCode.NotFound, (await staffOfA.GetAsync($"{Url}/{tenant.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staffOfA.PatchAsJsonAsync(
            $"{Url}/{tenant.Id}/profile", new UpdateTenantProfileRequest { PermanentAddress = "x" }, JsonOptions)).StatusCode);

        var roomInA = await CreateRoomAsync(_organizationA, "A-101");
        Assert.Equal(HttpStatusCode.Created, (await CreateDraftLeaseAsync(staffOfA, roomInA, tenant.Id)).StatusCode);

        var response = await staffOfA.GetAsync($"{Url}/{tenant.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await ReadDataAsync<TenantResponse>(response);
        Assert.Equal("0912345678", fetched.Phone);
        Assert.Null(fetched.Profile);
        Assert.Single(fetched.Leases);
    }

    [Fact]
    public async Task Get_TenantOfDraftLease_ListsOnlyLeasesInScope()
    {
        var staffOfA = await CreateStaffClientAsync(_organizationA);
        var staffOfB = await CreateStaffClientAsync(_organizationB);
        var tenant = await CreateTenantAsync(staffOfA, ValidCreateRequest(_organizationA));

        Assert.Equal(HttpStatusCode.NotFound, (await staffOfB.GetAsync($"{Url}/{tenant.Id}")).StatusCode);

        var roomInA = await CreateRoomAsync(_organizationA, "A-101");
        var roomInB = await CreateRoomAsync(_organizationB, "B-201");
        Assert.Equal(HttpStatusCode.Created, (await CreateDraftLeaseAsync(_admin, roomInA, tenant.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateDraftLeaseAsync(_admin, roomInB, tenant.Id)).StatusCode);

        var seenByB = await ReadDataAsync<TenantResponse>(await staffOfB.GetAsync($"{Url}/{tenant.Id}"));
        var lease = Assert.Single(seenByB.Leases);
        Assert.Equal(_organizationB, lease.OrganizationId);
        Assert.Equal("B-201", lease.RoomNumber);
        Assert.Equal(LeaseStatus.Draft, lease.Status);

        var seenByAdmin = await ReadDataAsync<TenantResponse>(await _admin.GetAsync($"{Url}/{tenant.Id}"));
        Assert.Equal(2, seenByAdmin.Leases.Count);
    }

    [Fact]
    public async Task Get_UnknownUser_Returns404()
    {
        var response = await _admin.GetAsync($"{Url}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProfile_TenantInScope_UpsertsProfileWithPartialSemantics()
    {
        var staffOfA = await CreateStaffClientAsync(_organizationA);
        var tenant = await CreateTenantAsync(staffOfA, ValidCreateRequest(_organizationA));
        var roomInA = await CreateRoomAsync(_organizationA, "A-103");
        Assert.Equal(HttpStatusCode.Created, (await CreateDraftLeaseAsync(staffOfA, roomInA, tenant.Id)).StatusCode);

        var created = await staffOfA.PatchAsJsonAsync($"{Url}/{tenant.Id}/profile", new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.Passport,
            IdentityNumber = "b1234567",
            EmergencyContactName = "Mother"
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        var updated = await staffOfA.PatchAsJsonAsync(
            $"{Url}/{tenant.Id}/profile", new UpdateTenantProfileRequest { EmergencyContactName = null }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var profile = await ReadDataAsync<TenantProfileResponse>(updated);
        Assert.Equal(tenant.Id, profile.UserId);
        Assert.Equal("B1234567", profile.IdentityNumber);
        Assert.Null(profile.EmergencyContactName);
    }

    [Fact]
    public async Task UpdateProfile_TenantAddedToOwnLease_Returns200()
    {
        var tenant = await CreateTenantAsync(_admin, ValidCreateRequest(_organizationB));
        var roomInA = await CreateRoomAsync(_organizationA, "A-102");
        Assert.Equal(HttpStatusCode.Created, (await CreateDraftLeaseAsync(_admin, roomInA, tenant.Id)).StatusCode);
        var staffOfA = await CreateStaffClientAsync(_organizationA);

        var response = await staffOfA.PatchAsJsonAsync(
            $"{Url}/{tenant.Id}/profile", new UpdateTenantProfileRequest { PermanentAddress = "12 Nguyen Trai" }, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutRole_Returns403OnEveryEndpoint()
    {
        var tenant = await CreateTenantAsync(_admin, ValidCreateRequest(_organizationA));
        var tenantClient = factory.CreateClient();
        await LoginAsync(tenantClient, new LoginRequest { Identifier = "0912345678", Password = Password });

        var responses = new[]
        {
            await tenantClient.GetAsync($"{Url}/lookup?phone=0912345678"),
            await tenantClient.GetAsync($"{Url}/{tenant.Id}"),
            await PostTenantAsync(tenantClient, ValidCreateRequest(_organizationA, phone: "0933333333")),
            await tenantClient.PatchAsJsonAsync(
                $"{Url}/{tenant.Id}/profile", new UpdateTenantProfileRequest { PermanentAddress = "x" }, JsonOptions)
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var response = await factory.CreateClient().GetAsync($"{Url}/lookup?phone=0912345678");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreatedTenant_CanBeAddedToDraftLeaseByStaff()
    {
        var staffOfA = await CreateStaffClientAsync(_organizationA);
        var roomInA = await CreateRoomAsync(_organizationA, "A-301");

        var tenant = await CreateTenantAsync(staffOfA, ValidCreateRequest(_organizationA));
        var found = await ReadDataAsync<TenantLookupResponse>(await staffOfA.GetAsync($"{Url}/lookup?phone=0912345678"));
        var leaseResponse = await CreateDraftLeaseAsync(staffOfA, roomInA, found.Id);

        Assert.Equal(HttpStatusCode.Created, leaseResponse.StatusCode);
        var lease = await ReadDataAsync<LeaseResponse>(leaseResponse);
        var leaseTenant = Assert.Single(lease.Tenants);
        Assert.Equal(tenant.Id, leaseTenant.UserId);
        Assert.Equal("0912345678", leaseTenant.Phone);
    }

    [Fact]
    public async Task Lookup_OverLimit_Returns429()
    {
        var staffOfA = await CreateStaffClientAsync(_organizationA);

        for (var i = 0; i < TenantService.LookupLimit; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await staffOfA.GetAsync($"{Url}/lookup?phone=0999999999")).StatusCode);
        }

        var response = await staffOfA.GetAsync($"{Url}/lookup?phone=0999999999");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
    }
}
