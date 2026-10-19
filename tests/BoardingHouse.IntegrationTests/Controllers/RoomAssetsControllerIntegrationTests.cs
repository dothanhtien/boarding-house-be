using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.RoomAssets;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Seed;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.DTOs.Properties;
using BoardingHouse.Application.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Application.DTOs.RoomAssets;
using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.Services.Storage;

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
    private Guid _organizationId;
    private Guid _roomId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        _organizationId = await CreateOrganizationAsync("Room Asset Org");
        var propertyId = await CreatePropertyAsync(_client, _organizationId, "Room Asset Property");
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
            Identifier = ActorEmail,
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

    private static async Task<RoomDetailsAssetResponse> CreateAssetAsync(
        HttpClient client, Guid roomId, CreateRoomAssetRequest request, int photoCount = 0)
    {
        var response = await client.PostAsync($"/api/rooms/{roomId}/assets", AssetForm(request, photoCount));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomDetailsAssetResponse>>(JsonOptions))!.Data!;
    }

    private static MultipartFormDataContent AssetForm(CreateRoomAssetRequest request, int photoCount = 0)
    {
        var form = PngForm(photoCount);
        form.Add(new StringContent(request.Name), "name");
        form.Add(new StringContent(request.Quantity.ToString(CultureInfo.InvariantCulture)), "quantity");

        if (request.PurchaseDate is { } purchaseDate)
        {
            form.Add(new StringContent(purchaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "purchaseDate");
        }

        if (request.PurchaseUnitPrice is { } purchaseUnitPrice)
        {
            form.Add(new StringContent(purchaseUnitPrice.ToString(CultureInfo.InvariantCulture)), "purchaseUnitPrice");
        }

        if (request.Note is not null)
        {
            form.Add(new StringContent(request.Note), "note");
        }

        return form;
    }

    private async Task<RoomDetailsResponse> GetRoomAsync(Guid roomId)
    {
        var response = await _client.GetAsync($"/api/rooms/{roomId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomDetailsResponse>>(JsonOptions))!.Data!;
    }

    private async Task<RoomAssetConditionHistoryResponse> RecordConditionAsync(
        Guid assetId, AssetCondition condition, string? note = null, int photoCount = 0)
    {
        var response = await _client.PostAsync(
            $"/api/rooms/{_roomId}/assets/{assetId}/condition-histories",
            ConditionForm(condition.ToString(), photoCount, note));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetConditionHistoryResponse>>(JsonOptions))!.Data!;
    }

    private async Task<RoomAssetConditionHistoryResponse> RecordFullConditionAsync(Guid assetId, AssetCondition condition)
    {
        var entry = await RecordConditionAsync(assetId, condition, photoCount: MediaLimits.MaxFilesPerRequest);
        while (entry.Media.Count < RoomAssetConditionHistory.MaxMedia)
        {
            var photoCount = Math.Min(MediaLimits.MaxFilesPerRequest, RoomAssetConditionHistory.MaxMedia - entry.Media.Count);
            entry = await ReadHistoryAsync(await PatchHistoryAsync(assetId, entry.Id, UpdateForm(photoCount)));
        }

        return entry;
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
            Identifier = email,
            Password = ActorPassword
        });
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        return (memberClient, organization.Id);
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201WithGoodConditionAndInitialHistoryEntry()
    {
        var response = await _client.PostAsync($"/api/rooms/{_roomId}/assets", AssetForm(new CreateRoomAssetRequest
        {
            Name = "Air conditioner",
            Quantity = 2,
            PurchaseDate = new DateOnly(2026, 1, 15),
            PurchaseUnitPrice = 7_500_000.5m,
            Note = "Daikin"
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/rooms/{_roomId}", response.Headers.Location!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomDetailsAssetResponse>>(JsonOptions))!.Data!;
        Assert.Equal(AssetCondition.Good, created.Condition);
        Assert.Equal(2, created.Quantity);
        Assert.Equal(new DateOnly(2026, 1, 15), created.PurchaseDate);
        Assert.Equal(7_500_000.5m, created.PurchaseUnitPrice);
        Assert.Equal("Daikin", created.Note);
        Assert.Null(created.CurrentPhotos);

        var history = Assert.Single(await GetHistoriesAsync(created.Id));
        Assert.Null(history.OldCondition);
        Assert.Equal(AssetCondition.Good, history.NewCondition);
        Assert.Equal(_actorId, history.ChangedBy);
        Assert.Empty(history.Media);
        Assert.Empty(factory.Storage.Files);

        var room = await GetRoomAsync(_roomId);
        Assert.Equal(created.Id, Assert.Single(room.Assets).Id);
    }

    [Fact]
    public async Task Create_QuantityOmitted_DefaultsToOne()
    {
        var form = new MultipartFormDataContent { { new StringContent("Bed"), "name" } };

        var response = await _client.PostAsync($"/api/rooms/{_roomId}/assets", form);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomDetailsAssetResponse>>(JsonOptions))!.Data!;
        Assert.Equal(1, created.Quantity);
    }

    [Fact]
    public async Task Create_WithPhotos_AttachesThemToInitialHistoryEntry()
    {
        var created = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" }, photoCount: 2);

        Assert.NotNull(created.CurrentPhotos);
        Assert.True(created.CurrentPhotos.IsLatest);
        Assert.Equal(AssetCondition.Good, created.CurrentPhotos.Condition);
        Assert.Equal(2, created.CurrentPhotos.Media.Count);
        Assert.All(created.CurrentPhotos.Media, m =>
            Assert.Contains($"/organizations/{_organizationId}/rooms/{_roomId}/assets/{created.Id}/", m.Url));
        Assert.Equal(2, factory.Storage.Files.Count);

        var history = Assert.Single(await GetHistoriesAsync(created.Id));
        Assert.Equal(created.CurrentPhotos.ConditionHistoryId, history.Id);
        Assert.Null(history.OldCondition);
        Assert.Equal(created.CurrentPhotos.Media.Select(m => m.MediaId), history.Media.Select(m => m.MediaId));

        var current = Assert.Single((await GetRoomAsync(_roomId)).Assets);
        Assert.Equal(history.Id, current.CurrentPhotos!.ConditionHistoryId);

        var rows = await GetPhotoRowsIncludingDeletedAsync(history.Id);
        Assert.Equal([0, 1], rows.Select(r => r.SortOrder).Order());
        Assert.All(rows, r =>
        {
            Assert.Equal(MediaAssetEntityType.RoomAssetConditionPhoto, r.MediaAsset!.EntityType);
            Assert.Equal(_organizationId, r.MediaAsset.OrganizationId);
        });
    }

    [Fact]
    public async Task Create_TooManyPhotos_Returns400AndCreatesNothing()
    {
        var response = await _client.PostAsync(
            $"/api/rooms/{_roomId}/assets",
            AssetForm(new CreateRoomAssetRequest { Name = "Bed" }, MediaLimits.MaxFilesPerRequest + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Storage.Files);
        Assert.Empty((await GetRoomAsync(_roomId)).Assets);
    }

    [Fact]
    public async Task Create_InvalidPayload_Returns400()
    {
        var response = await _client.PostAsync(
            $"/api/rooms/{_roomId}/assets", AssetForm(new CreateRoomAssetRequest { Name = "", Quantity = 0 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Create_UnknownOrDeletedRoom_Returns404AndUploadsNothing(int photoCount)
    {
        var unknown = await _client.PostAsync(
            $"/api/rooms/{Guid.NewGuid()}/assets", AssetForm(new CreateRoomAssetRequest { Name = "Bed" }, photoCount));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/rooms/{_roomId}")).StatusCode);
        var deleted = await _client.PostAsync(
            $"/api/rooms/{_roomId}/assets", AssetForm(new CreateRoomAssetRequest { Name = "Bed" }, photoCount));
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Empty(factory.Storage.Files);
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
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync(
            $"{url}/condition-histories", ConditionForm("broken", 0))).StatusCode);
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

        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PostAsync(url, AssetForm(new CreateRoomAssetRequest { Name = "X" }, photoCount: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PatchAsJsonAsync(
            $"{url}/{created.Id}", new UpdateRoomAssetRequest { Name = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"{url}/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PostAsJsonAsync(
            $"{url}/{created.Id}/split", new SplitRoomAssetRequest { Quantity = 1, NewCondition = AssetCondition.Broken })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"{url}/{created.Id}/condition-histories")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PostAsync(
            $"{url}/{created.Id}/condition-histories",
            ConditionForm("broken", 0))).StatusCode);

        Assert.Equal("Bed", Assert.Single((await GetRoomAsync(_roomId)).Assets).Name);
        Assert.Empty(factory.Storage.Files);
    }

    [Fact]
    public async Task OrganizationScope_MemberOfOwnOrganization_CanManageAssets()
    {
        var (memberClient, organizationId) = await CreateOrganizationScopedMemberAsync("organization_staff");
        var propertyId = await CreatePropertyAsync(_client, organizationId, "Member Property");
        var roomId = await CreateRoomAsync(_client, propertyId, "M1");

        var created = await CreateAssetAsync(memberClient, roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var history = await memberClient.PostAsync(
            $"/api/rooms/{roomId}/assets/{created.Id}/condition-histories",
            ConditionForm("damaged", 0));

        Assert.Equal(HttpStatusCode.Created, history.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync($"/api/rooms/{roomId}")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithoutAuthentication_Return401()
    {
        var anonymousClient = factory.CreateClient();
        var url = $"/api/rooms/{_roomId}/assets";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsync(url, AssetForm(new CreateRoomAssetRequest { Name = "Bed" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync($"{url}/{Guid.NewGuid()}/condition-histories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsync(
            $"{url}/{Guid.NewGuid()}/condition-histories", ConditionForm("damaged", 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PatchAsync(
            $"{url}/{Guid.NewGuid()}/condition-histories/{Guid.NewGuid()}", UpdateForm(note: "x"))).StatusCode);
    }

    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    private static MultipartFormDataContent PngForm(int count)
    {
        var form = new MultipartFormDataContent();
        for (var i = 0; i < count; i++)
        {
            var file = new ByteArrayContent(PngBytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "files", $"photo-{i}.png");
        }

        return form;
    }

    private static MultipartFormDataContent UpdateForm(
        int photoCount = 0, IEnumerable<Guid>? removeMediaIds = null, string? note = null)
    {
        var form = PngForm(photoCount);
        foreach (var id in removeMediaIds ?? [])
        {
            form.Add(new StringContent(id.ToString()), "removeMediaIds");
        }

        if (note is not null)
        {
            form.Add(new StringContent(note), "note");
        }

        return form;
    }

    private Task<HttpResponseMessage> PatchHistoryAsync(
        Guid assetId, Guid historyId, MultipartFormDataContent form, HttpClient? client = null) =>
        (client ?? _client).PatchAsync($"/api/rooms/{_roomId}/assets/{assetId}/condition-histories/{historyId}", form);

    private static async Task<RoomAssetConditionHistoryResponse> ReadHistoryAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetConditionHistoryResponse>>(JsonOptions))!.Data!;
    }

    private async Task<List<RoomAssetConditionHistoryMedia>> GetPhotoRowsIncludingDeletedAsync(Guid historyId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.RoomAssetConditionHistoryMedia
            .IgnoreQueryFilters()
            .Include(m => m.MediaAsset)
            .Where(m => m.ConditionHistoryId == historyId)
            .ToListAsync();
    }

    [Fact]
    public async Task CurrentPhotos_AssetWithoutPhotos_IsNull()
    {
        await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        Assert.Null(Assert.Single((await GetRoomAsync(_roomId)).Assets).CurrentPhotos);
    }

    [Fact]
    public async Task CurrentPhotos_NewerEntryWithoutPhotos_KeepsOlderPhotos()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var damaged = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, photoCount: 1);

        await RecordConditionAsync(asset.Id, AssetCondition.Broken);

        var current = Assert.Single((await GetRoomAsync(_roomId)).Assets).CurrentPhotos!;
        Assert.Equal(damaged.Id, current.ConditionHistoryId);
        Assert.Equal(AssetCondition.Damaged, current.Condition);
        Assert.False(current.IsLatest);
    }

    [Fact]
    public async Task Update_RemovePhotoOfOlderEntry_Returns409AndKeepsPhotoAndFile()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var damaged = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, photoCount: 1);
        await RecordConditionAsync(asset.Id, AssetCondition.Broken);

        var response = await PatchHistoryAsync(asset.Id, damaged.Id, UpdateForm(removeMediaIds: [damaged.Media[0].MediaId]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(factory.Storage.Files);
        Assert.Equal(damaged.Id, Assert.Single((await GetRoomAsync(_roomId)).Assets).CurrentPhotos!.ConditionHistoryId);
        var row = Assert.Single(await GetPhotoRowsIncludingDeletedAsync(damaged.Id));
        Assert.Null(row.DeletedAt);
        Assert.Null(row.MediaAsset!.DeletedAt);
    }

    [Fact]
    public async Task Update_NoteOfOlderEntry_Returns409AndKeepsNote()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var damaged = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, "Broken leg");
        await RecordConditionAsync(asset.Id, AssetCondition.Good);

        var response = await PatchHistoryAsync(asset.Id, damaged.Id, UpdateForm(note: ""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var stored = (await GetHistoriesAsync(asset.Id)).Single(h => h.Id == damaged.Id);
        Assert.Equal("Broken leg", stored.Note);
        Assert.Null(stored.UpdatedAt);
    }

    [Fact]
    public async Task CurrentPhotos_NewerEntryWithPhotos_MovesToNewerEntry()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        await RecordConditionAsync(asset.Id, AssetCondition.Good, photoCount: 1);

        var damaged = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, photoCount: 2);

        var current = Assert.Single((await GetRoomAsync(_roomId)).Assets).CurrentPhotos!;
        Assert.Equal(damaged.Id, current.ConditionHistoryId);
        Assert.Equal(AssetCondition.Damaged, current.Condition);
        Assert.True(current.IsLatest);
        Assert.Equal(damaged.Media.Select(m => m.MediaId), current.Media.Select(m => m.MediaId));
        Assert.Equal([2, 1, 0], (await GetHistoriesAsync(asset.Id)).Select(h => h.Media.Count));
    }

    [Fact]
    public async Task CurrentPhotos_SeveralAssets_EachGetsOwnPhotosInGetAndPatchRoom()
    {
        var bed = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var desk = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Desk" });
        var chair = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Chair" });
        var bedPhotos = (await RecordConditionAsync(bed.Id, AssetCondition.Good, photoCount: 1)).Media;
        var deskPhotos = (await RecordConditionAsync(desk.Id, AssetCondition.Good, photoCount: 2)).Media;

        var room = await GetRoomAsync(_roomId);
        Assert.Equal(bedPhotos.Select(m => m.MediaId), room.Assets.Single(a => a.Id == bed.Id).CurrentPhotos!.Media.Select(m => m.MediaId));
        Assert.Equal(deskPhotos.Select(m => m.MediaId), room.Assets.Single(a => a.Id == desk.Id).CurrentPhotos!.Media.Select(m => m.MediaId));
        Assert.Null(room.Assets.Single(a => a.Id == chair.Id).CurrentPhotos);

        var patch = await _client.PatchAsJsonAsync($"/api/rooms/{_roomId}", new { note = "Updated" });
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patched = (await patch.Content.ReadFromJsonAsync<ApiResponse<RoomDetailsResponse>>(JsonOptions))!.Data!;
        Assert.Equal(2, patched.Assets.Single(a => a.Id == desk.Id).CurrentPhotos!.Media.Count);
    }

    [Fact]
    public async Task Update_HistoryOfAnotherAsset_Returns404()
    {
        var bed = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var desk = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Desk" });
        var deskEntry = await RecordConditionAsync(desk.Id, AssetCondition.Good, photoCount: 1);

        var response = await PatchHistoryAsync(bed.Id, deskEntry.Id, UpdateForm(removeMediaIds: [deskEntry.Media[0].MediaId]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(factory.Storage.Files);
    }

    [Fact]
    public async Task Update_UnknownMediaId_Returns404()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var historyId = Assert.Single(await GetHistoriesAsync(asset.Id)).Id;

        var response = await PatchHistoryAsync(asset.Id, historyId, UpdateForm(removeMediaIds: [Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_RemovePhoto_SoftDeletesRowAndMediaAssetAndDeletesFile()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var entry = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, photoCount: 2);
        var photos = entry.Media;

        var response = await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(removeMediaIds: [photos[0].MediaId]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(photos[1].MediaId, Assert.Single((await GetHistoriesAsync(asset.Id))[0].Media).MediaId);
        Assert.Single(factory.Storage.Files);
        var removed = (await GetPhotoRowsIncludingDeletedAsync(entry.Id)).Single(r => r.MediaAssetId == photos[0].MediaId);
        Assert.NotNull(removed.DeletedAt);
        Assert.NotNull(removed.MediaAsset!.DeletedAt);
    }

    [Fact]
    public async Task DeleteAsset_RemovesConditionPhotosAndFiles()
    {
        var bed = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var bedGood = await RecordConditionAsync(bed.Id, AssetCondition.Good, photoCount: 1);
        var bedDamaged = await RecordConditionAsync(bed.Id, AssetCondition.Damaged, photoCount: 2);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/rooms/{_roomId}/assets/{bed.Id}")).StatusCode);

        Assert.Empty(factory.Storage.Files);
        foreach (var historyId in new[] { bedGood.Id, bedDamaged.Id })
        {
            Assert.All(await GetPhotoRowsIncludingDeletedAsync(historyId), row =>
            {
                Assert.NotNull(row.DeletedAt);
                Assert.NotNull(row.MediaAsset!.DeletedAt);
            });
        }
    }

    [Fact]
    public async Task DeleteRoom_RemovesConditionPhotosOfAllAssetsAndFiles()
    {
        var bed = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var desk = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Desk" });
        var bedGood = await RecordConditionAsync(bed.Id, AssetCondition.Good, photoCount: 1);
        var deskGood = await RecordConditionAsync(desk.Id, AssetCondition.Good, photoCount: 2);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/rooms/{_roomId}")).StatusCode);

        Assert.Empty(factory.Storage.Files);
        foreach (var historyId in new[] { bedGood.Id, deskGood.Id })
        {
            Assert.All(await GetPhotoRowsIncludingDeletedAsync(historyId), row =>
            {
                Assert.NotNull(row.DeletedAt);
                Assert.NotNull(row.MediaAsset!.DeletedAt);
            });
        }
    }

    [Fact]
    public async Task Update_MemberOfAnotherOrganization_Returns404AndUploadsNothing()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var entry = await RecordConditionAsync(asset.Id, AssetCondition.Good, photoCount: 1);
        var (memberClient, _) = await CreateOrganizationScopedMemberAsync("organization_admin");

        var remove = await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(removeMediaIds: [entry.Media[0].MediaId]), memberClient);
        var add = await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(photoCount: 1), memberClient);

        Assert.Equal(HttpStatusCode.NotFound, remove.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);
        Assert.Single(factory.Storage.Files);
        Assert.Single((await GetHistoriesAsync(asset.Id))[0].Media);
    }

    [Fact]
    public async Task Update_Note_ChangesOnlyNoteAndKeepsEverythingElse()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var entry = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, "Broken leg", photoCount: 1);

        var updated = await ReadHistoryAsync(await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(note: "Broken left leg")));

        Assert.Equal("Broken left leg", updated.Note);
        Assert.Null(entry.UpdatedAt);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(_actorId, updated.UpdatedBy);
        Assert.Equal(AssetCondition.Damaged, updated.NewCondition);
        Assert.Equal(entry.ChangedAt, updated.ChangedAt);
        Assert.Equal(entry.Media.Select(m => m.MediaId), updated.Media.Select(m => m.MediaId));
        var stored = (await GetHistoriesAsync(asset.Id))[0];
        Assert.Equal("Broken left leg", stored.Note);
        Assert.Equal(_actorId, stored.UpdatedBy);
        Assert.NotNull(stored.UpdatedAt);
    }

    [Fact]
    public async Task Update_NoteOmitted_KeepsNote()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var entry = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, "Broken leg", photoCount: 2);

        var updated = await ReadHistoryAsync(
            await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(removeMediaIds: [entry.Media[0].MediaId])));

        Assert.Equal("Broken leg", updated.Note);
        Assert.Single(updated.Media);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(_actorId, updated.UpdatedBy);
        var stored = (await GetHistoriesAsync(asset.Id))[0];
        Assert.Single(stored.Media);
        Assert.NotNull(stored.UpdatedAt);
    }

    [Fact]
    public async Task Update_NoteSentEmpty_ClearsNote()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var entry = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, "Broken leg");

        var updated = await ReadHistoryAsync(await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(note: "")));

        Assert.Null(updated.Note);
        Assert.Null((await GetHistoriesAsync(asset.Id))[0].Note);
    }

    [Fact]
    public async Task Update_ReplacePhotoOnLatestEntry_RemovesOldAndAppendsNewAtomically()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var entry = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, photoCount: 2);
        var (wrong, kept) = (entry.Media[0], entry.Media[1]);

        var updated = await ReadHistoryAsync(
            await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(photoCount: 1, removeMediaIds: [wrong.MediaId])));

        Assert.Equal(2, updated.Media.Count);
        Assert.Equal(_actorId, updated.UpdatedBy);
        Assert.Equal(kept.MediaId, updated.Media[0].MediaId);
        var added = updated.Media[1];
        Assert.DoesNotContain(added.MediaId, entry.Media.Select(m => m.MediaId));
        Assert.Equal(_actorId, added.CreatedBy);
        Assert.True(added.CreatedAt >= updated.ChangedAt);
        Assert.Equal(2, factory.Storage.Files.Count);

        var rows = await GetPhotoRowsIncludingDeletedAsync(entry.Id);
        Assert.NotNull(rows.Single(r => r.MediaAssetId == wrong.MediaId).DeletedAt);
        Assert.Equal(2, rows.Single(r => r.MediaAssetId == added.MediaId).SortOrder);
    }

    [Fact]
    public async Task Update_AddPhotosToOlderEntry_Returns409AndUploadsNothing()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var older = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, photoCount: 1);
        await RecordConditionAsync(asset.Id, AssetCondition.Broken);

        var response = await PatchHistoryAsync(asset.Id, older.Id, UpdateForm(photoCount: 1, removeMediaIds: [older.Media[0].MediaId]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(factory.Storage.Files);
        Assert.Single(await GetPhotoRowsIncludingDeletedAsync(older.Id), r => r.DeletedAt is null);
    }

    [Fact]
    public async Task Update_EmptyRemoveIdAtEntryLimit_Returns404AndUploadsNothing()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var entry = await RecordFullConditionAsync(asset.Id, AssetCondition.Damaged);

        var response = await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(photoCount: 1, removeMediaIds: [Guid.Empty]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RoomAssetConditionHistory.MaxMedia, factory.Storage.Files.Count);
        Assert.Equal(RoomAssetConditionHistory.MaxMedia, (await GetHistoriesAsync(asset.Id))[0].Media.Count);
    }

    [Fact]
    public async Task Update_OverEntryLimit_Returns400AndUploadsNothing()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var entry = await RecordFullConditionAsync(asset.Id, AssetCondition.Damaged);

        var response = await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(photoCount: 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(RoomAssetConditionHistory.MaxMedia, factory.Storage.Files.Count);
    }

    [Fact]
    public async Task Update_FullEntry_RemovingOneMakesRoomForOne()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var entry = await RecordFullConditionAsync(asset.Id, AssetCondition.Damaged);

        var updated = await ReadHistoryAsync(
            await PatchHistoryAsync(asset.Id, entry.Id, UpdateForm(photoCount: 1, removeMediaIds: [entry.Media[0].MediaId])));

        Assert.Equal(RoomAssetConditionHistory.MaxMedia, updated.Media.Count);
        Assert.Equal(RoomAssetConditionHistory.MaxMedia, factory.Storage.Files.Count);
    }

    [Fact]
    public async Task Update_UnknownHistory_Returns404()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        Assert.Equal(HttpStatusCode.NotFound, (await PatchHistoryAsync(asset.Id, Guid.NewGuid(), UpdateForm(note: "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PatchHistoryAsync(asset.Id, Guid.NewGuid(), UpdateForm(photoCount: 1))).StatusCode);
        Assert.Empty(factory.Storage.Files);
    }

    private static MultipartFormDataContent ConditionForm(string? condition, int photoCount, string? note = null)
    {
        var form = PngForm(photoCount);
        if (condition is not null)
        {
            form.Add(new StringContent(condition), "newCondition");
        }

        if (note is not null)
        {
            form.Add(new StringContent(note), "note");
        }

        return form;
    }

    private Task<HttpResponseMessage> PostConditionFormAsync(
        Guid assetId, MultipartFormDataContent form, HttpClient? client = null) =>
        (client ?? _client).PostAsync($"/api/rooms/{_roomId}/assets/{assetId}/condition-histories", form);

    [Fact]
    public async Task RecordCondition_CreatesEntryWithPhotosAndUpdatesAsset()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        var response = await PostConditionFormAsync(asset.Id, ConditionForm("damaged", 2, "Broken leg"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetConditionHistoryResponse>>(JsonOptions))!.Data!;
        Assert.Equal(AssetCondition.Good, created.OldCondition);
        Assert.Equal(AssetCondition.Damaged, created.NewCondition);
        Assert.Equal("Broken leg", created.Note);
        Assert.Equal(2, created.Media.Count);
        Assert.All(created.Media, m => Assert.Contains($"/organizations/{_organizationId}/rooms/{_roomId}/assets/{asset.Id}/", m.Url));
        Assert.Equal(2, factory.Storage.Files.Count);

        var histories = await GetHistoriesAsync(asset.Id);
        Assert.Equal(2, histories.Count);
        Assert.Equal(created.Media.Select(m => m.MediaId), histories[0].Media.Select(m => m.MediaId));

        var current = Assert.Single((await GetRoomAsync(_roomId)).Assets);
        Assert.Equal(AssetCondition.Damaged, current.Condition);
        Assert.Equal(created.Id, current.CurrentPhotos!.ConditionHistoryId);
        Assert.True(current.CurrentPhotos.IsLatest);

        var rows = await GetPhotoRowsIncludingDeletedAsync(created.Id);
        Assert.Equal([0, 1], rows.Select(r => r.SortOrder).Order());
        Assert.All(rows, r =>
        {
            Assert.Equal(MediaAssetEntityType.RoomAssetConditionPhoto, r.MediaAsset!.EntityType);
            Assert.Null(r.MediaAsset.EntityId);
            Assert.Equal(_organizationId, r.MediaAsset.OrganizationId);
        });
    }

    [Fact]
    public async Task RecordCondition_NoFiles_CreatesEntryWithoutPhotos()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        var response = await PostConditionFormAsync(asset.Id, ConditionForm("broken", 0));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomAssetConditionHistoryResponse>>(JsonOptions))!.Data!;
        Assert.Equal(AssetCondition.Broken, created.NewCondition);
        Assert.Empty(created.Media);
        Assert.Empty(factory.Storage.Files);
    }

    [Fact]
    public async Task RecordCondition_MaxFilesPerRequest_CreatesEntryWithAllPhotos()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        var created = await RecordConditionAsync(asset.Id, AssetCondition.Damaged, photoCount: MediaLimits.MaxFilesPerRequest);

        Assert.Equal(MediaLimits.MaxFilesPerRequest, created.Media.Count);
        Assert.Equal(MediaLimits.MaxFilesPerRequest, factory.Storage.Files.Count);
    }

    [Fact]
    public async Task RecordCondition_FilledUpOverSeveralRequests_HoldsMaxMediaPhotos()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        var created = await RecordFullConditionAsync(asset.Id, AssetCondition.Damaged);

        Assert.Equal(RoomAssetConditionHistory.MaxMedia, created.Media.Count);
        Assert.Equal(RoomAssetConditionHistory.MaxMedia, factory.Storage.Files.Count);
    }

    [Fact]
    public async Task RecordCondition_TooManyFiles_Returns400AndRecordsNothing()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        var response = await PostConditionFormAsync(asset.Id, ConditionForm("damaged", MediaLimits.MaxFilesPerRequest + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Storage.Files);
        Assert.Single(await GetHistoriesAsync(asset.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("melted")]
    public async Task RecordCondition_MissingOrUnknownCondition_Returns400(string? condition)
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });

        var response = await PostConditionFormAsync(asset.Id, ConditionForm(condition, 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Storage.Files);
        Assert.Single(await GetHistoriesAsync(asset.Id));
    }

    [Fact]
    public async Task RecordCondition_MemberOfAnotherOrganization_Returns404AndUploadsNothing()
    {
        var asset = await CreateAssetAsync(_client, _roomId, new CreateRoomAssetRequest { Name = "Bed" });
        var (memberClient, _) = await CreateOrganizationScopedMemberAsync("organization_admin");

        var response = await PostConditionFormAsync(asset.Id, ConditionForm("damaged", 1), memberClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Storage.Files);
        Assert.Single(await GetHistoriesAsync(asset.Id));
    }
}
