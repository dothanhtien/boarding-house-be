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
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.DTOs.Properties;
using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.DTOs.UtilityReadings;
using BoardingHouse.Application.DTOs.UtilityServices;

namespace BoardingHouse.IntegrationTests.Controllers;

public class InvoicesControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "invoice-actor@test.com";
    private const string Password = "password123";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly CurrentMonth => BillingMonth.Of(Today).FirstDay;

    // Leases start on the 1st of the month before, so the CurrentMonth invoice is a full month
    private static DateOnly PreviousMonth => CurrentMonth.AddMonths(-1);

    private Guid _actorId;
    private Guid _organizationId;
    private Guid _propertyId;
    private Guid _roomId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        _organizationId = await CreateOrganizationAsync("Invoice Org");
        _propertyId = await CreatePropertyAsync(_organizationId, "Invoice Property");
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
            Phone = "0900000093",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Invoice Actor"
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
        string name, decimal defaultUnitPrice, UtilityBillingMethod billingMethod, string unit)
    {
        var response = await _client.PostAsJsonAsync("/api/utility-services", new CreateUtilityServiceRequest
        {
            PropertyId = _propertyId,
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

    // Created + activated, starting on the 1st of the month before (startDate)
    private async Task<LeaseResponse> CreateActiveLeaseAsync(
        IEnumerable<Guid>? serviceIds = null, decimal depositAmount = 0, decimal monthlyRent = 3_000_000, DateOnly? startDate = null)
    {
        var createResponse = await _client.PostAsJsonAsync("/api/leases", new CreateLeaseRequest
        {
            RoomId = _roomId,
            StartDate = startDate ?? PreviousMonth,
            MonthlyRent = monthlyRent,
            DepositAmount = depositAmount,
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync(), IsRepresentative = true }],
            UtilityServices = (serviceIds ?? []).Select(id => new CreateLeaseUtilityServiceRequest { ServiceId = id }).ToList()
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var lease = (await createResponse.Content.ReadFromJsonAsync<ApiResponse<LeaseResponse>>(JsonOptions))!.Data!;

        var activateResponse = await _client.PostAsync($"/api/leases/{lease.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        return lease;
    }

    // Read on the 1st of the month before readingMonth (allowed by the ReadOn rule), billed to the lease active that month
    private async Task RecordAsync(Guid serviceId, DateOnly readingMonth, decimal? previousValue, decimal currentValue)
    {
        var response = await _client.PostAsJsonAsync($"/api/rooms/{_roomId}/utility-readings", new RecordRoomUtilityReadingsRequest
        {
            ReadingMonth = readingMonth,
            ReadOn = readingMonth.AddMonths(-1),
            Items = [new RecordRoomUtilityReadingItemRequest { ServiceId = serviceId, CurrentValue = currentValue, PreviousValue = previousValue }]
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static CreateInvoiceRequest Request(Guid leaseId, DateOnly? billingMonth = null) => new()
    {
        LeaseId = leaseId,
        BillingMonth = billingMonth ?? CurrentMonth
    };

    private Task<HttpResponseMessage> PreviewAsync(CreateInvoiceRequest request, HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync("/api/invoices/preview", request, JsonOptions);

    private async Task<InvoicePreviewResponse> PreviewOkAsync(CreateInvoiceRequest request)
    {
        var response = await PreviewAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<InvoicePreviewResponse>(response);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    private static async Task<JsonElement> ReadErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors").Clone();
    }

    private async Task<(int Invoices, int Sequences)> CountInvoiceRowsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (
            await context.Invoices.IgnoreQueryFilters().CountAsync(),
            await context.InvoiceNumberSequences.CountAsync());
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        return client;
    }

    private async Task<(Guid UserId, string Email)> RegisterUserAsync(string fullName)
    {
        var email = $"user-{Guid.NewGuid():N}@test.com";
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = fullName
        });
        var user = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;
        return (user.Id, email);
    }

    private async Task<HttpClient> CreateOtherOrganizationMemberClientAsync()
    {
        var (memberId, email) = await RegisterUserAsync("Other Org Member");

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await RbacSeeder.SeedAsync(context);

            var organization = TestEntities.New<Organization>(new { Name = $"Other Org {Guid.NewGuid()}", CreatedBy = SentinelActors.System });
            context.Organizations.Add(organization);
            await context.SaveChangesAsync();

            var role = await context.Roles.SingleAsync(r => r.Slug == RoleSlugs.OrganizationAdmin);
            context.OrganizationMembers.Add(OrganizationMember.Create(organization.Id, memberId, role, SentinelActors.System));
            await context.SaveChangesAsync();
        }

        return await LoginAsync(email);
    }

    private async Task<HttpClient> CreatePlatformStaffClientAsync()
    {
        var (userId, email) = await RegisterUserAsync("Platform Staff");

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await RbacSeeder.SeedAsync(context);

            var role = await context.Roles.SingleAsync(r => r.Slug == RoleSlugs.PlatformStaff);
            context.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id, CreatedBy = SentinelActors.System });
            await context.SaveChangesAsync();
        }

        return await LoginAsync(email);
    }

    [Fact]
    public async Task Preview_FullMonth_ReturnsRentDepositAndReadingLines()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        var lease = await CreateActiveLeaseAsync([electricity.Id, internet.Id], depositAmount: 3_000_000);
        await RecordAsync(electricity.Id, CurrentMonth, previousValue: 100, currentValue: 220);

        var preview = await PreviewOkAsync(Request(lease.Id));

        Assert.True(preview.CanCreate);
        Assert.Empty(preview.Errors);
        Assert.Equal(_organizationId, preview.OrganizationId);
        Assert.Equal(_propertyId, preview.PropertyId);
        Assert.Equal(_roomId, preview.RoomId);
        Assert.Equal("101", preview.RoomNumber);
        Assert.Equal(lease.Id, preview.LeaseId);
        Assert.Equal(CurrentMonth, preview.BillingMonth);
        // Billing day = day of StartDate (1st), already reached: due today (no grace days)
        Assert.Equal(Today, preview.DueDate);
        Assert.Equal(
            [
                $"Rent {CurrentMonth:MM/yyyy}",
                "Deposit",
                "Electricity: 100 → 220 (120 kWh)",
                $"Internet {CurrentMonth:MM/yyyy}"
            ],
            preview.Items.Select(i => i.Description));
        Assert.Equal([3_000_000m, 3_000_000m, 420_000m, 100_000m], preview.Items.Select(i => i.Amount));
        Assert.Equal([0, 1, 2, 3], preview.Items.Select(i => i.SortOrder));
        Assert.Equal(6_520_000, preview.TotalAmount);
    }

    [Fact]
    public async Task Preview_CustomItemsAndDiscount_AreIncluded()
    {
        var lease = await CreateActiveLeaseAsync();

        var preview = await PreviewOkAsync(Request(lease.Id) with
        {
            CustomItems = [new CreateInvoiceCustomItemRequest { Description = " Repair ", Quantity = 1, UnitPrice = 200_000 }],
            DiscountAmount = 100_000,
            DiscountReason = " Loyal tenant "
        });

        var custom = preview.Items.Last();
        Assert.Equal("Repair", custom.Description);
        Assert.Equal(InvoiceItemSourceType.Custom, custom.SourceType);
        Assert.Null(custom.SourceId);
        Assert.Equal("Loyal tenant", preview.DiscountReason);
        Assert.Equal(3_100_000, preview.TotalAmount);
    }

    [Fact]
    public async Task Preview_MissingReading_ReturnsCannotCreate()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([electricity.Id]);

        var response = await PreviewAsync(Request(lease.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = document.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("canCreate").GetBoolean());
        var error = data.GetProperty("errors")[0];
        Assert.Equal("missingReading", error.GetProperty("code").GetString());
        Assert.Equal(electricity.Id, error.GetProperty("serviceId").GetGuid());
    }

    [Fact]
    public async Task Preview_FirstReading_ReturnsAWarning()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([electricity.Id]);
        await RecordAsync(electricity.Id, CurrentMonth, previousValue: null, currentValue: 220);

        var preview = await PreviewOkAsync(Request(lease.Id));

        Assert.True(preview.CanCreate);
        Assert.DoesNotContain(preview.Items, i => i.SourceType == InvoiceItemSourceType.Utility);
        Assert.Equal(InvoiceIssueCode.ReadingWithoutConsumption, Assert.Single(preview.Warnings).Code);
    }

    [Fact]
    public async Task Preview_DoesNotWriteAnything()
    {
        var lease = await CreateActiveLeaseAsync(depositAmount: 3_000_000);

        await PreviewOkAsync(Request(lease.Id));

        Assert.Equal((0, 0), await CountInvoiceRowsAsync());
    }

    [Fact]
    public async Task Preview_UnknownLease_ReturnsNotFound()
    {
        var response = await PreviewAsync(Request(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_LeaseOfAnotherOrganization_ReturnsNotFound()
    {
        var lease = await CreateActiveLeaseAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await PreviewAsync(Request(lease.Id), otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Preview_BillingMonthNotFirstDay_ReturnsBadRequest()
    {
        var response = await PreviewAsync(Request(Guid.NewGuid(), CurrentMonth.AddDays(1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("billingMonth", out _));
    }

    [Fact]
    public async Task Preview_WithoutLeaseId_ReturnsBadRequest()
    {
        var response = await PreviewAsync(Request(Guid.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("leaseId", out _));
    }

    [Fact]
    public async Task Preview_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await PreviewAsync(Request(Guid.NewGuid()), factory.CreateClient());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Preview_WithoutInvoiceCreatePermission_ReturnsForbidden()
    {
        var lease = await CreateActiveLeaseAsync();
        var staffClient = await CreatePlatformStaffClientAsync();

        var response = await PreviewAsync(Request(lease.Id), staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
