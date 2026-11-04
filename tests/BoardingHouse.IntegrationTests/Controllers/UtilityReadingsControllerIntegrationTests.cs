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
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.DTOs.Properties;
using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.DTOs.UtilityReadings;
using BoardingHouse.Application.DTOs.UtilityServices;

namespace BoardingHouse.IntegrationTests.Controllers;

public class UtilityReadingsControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "utility-reading-actor@test.com";
    private const string Password = "password123";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly CurrentMonth => BillingMonth.Of(Today);

    private Guid _actorId;
    private Guid _organizationId;
    private Guid _propertyId;
    private Guid _roomId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        _organizationId = await CreateOrganizationAsync("Utility Reading Org");
        _propertyId = await CreatePropertyAsync(_organizationId, "Utility Reading Property");
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
            Phone = "0900000092",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Utility Reading Actor"
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
        string name, decimal defaultUnitPrice, UtilityBillingMethod billingMethod, string unit, Guid? propertyId = null)
    {
        var response = await _client.PostAsJsonAsync("/api/utility-services", new CreateUtilityServiceRequest
        {
            PropertyId = propertyId ?? _propertyId,
            Name = name,
            Type = UtilityType.Other,
            BillingMethod = billingMethod,
            Unit = unit,
            DefaultUnitPrice = defaultUnitPrice,
            IsActive = true
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

    private async Task<LeaseResponse> CreateActiveLeaseAsync(List<CreateLeaseUtilityServiceRequest> utilityServices)
    {
        var createResponse = await _client.PostAsJsonAsync("/api/leases", new CreateLeaseRequest
        {
            RoomId = _roomId,
            StartDate = Today,
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync(), IsRepresentative = true }],
            UtilityServices = utilityServices
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var lease = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<LeaseResponse>>(JsonOptions))!.Data!;

        var activateResponse = await _client.PostAsync($"/api/leases/{lease.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        return lease;
    }

    // Defaults to CurrentMonth read on the 1st of the month before; RecordNextMonthAsync continues the chain
    private async Task RecordAsync(
        Guid serviceId, decimal currentValue, decimal? previousValue = null, DateOnly? readingMonth = null, DateOnly? readOn = null)
    {
        var response = await _client.PostAsJsonAsync($"/api/rooms/{_roomId}/utility-readings", new RecordRoomUtilityReadingsRequest
        {
            ReadingMonth = readingMonth ?? CurrentMonth,
            ReadOn = readOn ?? CurrentMonth.AddMonths(-1),
            Items = [new RecordRoomUtilityReadingItemRequest { ServiceId = serviceId, CurrentValue = currentValue, PreviousValue = previousValue }]
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task RecordNextMonthAsync(Guid serviceId, decimal currentValue) =>
        RecordAsync(serviceId, currentValue, readingMonth: CurrentMonth.AddMonths(1), readOn: Today);

    private Task<HttpResponseMessage> DeleteReadingAsync(Guid readingId, HttpClient? client = null) =>
        (client ?? _client).DeleteAsync($"/api/rooms/{_roomId}/utility-readings/{readingId}");

    // No image endpoint yet: the asset and its stored file are written directly
    private async Task<MediaAsset> AttachImageAsync(Guid readingId)
    {
        var storageKey = $"test/utility-readings/{Guid.NewGuid():N}";
        factory.Storage.Files[storageKey] = [1, 2, 3];

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var asset = new MediaAsset
        {
            OrganizationId = _organizationId,
            FileName = "meter.png",
            StorageProvider = StorageProvider.Cloudinary,
            StorageKey = storageKey,
            FileUrl = $"https://storage.test/{storageKey}",
            MimeType = "image/png",
            FileSize = 3,
            EntityType = MediaAssetEntityType.UtilityReadingImage,
            EntityId = readingId,
            CreatedBy = SentinelActors.System
        };
        context.MediaAssets.Add(asset);
        await context.SaveChangesAsync();
        return asset;
    }

    private static RecordRoomUtilityReadingItemRequest Item(Guid serviceId, decimal currentValue, decimal? previousValue = null) => new()
    {
        ServiceId = serviceId,
        CurrentValue = currentValue,
        PreviousValue = previousValue
    };

    // ReadOn left out = today, which fits CurrentMonth and the month after it
    private static RecordRoomUtilityReadingsRequest Readings(DateOnly readingMonth, params RecordRoomUtilityReadingItemRequest[] items) => new()
    {
        ReadingMonth = readingMonth,
        Items = items.ToList()
    };

    private Task<HttpResponseMessage> PostReadingsAsync(RecordRoomUtilityReadingsRequest request, Guid? roomId = null) =>
        _client.PostAsJsonAsync($"/api/rooms/{roomId ?? _roomId}/utility-readings", request, JsonOptions);

    private async Task<UtilityReadingRoomMetersResponse> PostReadingsOkAsync(RecordRoomUtilityReadingsRequest request, Guid? roomId = null)
    {
        var response = await PostReadingsAsync(request, roomId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ReadErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors").Clone();
    }

    private static UtilityReadingRoomMeterResponse Meter(UtilityReadingRoomMetersResponse room, Guid serviceId) =>
        room.Meters.Single(m => m.ServiceId == serviceId);

    private async Task<UtilityReadingRoomMetersResponse> GetMetersOkAsync(DateOnly readingMonth)
    {
        var response = await GetRoomMetersAsync(_roomId, $"?readingMonth={readingMonth:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private Task<HttpResponseMessage> GetRoomsAsync(string queryString, HttpClient? client = null) =>
        (client ?? _client).GetAsync($"/api/properties/{_propertyId}/utility-readings/rooms{queryString}");

    private async Task<PagedResult<UtilityReadingRoomListItemResponse>> GetRoomsOkAsync(DateOnly readingMonth, string extraQuery = "")
    {
        var response = await GetRoomsAsync($"?readingMonth={readingMonth:yyyy-MM-dd}{extraQuery}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<PagedResult<UtilityReadingRoomListItemResponse>>(response);
    }

    private async Task TerminateAsync(Guid leaseId)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/leases/{leaseId}/terminate", new TerminateLeaseRequest { Reason = "Moving out" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> GetRoomMetersAsync(Guid roomId, string queryString, HttpClient? client = null) =>
        (client ?? _client).GetAsync($"/api/rooms/{roomId}/utility-readings/meters{queryString}");

    private static async Task<UtilityReadingRoomMetersResponse> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<UtilityReadingRoomMetersResponse>>(JsonOptions))!.Data!;

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    // Every reading of the room, newest first (no history endpoint yet)
    private async Task<List<UtilityReading>> GetHistoryAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UtilityReadings
            .AsNoTracking()
            .Where(r => r.RoomId == _roomId)
            .OrderByDescending(r => r.ReadOn)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync();
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
    public async Task GetRoomMeters_WithoutReadingMonth_ReturnsBadRequest()
    {
        var response = await GetRoomMetersAsync(_roomId, "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoomMeters_ReadingMonthNotFirstDay_ReturnsBadRequest()
    {
        var response = await GetRoomMetersAsync(_roomId, $"?readingMonth={CurrentMonth.AddDays(14):yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoomMeters_UnknownRoom_ReturnsNotFound()
    {
        var response = await GetRoomMetersAsync(Guid.NewGuid(), $"?readingMonth={CurrentMonth:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRoomMeters_RoomOfAnotherOrganization_ReturnsNotFound()
    {
        var memberClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await GetRoomMetersAsync(_roomId, $"?readingMonth={CurrentMonth:yyyy-MM-dd}", memberClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRoomMeters_WithLeaseAndReading_ReturnsMetersAndOtherCharges()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        var lease = await CreateActiveLeaseAsync(
        [
            new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id, UnitPriceOverride = 3_000 },
            new CreateLeaseUtilityServiceRequest { ServiceId = internet.Id }
        ]);
        await RecordAsync(electricity.Id, 150, previousValue: 100);

        var response = await GetRoomMetersAsync(_roomId, $"?readingMonth={CurrentMonth:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(_roomId, body.RoomId);
        Assert.Equal(CurrentMonth, body.ReadingMonth);
        Assert.Equal(lease.Id, body.Lease!.LeaseId);

        var meter = Assert.Single(body.Meters);
        Assert.Equal(electricity.Id, meter.ServiceId);
        Assert.Equal(lease.Id, meter.BilledLeaseId);
        Assert.Equal(3_000, meter.EffectiveUnitPrice);
        Assert.Equal(100, meter.Reading!.PreviousValue);
        Assert.Equal(150, meter.Reading.CurrentValue);
        Assert.Equal(50, meter.Reading.Consumption);
        Assert.Equal(150, meter.Latest!.CurrentValue);

        var charge = Assert.Single(body.OtherCharges);
        Assert.Equal(internet.Id, charge.ServiceId);
        Assert.Equal(100_000, charge.UnitPrice);
        Assert.Equal(1, charge.Quantity);
        Assert.Equal(100_000, charge.Amount);
    }

    [Fact]
    public async Task GetRoomMeters_MonthWithoutReading_ReturnsLatestWithoutReading()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await RecordAsync(electricity.Id, 150, previousValue: 100);

        var response = await GetRoomMetersAsync(_roomId, $"?readingMonth={CurrentMonth.AddMonths(1):yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Null(body.Lease);
        Assert.Empty(body.OtherCharges);

        var meter = Assert.Single(body.Meters);
        Assert.Null(meter.Reading);
        Assert.Null(meter.BilledLeaseId);
        Assert.Null(meter.EffectiveUnitPrice);
        Assert.Equal(150, meter.Latest!.CurrentValue);
    }

    [Fact]
    public async Task GetRoomMeters_PastMonth_ShowsTheMetersLatestReading()
    {
        // Same latest as recording sees, so a meter with a later month doesn't look recordable for the past month
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        await RecordAsync(electricity.Id, 150, previousValue: 100);
        await RecordNextMonthAsync(electricity.Id, 200);
        await RecordNextMonthAsync(water.Id, 30);

        var room = await GetMetersOkAsync(CurrentMonth);

        Assert.Equal(200, Meter(room, electricity.Id).Latest!.CurrentValue);
        Assert.Equal(CurrentMonth.AddMonths(1), Meter(room, water.Id).Latest!.ReadingMonth);
    }

    [Fact]
    public async Task GetRoomMeters_DeactivatedFixedService_IsNotCharged()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = internet.Id }]);
        var deactivate = await _client.PatchAsJsonAsync(
            $"/api/utility-services/{internet.Id}", new UpdateUtilityServiceRequest { IsActive = false }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        Assert.Empty((await GetMetersOkAsync(CurrentMonth)).OtherCharges);
    }


















    [Fact]
    public async Task GetRooms_ListsEveryRoomWithItsProgressInRoomNumberOrder()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        var lease = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);
        var emptyRoomId = await CreateRoomAsync(_propertyId, "102");
        await RecordAsync(electricity.Id, 150, previousValue: 100);

        var rooms = await GetRoomsOkAsync(CurrentMonth);

        Assert.Equal(2, rooms.TotalItems);
        Assert.Equal(["101", "102"], rooms.Items.Select(r => r.RoomNumber));

        var occupied = rooms.Items[0];
        Assert.Equal(lease.Id, occupied.Lease!.LeaseId);
        Assert.Equal(lease.LeaseCode, occupied.Lease.LeaseCode);
        Assert.Equal(2, occupied.MeterCount);
        Assert.Equal(1, occupied.RecordedCount);
        Assert.Equal(UtilityReadingRoomStatus.Partial, occupied.Status);

        var empty = rooms.Items[1];
        Assert.Equal(emptyRoomId, empty.RoomId);
        Assert.Null(empty.Lease);
        Assert.Equal(0, empty.RecordedCount);
        Assert.Equal(UtilityReadingRoomStatus.Pending, empty.Status);
    }

    [Fact]
    public async Task GetRooms_RecordedFilter_SplitsFullyRecordedRoomsFromTheRest()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        var partialRoomId = await CreateRoomAsync(_propertyId, "102");
        var pendingRoomId = await CreateRoomAsync(_propertyId, "103");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100), Item(water.Id, 20, 10)));
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 50, 40)), partialRoomId);

        var recorded = Assert.Single((await GetRoomsOkAsync(CurrentMonth, "&recorded=true")).Items);
        Assert.Equal(_roomId, recorded.RoomId);
        Assert.Equal(UtilityReadingRoomStatus.Recorded, recorded.Status);

        var notRecorded = await GetRoomsOkAsync(CurrentMonth, "&recorded=false");
        Assert.Equal(2, notRecorded.TotalItems);
        Assert.Equal([partialRoomId, pendingRoomId], notRecorded.Items.Select(r => r.RoomId));

        Assert.Equal(3, (await GetRoomsOkAsync(CurrentMonth.AddMonths(1), "&recorded=false")).TotalItems);
    }

    [Fact]
    public async Task GetRooms_PropertyWithoutMeters_ListsRoomsAsNoMeters()
    {
        await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");

        var room = Assert.Single((await GetRoomsOkAsync(CurrentMonth)).Items);

        Assert.Equal(0, room.MeterCount);
        Assert.Equal(UtilityReadingRoomStatus.NoMeters, room.Status);
        // Nothing left to record, so it counts as recorded and the two filters still add up to the whole list
        Assert.Single((await GetRoomsOkAsync(CurrentMonth, "&recorded=true")).Items);
        Assert.Empty((await GetRoomsOkAsync(CurrentMonth, "&recorded=false")).Items);
    }

    [Fact]
    public async Task GetRooms_RecordedCountsOnlyThisPropertysReadings()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var otherPropertyId = await CreatePropertyAsync(_organizationId, "Other Property");
        var otherElectricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh", otherPropertyId);
        var otherRoomId = await CreateRoomAsync(otherPropertyId, "101");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(otherElectricity.Id, 150, 100)), otherRoomId);
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 50, 40)));

        var room = Assert.Single((await GetRoomsOkAsync(CurrentMonth)).Items);

        Assert.Equal(_roomId, room.RoomId);
        Assert.Equal(1, room.RecordedCount);
        Assert.Equal(UtilityReadingRoomStatus.Recorded, room.Status);
    }

    [Fact]
    public async Task GetRooms_PagesAndSearchesTheRoomsOfTheProperty()
    {
        await CreateRoomAsync(_propertyId, "102");
        await CreateRoomAsync(_propertyId, "201");
        var otherPropertyId = await CreatePropertyAsync(_organizationId, "Other Property");
        await CreateRoomAsync(otherPropertyId, "103");

        var page = await GetRoomsOkAsync(CurrentMonth, "&pageSize=2");

        Assert.Equal(3, page.TotalItems);
        Assert.Equal(["101", "102"], page.Items.Select(r => r.RoomNumber));
        Assert.Equal(["201"], (await GetRoomsOkAsync(CurrentMonth, "&pageSize=2&page=2")).Items.Select(r => r.RoomNumber));
        Assert.Equal("201", Assert.Single((await GetRoomsOkAsync(CurrentMonth, "&search=20")).Items).RoomNumber);
        Assert.Equal(["201", "102", "101"], (await GetRoomsOkAsync(CurrentMonth, "&sortOrder=desc")).Items.Select(r => r.RoomNumber));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?readingMonth=2026-06-15")]
    [InlineData("?readingMonth=2026-06-01&recorded=unknown")]
    public async Task GetRooms_InvalidQuery_ReturnsBadRequest(string queryString)
    {
        var response = await GetRoomsAsync(queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRooms_UnknownProperty_ReturnsNotFound()
    {
        var response = await _client.GetAsync(
            $"/api/properties/{Guid.NewGuid()}/utility-readings/rooms?readingMonth={CurrentMonth:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRooms_PropertyOfAnotherOrganization_ReturnsNotFound()
    {
        var memberClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await GetRoomsAsync($"?readingMonth={CurrentMonth:yyyy-MM-dd}", memberClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRooms_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await GetRoomsAsync($"?readingMonth={CurrentMonth:yyyy-MM-dd}", factory.CreateClient());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRoomMeters_EmptyRoom_BillsNobody()
    {
        await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");

        var room = await GetMetersOkAsync(CurrentMonth);

        Assert.Null(room.Lease);
        Assert.Equal(["Electricity", "Water"], room.Meters.Select(m => m.ServiceName));
        Assert.All(room.Meters, m => Assert.Null(m.BilledLeaseId));
        Assert.All(room.Meters, m => Assert.Null(m.EffectiveUnitPrice));
        Assert.All(room.Meters, m => Assert.Null(m.Latest));
        Assert.Empty(room.OtherCharges);
    }

    [Fact]
    public async Task GetRoomMeters_PerTenantCharge_CountsCurrentTenants()
    {
        var garbage = await CreateUtilityServiceAsync("Garbage", 20_000, UtilityBillingMethod.PerTenant, "person");
        await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = garbage.Id }]);

        var charge = Assert.Single((await GetMetersOkAsync(CurrentMonth)).OtherCharges);

        Assert.Equal(UtilityBillingMethod.PerTenant, charge.BillingMethod);
        Assert.Equal(1, charge.Quantity);
        Assert.Equal(20_000, charge.Amount);
    }

    [Fact]
    public async Task GetRoomMeters_PerTenantCharge_CountsTenantsWhoLivedThereThatMonth()
    {
        var garbage = await CreateUtilityServiceAsync("Garbage", 20_000, UtilityBillingMethod.PerTenant, "person");
        var lease = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = garbage.Id }]);
        var leftTenantId = await CreateTenantUserAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.LeaseTenants.Add(new LeaseTenant
            {
                LeaseId = lease.Id,
                UserId = leftTenantId,
                LeftDate = Today,
                CreatedBy = SentinelActors.System
            });
            await context.SaveChangesAsync();
        }

        Assert.Equal(2, Assert.Single((await GetMetersOkAsync(CurrentMonth)).OtherCharges).Quantity);
        Assert.Equal(1, Assert.Single((await GetMetersOkAsync(CurrentMonth.AddMonths(1))).OtherCharges).Quantity);
    }

    [Fact]
    public async Task GetRoomMeters_LastRepresentableMonth_ReturnsOk()
    {
        await CreateActiveLeaseAsync([]);

        var response = await GetRoomMetersAsync(_roomId, "?readingMonth=9999-12-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRoomMeters_FixedCharge_UsesTheLeaseQuantityAndPriceOverride()
    {
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        await CreateActiveLeaseAsync(
            [new CreateLeaseUtilityServiceRequest { ServiceId = internet.Id, Quantity = 2, UnitPriceOverride = 80_000 }]);

        var charge = Assert.Single((await GetMetersOkAsync(CurrentMonth)).OtherCharges);

        Assert.Equal(80_000, charge.UnitPrice);
        Assert.Equal(2, charge.Quantity);
        Assert.Equal(160_000, charge.Amount);
    }

    [Fact]
    public async Task GetRoomMeters_MeterNotOnTheLease_IsListedWithoutUnitPrice()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        var lease = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);

        var room = await GetMetersOkAsync(CurrentMonth);

        var waterMeter = Meter(room, water.Id);
        Assert.Equal(lease.Id, waterMeter.BilledLeaseId);
        Assert.Null(waterMeter.EffectiveUnitPrice);
        Assert.Equal(3_500, Meter(room, electricity.Id).EffectiveUnitPrice);
    }

    [Fact]
    public async Task GetRoomMeters_DeactivatedMeteredService_IsNoLongerAMeter()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        var deactivate = await _client.PatchAsJsonAsync(
            $"/api/utility-services/{water.Id}", new UpdateUtilityServiceRequest { IsActive = false }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        Assert.Equal([electricity.Id], (await GetMetersOkAsync(CurrentMonth)).Meters.Select(m => m.ServiceId));
        Assert.Equal(1, Assert.Single((await GetRoomsOkAsync(CurrentMonth)).Items).MeterCount);

        var response = await PostReadingsAsync(Readings(CurrentMonth, Item(water.Id, 20, 10)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("items[0]", out _));
    }

    [Fact]
    public async Task GetRoomMeters_ReadingOfALeaseClosedSince_KeepsItsLeaseAndPrice()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);
        await RecordAsync(electricity.Id, 150, previousValue: 100);
        await TerminateAsync(lease.Id);

        var room = await GetMetersOkAsync(CurrentMonth);

        Assert.Equal(lease.Id, room.Lease!.LeaseId);
        Assert.Equal(LeaseStatus.Terminated, room.Lease.Status);
        var meter = Meter(room, electricity.Id);
        Assert.Equal(lease.Id, meter.BilledLeaseId);
        Assert.Equal(3_500, meter.EffectiveUnitPrice);
    }

    [Fact]
    public async Task GetRoomMeters_RecordedMonthAfterALeaseChange_ShowsThatMonthsLeaseAndCharges()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        var garbage = await CreateUtilityServiceAsync("Garbage", 20_000, UtilityBillingMethod.PerTenant, "person");
        var first = await CreateActiveLeaseAsync(
        [
            new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id },
            new CreateLeaseUtilityServiceRequest { ServiceId = internet.Id },
            new CreateLeaseUtilityServiceRequest { ServiceId = garbage.Id }
        ]);
        await RecordAsync(electricity.Id, 150, previousValue: 100);
        await TerminateAsync(first.Id);
        var second = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);

        var room = await GetMetersOkAsync(CurrentMonth);

        Assert.Equal(first.Id, room.Lease!.LeaseId);
        Assert.Equal(first.LeaseCode, room.Lease.LeaseCode);
        Assert.Equal(first.Id, Meter(room, electricity.Id).BilledLeaseId);
        // Not recorded yet: recording it now bills the active lease, as the form says
        Assert.Equal(second.Id, Meter(room, water.Id).BilledLeaseId);
        Assert.Equal([garbage.Id, internet.Id], room.OtherCharges.Select(c => c.ServiceId));
        Assert.Equal(1, room.OtherCharges.Single(c => c.ServiceId == garbage.Id).Quantity);

        var listed = Assert.Single((await GetRoomsOkAsync(CurrentMonth)).Items);
        Assert.Equal(first.Id, listed.Lease!.LeaseId);
        Assert.Equal(second.Id, Assert.Single((await GetRoomsOkAsync(CurrentMonth.AddMonths(1))).Items).Lease!.LeaseId);
    }

    [Fact]
    public async Task GetRoomMeters_LastReadingRecordedWhileVacant_KeepsTheLeaseEarlierReadingsBilled()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        var lease = await CreateActiveLeaseAsync(
        [
            new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id },
            new CreateLeaseUtilityServiceRequest { ServiceId = internet.Id }
        ]);
        await RecordAsync(electricity.Id, 150, previousValue: 100);
        await TerminateAsync(lease.Id);
        await RecordAsync(water.Id, 20, previousValue: 10);

        var room = await GetMetersOkAsync(CurrentMonth);

        Assert.Null(Meter(room, water.Id).BilledLeaseId);
        Assert.Equal(lease.Id, room.Lease!.LeaseId);
        Assert.Equal([internet.Id], room.OtherCharges.Select(c => c.ServiceId));
        Assert.Equal(lease.Id, Assert.Single((await GetRoomsOkAsync(CurrentMonth)).Items).Lease!.LeaseId);
    }

    [Fact]
    public async Task GetRoomMeters_ServiceDeactivatedAfterRecording_StillShowsItsReading()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100), Item(water.Id, 20, 10)));
        var deactivate = await _client.PatchAsJsonAsync(
            $"/api/utility-services/{water.Id}", new UpdateUtilityServiceRequest { IsActive = false }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var room = await GetMetersOkAsync(CurrentMonth);

        Assert.Equal(["Electricity", "Water"], room.Meters.Select(m => m.ServiceName));
        Assert.Equal(20, Meter(room, water.Id).Reading!.CurrentValue);
        Assert.Equal([electricity.Id], (await GetMetersOkAsync(CurrentMonth.AddMonths(1))).Meters.Select(m => m.ServiceId));
    }

    [Fact]
    public async Task GetRooms_ServiceDeactivatedAfterRecording_CountsItsReadingLikeTheMetersView()
    {
        await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(water.Id, 20, 10)));
        var deactivate = await _client.PatchAsJsonAsync(
            $"/api/utility-services/{water.Id}", new UpdateUtilityServiceRequest { IsActive = false }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var room = Assert.Single((await GetRoomsOkAsync(CurrentMonth)).Items);

        Assert.Equal(2, room.MeterCount);
        Assert.Equal(1, room.RecordedCount);
        Assert.Equal(UtilityReadingRoomStatus.Partial, room.Status);
        Assert.Equal(_roomId, Assert.Single((await GetRoomsOkAsync(CurrentMonth, "&recorded=false")).Items).RoomId);
        Assert.Empty((await GetRoomsOkAsync(CurrentMonth, "&recorded=true")).Items);
        Assert.Equal(2, (await GetMetersOkAsync(CurrentMonth)).Meters.Count);
    }

    [Fact]
    public async Task GetRooms_OnlyServiceDeactivatedAfterRecording_ListsTheRoomAsRecorded()
    {
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(water.Id, 20, 10)));
        var deactivate = await _client.PatchAsJsonAsync(
            $"/api/utility-services/{water.Id}", new UpdateUtilityServiceRequest { IsActive = false }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var room = Assert.Single((await GetRoomsOkAsync(CurrentMonth, "&recorded=true")).Items);

        Assert.Equal(1, room.MeterCount);
        Assert.Equal(UtilityReadingRoomStatus.Recorded, room.Status);
        Assert.Empty((await GetRoomsOkAsync(CurrentMonth, "&recorded=false")).Items);
        Assert.Equal(UtilityReadingRoomStatus.NoMeters, Assert.Single((await GetRoomsOkAsync(CurrentMonth.AddMonths(1))).Items).Status);
    }

    [Fact]
    public async Task GetRoomMeters_LeaseServiceDeactivatedAfterRecording_KeepsTheRecordedPrice()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        var lease = await CreateActiveLeaseAsync(
        [
            new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id },
            new CreateLeaseUtilityServiceRequest { ServiceId = water.Id }
        ]);
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100), Item(water.Id, 20, 10)));
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.LeaseUtilityServices
                .Where(s => s.LeaseId == lease.Id && s.ServiceId == electricity.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
            await context.UtilityServices
                .Where(s => s.Id == water.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, DateTimeOffset.UtcNow));
        }

        var room = await GetMetersOkAsync(CurrentMonth);

        Assert.Equal(3_500, Meter(room, electricity.Id).EffectiveUnitPrice);
        Assert.Equal(15_000, Meter(room, water.Id).EffectiveUnitPrice);
        // Not recorded yet: an inactive lease service has no price to bill
        Assert.Null(Meter(await GetMetersOkAsync(CurrentMonth.AddMonths(1)), electricity.Id).EffectiveUnitPrice);
    }

    [Fact]
    public async Task GetRoomMeters_ReadingRecordedBeforeTheLeaseStarted_ShowsTheActiveLease()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));
        var lease = await CreateActiveLeaseAsync(
        [
            new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id },
            new CreateLeaseUtilityServiceRequest { ServiceId = internet.Id }
        ]);

        var room = await GetMetersOkAsync(CurrentMonth);

        Assert.Equal(lease.Id, room.Lease!.LeaseId);
        Assert.Equal([internet.Id], room.OtherCharges.Select(c => c.ServiceId));
        Assert.Null(Meter(room, electricity.Id).BilledLeaseId);
        Assert.Equal(lease.Id, Assert.Single((await GetRoomsOkAsync(CurrentMonth)).Items).Lease!.LeaseId);
    }

    [Fact]
    public async Task GetRoomMeters_ServiceSwitchedFromMeteredAfterRecording_IsNotChargedTwice()
    {
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        await CreateActiveLeaseAsync(
        [
            new CreateLeaseUtilityServiceRequest { ServiceId = water.Id },
            new CreateLeaseUtilityServiceRequest { ServiceId = internet.Id }
        ]);
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(water.Id, 20, 10)));
        // The API refuses this switch while a lease uses the service; written directly so the view stays safe if that
        // rule is ever relaxed
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().UtilityServices
                .Where(s => s.Id == water.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.BillingMethod, UtilityBillingMethod.Fixed));
        }

        var room = await GetMetersOkAsync(CurrentMonth);

        Assert.Equal(20, Meter(room, water.Id).Reading!.CurrentValue);
        Assert.Equal([internet.Id], room.OtherCharges.Select(c => c.ServiceId));
    }

    [Fact]
    public async Task GetRoomMeters_MonthBeforeTheActiveLeaseStarted_BillsNobody()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        var lease = await CreateActiveLeaseAsync(
        [
            new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id },
            new CreateLeaseUtilityServiceRequest { ServiceId = internet.Id }
        ]);
        var lastMonth = CurrentMonth.AddMonths(-1);

        var before = await GetMetersOkAsync(lastMonth);
        Assert.Null(before.Lease);
        Assert.Null(Meter(before, electricity.Id).BilledLeaseId);
        Assert.Empty(before.OtherCharges);
        Assert.Null(Assert.Single((await GetRoomsOkAsync(lastMonth)).Items).Lease);
        Assert.Equal(lease.Id, (await GetMetersOkAsync(CurrentMonth)).Lease!.LeaseId);

        var recorded = await PostReadingsOkAsync(Readings(lastMonth, Item(electricity.Id, 150, 100)) with { ReadOn = lastMonth });

        Assert.Null(recorded.Lease);
        Assert.Null(Meter(recorded, electricity.Id).Reading!.BilledLeaseId);
    }


    [Fact]
    public async Task Record_ReadOnSentAsNull_RecordsItAsToday()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");

        var response = await _client.PostAsync(
            $"/api/rooms/{_roomId}/utility-readings",
            JsonContent.Create(new
            {
                readOn = (DateOnly?)null,
                readingMonth = CurrentMonth,
                items = new[] { new { serviceId = electricity.Id, currentValue = 150 } }
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(Today, Meter(await ReadAsync(response), electricity.Id).Reading!.ReadOn);
    }

    [Fact]
    public async Task Record_EmptyRoom_FirstReadingUsesPreviousValueAndBillsNobody()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");

        var response = await PostReadingsAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var room = await ReadAsync(response);
        Assert.Equal(CurrentMonth, room.ReadingMonth);
        var meter = Meter(room, electricity.Id);
        var reading = meter.Reading!;
        Assert.Equal(CurrentMonth, reading.ReadingMonth);
        Assert.Equal(Today, reading.ReadOn);
        Assert.Equal(100, reading.PreviousValue);
        Assert.Equal(150, reading.CurrentValue);
        Assert.Equal(50, reading.Consumption);
        Assert.Null(reading.BilledLeaseId);
        Assert.Equal(_actorId, reading.CreatedBy);
        Assert.Equal(reading.Id, meter.Latest!.Id);
        Assert.Null(Meter(room, water.Id).Reading);
    }

    [Fact]
    public async Task Record_ActiveLease_BillsIt()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);

        var room = await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));

        var meter = Meter(room, electricity.Id);
        Assert.Equal(lease.Id, meter.Reading!.BilledLeaseId);
        Assert.Equal(lease.LeaseCode, meter.Reading.BilledLeaseCode);
        Assert.Equal(3_500, meter.EffectiveUnitPrice);
    }

    [Fact]
    public async Task Record_FirstReadingWithoutPreviousValue_HasNoConsumption()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");

        var reading = Meter(await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150))), electricity.Id).Reading!;

        Assert.Null(reading.PreviousValue);
        Assert.Null(reading.Consumption);
    }

    [Fact]
    public async Task Record_NextMonth_ContinuesFromLatestReading()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));

        var before = Meter(await GetMetersOkAsync(CurrentMonth.AddMonths(1)), electricity.Id);
        Assert.Null(before.Reading);
        Assert.Equal(CurrentMonth, before.Latest!.ReadingMonth);
        Assert.Equal(150, before.Latest.CurrentValue);

        // previousValue is only used for a first reading
        var room = await PostReadingsOkAsync(Readings(CurrentMonth.AddMonths(1), Item(electricity.Id, 230, 120)));

        var meter = Meter(room, electricity.Id);
        Assert.Equal(150, meter.Reading!.PreviousValue);
        Assert.Equal(80, meter.Reading.Consumption);
        Assert.Equal(meter.Reading.Id, meter.Latest!.Id);
    }

    [Fact]
    public async Task Record_EveryMeterOfTheRoom_SavesThemTogether()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var water = await CreateUtilityServiceAsync("Water", 15_000, UtilityBillingMethod.Metered, "m3");

        var room = await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100), Item(water.Id, 20, 10)));

        Assert.All(room.Meters, m => Assert.NotNull(m.Reading));
        Assert.Equal(10, Meter(room, water.Id).Reading!.Consumption);
        Assert.Equal(UtilityReadingRoomStatus.Recorded, Assert.Single((await GetRoomsOkAsync(CurrentMonth)).Items).Status);
    }

    [Fact]
    public async Task Record_OneRoomDoesNotTouchAnother()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var secondRoomId = await CreateRoomAsync(_propertyId, "102");

        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)), secondRoomId);

        Assert.Null(Meter(await GetMetersOkAsync(CurrentMonth), electricity.Id).Latest);
        Assert.Equal(
            [UtilityReadingRoomStatus.Pending, UtilityReadingRoomStatus.Recorded],
            (await GetRoomsOkAsync(CurrentMonth)).Items.Select(r => r.Status));
    }

    [Fact]
    public async Task Record_OneInvalidRow_SavesNothingAndReportsItsIndex()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");

        var response = await PostReadingsAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100), Item(internet.Id, 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.True(errors.TryGetProperty("items[1]", out _));
        Assert.False(errors.TryGetProperty("items[0]", out _));
        Assert.Empty(await GetHistoryAsync());
    }

    [Fact]
    public async Task Record_ServiceOfAnotherProperty_ReturnsBadRequest()
    {
        var otherPropertyId = await CreatePropertyAsync(_organizationId, "Other Property");
        var otherElectricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh", otherPropertyId);

        var response = await PostReadingsAsync(Readings(CurrentMonth, Item(otherElectricity.Id, 150, 100)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("items[0]", out _));
    }

    [Fact]
    public async Task Record_DecreasingValue_ReportsCurrentValue()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));

        var response = await PostReadingsAsync(Readings(CurrentMonth.AddMonths(1), Item(electricity.Id, 149)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("items[0].CurrentValue", out _));
    }

    [Fact]
    public async Task Record_BodyAndBusinessErrors_UseTheSameKeys()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));

        var bodyError = await PostReadingsAsync(Readings(CurrentMonth.AddMonths(1), Item(electricity.Id, -1)));
        var businessError = await PostReadingsAsync(Readings(CurrentMonth.AddMonths(1), Item(electricity.Id, 149)));

        Assert.Equal(HttpStatusCode.BadRequest, bodyError.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, businessError.StatusCode);
        Assert.Equal(
            (await ReadErrorsAsync(bodyError)).EnumerateObject().Select(p => p.Name),
            (await ReadErrorsAsync(businessError)).EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Record_SameMonthTwice_ReturnsBadRequest()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));

        var response = await PostReadingsAsync(Readings(CurrentMonth, Item(electricity.Id, 160)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("items[0]", out _));
        Assert.Single(await GetHistoryAsync());
    }

    [Fact]
    public async Task Record_SameMonthWithAnEarlierReadOn_SaysTheMonthIsAlreadyRecorded()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));

        var response = await PostReadingsAsync(
            Readings(CurrentMonth, Item(electricity.Id, 160)) with { ReadOn = CurrentMonth.AddMonths(-1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("already exists for this month", (await ReadErrorsAsync(response)).GetProperty("items[0]")[0].GetString());
    }

    [Fact]
    public async Task Record_PreviousValueAboveCurrentValueOnAMeterWithReadings_IgnoresIt()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await RecordAsync(electricity.Id, 150, previousValue: 100);

        var room = await PostReadingsOkAsync(Readings(CurrentMonth.AddMonths(1), Item(electricity.Id, 160, 500)));

        Assert.Equal(150, Meter(room, electricity.Id).Reading!.PreviousValue);
    }

    [Fact]
    public async Task Record_ActiveLeasePastItsEndDate_BillsNobodyForTheMonthsAfter()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);
        // Still ACTIVE: ExpireLeasesJob hasn't run yet
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Leases
                .Where(l => l.Id == lease.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.EndDate, BillingMonth.LastDay(CurrentMonth)));
        }

        var room = await PostReadingsOkAsync(Readings(CurrentMonth.AddMonths(1), Item(electricity.Id, 150, 100)));

        Assert.Null(Meter(room, electricity.Id).Reading!.BilledLeaseId);
        Assert.Null(room.Lease);
        Assert.Equal(lease.Id, (await GetMetersOkAsync(CurrentMonth)).Lease!.LeaseId);
    }

    [Fact]
    public async Task UpdateUtilityService_BillingMethodOfAServiceWithReadings_ReturnsConflict()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await RecordAsync(electricity.Id, 150, previousValue: 100);

        var response = await _client.PatchAsJsonAsync(
            $"/api/utility-services/{electricity.Id}",
            new UpdateUtilityServiceRequest { BillingMethod = UtilityBillingMethod.Fixed },
            JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Record_EarlierMonthThanLatest_ReturnsBadRequest()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await PostReadingsOkAsync(Readings(CurrentMonth.AddMonths(1), Item(electricity.Id, 150, 100)));

        var response = await PostReadingsAsync(Readings(CurrentMonth, Item(electricity.Id, 160)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("items[0]", out _));
    }

    [Fact]
    public async Task Record_ReadOnBeforeTheLatestReading_ReturnsBadRequest()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lastMonth = CurrentMonth.AddMonths(-1);
        await RecordAsync(electricity.Id, 150, previousValue: 100, readingMonth: lastMonth, readOn: lastMonth.AddDays(5));

        // A later month read on an earlier day, both within what the validator accepts for that month
        var response = await PostReadingsAsync(
            Readings(CurrentMonth, Item(electricity.Id, 160)) with { ReadOn = lastMonth.AddDays(2) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("items[0]", out _));
    }

    [Fact]
    public async Task Record_ReadOnOutsideReadingMonthAndTheMonthBefore_ReturnsBadRequest()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");

        var response = await PostReadingsAsync(
            Readings(CurrentMonth, Item(electricity.Id, 150, 100)) with { ReadOn = CurrentMonth.AddMonths(-2) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await GetHistoryAsync());
    }

    [Fact]
    public async Task Record_InvalidBody_ReturnsBadRequest()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");

        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostReadingsAsync(Readings(CurrentMonth.AddDays(1), Item(electricity.Id, 150, 100)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostReadingsAsync(Readings(CurrentMonth, Item(electricity.Id, 150)) with { ReadingMonth = null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostReadingsAsync(Readings(CurrentMonth))).StatusCode);
    }

    [Fact]
    public async Task Record_UnknownRoom_ReturnsNotFound()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");

        var response = await PostReadingsAsync(Readings(CurrentMonth, Item(electricity.Id, 1)), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_RoomOfAnotherOrganization_ReturnsNotFoundAndSavesNothing()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var memberClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await memberClient.PostAsJsonAsync(
            $"/api/rooms/{_roomId}/utility-readings", Readings(CurrentMonth, Item(electricity.Id, 1)), JsonOptions);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetHistoryAsync());
    }

    // Known limitation until TICKET-054: a closed lease gets no final reading
    [Fact]
    public async Task Record_AfterTermination_BillsNobody()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));
        await TerminateAsync(lease.Id);

        Assert.Null(Meter(await GetMetersOkAsync(CurrentMonth.AddMonths(1)), electricity.Id).BilledLeaseId);

        var room = await PostReadingsOkAsync(Readings(CurrentMonth.AddMonths(1), Item(electricity.Id, 160)));

        Assert.Null(Meter(room, electricity.Id).Reading!.BilledLeaseId);
        Assert.Equal([lease.Id], (await GetHistoryAsync()).Where(r => r.BilledLeaseId is not null).Select(r => r.BilledLeaseId!.Value));
    }

    [Fact]
    public async Task Record_NewLeaseAfterTermination_BillsTheNewLease()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var first = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);
        await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));
        await TerminateAsync(first.Id);
        var second = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);

        Assert.Equal(second.Id, Meter(await GetMetersOkAsync(CurrentMonth.AddMonths(1)), electricity.Id).BilledLeaseId);

        var reading = Meter(await PostReadingsOkAsync(Readings(CurrentMonth.AddMonths(1), Item(electricity.Id, 200))), electricity.Id).Reading!;

        Assert.Equal(second.Id, reading.BilledLeaseId);
        Assert.Equal(50, reading.Consumption);
        var history = await GetHistoryAsync();
        Assert.Equal(first.Id, history.Single(r => r.ReadingMonth == CurrentMonth).BilledLeaseId);
        Assert.Equal(second.Id, history.Single(r => r.ReadingMonth == CurrentMonth.AddMonths(1)).BilledLeaseId);
    }

    [Fact]
    public async Task Delete_LatestReading_SoftDeletesItAndAllowsReRecording()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await RecordAsync(electricity.Id, 100);
        await RecordNextMonthAsync(electricity.Id, 150);
        var latest = (await GetHistoryAsync())[0];

        var response = await DeleteReadingAsync(latest.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([100m], (await GetHistoryAsync()).Select(r => r.CurrentValue));
        using (var scope = factory.Services.CreateScope())
        {
            var deleted = await scope.ServiceProvider.GetRequiredService<AppDbContext>().UtilityReadings
                .IgnoreQueryFilters()
                .SingleAsync(r => r.Id == latest.Id);
            Assert.NotNull(deleted.DeletedAt);
            Assert.Equal(_actorId, deleted.DeletedBy);
        }

        await RecordNextMonthAsync(electricity.Id, 140);
        var history = await GetHistoryAsync();
        Assert.Equal([140m, 100m], history.Select(r => r.CurrentValue));
        Assert.Equal(100, history[0].PreviousValue);
    }

    [Fact]
    public async Task Delete_OnlyReadingOfTheMonth_AllowsRecordingTheSameMonthAgain()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var recorded = await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteReadingAsync(Meter(recorded, electricity.Id).Reading!.Id)).StatusCode);

        var reRecorded = await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 160, 100)));
        Assert.Equal(60, Meter(reRecorded, electricity.Id).Reading!.Consumption);
    }

    [Fact]
    public async Task Delete_DeletedReading_IsGoneFromTheMetersAndTheRoomList()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([new CreateLeaseUtilityServiceRequest { ServiceId = electricity.Id }]);
        var recorded = await PostReadingsOkAsync(Readings(CurrentMonth, Item(electricity.Id, 150, 100)));

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteReadingAsync(Meter(recorded, electricity.Id).Reading!.Id)).StatusCode);

        var meter = Meter(await GetMetersOkAsync(CurrentMonth), electricity.Id);
        Assert.Null(meter.Reading);
        Assert.Null(meter.Latest);
        var room = Assert.Single((await GetRoomsOkAsync(CurrentMonth)).Items);
        Assert.Equal(0, room.RecordedCount);
        Assert.Equal(UtilityReadingRoomStatus.Pending, room.Status);
        Assert.Equal(lease.Id, room.Lease!.LeaseId);
        Assert.Empty((await GetRoomsOkAsync(CurrentMonth, "&recorded=true")).Items);
    }

    [Fact]
    public async Task Delete_OlderReading_ReturnsConflict()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await RecordAsync(electricity.Id, 100);
        await RecordNextMonthAsync(electricity.Id, 150);
        var older = (await GetHistoryAsync())[1];

        var response = await DeleteReadingAsync(older.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, (await GetHistoryAsync()).Count);
    }

    [Fact]
    public async Task Delete_UnknownReading_ReturnsNotFound()
    {
        var response = await DeleteReadingAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReadingOfAnotherRoom_ReturnsNotFoundAndKeepsIt()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await RecordAsync(electricity.Id, 100);
        var reading = Assert.Single(await GetHistoryAsync());
        var otherRoomId = await CreateRoomAsync(_propertyId, "102");

        var response = await _client.DeleteAsync($"/api/rooms/{otherRoomId}/utility-readings/{reading.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(await GetHistoryAsync());
    }

    [Fact]
    public async Task Delete_RoomOfAnotherOrganization_ReturnsNotFoundAndKeepsTheReading()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await RecordAsync(electricity.Id, 100);
        var reading = Assert.Single(await GetHistoryAsync());
        var memberClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await DeleteReadingAsync(reading.Id, memberClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(await GetHistoryAsync());
    }

    [Fact]
    public async Task Delete_ReadingWithImage_KeepsTheImageForAudit()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        await RecordAsync(electricity.Id, 100);
        var reading = Assert.Single(await GetHistoryAsync());
        var image = await AttachImageAsync(reading.Id);

        var response = await DeleteReadingAsync(reading.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(image.StorageKey, factory.Storage.Files.Keys);
        using var scope = factory.Services.CreateScope();
        var kept = await scope.ServiceProvider.GetRequiredService<AppDbContext>().MediaAssets
            .IgnoreQueryFilters()
            .SingleAsync(m => m.Id == image.Id);
        Assert.Null(kept.DeletedAt);
    }

    [Fact]
    public async Task Delete_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().DeleteAsync($"/api/rooms/{_roomId}/utility-readings/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
