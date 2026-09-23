using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.DTOs.Properties;
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

public class RoomsControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "room-actor@test.com";
    private const string ActorPassword = "password123";

    private Guid _actorId;
    private Guid _organizationId;
    private Guid _propertyId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        _organizationId = await CreateOrganizationAsync("Room Org");
        _propertyId = await CreatePropertyAsync(_client, _organizationId, "Room Property");
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
            Phone = "0900000095",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "Room Actor"
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

    private CreateRoomRequest ValidCreateRequest() => new()
    {
        PropertyId = _propertyId,
        RoomNumber = "101"
    };

    private async Task<RoomResponse> CreateRoomAsync(HttpClient client, CreateRoomRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
    }

    private async Task<PagedResult<RoomResponse>> GetRoomsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<RoomResponse>>>(JsonOptions))!.Data!;
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
        var response = await _client.PostAsJsonAsync("/api/rooms", ValidCreateRequest() with
        {
            RoomCategory = RoomCategory.Studio,
            FloorNumber = 1,
            Area = 25.5m,
            Capacity = 2,
            MonthlyRent = 3_000_000,
            DepositAmount = 3_000_000,
            Note = "Corner room"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var created = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))?.Data;
        Assert.NotNull(created);
        Assert.Equal(_propertyId, created!.PropertyId);
        Assert.Equal("101", created.RoomNumber);
        Assert.Equal(RoomStatus.Available, created.RoomStatus);

        var getResponse = await _client.GetAsync($"/api/rooms/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = (await getResponse.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))?.Data;
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal(RoomCategory.Studio, fetched.RoomCategory);
        Assert.Equal(RoomStatus.Available, fetched.RoomStatus);
        Assert.Equal(1, fetched.FloorNumber);
        Assert.Equal(25.5m, fetched.Area);
        Assert.Equal(2, fetched.Capacity);
        Assert.Equal(3_000_000, fetched.MonthlyRent);
        Assert.Equal(3_000_000, fetched.DepositAmount);
        Assert.Equal("Corner room", fetched.Note);
    }

    [Fact]
    public async Task Create_PropertyIdNotFound_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/rooms", ValidCreateRequest() with { PropertyId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidPayload_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/rooms", ValidCreateRequest() with { RoomNumber = "", Capacity = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_RoomCategoryOutOfRangeInteger_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/rooms", new { propertyId = _propertyId, roomNumber = "101", roomCategory = 99 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_DuplicateRoomNumberInSameProperty_ReturnsConflict()
    {
        await CreateRoomAsync(_client, ValidCreateRequest());

        var secondResponse = await _client.PostAsJsonAsync("/api/rooms", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Create_SameRoomNumberInDifferentProperty_Returns201()
    {
        var otherPropertyId = await CreatePropertyAsync(_client, _organizationId, "Other Property");
        await CreateRoomAsync(_client, ValidCreateRequest());

        var secondResponse = await _client.PostAsJsonAsync("/api/rooms", ValidCreateRequest() with { PropertyId = otherPropertyId });

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Create_SameRoomNumberAfterSoftDelete_Returns201()
    {
        var room = await CreateRoomAsync(_client, ValidCreateRequest());
        await _client.DeleteAsync($"/api/rooms/{room.Id}");

        var response = await _client.PostAsJsonAsync("/api/rooms", ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Update_RoomNumberTakenInSameProperty_ReturnsConflict()
    {
        await CreateRoomAsync(_client, ValidCreateRequest());
        var other = await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = "102" });

        var response = await _client.PatchAsJsonAsync($"/api/rooms/{other.Id}", new UpdateRoomRequest { RoomNumber = "101" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_OnlyRoomStatus_KeepsOtherFieldsUnchanged()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with
        {
            RoomCategory = RoomCategory.Duplex,
            FloorNumber = 2,
            MonthlyRent = 4_000_000,
            Note = "Keep me"
        });

        var response = await _client.PatchAsJsonAsync($"/api/rooms/{created.Id}", new UpdateRoomRequest { RoomStatus = RoomStatus.Maintenance });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))?.Data;
        Assert.Equal(RoomStatus.Maintenance, updated!.RoomStatus);
        Assert.Equal("101", updated.RoomNumber);
        Assert.Equal(RoomCategory.Duplex, updated.RoomCategory);
        Assert.Equal(2, updated.FloorNumber);
        Assert.Equal(4_000_000, updated.MonthlyRent);
        Assert.Equal("Keep me", updated.Note);
        Assert.Equal(_propertyId, updated.PropertyId);
    }

    [Fact]
    public async Task Update_NullableFieldSetToNull_ClearsField()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Note = "Temporary", MonthlyRent = 4_000_000 });

        var response = await _client.PatchAsJsonAsync($"/api/rooms/{created.Id}", new UpdateRoomRequest { Note = null });

        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))?.Data;
        Assert.Null(updated!.Note);
        Assert.Equal(4_000_000, updated.MonthlyRent);
    }

    [Fact]
    public async Task FullLifecycle_CreateUpdateDelete_BehavesCorrectly()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest());

        var updateResponse = await _client.PatchAsJsonAsync($"/api/rooms/{created.Id}", new UpdateRoomRequest
        {
            RoomNumber = "201",
            Capacity = 3
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = (await updateResponse.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))?.Data;
        Assert.Equal("201", updated!.RoomNumber);
        Assert.Equal(3, updated.Capacity);
        Assert.NotNull(updated.UpdatedAt);

        var deleteResponse = await _client.DeleteAsync($"/api/rooms/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDeleteResponse = await _client.GetAsync($"/api/rooms/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);

        var list = await GetRoomsAsync(_client, "/api/rooms");
        Assert.DoesNotContain(list.Items, r => r.Id == created.Id);
    }

    [Fact]
    public async Task DeleteProperty_WithRooms_ReturnsConflictUntilRoomsAreDeleted()
    {
        var room = await CreateRoomAsync(_client, ValidCreateRequest());

        var blockedResponse = await _client.DeleteAsync($"/api/properties/{_propertyId}");
        Assert.Equal(HttpStatusCode.Conflict, blockedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"/api/rooms/{room.Id}")).StatusCode);

        await _client.DeleteAsync($"/api/rooms/{room.Id}");

        var allowedResponse = await _client.DeleteAsync($"/api/properties/{_propertyId}");
        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
    }

    [Fact]
    public async Task Endpoints_UnknownId_Return404()
    {
        var unknownId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/rooms/{unknownId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.PatchAsJsonAsync(
            $"/api/rooms/{unknownId}", new UpdateRoomRequest { RoomNumber = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/rooms/{unknownId}")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_WithoutAuthentication_Return401()
    {
        using var anonymousClient = factory.CreateClient();
        var room = await CreateRoomAsync(_client, ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync("/api/rooms")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PostAsJsonAsync("/api/rooms", ValidCreateRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.PatchAsJsonAsync(
            $"/api/rooms/{room.Id}", new UpdateRoomRequest { RoomNumber = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymousClient.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_UserWithoutPlatformPermissionOrMembership_Return403()
    {
        using var unprivilegedClient = factory.CreateClient();
        var registerResponse = await unprivilegedClient.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = "no-permission-room@test.com",
            Phone = "0900000094",
            Password = ActorPassword,
            PasswordConfirmation = ActorPassword,
            FullName = "No Permission User"
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await unprivilegedClient.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "no-permission-room@test.com",
            Password = ActorPassword
        });
        unprivilegedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        var room = await CreateRoomAsync(_client, ValidCreateRequest());

        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.GetAsync("/api/rooms")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.PostAsJsonAsync(
            "/api/rooms", ValidCreateRequest() with { RoomNumber = "999" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.PatchAsJsonAsync(
            $"/api/rooms/{room.Id}", new UpdateRoomRequest { RoomNumber = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedClient.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetAll_MultiplePages_ReturnsCorrectPageAndMetadata()
    {
        for (var i = 0; i < 25; i++)
        {
            await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = $"R{i:D2}" });
        }

        var page1 = await GetRoomsAsync(_client, "/api/rooms?page=1&pageSize=10");
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(25, page1.TotalItems);

        var page3 = await GetRoomsAsync(_client, "/api/rooms?page=3&pageSize=10");
        Assert.Equal(5, page3.Items.Count);
    }

    [Fact]
    public async Task GetAll_SearchByRoomNumber_ReturnsMatchingItemsOnly()
    {
        await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = "A101" });
        await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = "B202" });

        var result = await GetRoomsAsync(_client, "/api/rooms?search=a10");

        Assert.Single(result.Items);
        Assert.Equal("A101", result.Items[0].RoomNumber);
    }

    [Fact]
    public async Task GetAll_FilterByPropertyId_ReturnsOnlyThatPropertysRooms()
    {
        var otherPropertyId = await CreatePropertyAsync(_client, _organizationId, "Filter Property");
        var own = await CreateRoomAsync(_client, ValidCreateRequest());
        var other = await CreateRoomAsync(_client, ValidCreateRequest() with { PropertyId = otherPropertyId });

        var result = await GetRoomsAsync(_client, $"/api/rooms?propertyId={otherPropertyId}");

        Assert.Contains(result.Items, r => r.Id == other.Id);
        Assert.DoesNotContain(result.Items, r => r.Id == own.Id);
    }

    [Fact]
    public async Task GetAll_FilterByRoomCategory_ReturnsMatchingItemsOnly()
    {
        var standard = await CreateRoomAsync(_client, ValidCreateRequest());
        var studio = await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = "102", RoomCategory = RoomCategory.Studio });

        var result = await GetRoomsAsync(_client, "/api/rooms?roomCategory=studio");

        Assert.All(result.Items, r => Assert.Equal(RoomCategory.Studio, r.RoomCategory));
        Assert.Contains(result.Items, r => r.Id == studio.Id);
        Assert.DoesNotContain(result.Items, r => r.Id == standard.Id);
    }

    [Fact]
    public async Task GetAll_FilterByRoomStatus_ReturnsMatchingItemsOnly()
    {
        var available = await CreateRoomAsync(_client, ValidCreateRequest());
        var maintenance = await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = "102" });
        await _client.PatchAsJsonAsync($"/api/rooms/{maintenance.Id}", new UpdateRoomRequest { RoomStatus = RoomStatus.Maintenance });

        var result = await GetRoomsAsync(_client, "/api/rooms?roomStatus=maintenance");

        Assert.All(result.Items, r => Assert.Equal(RoomStatus.Maintenance, r.RoomStatus));
        Assert.Contains(result.Items, r => r.Id == maintenance.Id);
        Assert.DoesNotContain(result.Items, r => r.Id == available.Id);
    }

    [Fact]
    public async Task GetAll_SortByRoomNumberDescending_ReturnsInDescendingOrder()
    {
        await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = "A" });
        await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = "Z" });

        var result = await GetRoomsAsync(_client, "/api/rooms?sortBy=roomNumber&sortOrder=desc");

        var roomNumbers = result.Items.Select(r => r.RoomNumber).ToList();
        Assert.Equal(["Z", "A"], roomNumbers);
    }

    [Fact]
    public async Task GetAll_SortByUnknownField_FallsBackToDefaultWithoutError()
    {
        var response = await _client.GetAsync("/api/rooms?sortBy=unknown-field");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OrganizationScope_MemberOfOrganizationA_CanOnlyAccessRoomsOfOwnOrganizationsProperties()
    {
        var (memberClient, organizationAId) = await CreateOrganizationScopedMemberAsync("organization_admin");
        var propertyAId = await CreatePropertyAsync(_client, organizationAId, "Property A");
        var organizationBId = await CreateOrganizationAsync("Org B");
        var propertyBId = await CreatePropertyAsync(_client, organizationBId, "Property B");

        var createOwnResponse = await memberClient.PostAsJsonAsync(
            "/api/rooms", new CreateRoomRequest { PropertyId = propertyAId, RoomNumber = "A1" });
        Assert.Equal(HttpStatusCode.Created, createOwnResponse.StatusCode);
        var own = (await createOwnResponse.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))?.Data;

        var createOtherResponse = await memberClient.PostAsJsonAsync(
            "/api/rooms", new CreateRoomRequest { PropertyId = propertyBId, RoomNumber = "B1" });
        Assert.Equal(HttpStatusCode.NotFound, createOtherResponse.StatusCode);

        var otherOrgRoom = await CreateRoomAsync(_client, new CreateRoomRequest { PropertyId = propertyBId, RoomNumber = "B1" });

        var list = await GetRoomsAsync(memberClient, "/api/rooms");
        Assert.Contains(list.Items, r => r.Id == own!.Id);
        Assert.DoesNotContain(list.Items, r => r.Id == otherOrgRoom.Id);

        Assert.Equal(HttpStatusCode.OK, (await memberClient.GetAsync($"/api/rooms/{own!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.GetAsync($"/api/rooms/{otherOrgRoom.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PatchAsJsonAsync(
            $"/api/rooms/{otherOrgRoom.Id}", new UpdateRoomRequest { RoomNumber = "X" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.DeleteAsync($"/api/rooms/{otherOrgRoom.Id}")).StatusCode);

        var stillThere = await _client.GetAsync($"/api/rooms/{otherOrgRoom.Id}");
        var fetched = (await stillThere.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))?.Data;
        Assert.Equal("B1", fetched!.RoomNumber);
    }

    [Fact]
    public async Task GetAll_HeaderOrganizationId_ReturnsOnlyThatOrganizationsRooms()
    {
        var otherOrganizationId = await CreateOrganizationAsync("Header Room Org");
        var otherPropertyId = await CreatePropertyAsync(_client, otherOrganizationId, "Header Room Property");
        var own = await CreateRoomAsync(_client, ValidCreateRequest());
        var other = await CreateRoomAsync(_client, new CreateRoomRequest { PropertyId = otherPropertyId, RoomNumber = "201" });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/rooms");
        request.Headers.Add("X-Organization-Id", _organizationId.ToString());

        var response = await _client.SendAsync(request);
        var result = (await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<RoomResponse>>>(JsonOptions))?.Data;

        Assert.Contains(result!.Items, r => r.Id == own.Id);
        Assert.DoesNotContain(result.Items, r => r.Id == other.Id);
    }

    [Fact]
    public async Task GetAll_HeaderOrganizationIdOutsideCallerScope_Returns404()
    {
        var (memberClient, _) = await CreateOrganizationScopedMemberAsync("organization_admin");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/rooms");
        request.Headers.Add("X-Organization-Id", _organizationId.ToString());

        var response = await memberClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProperty_WhileRoomCreationHoldsPropertyLock_WaitsThenReturnsConflict()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Simulate an in-flight room creation: property row locked FOR SHARE, room inserted but not yet committed.
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await context.Properties.FromSql($"SELECT * FROM properties WHERE id = {_propertyId} FOR SHARE").SingleAsync();
            context.Rooms.Add(new Room { PropertyId = _propertyId, RoomNumber = "LOCK1", CreatedBy = _actorId });
            await context.SaveChangesAsync();

            var deleteTask = _client.DeleteAsync($"/api/properties/{_propertyId}");
            await Task.Delay(500);
            Assert.False(deleteTask.IsCompleted);

            await transaction.CommitAsync();

            Assert.Equal(HttpStatusCode.Conflict, (await deleteTask).StatusCode);
        }
    }

    [Fact]
    public async Task CreateRoom_WhilePropertyDeletionHoldsPropertyLock_WaitsThenReturns404()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Simulate an in-flight property deletion: property row locked FOR UPDATE and soft-deleted but not yet committed.
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var property = await context.Properties
                .FromSql($"SELECT * FROM properties WHERE id = {_propertyId} FOR UPDATE").SingleAsync();
            context.Properties.Remove(property);
            await context.SaveChangesAsync();

            var createTask = _client.PostAsJsonAsync("/api/rooms", ValidCreateRequest());
            await Task.Delay(500);
            Assert.False(createTask.IsCompleted);

            await transaction.CommitAsync();

            Assert.Equal(HttpStatusCode.NotFound, (await createTask).StatusCode);
        }

        Assert.False(await context.Rooms.IgnoreQueryFilters().AnyAsync(r => r.PropertyId == _propertyId));
    }
}
