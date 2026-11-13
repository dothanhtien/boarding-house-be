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
        IEnumerable<Guid>? serviceIds = null,
        decimal depositAmount = 0,
        decimal monthlyRent = 3_000_000,
        DateOnly? startDate = null,
        Guid? roomId = null,
        Guid? previousLeaseId = null)
    {
        var lease = await CreateDraftLeaseAsync(serviceIds, depositAmount, monthlyRent, startDate, roomId, previousLeaseId);

        var activateResponse = await _client.PostAsync($"/api/leases/{lease.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
        return lease;
    }

    private async Task<LeaseResponse> CreateDraftLeaseAsync(
        IEnumerable<Guid>? serviceIds = null,
        decimal depositAmount = 0,
        decimal monthlyRent = 3_000_000,
        DateOnly? startDate = null,
        Guid? roomId = null,
        Guid? previousLeaseId = null)
    {
        var createResponse = await _client.PostAsJsonAsync("/api/leases", new CreateLeaseRequest
        {
            RoomId = roomId ?? _roomId,
            PreviousLeaseId = previousLeaseId,
            StartDate = startDate ?? PreviousMonth,
            MonthlyRent = monthlyRent,
            DepositAmount = depositAmount,
            Tenants = [new CreateLeaseTenantRequest { UserId = await CreateTenantUserAsync(), IsRepresentative = true }],
            UtilityServices = (serviceIds ?? []).Select(id => new CreateLeaseUtilityServiceRequest { ServiceId = id }).ToList()
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        return (await createResponse.Content.ReadFromJsonAsync<ApiResponse<LeaseResponse>>(JsonOptions))!.Data!;
    }

    private async Task TerminateAsync(Guid leaseId)
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/leases/{leaseId}/terminate", new TerminateLeaseRequest { Reason = "Moving out" }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private Task<HttpResponseMessage> CreateAsync(CreateInvoiceRequest request, HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync("/api/invoices", request, JsonOptions);

    private async Task<InvoiceDetailResponse> CreateOkAsync(CreateInvoiceRequest request)
    {
        var response = await CreateAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadDataAsync<InvoiceDetailResponse>(response);
    }

    private Task<HttpResponseMessage> GetAsync(Guid id, HttpClient? client = null) =>
        (client ?? _client).GetAsync($"/api/invoices/{id}");

    private async Task<int> CountInvoicesAsync(Guid leaseId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Invoices.CountAsync(i => i.LeaseId == leaseId);
    }

    // No cancel / payment endpoint yet (TICKET-050 / TICKET-052): the state is written directly
    private async Task SetInvoiceStateAsync(Guid id, InvoiceStatus status, decimal paidAmount = 0)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await context.Invoices.SingleAsync(i => i.Id == id);
        TestEntities.Set(invoice, new { Status = status, PaidAmount = paidAmount });
        await context.SaveChangesAsync();
    }

    private async Task<List<InvoiceStatusHistory>> GetStatusHistoriesAsync(Guid invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InvoiceStatusHistories
            .AsNoTracking()
            .Where(h => h.InvoiceId == invoiceId)
            .ToListAsync();
    }

    private static List<InvoiceItemResponse> ItemsOf(InvoiceDetailResponse invoice, InvoiceItemSourceType sourceType) =>
        invoice.Items.Where(i => i.SourceType == sourceType).ToList();

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

    [Fact]
    public async Task Create_SameBodyAsPreview_SavesExactlyThePreviewedLines()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var internet = await CreateUtilityServiceAsync("Internet", 100_000, UtilityBillingMethod.Fixed, "month");
        var lease = await CreateActiveLeaseAsync([electricity.Id, internet.Id], depositAmount: 3_000_000);
        await RecordAsync(electricity.Id, PreviousMonth, previousValue: 1_000, currentValue: 1_100);
        await RecordAsync(electricity.Id, CurrentMonth, previousValue: null, currentValue: 1_220);
        var request = Request(lease.Id) with
        {
            CustomItems = [new CreateInvoiceCustomItemRequest { Description = "Faucet repair", UnitPrice = 50_000 }],
            DiscountAmount = 20_000,
            DiscountReason = "Regular tenant",
            Notes = "Pay by transfer"
        };

        var preview = await PreviewOkAsync(request);
        var invoice = await CreateOkAsync(request);

        Assert.True(preview.CanCreate);
        Assert.Equal(
            preview.Items.Select(i => (i.Description, i.Quantity, i.UnitPrice, i.Amount, i.SortOrder, i.SourceType, i.SourceId)),
            invoice.Items.Select(i => (i.Description, i.Quantity, i.UnitPrice, i.Amount, i.SortOrder, i.SourceType, i.SourceId)));
        Assert.Equal(preview.Subtotal, invoice.Subtotal);
        Assert.Equal(preview.VatAmount, invoice.VatAmount);
        Assert.Equal(preview.TotalAmount, invoice.TotalAmount);
        Assert.Equal(preview.DueDate, invoice.DueDate);
        Assert.Equal(20_000, invoice.DiscountAmount);
        Assert.Equal("Regular tenant", invoice.DiscountReason);
        Assert.Equal("Pay by transfer", invoice.Notes);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal($"INV-{CurrentMonth:yyyyMM}-0001", invoice.InvoiceNumber);
    }

    [Fact]
    public async Task Create_ReturnsCreatedWithLocationAndAddsPendingStatusHistory()
    {
        var lease = await CreateActiveLeaseAsync();

        var response = await CreateAsync(Request(lease.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var invoice = await ReadDataAsync<InvoiceDetailResponse>(response);
        Assert.EndsWith($"/api/invoices/{invoice.Id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);

        var history = Assert.Single(await GetStatusHistoriesAsync(invoice.Id));
        Assert.Null(history.OldStatus);
        Assert.Equal(InvoiceStatus.Pending, history.NewStatus);
        Assert.Equal(_actorId, history.ChangedBy);
    }

    [Fact]
    public async Task Create_MissingReading_ReturnsBadRequest()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([electricity.Id]);

        var response = await CreateAsync(Request(lease.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("missingReading", out _));
        Assert.Equal(0, await CountInvoicesAsync(lease.Id));
    }

    [Fact]
    public async Task Create_DraftLease_ReturnsBadRequest()
    {
        var lease = await CreateDraftLeaseAsync();

        var response = await CreateAsync(Request(lease.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("leaseNotBillable", out _));
    }

    [Fact]
    public async Task Create_SameMonthTwice_ReturnsConflict()
    {
        var lease = await CreateActiveLeaseAsync();
        await CreateOkAsync(Request(lease.Id));

        var response = await CreateAsync(Request(lease.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await CountInvoicesAsync(lease.Id));
    }

    [Fact]
    public async Task Create_TwoConcurrentRequests_CreatesOneInvoice()
    {
        var lease = await CreateActiveLeaseAsync();

        var responses = await Task.WhenAll(CreateAsync(Request(lease.Id)), CreateAsync(Request(lease.Id)));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await CountInvoicesAsync(lease.Id));
    }

    [Fact]
    public async Task Create_TwoMonthsConcurrently_BillsTheDepositOnce()
    {
        var lease = await CreateActiveLeaseAsync(depositAmount: 3_000_000);

        var responses = await Task.WhenAll(
            CreateAsync(Request(lease.Id, PreviousMonth)),
            CreateAsync(Request(lease.Id, CurrentMonth)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var invoices = await Task.WhenAll(responses.Select(ReadDataAsync<InvoiceDetailResponse>));
        Assert.Single(invoices.SelectMany(i => i.Items), i => i.SourceType == InvoiceItemSourceType.Deposit);
    }

    [Fact]
    public async Task Create_UnknownLease_ReturnsNotFound()
    {
        var response = await CreateAsync(Request(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_LeaseOfAnotherOrganization_ReturnsNotFound()
    {
        var lease = await CreateActiveLeaseAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await CreateAsync(Request(lease.Id), otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await CountInvoicesAsync(lease.Id));
    }

    [Fact]
    public async Task Create_WithoutInvoiceCreatePermission_ReturnsForbidden()
    {
        var lease = await CreateActiveLeaseAsync();
        var staffClient = await CreatePlatformStaffClientAsync();

        var response = await CreateAsync(Request(lease.Id), staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_TwoLeasesOfTheOrganizationInTheSameMonth_NumbersThemInOrder()
    {
        var first = await CreateActiveLeaseAsync();
        var second = await CreateActiveLeaseAsync(roomId: await CreateRoomAsync(_propertyId, "102"));

        var firstInvoice = await CreateOkAsync(Request(first.Id));
        var secondInvoice = await CreateOkAsync(Request(second.Id));

        Assert.Equal($"INV-{CurrentMonth:yyyyMM}-0001", firstInvoice.InvoiceNumber);
        Assert.Equal($"INV-{CurrentMonth:yyyyMM}-0002", secondInvoice.InvoiceNumber);
    }

    [Fact]
    public async Task Create_AnotherMonth_StartsNumberingAgain()
    {
        var lease = await CreateActiveLeaseAsync();

        var current = await CreateOkAsync(Request(lease.Id, CurrentMonth));
        var previous = await CreateOkAsync(Request(lease.Id, PreviousMonth));

        Assert.Equal($"INV-{CurrentMonth:yyyyMM}-0001", current.InvoiceNumber);
        Assert.Equal($"INV-{PreviousMonth:yyyyMM}-0001", previous.InvoiceNumber);
    }

    [Fact]
    public async Task Create_AnotherOrganization_StartsNumberingAgain()
    {
        var otherOrganizationId = await CreateOrganizationAsync("Other Invoice Org");
        var otherPropertyId = await CreatePropertyAsync(otherOrganizationId, "Other Invoice Property");
        var otherLease = await CreateActiveLeaseAsync(roomId: await CreateRoomAsync(otherPropertyId, "201"));
        var lease = await CreateActiveLeaseAsync();

        var invoice = await CreateOkAsync(Request(lease.Id));
        var otherInvoice = await CreateOkAsync(Request(otherLease.Id));

        Assert.Equal($"INV-{CurrentMonth:yyyyMM}-0001", invoice.InvoiceNumber);
        Assert.Equal($"INV-{CurrentMonth:yyyyMM}-0001", otherInvoice.InvoiceNumber);
        Assert.Equal(otherOrganizationId, otherInvoice.OrganizationId);
    }

    [Fact]
    public async Task Create_FailedRequest_DoesNotUseANumber()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var withoutReading = await CreateActiveLeaseAsync([electricity.Id]);
        var lease = await CreateActiveLeaseAsync(roomId: await CreateRoomAsync(_propertyId, "102"));

        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(Request(withoutReading.Id))).StatusCode);
        var invoice = await CreateOkAsync(Request(lease.Id));

        Assert.Equal($"INV-{CurrentMonth:yyyyMM}-0001", invoice.InvoiceNumber);
    }

    [Fact]
    public async Task Create_ReadingAlreadyInvoiced_IsNotBilledAgainNextMonth()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([electricity.Id]);
        await RecordAsync(electricity.Id, PreviousMonth, previousValue: 100, currentValue: 150);
        var previous = await CreateOkAsync(Request(lease.Id, PreviousMonth));
        await RecordAsync(electricity.Id, CurrentMonth, previousValue: 150, currentValue: 200);

        var current = await CreateOkAsync(Request(lease.Id, CurrentMonth));

        var previousLine = Assert.Single(ItemsOf(previous, InvoiceItemSourceType.Utility));
        var currentLine = Assert.Single(ItemsOf(current, InvoiceItemSourceType.Utility));
        Assert.Equal("Electricity: 100 → 150 (50 kWh)", previousLine.Description);
        Assert.Equal("Electricity: 150 → 200 (50 kWh)", currentLine.Description);
        Assert.NotEqual(previousLine.SourceId, currentLine.SourceId);
    }

    [Fact]
    public async Task Create_EarlierReadingNotYetInvoiced_IsBilledWithItsPeriod()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([electricity.Id]);
        await RecordAsync(electricity.Id, PreviousMonth, previousValue: 100, currentValue: 150);
        await RecordAsync(electricity.Id, CurrentMonth, previousValue: 150, currentValue: 200);

        var invoice = await CreateOkAsync(Request(lease.Id));

        Assert.Equal(
            [$"Electricity: 100 → 150 (50 kWh) (period {PreviousMonth:MM/yyyy})", "Electricity: 150 → 200 (50 kWh)"],
            ItemsOf(invoice, InvoiceItemSourceType.Utility).Select(i => i.Description));
    }

    [Fact]
    public async Task Create_AfterTheInvoiceOfTheMonthWasCancelled_BillsItsReadingsAndDepositAgain()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var lease = await CreateActiveLeaseAsync([electricity.Id], depositAmount: 3_000_000);
        await RecordAsync(electricity.Id, CurrentMonth, previousValue: 100, currentValue: 150);
        var cancelled = await CreateOkAsync(Request(lease.Id));
        await SetInvoiceStateAsync(cancelled.Id, InvoiceStatus.Cancelled);

        var invoice = await CreateOkAsync(Request(lease.Id));

        Assert.NotEqual(cancelled.Id, invoice.Id);
        Assert.Equal($"INV-{CurrentMonth:yyyyMM}-0002", invoice.InvoiceNumber);
        Assert.Equal(
            ItemsOf(cancelled, InvoiceItemSourceType.Utility).Select(i => i.SourceId),
            ItemsOf(invoice, InvoiceItemSourceType.Utility).Select(i => i.SourceId));
        Assert.Single(ItemsOf(invoice, InvoiceItemSourceType.Deposit));
    }

    [Fact]
    public async Task Create_DepositIsOnlyOnTheFirstInvoice()
    {
        var lease = await CreateActiveLeaseAsync(depositAmount: 3_000_000);

        var first = await CreateOkAsync(Request(lease.Id, PreviousMonth));
        var second = await CreateOkAsync(Request(lease.Id, CurrentMonth));

        var deposit = Assert.Single(ItemsOf(first, InvoiceItemSourceType.Deposit));
        Assert.Equal(3_000_000, deposit.Amount);
        Assert.Equal(lease.Id, deposit.SourceId);
        Assert.Empty(ItemsOf(second, InvoiceItemSourceType.Deposit));
    }

    [Fact]
    public async Task Create_RenewalLease_DoesNotBillTheDeposit()
    {
        var previousLease = await CreateActiveLeaseAsync(depositAmount: 3_000_000, startDate: PreviousMonth.AddMonths(-1));
        await TerminateAsync(previousLease.Id);
        var startDate = Today.AddDays(1);
        var renewal = await CreateActiveLeaseAsync(
            depositAmount: 3_000_000, startDate: startDate, previousLeaseId: previousLease.Id);

        var invoice = await CreateOkAsync(Request(renewal.Id, BillingMonth.Of(startDate).FirstDay));

        Assert.Single(ItemsOf(invoice, InvoiceItemSourceType.Rent));
        Assert.Empty(ItemsOf(invoice, InvoiceItemSourceType.Deposit));
    }

    [Fact]
    public async Task GetById_ReturnsTheInvoiceWithItsLeaseRoomAndProperty()
    {
        var lease = await CreateActiveLeaseAsync(depositAmount: 3_000_000);
        var created = await CreateOkAsync(Request(lease.Id) with
        {
            CustomItems = [new CreateInvoiceCustomItemRequest { Description = "Faucet repair", UnitPrice = 50_000 }]
        });

        var response = await GetAsync(created.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var invoice = await ReadDataAsync<InvoiceDetailResponse>(response);
        Assert.Equal(created, invoice with { Items = created.Items });
        Assert.Equal(created.Items, invoice.Items);
        Assert.Equal([$"Rent {CurrentMonth:MM/yyyy}", "Deposit", "Faucet repair"], invoice.Items.Select(i => i.Description));
        Assert.Equal([0, 1, 2], invoice.Items.Select(i => i.SortOrder));
        Assert.Equal("Invoice Property", invoice.PropertyName);
        Assert.Equal(_propertyId, invoice.PropertyId);
        Assert.Equal(_roomId, invoice.RoomId);
        Assert.Equal("101", invoice.RoomNumber);
        Assert.Equal(lease.LeaseCode, invoice.LeaseCode);
        Assert.Equal(CurrentMonth, invoice.BillingMonth);
        Assert.Equal(6_050_000, invoice.TotalAmount);
        Assert.Equal(0, invoice.PaidAmount);
        Assert.Equal(6_050_000, invoice.RemainingAmount);
    }

    [Fact]
    public async Task GetById_UnknownInvoice_ReturnsNotFound()
    {
        var response = await GetAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_InvoiceOfAnotherOrganization_ReturnsNotFound()
    {
        var lease = await CreateActiveLeaseAsync();
        var invoice = await CreateOkAsync(Request(lease.Id));
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await GetAsync(invoice.Id, otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_PlatformStaff_ReturnsOk()
    {
        var lease = await CreateActiveLeaseAsync();
        var invoice = await CreateOkAsync(Request(lease.Id));
        var staffClient = await CreatePlatformStaffClientAsync();

        var response = await GetAsync(invoice.Id, staffClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
