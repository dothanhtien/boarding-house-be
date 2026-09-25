using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.DTOs.Properties;
using BoardingHouse.Api.DTOs.UtilityServices;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardingHouse.IntegrationTests.Controllers;

public class UtilityServicesControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "utility-service-actor@test.com";
    private const string ActorPassword = "password123";

    private Guid _actorId;
    private Guid _organizationId;
    private Guid _propertyId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        _organizationId = await CreateOrganizationAsync("Utility Service Org");
        _propertyId = await CreatePropertyAsync(_client, _organizationId, "Utility Service Property");
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
            Phone = "0900000093",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "Utility Service Actor"
        });
        var actor = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        await factory.GrantPlatformAdminRoleAsync(actor!.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = ActorEmail,
            Password = ActorPassword
        });

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
        var property = (await response.Content.ReadFromJsonAsync<ApiResponse<PropertyResponse>>())?.Data;
        return property!.Id;
    }

    private CreateUtilityServiceRequest ValidCreateRequest() => new()
    {
        PropertyId = _propertyId,
        Name = "Electricity",
        Type = UtilityType.Electricity,
        Unit = "kWh",
        DefaultUnitPrice = 3500
    };

    private async Task<UtilityServiceResponse> CreateUtilityServiceAsync(HttpClient client, CreateUtilityServiceRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/utility-services", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<UtilityServiceResponse>>(JsonOptions))!.Data!;
    }

    private async Task<List<UtilityServiceResponse>> GetUtilityServicesAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<List<UtilityServiceResponse>>>(JsonOptions))!.Data!;
    }

    private async Task<UtilityServiceResponse> GetUtilityServiceAsync(Guid id)
    {
        var response = await _client.GetAsync($"/api/utility-services/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<UtilityServiceResponse>>(JsonOptions))!.Data!;
    }

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
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
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

        var loginResponse = await memberClient.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = ActorPassword
        });
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        return (memberClient, organization.Id);
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201AndGetByIdSeesSameData()
    {
        var response = await _client.PostAsJsonAsync("/api/utility-services", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<UtilityServiceResponse>>(JsonOptions))?.Data;
        Assert.NotNull(created);
        Assert.Equal(_propertyId, created!.PropertyId);
        Assert.True(created.IsActive);

        var fetched = await GetUtilityServiceAsync(created.Id);
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("Electricity", fetched.Name);
        Assert.Equal(UtilityType.Electricity, fetched.Type);
        Assert.Equal("kWh", fetched.Unit);
        Assert.Equal(3500, fetched.DefaultUnitPrice);
        Assert.True(fetched.IsActive);
    }

    [Fact]
    public async Task Create_ValidRequest_SerializesTypeAsCamelCase()
    {
        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        var json = await (await _client.GetAsync($"/api/utility-services/{created.Id}")).Content.ReadFromJsonAsync<JsonObject>();

        Assert.Equal("electricity", json!["data"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_WithoutDefaultUnitPrice_StoresNull()
    {
        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { DefaultUnitPrice = null });

        Assert.Null((await GetUtilityServiceAsync(created.Id)).DefaultUnitPrice);
    }

    [Fact]
    public async Task Create_DefaultUnitPriceZero_StoresZero()
    {
        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { DefaultUnitPrice = 0 });

        Assert.Equal(0, (await GetUtilityServiceAsync(created.Id)).DefaultUnitPrice);
    }

    [Fact]
    public async Task Create_PropertyIdNotFound_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/utility-services", ValidCreateRequest() with { PropertyId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidPayload_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/utility-services", ValidCreateRequest() with { Name = "", DefaultUnitPrice = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateNameInSameProperty_ReturnsConflict()
    {
        await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        var response = await _client.PostAsJsonAsync("/api/utility-services", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameInDifferentProperty_Returns201()
    {
        var otherPropertyId = await CreatePropertyAsync(_client, _organizationId, "Other Property");
        await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        var response = await _client.PostAsJsonAsync("/api/utility-services", ValidCreateRequest() with { PropertyId = otherPropertyId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_SameNameAfterSoftDelete_Returns201()
    {
        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest());
        await _client.DeleteAsync($"/api/utility-services/{created.Id}");

        var response = await _client.PostAsJsonAsync("/api/utility-services", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Update_OnlyIsActive_KeepsOtherFieldsUnchanged()
    {
        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        var response = await _client.PatchAsJsonAsync(
            $"/api/utility-services/{created.Id}", new UpdateUtilityServiceRequest { IsActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<UtilityServiceResponse>>(JsonOptions))!.Data!;
        Assert.False(updated.IsActive);
        Assert.Equal("Electricity", updated.Name);
        Assert.Equal(UtilityType.Electricity, updated.Type);
        Assert.Equal("kWh", updated.Unit);
        Assert.Equal(3500, updated.DefaultUnitPrice);
        Assert.Equal(_propertyId, updated.PropertyId);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Update_DefaultUnitPriceSetToNull_ClearsPrice()
    {
        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        var response = await _client.PatchAsJsonAsync(
            $"/api/utility-services/{created.Id}", new UpdateUtilityServiceRequest { DefaultUnitPrice = null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await GetUtilityServiceAsync(created.Id)).DefaultUnitPrice);
    }

    [Fact]
    public async Task Update_NameTakenInSameProperty_ReturnsConflict()
    {
        await CreateUtilityServiceAsync(_client, ValidCreateRequest());
        var other = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { Name = "Water", Type = UtilityType.Water, Unit = "m³" });

        var response = await _client.PatchAsJsonAsync(
            $"/api/utility-services/{other.Id}", new UpdateUtilityServiceRequest { Name = "Electricity" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_InvalidPayload_Returns400()
    {
        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        var response = await _client.PatchAsJsonAsync($"/api/utility-services/{created.Id}", new { name = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingService_SoftDeletesAndGetReturns404()
    {
        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/utility-services/{created.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/utility-services/{created.Id}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await context.UtilityServices.IgnoreQueryFilters().SingleAsync(s => s.Id == created.Id);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(_actorId, stored.DeletedBy);
    }

    [Fact]
    public async Task ByIdEndpoints_WithMultipleServices_ActOnRequestedServiceOnly()
    {
        var first = await CreateUtilityServiceAsync(_client, ValidCreateRequest());
        var second = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { Name = "Water", Type = UtilityType.Water, Unit = "m³" });

        var fetched = await GetUtilityServiceAsync(second.Id);
        Assert.Equal(second.Id, fetched.Id);
        Assert.Equal("Water", fetched.Name);

        var updated = (await (await _client.PatchAsJsonAsync(
                $"/api/utility-services/{second.Id}", new UpdateUtilityServiceRequest { Unit = "m3" }))
            .Content.ReadFromJsonAsync<ApiResponse<UtilityServiceResponse>>(JsonOptions))!.Data!;
        Assert.Equal(second.Id, updated.Id);
        Assert.Equal("m3", updated.Unit);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/utility-services/{second.Id}")).StatusCode);

        var firstAfter = await GetUtilityServiceAsync(first.Id);
        Assert.Equal("Electricity", firstAfter.Name);
        Assert.Equal("kWh", firstAfter.Unit);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/utility-services/{second.Id}")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_UnknownId_Return404()
    {
        var unknownId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/utility-services/{unknownId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PatchAsJsonAsync(
            $"/api/utility-services/{unknownId}", new UpdateUtilityServiceRequest { Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/utility-services/{unknownId}")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithoutAuthentication_Return401()
    {
        using var anonymousClient = factory.CreateClient();
        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync(
            $"/api/utility-services?propertyId={_propertyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync($"/api/utility-services/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsJsonAsync(
            "/api/utility-services", ValidCreateRequest() with { Name = "Water" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PatchAsJsonAsync(
            $"/api/utility-services/{created.Id}", new UpdateUtilityServiceRequest { Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.DeleteAsync($"/api/utility-services/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_UserWithoutPlatformPermissionOrMembership_Return403()
    {
        using var unprivilegedClient = factory.CreateClient();
        var registerResponse = await unprivilegedClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = "no-permission-utility-service@test.com",
            Phone = "0900000092",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "No Permission User"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await unprivilegedClient.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "no-permission-utility-service@test.com",
            Password = ActorPassword
        });
        unprivilegedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        var created = await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.GetAsync(
            $"/api/utility-services?propertyId={_propertyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.GetAsync($"/api/utility-services/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.PostAsJsonAsync(
            "/api/utility-services", ValidCreateRequest() with { Name = "Water" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.PatchAsJsonAsync(
            $"/api/utility-services/{created.Id}", new UpdateUtilityServiceRequest { Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.DeleteAsync($"/api/utility-services/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetAll_MissingPropertyId_Returns400()
    {
        var response = await _client.GetAsync("/api/utility-services");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_PropertyIdNotFound_Returns404()
    {
        var response = await _client.GetAsync($"/api/utility-services?propertyId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyThatPropertysServicesSortedByNameWithoutDeleted()
    {
        var otherPropertyId = await CreatePropertyAsync(_client, _organizationId, "Other Property");
        var water = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { Name = "Water", Type = UtilityType.Water, Unit = "m³" });
        var electricity = await CreateUtilityServiceAsync(_client, ValidCreateRequest());
        var garbage = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { Name = "Garbage", Type = UtilityType.Garbage, Unit = "month" });
        var deleted = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { Name = "Internet", Type = UtilityType.Internet, Unit = "month" });
        await _client.DeleteAsync($"/api/utility-services/{deleted.Id}");
        var otherProperty = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { PropertyId = otherPropertyId });

        var result = await GetUtilityServicesAsync(_client, $"/api/utility-services?propertyId={_propertyId}");

        Assert.Equal([electricity.Id, garbage.Id, water.Id], result.Select(s => s.Id));
        Assert.DoesNotContain(result, s => s.Id == otherProperty.Id);
    }

    [Fact]
    public async Task GetAll_FilterByType_ReturnsMatchingItemsOnly()
    {
        var electricity = await CreateUtilityServiceAsync(_client, ValidCreateRequest());
        var water = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { Name = "Water", Type = UtilityType.Water, Unit = "m³" });

        var result = await GetUtilityServicesAsync(_client, $"/api/utility-services?propertyId={_propertyId}&type=water");

        Assert.Equal([water.Id], result.Select(s => s.Id));
        Assert.DoesNotContain(result, s => s.Id == electricity.Id);
    }

    [Fact]
    public async Task GetAll_FilterByIsActive_ReturnsMatchingItemsOnly()
    {
        var active = await CreateUtilityServiceAsync(_client, ValidCreateRequest());
        var inactive = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { Name = "Water", Type = UtilityType.Water, Unit = "m³", IsActive = false });

        var inactiveResult = await GetUtilityServicesAsync(_client, $"/api/utility-services?propertyId={_propertyId}&isActive=false");
        Assert.Equal([inactive.Id], inactiveResult.Select(s => s.Id));

        var activeResult = await GetUtilityServicesAsync(_client, $"/api/utility-services?propertyId={_propertyId}&isActive=true");
        Assert.Equal([active.Id], activeResult.Select(s => s.Id));
    }

    [Fact]
    public async Task GetAll_ResponseDataIsPlainArrayWithoutPaging()
    {
        await CreateUtilityServiceAsync(_client, ValidCreateRequest());

        var json = await (await _client.GetAsync($"/api/utility-services?propertyId={_propertyId}")).Content.ReadFromJsonAsync<JsonObject>();

        var data = Assert.IsType<JsonArray>(json!["data"]);
        Assert.Single(data);
    }

    [Fact]
    public async Task OrganizationScope_MemberOfOrganizationA_CanOnlyAccessServicesOfOwnOrganizationsProperties()
    {
        var (memberClient, organizationAId) = await CreateOrganizationScopedMemberAsync("organization_admin");
        var propertyAId = await CreatePropertyAsync(_client, organizationAId, "Property A");
        var organizationBId = await CreateOrganizationAsync("Org B");
        var propertyBId = await CreatePropertyAsync(_client, organizationBId, "Property B");

        var createOwnResponse = await memberClient.PostAsJsonAsync(
            "/api/utility-services", ValidCreateRequest() with { PropertyId = propertyAId });
        Assert.Equal(HttpStatusCode.Created, createOwnResponse.StatusCode);
        var own = (await createOwnResponse.Content.ReadFromJsonAsync<ApiResponse<UtilityServiceResponse>>(JsonOptions))!.Data!;

        var createOtherResponse = await memberClient.PostAsJsonAsync(
            "/api/utility-services", ValidCreateRequest() with { PropertyId = propertyBId });
        Assert.Equal(HttpStatusCode.NotFound, createOtherResponse.StatusCode);

        var otherOrgService = await CreateUtilityServiceAsync(_client, ValidCreateRequest() with { PropertyId = propertyBId });

        var list = await GetUtilityServicesAsync(memberClient, $"/api/utility-services?propertyId={propertyAId}");
        Assert.Equal([own.Id], list.Select(s => s.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync(
            $"/api/utility-services?propertyId={propertyBId}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync($"/api/utility-services/{own.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/utility-services/{otherOrgService.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PatchAsJsonAsync(
            $"/api/utility-services/{otherOrgService.Id}", new UpdateUtilityServiceRequest { Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"/api/utility-services/{otherOrgService.Id}")).StatusCode);

        var stillThere = await GetUtilityServiceAsync(otherOrgService.Id);
        Assert.Equal("Electricity", stillThere.Name);
    }
}
