using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.DTOs.Properties;
using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.DTOs.RoomAssets;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardingHouse.IntegrationTests.Controllers;

public class RoomAssetsControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "room-asset-actor@test.com";
    private const string ActorPassword = "password123";

    private Guid _actorId;
    private Guid _roomId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        var organizationId = await CreateOrganizationAsync("Room Asset Org");
        var propertyId = await CreatePropertyAsync(_client, organizationId, "Room Asset Property");
        _roomId = await CreateRoomAsync(_client, propertyId, "101");
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
            Phone = "0900000009",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "Room Asset Actor"
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

    private static async Task<Guid> CreateRoomAsync(HttpClient client, Guid propertyId, string roomNumber)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest { PropertyId = propertyId, RoomNumber = roomNumber });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!.Id;
    }

    private static async Task<RoomAssetResponse> CreateAssetAsync(HttpClient client, Guid roomId, CreateRoomAssetRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/rooms/{roomId}/assets", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetResponse>>(JsonOptions))!.Data!;
    }

    private async Task<RoomDetailsResponse> GetRoomAsync(Guid roomId)
    {
        var response = await _client.GetAsync($"/api/rooms/{roomId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomDetailsResponse>>(JsonOptions))!.Data!;
    }

    private async Task<RoomAssetConditionHistoryResponse> RecordConditionAsync(
        Guid assetId, AssetCondition condition, string? note = null)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/rooms/{_roomId}/assets/{assetId}/condition-histories",
            new CreateRoomAssetConditionHistoryRequest { NewCondition = condition, Note = note });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetConditionHistoryResponse>>(JsonOptions))!.Data!;
    }

    private async Task<List<RoomAssetConditionHistoryResponse>> GetHistoriesAsync(Guid assetId)
    {
        var response = await _client.GetAsync($"/api/rooms/{_roomId}/assets/{assetId}/condition-histories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<List<RoomAssetConditionHistoryResponse>>>(JsonOptions))!.Data!;
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
    public async Task Create_ValidRequest_Returns201WithGoodConditionAndInitialHistoryEntry()
    {
        var response = await _client.PostAsJsonAsync($"/api/rooms/{_roomId}/assets", new CreateRoomAssetRequest
        {
            Name = "Air conditioner",
            Quantity = 2,
            PurchaseDate = new DateOnly(2026, 1, 15),
            PurchaseUnitPrice = 7_500_000,
            Note = "Daikin"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/rooms/{_roomId}", response.Headers.Location!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetResponse>>(JsonOptions))!.Data!;
        Assert.Equal(AssetCondition.Good, created.Condition);
        Assert.Equal(2, created.Quantity);
        Assert.Equal(7_500_000, created.PurchaseUnitPrice);

        var history = Assert.Single(await GetHistoriesAsync(created.Id));
        Assert.Null(history.OldCondition);
        Assert.Equal(AssetCondition.Good, history.NewCondition);
        Assert.Equal(_actorId, history.ChangedBy);

        var room = await GetRoomAsync(_roomId);
        Assert.Equal(created.Id, Assert.Single(room.Assets).Id);
    }

    [Fact]
    public async Task Create_InvalidPayload_Returns400()
    {
        var response = await _client.PostAsJsonAsync($"/api/rooms/{_roomId}/assets", new { name = "", quantity = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownOrDeletedRoom_Returns404()
    {
        var unknown = await _client.PostAsJsonAsync($"/api/rooms/{Guid.NewGuid()}/assets", new CreateRoomAssetRequest { Name = "Bed" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/rooms/{_roomId}")).StatusCode);
        var deleted = await _client.PostAsJsonAsync($"/api/rooms/{_roomId}/assets", new CreateRoomAssetRequest { Name = "Bed" });
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    [Fact]
    public async Task Update_OnlyName_KeepsOtherFieldsAndCondition()
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed", Quantity = 2, PurchaseUnitPrice = 3_000_000 });
        await RecordConditionAsync(created.Id, AssetCondition.Damaged);

        var response = await _client.PatchAsJsonAsync(
            $"/api/rooms/{_roomId}/assets/{created.Id}", new UpdateRoomAssetRequest { Name = "Double bed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetResponse>>(JsonOptions))!.Data!;
        Assert.Equal("Double bed", updated.Name);
        Assert.Equal(2, updated.Quantity);
        Assert.Equal(3_000_000, updated.PurchaseUnitPrice);
        Assert.Equal(AssetCondition.Damaged, updated.Condition);
    }

    [Fact]
    public async Task Update_PurchaseUnitPriceSetToNull_ClearsPrice()
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed", PurchaseUnitPrice = 3_000_000 });

        var response = await _client.PatchAsJsonAsync(
            $"/api/rooms/{_roomId}/assets/{created.Id}", new UpdateRoomAssetRequest { PurchaseUnitPrice = null });

        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetResponse>>(JsonOptions))!.Data!;
        Assert.Null(updated.PurchaseUnitPrice);
    }

    [Fact]
    public async Task Update_ConditionInBody_IsIgnored()
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        var response = await _client.PatchAsJsonAsync(
            $"/api/rooms/{_roomId}/assets/{created.Id}", new { note = "Checked", condition = "broken" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetResponse>>(JsonOptions))!.Data!;
        Assert.Equal(AssetCondition.Good, updated.Condition);
        Assert.Equal("Checked", updated.Note);
    }

    [Fact]
    public async Task Delete_ExistingAsset_SoftDeletesAndHidesFromRoomDetail()
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/rooms/{_roomId}/assets/{created.Id}")).StatusCode);

        Assert.Empty((await GetRoomAsync(_roomId)).Assets);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await context.RoomAssets.IgnoreQueryFilters().SingleAsync(a => a.Id == created.Id);
        Assert.NotNull(stored.DeletedAt);
        Assert.Equal(_actorId, stored.DeletedBy);
    }

    [Fact]
    public async Task Endpoints_AssetOfAnotherRoom_Return404()
    {
        var room = await GetRoomAsync(_roomId);
        var otherRoomId = await CreateRoomAsync(_client, room.PropertyId, "102");
        var otherAsset = await CreateAssetAsync(_client, otherRoomId, new CreateRoomAssetRequest { Name = "Bed" });
        var url = $"/api/rooms/{_roomId}/assets/{otherAsset.Id}";

        Assert.Equal(HttpStatusCode.NotFound, (await _client.PatchAsJsonAsync(url, new UpdateRoomAssetRequest { Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync(
            $"{url}/split", new SplitRoomAssetRequest { Quantity = 1, NewCondition = AssetCondition.Broken })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"{url}/condition-histories")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsJsonAsync(
            $"{url}/condition-histories", new CreateRoomAssetConditionHistoryRequest { NewCondition = AssetCondition.Broken })).StatusCode);
    }

    [Fact]
    public async Task RecordCondition_UpdatesAssetAndChainsOldCondition()
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        var first = await RecordConditionAsync(created.Id, AssetCondition.Damaged, "Broken leg");
        var second = await RecordConditionAsync(created.Id, AssetCondition.Broken);

        Assert.Equal(AssetCondition.Good, first.OldCondition);
        Assert.Equal(AssetCondition.Damaged, first.NewCondition);
        Assert.Equal("Broken leg", first.Note);
        Assert.Equal(_actorId, first.ChangedBy);
        Assert.Equal(AssetCondition.Damaged, second.OldCondition);
        Assert.Equal(AssetCondition.Broken, Assert.Single((await GetRoomAsync(_roomId)).Assets).Condition);

        var histories = await GetHistoriesAsync(created.Id);
        Assert.Equal([second.Id, first.Id], histories.Take(2).Select(h => h.Id));
        Assert.Equal(3, histories.Count);
    }

    [Fact]
    public async Task RecordCondition_SameAsCurrent_AppendsEntry()
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        var entry = await RecordConditionAsync(created.Id, AssetCondition.Good, "Re-checked");

        Assert.Equal(AssetCondition.Good, entry.OldCondition);
        Assert.Equal(AssetCondition.Good, entry.NewCondition);
        Assert.Equal(2, (await GetHistoriesAsync(created.Id)).Count);
    }

    [Fact]
    public async Task Split_PartOfQuantity_CreatesRowWithOwnConditionAndChainedHistory()
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest
        {
            Name = "Plastic chair",
            Quantity = 4,
            PurchaseDate = new DateOnly(2026, 1, 15),
            PurchaseUnitPrice = 150_000,
            Note = "Blue"
        });

        var response = await _client.PostAsJsonAsync(
            $"/api/rooms/{_roomId}/assets/{created.Id}/split",
            new SplitRoomAssetRequest { Quantity = 1, NewCondition = AssetCondition.Damaged, Note = "Broken leg" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<SplitRoomAssetResponse>>(JsonOptions))!.Data!;
        Assert.Equal(3, result.Original.Quantity);
        Assert.Equal(AssetCondition.Good, result.Original.Condition);
        Assert.Equal(1, result.Split.Quantity);
        Assert.Equal(AssetCondition.Damaged, result.Split.Condition);
        Assert.Equal(created.Id, result.Split.SplitFromAssetId);
        Assert.Equal(("Plastic chair", new DateOnly(2026, 1, 15), 150_000m, "Blue"),
            (result.Split.Name, result.Split.PurchaseDate!.Value, result.Split.PurchaseUnitPrice!.Value, result.Split.Note));

        var history = Assert.Single(await GetHistoriesAsync(result.Split.Id));
        Assert.Equal(AssetCondition.Good, history.OldCondition);
        Assert.Equal(AssetCondition.Damaged, history.NewCondition);
        Assert.Equal("Broken leg", history.Note);
        Assert.Single(await GetHistoriesAsync(created.Id));

        Assert.Equal(2, (await GetRoomAsync(_roomId)).Assets.Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Split_QuantityNotLessThanOriginal_Returns400(int quantity)
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Plastic chair", Quantity = 2 });

        var response = await _client.PostAsJsonAsync(
            $"/api/rooms/{_roomId}/assets/{created.Id}/split",
            new SplitRoomAssetRequest { Quantity = quantity, NewCondition = AssetCondition.Damaged });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2, Assert.Single((await GetRoomAsync(_roomId)).Assets).Quantity);
    }

    [Fact]
    public async Task OrganizationScope_MemberOfAnotherOrganization_Gets404OnEveryEndpoint()
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var (memberClient, _) = await CreateOrganizationScopedMemberAsync("organization_admin");
        var url = $"/api/rooms/{_roomId}/assets";

        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PostAsJsonAsync(url, new CreateRoomAssetRequest { Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PatchAsJsonAsync(
            $"{url}/{created.Id}", new UpdateRoomAssetRequest { Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"{url}/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PostAsJsonAsync(
            $"{url}/{created.Id}/split", new SplitRoomAssetRequest { Quantity = 1, NewCondition = AssetCondition.Broken })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"{url}/{created.Id}/condition-histories")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PostAsJsonAsync(
            $"{url}/{created.Id}/condition-histories",
            new CreateRoomAssetConditionHistoryRequest { NewCondition = AssetCondition.Broken })).StatusCode);

        Assert.Equal("Bed", Assert.Single((await GetRoomAsync(_roomId)).Assets).Name);
    }

    [Fact]
    public async Task OrganizationScope_MemberOfOwnOrganization_CanManageAssets()
    {
        var (memberClient, organizationId) = await CreateOrganizationScopedMemberAsync("organization_staff");
        var propertyId = await CreatePropertyAsync(_client, organizationId, "Member Property");
        var roomId = await CreateRoomAsync(_client, propertyId, "M1");

        var created = await CreateAssetAsync(memberClient, roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var history = await memberClient.PostAsJsonAsync(
            $"/api/rooms/{roomId}/assets/{created.Id}/condition-histories",
            new CreateRoomAssetConditionHistoryRequest { NewCondition = AssetCondition.Damaged });

        Assert.Equal(HttpStatusCode.Created, history.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync($"/api/rooms/{roomId}")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithoutAuthentication_Return401()
    {
        var anonymousClient = factory.CreateClient();
        var url = $"/api/rooms/{_roomId}/assets";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsJsonAsync(url, new CreateRoomAssetRequest { Name = "Bed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync($"{url}/{Guid.NewGuid()}/condition-histories")).StatusCode);
    }
}
