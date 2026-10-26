using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Api.Common;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Seed;
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
using BoardingHouse.Application.DTOs.UtilityServices;

namespace BoardingHouse.IntegrationTests.Controllers;

public class LeaseUtilityServicesControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "lease-utility-actor@test.com";
    private const string Password = "password123";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private Guid _actorId;
    private Guid _organizationId;
    private Guid _propertyId;
    private Guid _roomId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        _organizationId = await CreateOrganizationAsync("Lease Utility Org");
        _propertyId = await CreatePropertyAsync(_organizationId, "Lease Utility Property");
        _roomId = await CreateRoomAsync(_propertyId, "101");
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
            Phone = "0900000091",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Lease Utility Actor"
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
        return (await response.Content.ReadFromJsonAsync<ApiResponse<OrganizationResponse>>())!.Data!.Id;
    }

    private async Task<Guid> CreatePropertyAsync(Guid organizationId, string name)
    {
        var response = await _client.PostAsJsonAsync("/api/properties", new CreatePropertyRequest { OrganizationId = organizationId, Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())!.Data!.Id;
    }

    private async Task<Guid> CreateRoomAsync(Guid propertyId, string roomNumber)
    {
        var response = await _client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest
        {
            PropertyId = propertyId,
            RoomNumber = roomNumber,
            MonthlyRent = 3_000_000,
            DepositAmount = 2_000_000,
            Capacity = 3
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!.Id;
    }

    private async Task<UtilityServiceResponse> CreateUtilityServiceAsync(
        string name,
        decimal defaultUnitPrice = 100_000,
        UtilityBillingMethod billingMethod = UtilityBillingMethod.Fixed,
        bool isActive = true,
        Guid? propertyId = null)
    {
        var response = await _client.PostAsJsonAsync("/api/utility-services", new CreateUtilityServiceRequest
        {
            PropertyId = propertyId ?? _propertyId,
            Name = name,
            Type = UtilityType.Other,
            BillingMethod = billingMethod,
            Unit = "month",
            DefaultUnitPrice = defaultUnitPrice,
            IsActive = isActive
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<UtilityServiceResponse>>(JsonOptions))!.Data!;
    }

    private async Task<Guid> CreateTenantUserAsync()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"tenant-{Guid.NewGuid():N}@test.com",
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Tenant"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!.Id;
    }

    private Task<HttpResponseMessage> PostLeaseAsync(
        List<CreateLeaseUtilityServiceRequest>? utilityServices, List<CreateLeaseTenantRequest>? tenants = null) =>
        _client.PostAsJsonAsync("/api/leases", new CreateLeaseRequest
        {
            RoomId = _roomId,
            StartDate = Today,
            Tenants = tenants,
            UtilityServices = utilityServices
        }, JsonOptions);

    private async Task<LeaseResponse> CreateLeaseAsync(
        List<UtilityServiceResponse>? utilityServices = null, List<CreateLeaseTenantRequest>? tenants = null)
    {
        var response = await PostLeaseAsync(
            utilityServices?.Select(s => new CreateLeaseUtilityServiceRequest { ServiceId = s.Id }).ToList(), tenants);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<LeaseResponse>>(JsonOptions))!.Data!;
    }

    private async Task<LeaseResponse> CreateActiveLeaseAsync(List<UtilityServiceResponse>? utilityServices = null)
    {
        var lease = await CreateLeaseAsync(
            utilityServices,
            [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync(), IsRepresentative = true }]);
        var response = await _client.PostAsync($"/api/leases/{lease.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return lease;
    }

    private async Task<List<LeaseUtilityServiceResponse>> GetLeaseServicesAsync(Guid leaseId, HttpClient? client = null)
    {
        var response = await (client ?? _client).GetAsync($"/api/leases/{leaseId}/utility-services");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<List<LeaseUtilityServiceResponse>>>(JsonOptions))!.Data!;
    }

    private Task<HttpResponseMessage> PutAsync(Guid leaseId, Guid serviceId, UpsertLeaseUtilityServiceRequest request, HttpClient? client = null) =>
        (client ?? _client).PutAsJsonAsync($"/api/leases/{leaseId}/utility-services/{serviceId}", request, JsonOptions);

    private static async Task<LeaseUtilityServiceResponse> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<LeaseUtilityServiceResponse>>(JsonOptions))!.Data!;

    private Task<HttpResponseMessage> PatchUtilityServiceAsync(Guid serviceId, UpdateUtilityServiceRequest request) =>
        _client.PatchAsJsonAsync($"/api/utility-services/{serviceId}", request, JsonOptions);

    private async Task SetLeaseStatusAsync(Guid leaseId, LeaseStatus status)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Leases
            .Where(l => l.Id == leaseId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, status));
    }

    private async Task<HttpClient> CreateOtherOrganizationMemberClientAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await RbacSeeder.SeedAsync(context);

        var organization = new Organization { Name = $"Other Org {Guid.NewGuid()}", CreatedBy = SentinelActors.System };
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
            FullName = "Other Org Member"
        });
        var member = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        var role = await context.Roles.SingleAsync(r => r.Slug == RoleSlugs.OrganizationAdmin);
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

        return memberClient;
    }

    [Fact]
    public async Task CreateLease_AttachesOnlySelectedServices()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered);
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        await CreateUtilityServiceAsync("Parking", 150_000);

        var response = await PostLeaseAsync(
        [
            new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id },
            new CreateLeaseUtilityServiceRequest { ServiceId = internet.Id, UnitPriceOverride = 80_000 }
        ]);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var lease = (await response.Content.ReadFromJsonAsync<ApiResponse<LeaseResponse>>(JsonOptions))!.Data!;

        var services = await GetLeaseServicesAsync(lease.Id);
        Assert.Equal([electricity.Id, internet.Id], services.Select(s => s.ServiceId));

        var attachedElectricity = services[0];
        Assert.Equal("Electricity", attachedElectricity.Name);
        Assert.Equal(UtilityBillingMethod.Metered, attachedElectricity.BillingMethod);
        Assert.Equal(3_500, attachedElectricity.DefaultUnitPrice);
        Assert.Null(attachedElectricity.UnitPriceOverride);
        Assert.Equal(3_500, attachedElectricity.EffectiveUnitPrice);
        Assert.Equal(1, attachedElectricity.Quantity);
        Assert.True(attachedElectricity.IsActive);
        Assert.True(attachedElectricity.ServiceIsActive);

        var attachedInternet = services[1];
        Assert.Equal(80_000, attachedInternet.UnitPriceOverride);
        Assert.Equal(80_000, attachedInternet.EffectiveUnitPrice);
    }

    [Fact]
    public async Task CreateLease_QuantityOnFixedService_IsStored()
    {
        var airConditioner = await CreateUtilityServiceAsync("Air conditioner", 150_000);

        var response = await PostLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = airConditioner.Id, Quantity = 2 }]);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var lease = (await response.Content.ReadFromJsonAsync<ApiResponse<LeaseResponse>>(JsonOptions))!.Data!;

        Assert.Equal(2, Assert.Single(await GetLeaseServicesAsync(lease.Id)).Quantity);
    }

    [Theory]
    [InlineData(UtilityBillingMethod.Metered)]
    [InlineData(UtilityBillingMethod.PerTenant)]
    public async Task CreateLease_QuantityOnNonFixedService_Returns400(UtilityBillingMethod billingMethod)
    {
        var water = await CreateUtilityServiceAsync("Water", 15_000, billingMethod);

        var response = await PostLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = water.Id, Quantity = 2 }]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLease_NoServicesSelected_AttachesNone()
    {
        await CreateUtilityServiceAsync("Internet", 100_000);

        var lease = await CreateLeaseAsync();

        Assert.Empty(await GetLeaseServicesAsync(lease.Id));
    }

    [Fact]
    public async Task CreateLease_InactiveService_Returns400()
    {
        var garbage = await CreateUtilityServiceAsync("Garbage", 20_000, isActive: false);

        var response = await PostLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = garbage.Id }]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLease_ServiceOfAnotherProperty_Returns400()
    {
        var otherPropertyId = await CreatePropertyAsync(_organizationId, "Other Property");
        var water = await CreateUtilityServiceAsync("Water", 15_000, propertyId: otherPropertyId);

        var response = await PostLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = water.Id }]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLease_UnknownService_Returns400()
    {
        var response = await PostLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = Guid.NewGuid() }]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_OverridePrice_ChangesEffectiveUnitPrice()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync([internet]);

        var response = await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = 80_000m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadAsync(response);
        Assert.Equal(80_000, updated.UnitPriceOverride);
        Assert.Equal(80_000, updated.EffectiveUnitPrice);
        Assert.NotNull(updated.UpdatedAt);

        var cleared = await ReadAsync(await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = null }));
        Assert.Null(cleared.UnitPriceOverride);
        Assert.Equal(100_000, cleared.EffectiveUnitPrice);
    }

    [Fact]
    public async Task Put_OmittedFields_AreLeftUnchanged()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync();
        await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = 80_000m });

        var response = await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { IsActive = false });

        var updated = await ReadAsync(response);
        Assert.False(updated.IsActive);
        Assert.Equal(80_000, updated.UnitPriceOverride);
    }

    [Fact]
    public async Task Put_ServiceAddedAfterLease_AttachesWithDefaultPrice()
    {
        var lease = await CreateLeaseAsync();
        var parking = await CreateUtilityServiceAsync("Parking", 150_000);

        var response = await PutAsync(lease.Id, parking.Id, new UpsertLeaseUtilityServiceRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attached = await ReadAsync(response);
        Assert.Null(attached.UnitPriceOverride);
        Assert.Equal(150_000, attached.EffectiveUnitPrice);
    }

    [Fact]
    public async Task Put_ServiceOfAnotherProperty_Returns400()
    {
        var otherPropertyId = await CreatePropertyAsync(_organizationId, "Other Property");
        var otherService = await CreateUtilityServiceAsync("Internet", 100_000, propertyId: otherPropertyId);
        var lease = await CreateLeaseAsync();

        var response = await PutAsync(lease.Id, otherService.Id, new UpsertLeaseUtilityServiceRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_InactiveServiceNotYetAttached_Returns400()
    {
        var garbage = await CreateUtilityServiceAsync("Garbage", 20_000, isActive: false);
        var lease = await CreateLeaseAsync();

        var response = await PutAsync(lease.Id, garbage.Id, new UpsertLeaseUtilityServiceRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_AttachedServiceDeactivatedInProperty_CanStillBeUpdated()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync([internet]);
        await PatchUtilityServiceAsync(internet.Id, new UpdateUtilityServiceRequest { IsActive = false });

        var response = await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = 90_000m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await ReadAsync(response)).ServiceIsActive);
    }

    [Fact]
    public async Task GetAll_AttachedServiceSoftDeletedInProperty_IsStillListed()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync([internet]);
        Assert.Equal(HttpStatusCode.OK,
            (await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { IsActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/utility-services/{internet.Id}")).StatusCode);

        var services = await GetLeaseServicesAsync(lease.Id);

        var attached = Assert.Single(services);
        Assert.Equal(internet.Id, attached.ServiceId);
        Assert.Equal(100_000, attached.EffectiveUnitPrice);
    }

    [Fact]
    public async Task DeleteService_UsedByDraftLease_Returns409()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        await CreateLeaseAsync([internet]);

        var response = await _client.DeleteAsync($"/api/utility-services/{internet.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/utility-services/{internet.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteService_UsedByActiveLease_Returns409()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        await CreateActiveLeaseAsync([internet]);

        var response = await _client.DeleteAsync($"/api/utility-services/{internet.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteService_DeactivatedOnActiveLease_Returns204()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateActiveLeaseAsync([internet]);
        Assert.Equal(HttpStatusCode.OK,
            (await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { IsActive = false })).StatusCode);

        var response = await _client.DeleteAsync($"/api/utility-services/{internet.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(LeaseStatus.Terminated)]
    [InlineData(LeaseStatus.Expired)]
    public async Task DeleteService_UsedOnlyByClosedLease_Returns204(LeaseStatus status)
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync([internet]);
        await SetLeaseStatusAsync(lease.Id, status);

        var response = await _client.DeleteAsync($"/api/utility-services/{internet.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(await GetLeaseServicesAsync(lease.Id));
    }

    [Fact]
    public async Task DeleteService_UsedOnlyBySoftDeletedLease_Returns204()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync([internet]);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/leases/{lease.Id}")).StatusCode);

        var response = await _client.DeleteAsync($"/api/utility-services/{internet.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Put_Quantity_UpdatesQuantityAndKeepsOverride()
    {
        var airConditioner = await CreateUtilityServiceAsync("Air conditioner", 150_000);
        var lease = await CreateLeaseAsync([airConditioner]);
        await PutAsync(lease.Id, airConditioner.Id, new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = 120_000m });

        var response = await PutAsync(lease.Id, airConditioner.Id, new UpsertLeaseUtilityServiceRequest { Quantity = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadAsync(response);
        Assert.Equal(3, updated.Quantity);
        Assert.Equal(120_000, updated.UnitPriceOverride);
    }

    [Fact]
    public async Task Put_QuantityOnMeteredService_Returns400()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered);
        var lease = await CreateLeaseAsync([electricity]);

        var response = await PutAsync(lease.Id, electricity.Id, new UpsertLeaseUtilityServiceRequest { Quantity = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, Assert.Single(await GetLeaseServicesAsync(lease.Id)).Quantity);
    }

    [Fact]
    public async Task Put_ZeroQuantity_Returns400()
    {
        var airConditioner = await CreateUtilityServiceAsync("Air conditioner", 150_000);
        var lease = await CreateLeaseAsync([airConditioner]);

        var response = await PutAsync(lease.Id, airConditioner.Id, new UpsertLeaseUtilityServiceRequest { Quantity = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_NegativeOverride_Returns400()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync();

        var response = await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { UnitPriceOverride = -1m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UnknownService_Returns404()
    {
        var lease = await CreateLeaseAsync();

        var response = await PutAsync(lease.Id, Guid.NewGuid(), new UpsertLeaseUtilityServiceRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActiveLease_DeactivateService_Returns200()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateActiveLeaseAsync([internet]);

        var response = await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { IsActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(Assert.Single(await GetLeaseServicesAsync(lease.Id)).IsActive);
    }

    [Fact]
    public async Task ActiveLease_Delete_Returns409()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateActiveLeaseAsync([internet]);

        var response = await _client.DeleteAsync($"/api/leases/{lease.Id}/utility-services/{internet.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await GetLeaseServicesAsync(lease.Id));
    }

    [Fact]
    public async Task DraftLease_Delete_RemovesTheService()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync([internet]);

        var response = await _client.DeleteAsync($"/api/leases/{lease.Id}/utility-services/{internet.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await GetLeaseServicesAsync(lease.Id));
    }

    [Fact]
    public async Task DraftLease_DeleteServiceNotAttached_Returns404()
    {
        var lease = await CreateLeaseAsync();

        var response = await _client.DeleteAsync($"/api/leases/{lease.Id}/utility-services/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(LeaseStatus.Terminated)]
    [InlineData(LeaseStatus.Expired)]
    public async Task ClosedLease_Put_Returns409(LeaseStatus status)
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync([internet]);
        await SetLeaseStatusAsync(lease.Id, status);

        var response = await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { IsActive = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBillingMethod_ServiceUsedByLease_Returns409()
    {
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered);
        await CreateLeaseAsync([water]);

        var response = await PatchUtilityServiceAsync(water.Id, new UpdateUtilityServiceRequest { BillingMethod = UtilityBillingMethod.PerTenant });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBillingMethod_SameValueOnUsedService_Returns200()
    {
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered);
        await CreateLeaseAsync([water]);

        var response = await PatchUtilityServiceAsync(water.Id, new UpdateUtilityServiceRequest { BillingMethod = UtilityBillingMethod.Metered });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBillingMethod_OnlyUsedBySoftDeletedLease_Returns200()
    {
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered);
        var lease = await CreateLeaseAsync([water]);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/leases/{lease.Id}")).StatusCode);

        var response = await PatchUtilityServiceAsync(water.Id, new UpdateUtilityServiceRequest { BillingMethod = UtilityBillingMethod.PerTenant });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OtherOrganizationMember_Returns404()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000);
        var lease = await CreateLeaseAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await otherClient.GetAsync($"/api/leases/{lease.Id}/utility-services")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PutAsync(lease.Id, internet.Id, new UpsertLeaseUtilityServiceRequest { IsActive = false }, otherClient)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await otherClient.DeleteAsync($"/api/leases/{lease.Id}/utility-services/{internet.Id}")).StatusCode);
    }

    [Fact]
    public async Task Get_UnknownLease_Returns404()
    {
        var response = await _client.GetAsync($"/api/leases/{Guid.NewGuid()}/utility-services");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        var response = await factory.CreateClient().GetAsync($"/api/leases/{Guid.NewGuid()}/utility-services");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
