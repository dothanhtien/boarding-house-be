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

    private async Task<List<RoomAmenity>> GetAmenitiesIncludingDeletedAsync(Guid roomId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.RoomAmenities.IgnoreQueryFilters().Where(a => a.RoomId == roomId).ToListAsync();
    }

    private static List<CreateRoomAmenityRequest> Amenities(params (string Name, int? Quantity)[] items) =>
        items.Select(i => new CreateRoomAmenityRequest { Name = i.Name, Quantity = i.Quantity }).ToList();

    [Fact]
    public async Task ByIdEndpoints_WithMultipleRooms_ActOnRequestedRoomOnly()
    {
        var first = await CreateRoomAsync(_client, ValidCreateRequest());
        var second = await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = "102" });

        var fetched = (await (await _client.GetAsync($"/api/rooms/{second.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal(second.Id, fetched.Id);
        Assert.Equal("102", fetched.RoomNumber);

        var updated = (await (await _client.PatchAsJsonAsync($"/api/rooms/{second.Id}", new UpdateRoomRequest { Note = "Second" }))
            .Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal(second.Id, updated.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/rooms/{second.Id}")).StatusCode);

        var firstAfter = (await (await _client.GetAsync($"/api/rooms/{first.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal("101", firstAfter.RoomNumber);
        Assert.Null(firstAfter.Note);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/rooms/{second.Id}")).StatusCode);
    }

    [Fact]
    public async Task Create_WithAmenities_ReturnsAmenitiesSortedByNameInAllResponses()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with
        {
            Amenities = Amenities(("WiFi", null), ("Air conditioner", 2), ("Balcony", null))
        });

        Assert.Equal(["Air conditioner", "Balcony", "WiFi"], created.Amenities.Select(a => a.Name));
        Assert.Equal(2, created.Amenities.Single(a => a.Name == "Air conditioner").Quantity);
        Assert.Null(created.Amenities.Single(a => a.Name == "WiFi").Quantity);

        var fetched = (await (await _client.GetAsync($"/api/rooms/{created.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal(["Air conditioner", "Balcony", "WiFi"], fetched.Amenities.Select(a => a.Name));

        var list = await GetRoomsAsync(_client, "/api/rooms");
        Assert.Equal(3, list.Items.Single(r => r.Id == created.Id).Amenities.Count);
    }

    [Fact]
    public async Task Create_WithoutAmenities_ReturnsEmptyAmenities()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest());

        Assert.Empty(created.Amenities);
    }

    [Fact]
    public async Task Create_DuplicateAmenityNames_CreatesBoth()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with
        {
            Amenities = Amenities(("WiFi", 1), ("WiFi", 2))
        });

        Assert.Equal(2, created.Amenities.Count(a => a.Name == "WiFi"));
    }

    [Fact]
    public async Task Update_AmenitiesNotSent_KeepsAmenitiesUnchanged()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Amenities = Amenities(("WiFi", 1)) });

        var response = await _client.PatchAsJsonAsync($"/api/rooms/{created.Id}", new UpdateRoomRequest { Note = "Changed" });

        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal(created.Amenities.Single().Id, updated.Amenities.Single().Id);
    }

    private async Task<HttpResponseMessage> PatchAmenitiesAsync(Guid roomId, params UpdateRoomAmenityRequest[] changes) =>
        await _client.PatchAsJsonAsync($"/api/rooms/{roomId}", new UpdateRoomRequest { Amenities = changes.ToList() });

    [Fact]
    public async Task Update_AmenityChanges_MergesByIdAndLeavesUnlistedAmenitiesUntouched()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with
        {
            Amenities = Amenities(("WiFi", 1), ("Bed", 1), ("Balcony", 1))
        });
        var wifiId = created.Amenities.Single(a => a.Name == "WiFi").Id;
        var bedId = created.Amenities.Single(a => a.Name == "Bed").Id;
        var balconyId = created.Amenities.Single(a => a.Name == "Balcony").Id;

        // WiFi not listed, Bed quantity changed, Balcony deleted, Fridge added.
        var response = await PatchAmenitiesAsync(created.Id,
            new UpdateRoomAmenityRequest { Id = bedId, Quantity = 2 },
            new UpdateRoomAmenityRequest { Id = balconyId, IsDeleted = true },
            new UpdateRoomAmenityRequest { Name = "Fridge" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal(["Bed", "Fridge", "WiFi"], updated.Amenities.Select(a => a.Name));
        Assert.Equal(wifiId, updated.Amenities.Single(a => a.Name == "WiFi").Id);
        Assert.Equal(bedId, updated.Amenities.Single(a => a.Name == "Bed").Id);
        Assert.Equal(2, updated.Amenities.Single(a => a.Name == "Bed").Quantity);
        Assert.Null(updated.Amenities.Single(a => a.Name == "Fridge").Quantity);

        var balcony = (await GetAmenitiesIncludingDeletedAsync(created.Id)).Single(a => a.Id == balconyId);
        Assert.NotNull(balcony.DeletedAt);
        Assert.Equal(_actorId, balcony.DeletedBy);

        var fetched = (await (await _client.GetAsync($"/api/rooms/{created.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal(["Bed", "Fridge", "WiFi"], fetched.Amenities.Select(a => a.Name));
    }

    [Fact]
    public async Task Update_RenameExistingAmenity_KeepsIdAndQuantity()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Amenities = Amenities(("Wifi", 2)) });
        var amenityId = created.Amenities.Single().Id;

        var response = await PatchAmenitiesAsync(created.Id, new UpdateRoomAmenityRequest { Id = amenityId, Name = "WiFi" });

        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        var amenity = updated.Amenities.Single();
        Assert.Equal(amenityId, amenity.Id);
        Assert.Equal("WiFi", amenity.Name);
        Assert.Equal(2, amenity.Quantity);
    }

    [Fact]
    public async Task Update_AmenityQuantitySetToNull_ClearsQuantity()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Amenities = Amenities(("WiFi", 2)) });
        var amenityId = created.Amenities.Single().Id;

        var response = await PatchAmenitiesAsync(created.Id, new UpdateRoomAmenityRequest { Id = amenityId, Quantity = null });

        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Null(updated.Amenities.Single().Quantity);
    }

    [Fact]
    public async Task Create_AmenityWithIcon_ReturnsIcon()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with
        {
            Amenities = [new CreateRoomAmenityRequest { Name = "Air conditioner", Icon = "air-conditioner" }, new CreateRoomAmenityRequest { Name = "Bed" }]
        });

        Assert.Equal("air-conditioner", created.Amenities.Single(a => a.Name == "Air conditioner").Icon);
        Assert.Null(created.Amenities.Single(a => a.Name == "Bed").Icon);
    }

    [Fact]
    public async Task Update_AmenityIcon_SetsKeepsAndClearsIcon()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with
        {
            Amenities = [new CreateRoomAmenityRequest { Name = "WiFi", Icon = "wifi" }, new CreateRoomAmenityRequest { Name = "Bed" }]
        });
        var wifiId = created.Amenities.Single(a => a.Name == "WiFi").Id;
        var bedId = created.Amenities.Single(a => a.Name == "Bed").Id;

        var response = await PatchAmenitiesAsync(created.Id,
            new UpdateRoomAmenityRequest { Id = bedId, Icon = "bed" },
            new UpdateRoomAmenityRequest { Id = wifiId, Quantity = 2 });
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal("bed", updated.Amenities.Single(a => a.Name == "Bed").Icon);
        Assert.Equal("wifi", updated.Amenities.Single(a => a.Name == "WiFi").Icon);

        response = await PatchAmenitiesAsync(created.Id, new UpdateRoomAmenityRequest { Id = wifiId, Icon = null });
        updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Null(updated.Amenities.Single(a => a.Name == "WiFi").Icon);
    }

    [Fact]
    public async Task Update_AmenitiesEmptyList_LeavesAmenitiesUnchanged()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Amenities = Amenities(("WiFi", 1), ("Bed", 1)) });

        var response = await PatchAmenitiesAsync(created.Id);

        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal(2, updated.Amenities.Count);
    }

    [Fact]
    public async Task Update_DeleteAllByFlag_RemovesAllAmenities()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Amenities = Amenities(("WiFi", 1), ("Bed", 1)) });

        var response = await PatchAmenitiesAsync(created.Id,
            created.Amenities.Select(a => new UpdateRoomAmenityRequest { Id = a.Id, IsDeleted = true }).ToArray());

        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Empty(updated.Amenities);
        Assert.All(await GetAmenitiesIncludingDeletedAsync(created.Id), a => Assert.NotNull(a.DeletedAt));
    }

    [Fact]
    public async Task Update_DeleteAndReAddSameNameInOneRequest_Succeeds()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Amenities = Amenities(("WiFi", 1)) });
        var oldId = created.Amenities.Single().Id;

        var response = await PatchAmenitiesAsync(created.Id,
            new UpdateRoomAmenityRequest { Id = oldId, IsDeleted = true },
            new UpdateRoomAmenityRequest { Name = "WiFi", Quantity = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        var amenity = updated.Amenities.Single();
        Assert.NotEqual(oldId, amenity.Id);
        Assert.Equal(3, amenity.Quantity);
    }

    [Fact]
    public async Task Update_AddOrRenameToNameAlreadyUsed_Succeeds()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Amenities = Amenities(("WiFi", 1), ("Bed", 1)) });
        var bedId = created.Amenities.Single(a => a.Name == "Bed").Id;

        Assert.Equal(HttpStatusCode.OK,
            (await PatchAmenitiesAsync(created.Id, new UpdateRoomAmenityRequest { Name = "WiFi" })).StatusCode);
        var response = await PatchAmenitiesAsync(created.Id, new UpdateRoomAmenityRequest { Id = bedId, Name = "WiFi" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal(3, updated.Amenities.Count(a => a.Name == "WiFi"));
    }

    [Fact]
    public async Task Update_AmenityIdNotInRoom_Returns404AndChangesNothing()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Amenities = Amenities(("WiFi", 1)) });
        var otherRoom = await CreateRoomAsync(_client, ValidCreateRequest() with { RoomNumber = "102", Amenities = Amenities(("Bed", 1)) });

        var response = await _client.PatchAsJsonAsync($"/api/rooms/{created.Id}", new UpdateRoomRequest
        {
            Note = "Should not persist",
            Amenities = new List<UpdateRoomAmenityRequest> { new() { Id = otherRoom.Amenities.Single().Id, IsDeleted = true } }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var otherAfter = (await (await _client.GetAsync($"/api/rooms/{otherRoom.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Single(otherAfter.Amenities);
        var roomAfter = (await (await _client.GetAsync($"/api/rooms/{created.Id}"))
            .Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Null(roomAfter.Note);
    }

    [Fact]
    public async Task Update_AmenitiesExceedRoomLimit_Returns400AndChangesNothing()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with
        {
            Amenities = Enumerable.Range(0, 15).Select(i => new CreateRoomAmenityRequest { Name = $"Amenity {i}" }).ToList()
        });

        // 15 existing - 1 deleted + 7 added = 21 > 20.
        var changes = Enumerable.Range(0, 7).Select(i => new UpdateRoomAmenityRequest { Name = $"New {i}" })
            .Prepend(new UpdateRoomAmenityRequest { Id = created.Amenities[0].Id, IsDeleted = true })
            .ToArray();
        var response = await PatchAmenitiesAsync(created.Id, changes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(15, (await GetAmenitiesIncludingDeletedAsync(created.Id)).Count(a => a.DeletedAt is null));
    }

    [Fact]
    public async Task Update_AmenitiesReachRoomLimit_Succeeds()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with
        {
            Amenities = Enumerable.Range(0, 15).Select(i => new CreateRoomAmenityRequest { Name = $"Amenity {i}" }).ToList()
        });

        // 15 existing - 1 deleted + 6 added = 20.
        var changes = Enumerable.Range(0, 6).Select(i => new UpdateRoomAmenityRequest { Name = $"New {i}" })
            .Prepend(new UpdateRoomAmenityRequest { Id = created.Amenities[0].Id, IsDeleted = true })
            .ToArray();
        var response = await PatchAmenitiesAsync(created.Id, changes);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<ApiResponse<RoomResponse>>(JsonOptions))!.Data!;
        Assert.Equal(20, updated.Amenities.Count);
    }

    [Fact]
    public async Task Update_AmenitiesNull_Returns400()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest());

        var response = await _client.PatchAsJsonAsync($"/api/rooms/{created.Id}", new { amenities = (object?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RoomWithAmenities_SoftDeletesAllAmenities()
    {
        var created = await CreateRoomAsync(_client, ValidCreateRequest() with { Amenities = Amenities(("WiFi", 1), ("Bed", 1)) });

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/rooms/{created.Id}")).StatusCode);

        var stored = await GetAmenitiesIncludingDeletedAsync(created.Id);
        Assert.Equal(2, stored.Count);
        Assert.All(stored, a => Assert.NotNull(a.DeletedAt));
    }
}
