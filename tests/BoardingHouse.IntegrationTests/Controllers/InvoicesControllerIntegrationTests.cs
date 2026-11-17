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
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.DTOs.Properties;
using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.DTOs.UtilityReadings;
using BoardingHouse.Application.DTOs.UtilityServices;
using BoardingHouse.Application.Jobs;
using BoardingHouse.Application.Repositories;

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
        Guid? previousLeaseId = null,
        int? billingDay = null)
    {
        var lease = await CreateDraftLeaseAsync(
            serviceIds, depositAmount, monthlyRent, startDate, roomId, previousLeaseId, billingDay);

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
        Guid? previousLeaseId = null,
        int? billingDay = null)
    {
        var createResponse = await _client.PostAsJsonAsync("/api/leases", new CreateLeaseRequest
        {
            RoomId = roomId ?? _roomId,
            PreviousLeaseId = previousLeaseId,
            StartDate = startDate ?? PreviousMonth,
            BillingDay = billingDay,
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

    private async Task<HttpClient> CreateOrganizationMemberClientAsync(Guid organizationId, string roleSlug = RoleSlugs.OrganizationStaff)
    {
        var (memberId, email) = await RegisterUserAsync("Organization Member");

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await RbacSeeder.SeedAsync(context);

            var role = await context.Roles.SingleAsync(r => r.Slug == roleSlug);
            context.OrganizationMembers.Add(OrganizationMember.Create(organizationId, memberId, role, SentinelActors.System));
            await context.SaveChangesAsync();
        }

        return await LoginAsync(email);
    }

    private Task<HttpResponseMessage> ListAsync(string queryString = "", HttpClient? client = null) =>
        (client ?? _client).GetAsync($"/api/invoices{queryString}");

    private async Task<PagedResult<InvoiceResponse>> ListOkAsync(string queryString = "", HttpClient? client = null)
    {
        var response = await ListAsync(queryString, client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<PagedResult<InvoiceResponse>>(response);
    }

    private async Task SoftDeleteInvoiceAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await context.Invoices.SingleAsync(i => i.Id == id);
        TestEntities.Set(invoice, new { DeletedAt = (DateTimeOffset?)DateTimeOffset.UtcNow, DeletedBy = (Guid?)SentinelActors.System });
        await context.SaveChangesAsync();
    }

    private sealed record ListData(
        Guid OtherOrganizationId,
        Guid OtherPropertyId,
        LeaseResponse Lease101,
        LeaseResponse Lease102,
        LeaseResponse Lease201,
        InvoiceDetailResponse Current101,   // INV-<current>-0001, 3_000_000
        InvoiceDetailResponse Current102,   // INV-<current>-0002, 2_000_000
        InvoiceDetailResponse Current201,   // INV-<current>-0003, 4_000_000 (second property)
        InvoiceDetailResponse Previous101,  // INV-<previous>-0001, 3_000_000
        InvoiceDetailResponse OtherOrg);    // INV-<current>-0001 of another organization, 1_000_000

    private async Task<ListData> CreateListDataAsync()
    {
        var secondPropertyId = await CreatePropertyAsync(_organizationId, "Second Property");
        var lease101 = await CreateActiveLeaseAsync(monthlyRent: 3_000_000);
        var lease102 = await CreateActiveLeaseAsync(monthlyRent: 2_000_000, roomId: await CreateRoomAsync(_propertyId, "102"));
        var lease201 = await CreateActiveLeaseAsync(monthlyRent: 4_000_000, roomId: await CreateRoomAsync(secondPropertyId, "201"));

        var otherOrganizationId = await CreateOrganizationAsync("Other Invoice Org");
        var otherPropertyId = await CreatePropertyAsync(otherOrganizationId, "Other Invoice Property");
        var otherLease = await CreateActiveLeaseAsync(monthlyRent: 1_000_000, roomId: await CreateRoomAsync(otherPropertyId, "301"));

        var current101 = await CreateOkAsync(Request(lease101.Id));
        var current102 = await CreateOkAsync(Request(lease102.Id));
        var current201 = await CreateOkAsync(Request(lease201.Id));
        var previous101 = await CreateOkAsync(Request(lease101.Id, PreviousMonth));
        var otherOrg = await CreateOkAsync(Request(otherLease.Id));

        return new ListData(otherOrganizationId, otherPropertyId, lease101, lease102, lease201,
            current101, current102, current201, previous101, otherOrg);
    }

    private Task<HttpResponseMessage> PatchAsync(Guid id, UpdateInvoiceRequest request, HttpClient? client = null) =>
        (client ?? _client).PatchAsJsonAsync($"/api/invoices/{id}", request, JsonOptions);

    private async Task<InvoiceDetailResponse> PatchOkAsync(Guid id, UpdateInvoiceRequest request)
    {
        var response = await PatchAsync(id, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<InvoiceDetailResponse>(response);
    }

    private static async Task<string?> ReadTitleAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("title").GetString();
    }

    private async Task<InvoiceDetailResponse> GetOkAsync(Guid id)
    {
        var response = await GetAsync(id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<InvoiceDetailResponse>(response);
    }

    // Rent 3,000,000 (sortOrder 0) + "Faucet repair" 50,000 (sortOrder 1), no VAT, no discount
    private async Task<InvoiceDetailResponse> CreateEditableInvoiceAsync()
    {
        var lease = await CreateActiveLeaseAsync();
        return await CreateOkAsync(Request(lease.Id) with
        {
            CustomItems = [new CreateInvoiceCustomItemRequest { Description = "Faucet repair", UnitPrice = 50_000 }]
        });
    }

    private async Task<int> CountInvoiceItemsAsync(Guid invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InvoiceItems.CountAsync(i => i.InvoiceId == invoiceId);
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

    private Task<HttpResponseMessage> CancelAsync(Guid id, string reason = "Wrong meter reading", HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync($"/api/invoices/{id}/cancel", new CancelInvoiceRequest { Reason = reason }, JsonOptions);

    private async Task<InvoiceDetailResponse> CancelOkAsync(Guid id, string reason = "Wrong meter reading")
    {
        var response = await CancelAsync(id, reason);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<InvoiceDetailResponse>(response);
    }

    private async Task<InvoiceStatusHistory> GetLastStatusHistoryAsync(Guid invoiceId) =>
        (await GetStatusHistoriesAsync(invoiceId)).OrderBy(h => h.ChangedAt).ThenBy(h => h.Id).Last();

    private Task<HttpResponseMessage> ListStatusHistoriesAsync(Guid id, HttpClient? client = null) =>
        (client ?? _client).GetAsync($"/api/invoices/{id}/status-histories");

    private async Task<List<InvoiceStatusHistoryResponse>> ListStatusHistoriesOkAsync(Guid id, HttpClient? client = null)
    {
        var response = await ListStatusHistoriesAsync(id, client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<List<InvoiceStatusHistoryResponse>>(response);
    }

    private async Task AddStatusHistoryAsync(InvoiceStatusHistory history)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.InvoiceStatusHistories.Add(history);
        await context.SaveChangesAsync();
    }

    private async Task SetDueDateAsync(Guid id, DateOnly dueDate)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Invoices
            .Where(i => i.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.DueDate, dueDate));
    }

    private async Task RunMarkOverdueInvoicesJobAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MarkOverdueInvoicesJob>().RunAsync();
    }

    private async Task<List<Guid>> ListIdsToMarkOverdueAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IInvoiceRepository>().ListIdsToMarkOverdueAsync(Today);
    }

    private async Task<Guid?> GetInvoiceUpdatedByAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Invoices
            .Where(i => i.Id == id)
            .Select(i => i.UpdatedBy)
            .SingleAsync();
    }

    private async Task SetOrganizationLateFeeAsync(LateFeeType type, decimal value)
    {
        var response = await _client.PatchAsJsonAsync(
            $"/api/organizations/{_organizationId}/settings",
            new UpdateOrganizationSettingsRequest { LateFeeType = type, LateFeeValue = value },
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SetPropertyLateFeeAsync(LateFeeType type, decimal value)
    {
        var response = await _client.PatchAsJsonAsync(
            $"/api/properties/{_propertyId}",
            new UpdatePropertyRequest { LateFeeType = type, LateFeeValue = value },
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
        await CancelOkAsync(cancelled.Id);

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

    [Fact]
    public async Task GetAll_PlatformAdmin_ReturnsInvoicesOfEveryOrganizationByBillingMonthDesc()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync();

        Assert.Equal(5, page.TotalItems);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal(
            new[] { data.Current101.Id, data.Current102.Id, data.Current201.Id, data.OtherOrg.Id }.Order(),
            page.Items.Take(4).Select(i => i.Id).Order());
        Assert.Equal(data.Previous101.Id, page.Items[4].Id);
    }

    [Fact]
    public async Task GetAll_ReturnsTheSummaryWithLeaseRoomAndProperty()
    {
        var lease = await CreateActiveLeaseAsync(depositAmount: 3_000_000);
        var created = await CreateOkAsync(Request(lease.Id) with { Notes = "First month" });

        var invoice = Assert.Single((await ListOkAsync()).Items);

        Assert.Equal(created.Id, invoice.Id);
        Assert.Equal(_organizationId, invoice.OrganizationId);
        Assert.Equal(_propertyId, invoice.PropertyId);
        Assert.Equal("Invoice Property", invoice.PropertyName);
        Assert.Equal(_roomId, invoice.RoomId);
        Assert.Equal("101", invoice.RoomNumber);
        Assert.Equal(lease.Id, invoice.LeaseId);
        Assert.Equal(lease.LeaseCode, invoice.LeaseCode);
        Assert.Equal(created.InvoiceNumber, invoice.InvoiceNumber);
        Assert.Equal(CurrentMonth, invoice.BillingMonth);
        Assert.Equal(created.DueDate, invoice.DueDate);
        Assert.Equal(6_000_000, invoice.Subtotal);
        Assert.Equal(6_000_000, invoice.TotalAmount);
        Assert.Equal(0, invoice.PaidAmount);
        Assert.Equal(6_000_000, invoice.RemainingAmount);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal("First month", invoice.Notes);
    }

    [Fact]
    public async Task GetAll_NoInvoices_ReturnsEmptyPage()
    {
        var page = await ListOkAsync();

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    [Fact]
    public async Task GetAll_FilterByOrganizationId_ReturnsOnlyThatOrganization()
    {
        var data = await CreateListDataAsync();

        var own = await ListOkAsync($"?organizationId={_organizationId}");
        var other = await ListOkAsync($"?organizationId={data.OtherOrganizationId}");

        Assert.Equal(4, own.TotalItems);
        Assert.All(own.Items, i => Assert.Equal(_organizationId, i.OrganizationId));
        Assert.Equal([data.OtherOrg.Id], other.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_OrganizationHeader_IsUsedWhenTheQueryHasNoOrganization()
    {
        var data = await CreateListDataAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoices");
        request.Headers.Add("X-Organization-Id", data.OtherOrganizationId.ToString());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadDataAsync<PagedResult<InvoiceResponse>>(response);
        Assert.Equal([data.OtherOrg.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_OrganizationQuery_WinsOverTheHeader()
    {
        var data = await CreateListDataAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/invoices?organizationId={_organizationId}");
        request.Headers.Add("X-Organization-Id", data.OtherOrganizationId.ToString());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadDataAsync<PagedResult<InvoiceResponse>>(response);
        Assert.Equal(4, page.TotalItems);
        Assert.All(page.Items, i => Assert.Equal(_organizationId, i.OrganizationId));
    }

    [Fact]
    public async Task GetAll_FilterByPropertyId_ReturnsOnlyThatProperty()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?propertyId={_propertyId}");

        Assert.Equal(
            new[] { data.Current101.Id, data.Current102.Id, data.Previous101.Id }.Order(),
            page.Items.Select(i => i.Id).Order());
        Assert.All(page.Items, i => Assert.Equal(_propertyId, i.PropertyId));
    }

    [Fact]
    public async Task GetAll_FilterByRoomId_ReturnsOnlyThatRoom()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?roomId={_roomId}");

        Assert.Equal([data.Current101.Id, data.Previous101.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_FilterByLeaseId_ReturnsOnlyThatLease()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?leaseId={data.Lease102.Id}");

        Assert.Equal([data.Current102.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_FilterByStatus_ReturnsOnlyThatStatus()
    {
        var data = await CreateListDataAsync();
        await SetInvoiceStateAsync(data.Current102.Id, InvoiceStatus.Paid, paidAmount: 2_000_000);

        var paid = await ListOkAsync("?status=paid");
        var pending = await ListOkAsync("?status=pending");

        var paidInvoice = Assert.Single(paid.Items);
        Assert.Equal(data.Current102.Id, paidInvoice.Id);
        Assert.Equal(InvoiceStatus.Paid, paidInvoice.Status);
        Assert.Equal(2_000_000, paidInvoice.PaidAmount);
        Assert.Equal(0, paidInvoice.RemainingAmount);
        Assert.Equal(4, pending.TotalItems);
        Assert.DoesNotContain(pending.Items, i => i.Id == data.Current102.Id);
    }

    [Fact]
    public async Task GetAll_FilterByBillingMonth_ReturnsOnlyThatMonth()
    {
        var data = await CreateListDataAsync();

        var previous = await ListOkAsync($"?billingMonth={PreviousMonth:yyyy-MM-dd}");
        var current = await ListOkAsync($"?billingMonth={CurrentMonth:yyyy-MM-dd}");
        var next = await ListOkAsync($"?billingMonth={CurrentMonth.AddMonths(1):yyyy-MM-dd}");

        Assert.Equal([data.Previous101.Id], previous.Items.Select(i => i.Id));
        Assert.Equal(4, current.TotalItems);
        Assert.All(current.Items, i => Assert.Equal(CurrentMonth, i.BillingMonth));
        Assert.Empty(next.Items);
    }

    [Fact]
    public async Task GetAll_CombinedFilters_AreAppliedTogether()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?propertyId={_propertyId}&billingMonth={CurrentMonth:yyyy-MM-dd}&status=pending");

        Assert.Equal(
            new[] { data.Current101.Id, data.Current102.Id }.Order(),
            page.Items.Select(i => i.Id).Order());
    }

    [Fact]
    public async Task GetAll_SearchByInvoiceNumber_IsCaseInsensitive()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?organizationId={_organizationId}&search={data.Current102.InvoiceNumber.ToLowerInvariant()}");

        Assert.Equal([data.Current102.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_SearchByInvoiceNumberPrefix_ReturnsEveryMatch()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?search=INV-{PreviousMonth:yyyyMM}");

        Assert.Equal([data.Previous101.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_SearchByLeaseCode_ReturnsTheInvoicesOfThatLease()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?search={Uri.EscapeDataString($"  {data.Lease201.LeaseCode}  ")}");

        Assert.Equal([data.Current201.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_SearchWithLikeWildcards_IsMatchedLiterally()
    {
        await CreateListDataAsync();

        var page = await ListOkAsync($"?search={Uri.EscapeDataString("INV%")}");

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetAll_SortByTotalAmountAsc_OrdersByTotal()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?billingMonth={CurrentMonth:yyyy-MM-dd}&sortBy=totalAmount&sortOrder=asc");

        Assert.Equal(
            [data.OtherOrg.Id, data.Current102.Id, data.Current101.Id, data.Current201.Id],
            page.Items.Select(i => i.Id));
        Assert.Equal([1_000_000m, 2_000_000m, 3_000_000m, 4_000_000m], page.Items.Select(i => i.TotalAmount));
    }

    [Fact]
    public async Task GetAll_SortByTotalAmountDesc_OrdersByTotal()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?billingMonth={CurrentMonth:yyyy-MM-dd}&sortBy=totalAmount&sortOrder=desc");

        Assert.Equal(
            [data.Current201.Id, data.Current101.Id, data.Current102.Id, data.OtherOrg.Id],
            page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_SortByInvoiceNumberAsc_OrdersByNumber()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync($"?organizationId={_organizationId}&sortBy=invoiceNumber&sortOrder=asc");

        Assert.Equal(
            [data.Previous101.Id, data.Current101.Id, data.Current102.Id, data.Current201.Id],
            page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_SortByBillingMonthAsc_PutsTheEarlierMonthFirst()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync("?sortBy=billingMonth&sortOrder=asc");

        Assert.Equal(data.Previous101.Id, page.Items[0].Id);
    }

    [Fact]
    public async Task GetAll_UnknownSortBy_FallsBackToBillingMonth()
    {
        var data = await CreateListDataAsync();

        var page = await ListOkAsync("?sortBy=unknown&sortOrder=asc");

        Assert.Equal(5, page.TotalItems);
        Assert.Equal(data.Previous101.Id, page.Items[0].Id);
    }

    [Fact]
    public async Task GetAll_Paging_ReturnsTheRequestedPage()
    {
        var data = await CreateListDataAsync();

        var first = await ListOkAsync($"?organizationId={_organizationId}&sortBy=invoiceNumber&sortOrder=asc&page=1&pageSize=3");
        var second = await ListOkAsync($"?organizationId={_organizationId}&sortBy=invoiceNumber&sortOrder=asc&page=2&pageSize=3");

        Assert.Equal(4, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(3, first.Items.Count);
        Assert.Equal(2, second.Page);
        Assert.Equal(3, second.PageSize);
        Assert.Equal([data.Current201.Id], second.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_SoftDeletedInvoice_IsNotListed()
    {
        var data = await CreateListDataAsync();
        await SoftDeleteInvoiceAsync(data.Current102.Id);

        var page = await ListOkAsync();

        Assert.Equal(4, page.TotalItems);
        Assert.DoesNotContain(page.Items, i => i.Id == data.Current102.Id);
    }

    [Fact]
    public async Task GetAll_InvoiceOfATerminatedLease_IsStillListed()
    {
        var lease = await CreateActiveLeaseAsync();
        var invoice = await CreateOkAsync(Request(lease.Id));
        await TerminateAsync(lease.Id);

        var page = await ListOkAsync($"?leaseId={lease.Id}");

        Assert.Equal([invoice.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetAll_OrganizationMember_SeesOnlyItsOrganization()
    {
        var data = await CreateListDataAsync();
        var memberClient = await CreateOrganizationMemberClientAsync(_organizationId);

        var page = await ListOkAsync(client: memberClient);

        Assert.Equal(4, page.TotalItems);
        Assert.DoesNotContain(page.Items, i => i.Id == data.OtherOrg.Id);
        Assert.All(page.Items, i => Assert.Equal(_organizationId, i.OrganizationId));
    }

    [Fact]
    public async Task GetAll_OrganizationMember_FilterByItsOrganization_ReturnsOk()
    {
        await CreateListDataAsync();
        var memberClient = await CreateOrganizationMemberClientAsync(_organizationId, RoleSlugs.OrganizationAdmin);

        var page = await ListOkAsync($"?organizationId={_organizationId}", memberClient);

        Assert.Equal(4, page.TotalItems);
    }

    [Fact]
    public async Task GetAll_MemberOfAnotherOrganization_SeesNoneOfThisOrganizationInvoices()
    {
        await CreateListDataAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var page = await ListOkAsync(client: otherClient);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalItems);
    }

    [Fact]
    public async Task GetAll_OrganizationIdOfAnotherOrganization_ReturnsNotFound()
    {
        await CreateListDataAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await ListAsync($"?organizationId={_organizationId}", otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_OrganizationHeaderOfAnotherOrganization_ReturnsNotFound()
    {
        await CreateListDataAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoices");
        request.Headers.Add("X-Organization-Id", _organizationId.ToString());

        var response = await otherClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_OrganizationMember_FilterByPropertyOfAnotherOrganization_ReturnsEmpty()
    {
        var data = await CreateListDataAsync();
        var memberClient = await CreateOrganizationMemberClientAsync(_organizationId);

        var page = await ListOkAsync($"?propertyId={data.OtherPropertyId}", memberClient);

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetAll_PlatformStaff_ReturnsOk()
    {
        await CreateListDataAsync();
        var staffClient = await CreatePlatformStaffClientAsync();

        var page = await ListOkAsync(client: staffClient);

        Assert.Equal(5, page.TotalItems);
    }

    [Fact]
    public async Task GetAll_WithoutInvoiceReadPermission_ReturnsForbidden()
    {
        var (_, email) = await RegisterUserAsync("No Role");
        var client = await LoginAsync(email);

        var response = await ListAsync(client: client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await ListAsync(client: factory.CreateClient());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_BillingMonthNotFirstDay_ReturnsBadRequest()
    {
        var response = await ListAsync($"?billingMonth={CurrentMonth.AddDays(1):yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.True(errors.TryGetProperty("BillingMonth", out _) || errors.TryGetProperty("billingMonth", out _));
    }

    [Fact]
    public async Task Patch_Discount_RecalculatesTheTotalAndSetsUpdatedAt()
    {
        var created = await CreateEditableInvoiceAsync();
        Assert.Null(created.UpdatedAt);

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest
        {
            DiscountAmount = 20_000m,
            DiscountReason = "  Regular tenant  "
        });

        Assert.Equal(20_000, invoice.DiscountAmount);
        Assert.Equal("Regular tenant", invoice.DiscountReason);
        Assert.Equal(3_050_000, invoice.Subtotal);
        Assert.Equal(3_030_000, invoice.TotalAmount);
        Assert.Equal(3_030_000, invoice.RemainingAmount);
        Assert.NotNull(invoice.UpdatedAt);
        Assert.Equal(created.Items, invoice.Items);
        Assert.Equal(invoice, await GetOkAsync(created.Id) with { Items = invoice.Items });
    }

    [Fact]
    public async Task Patch_EmptyBody_LeavesTheInvoiceUnchanged()
    {
        var created = await CreateEditableInvoiceAsync();

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest());

        Assert.Equal(created.TotalAmount, invoice.TotalAmount);
        Assert.Equal(created.Items, invoice.Items);
        Assert.Equal(created.DueDate, invoice.DueDate);
    }

    [Fact]
    public async Task Patch_NotesAndDueDate_AreSavedAndNullClearsNotes()
    {
        var created = await CreateEditableInvoiceAsync();
        var dueDate = CurrentMonth.AddDays(20);

        var updated = await PatchOkAsync(created.Id, new UpdateInvoiceRequest { Notes = "Pay by transfer", DueDate = dueDate });
        Assert.Equal("Pay by transfer", updated.Notes);
        Assert.Equal(dueDate, updated.DueDate);

        var cleared = await PatchOkAsync(created.Id, new UpdateInvoiceRequest { Notes = null });
        Assert.Null(cleared.Notes);
        Assert.Equal(dueDate, cleared.DueDate);
    }

    [Fact]
    public async Task Patch_DiscountWithoutReason_ReturnsBadRequest()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { DiscountAmount = 20_000m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("DiscountReason is required when DiscountAmount is greater than 0", await ReadTitleAsync(response));
        Assert.Equal(0, (await GetOkAsync(created.Id)).DiscountAmount);
    }

    [Fact]
    public async Task Patch_DiscountRemovingTheStoredReason_ReturnsBadRequest()
    {
        var lease = await CreateActiveLeaseAsync();
        var created = await CreateOkAsync(Request(lease.Id) with { DiscountAmount = 10_000, DiscountReason = "Loyal" });

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { DiscountReason = null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_DiscountAmountOnly_KeepsTheStoredReason()
    {
        var lease = await CreateActiveLeaseAsync();
        var created = await CreateOkAsync(Request(lease.Id) with { DiscountAmount = 10_000, DiscountReason = "Loyal" });

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest { DiscountAmount = 30_000m });

        Assert.Equal(30_000, invoice.DiscountAmount);
        Assert.Equal("Loyal", invoice.DiscountReason);
        Assert.Equal(2_970_000, invoice.TotalAmount);
    }

    [Fact]
    public async Task Patch_ZeroDiscount_DropsTheReason()
    {
        var lease = await CreateActiveLeaseAsync();
        var created = await CreateOkAsync(Request(lease.Id) with { DiscountAmount = 10_000, DiscountReason = "Loyal" });

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest { DiscountAmount = 0m });

        Assert.Equal(0, invoice.DiscountAmount);
        Assert.Null(invoice.DiscountReason);
        Assert.Equal(3_000_000, invoice.TotalAmount);
    }

    [Fact]
    public async Task Patch_AddsCustomItemsAfterTheLastSortOrder()
    {
        var created = await CreateEditableInvoiceAsync();

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest>
            {
                new() { Description = "Cleaning", Quantity = 2, UnitPrice = 25_000 },
                new() { Description = "Refund", Quantity = 1, UnitPrice = -10_000 }
            }
        });

        Assert.Equal([$"Rent {CurrentMonth:MM/yyyy}", "Faucet repair", "Cleaning", "Refund"], invoice.Items.Select(i => i.Description));
        Assert.Equal([0, 1, 2, 3], invoice.Items.Select(i => i.SortOrder));
        Assert.Equal([3_000_000m, 50_000m, 50_000m, -10_000m], invoice.Items.Select(i => i.Amount));
        Assert.Equal(3_090_000, invoice.Subtotal);
        Assert.Equal(3_090_000, invoice.TotalAmount);
        Assert.Equal(4, await CountInvoiceItemsAsync(created.Id));
    }

    [Fact]
    public async Task Patch_EditsCustomItem_RecomputesItsAmount()
    {
        var created = await CreateEditableInvoiceAsync();
        var faucet = ItemsOf(created, InvoiceItemSourceType.Custom).Single();

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest> { new() { Id = faucet.Id, Quantity = 3 } }
        });

        var edited = invoice.Items.Single(i => i.Id == faucet.Id);
        Assert.Equal("Faucet repair", edited.Description);
        Assert.Equal(3, edited.Quantity);
        Assert.Equal(50_000, edited.UnitPrice);
        Assert.Equal(150_000, edited.Amount);
        Assert.Equal(faucet.SortOrder, edited.SortOrder);
        Assert.Equal(3_150_000, invoice.TotalAmount);
        Assert.Equal(invoice.Items, (await GetOkAsync(created.Id)).Items);
    }

    [Fact]
    public async Task Patch_DeletesCustomItem()
    {
        var created = await CreateEditableInvoiceAsync();
        var faucet = ItemsOf(created, InvoiceItemSourceType.Custom).Single();

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest> { new() { Id = faucet.Id, IsDeleted = true } }
        });

        Assert.DoesNotContain(invoice.Items, i => i.Id == faucet.Id);
        Assert.Equal(3_000_000, invoice.TotalAmount);
        Assert.Equal(1, await CountInvoiceItemsAsync(created.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Patch_GeneratedItem_ReturnsConflict(bool delete)
    {
        var created = await CreateEditableInvoiceAsync();
        var rent = ItemsOf(created, InvoiceItemSourceType.Rent).Single();
        UpdateInvoiceCustomItemRequest change = delete ? new() { Id = rent.Id, IsDeleted = true } : new() { Id = rent.Id, UnitPrice = 1 };

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { CustomItems = new List<UpdateInvoiceCustomItemRequest> { change } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(created.Items, (await GetOkAsync(created.Id)).Items);
    }

    [Fact]
    public async Task Patch_InvalidChange_LeavesTheInvoiceUnchanged()
    {
        var created = await CreateEditableInvoiceAsync();
        var faucet = ItemsOf(created, InvoiceItemSourceType.Custom).Single();
        var rent = ItemsOf(created, InvoiceItemSourceType.Rent).Single();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest
        {
            Notes = "Changed",
            CustomItems = new List<UpdateInvoiceCustomItemRequest>
            {
                new() { Id = faucet.Id, Quantity = 5 },
                new() { Description = "Cleaning", Quantity = 1, UnitPrice = 25_000 },
                new() { Id = rent.Id, IsDeleted = true }
            }
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var invoice = await GetOkAsync(created.Id);
        Assert.Equal(created.Items, invoice.Items);
        Assert.Null(invoice.Notes);
        Assert.Equal(created.TotalAmount, invoice.TotalAmount);
        Assert.Equal(2, await CountInvoiceItemsAsync(created.Id));
    }

    [Fact]
    public async Task Patch_ItemOfAnotherInvoice_ReturnsNotFound()
    {
        var created = await CreateEditableInvoiceAsync();
        var otherLease = await CreateActiveLeaseAsync(roomId: await CreateRoomAsync(_propertyId, "102"));
        var other = await CreateOkAsync(Request(otherLease.Id) with
        {
            CustomItems = [new CreateInvoiceCustomItemRequest { Description = "Other", UnitPrice = 10_000 }]
        });
        var otherItem = ItemsOf(other, InvoiceItemSourceType.Custom).Single();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest> { new() { Id = otherItem.Id, IsDeleted = true } }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(2, await CountInvoiceItemsAsync(other.Id));
    }

    [Fact]
    public async Task Patch_UnknownItem_ReturnsNotFound()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest> { new() { Id = Guid.NewGuid(), Quantity = 2 } }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_DueDateBeforeTheBillingMonth_ReturnsBadRequest()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { DueDate = CurrentMonth.AddDays(-1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("DueDate must not be before the billing month", await ReadTitleAsync(response));
    }

    [Fact]
    public async Task Patch_RemovingEveryItem_ReturnsBadRequest()
    {
        var created = await CreateEditableInvoiceAsync();
        // A generated invoice always has a rent line: turn it into a custom one so every line can be deleted
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rent = await context.InvoiceItems.SingleAsync(i => i.InvoiceId == created.Id && i.SourceType == InvoiceItemSourceType.Rent);
            TestEntities.Set(rent, new { SourceType = InvoiceItemSourceType.Custom, SourceId = (Guid?)null });
            await context.SaveChangesAsync();
        }

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest
        {
            CustomItems = created.Items.Select(i => new UpdateInvoiceCustomItemRequest { Id = i.Id, IsDeleted = true }).ToList()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("An invoice must keep at least one item", await ReadTitleAsync(response));
        Assert.Equal(2, await CountInvoiceItemsAsync(created.Id));
    }

    [Fact]
    public async Task Patch_NegativeTotal_ReturnsBadRequest()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest
        {
            DiscountAmount = 3_050_001m,
            DiscountReason = "Too much"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("The invoice total must not be negative", await ReadTitleAsync(response));
    }

    [Fact]
    public async Task Patch_TooManyCustomItems_ReturnsBadRequest()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest
        {
            CustomItems = Enumerable.Range(0, Invoice.MaxCustomItems)
                .Select(i => new UpdateInvoiceCustomItemRequest { Description = $"Item {i}", Quantity = 1, UnitPrice = 1_000 })
                .ToList()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal($"At most {Invoice.MaxCustomItems} custom items are allowed", await ReadTitleAsync(response));
    }

    [Fact]
    public async Task Patch_NewItemMissingFields_ReturnsValidationErrors()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest
        {
            CustomItems = new List<UpdateInvoiceCustomItemRequest> { new() { Description = "Cleaning" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.Contains(errors.EnumerateObject(), p => p.Name.EndsWith("Quantity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors.EnumerateObject(), p => p.Name.EndsWith("UnitPrice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Patch_CustomItemsNull_ReturnsBadRequest()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { CustomItems = null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_NegativeDiscount_ReturnsBadRequest()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { DiscountAmount = -1m, DiscountReason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(InvoiceStatus.Pending, 1)]
    [InlineData(InvoiceStatus.Partial, 1_000)]
    [InlineData(InvoiceStatus.Paid, 3_050_000)]
    [InlineData(InvoiceStatus.Cancelled, 0)]
    public async Task Patch_NotEditable_ReturnsConflict(InvoiceStatus status, int paidAmount)
    {
        var created = await CreateEditableInvoiceAsync();
        await SetInvoiceStateAsync(created.Id, status, paidAmount);

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { Notes = "Changed" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Only unpaid pending or overdue invoices can be edited", await ReadTitleAsync(response));
        Assert.Null((await GetOkAsync(created.Id)).Notes);
    }

    [Fact]
    public async Task Patch_OverdueUnpaid_ReturnsOk()
    {
        // Last month's invoice: due "yesterday" is never before its billing month, even on the 1st
        var lease = await CreateActiveLeaseAsync();
        var created = await CreateOkAsync(Request(lease.Id, PreviousMonth));
        await SetDueDateAsync(created.Id, Today.AddDays(-1));
        await RunMarkOverdueInvoicesJobAsync();

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest { Notes = "Reminder sent" });

        Assert.Equal("Reminder sent", invoice.Notes);
        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
    }

    [Fact]
    public async Task Patch_DoesNotAddStatusHistory()
    {
        var created = await CreateEditableInvoiceAsync();

        await PatchOkAsync(created.Id, new UpdateInvoiceRequest { Notes = "Changed" });

        Assert.Single(await GetStatusHistoriesAsync(created.Id));
    }

    [Fact]
    public async Task Patch_UnknownInvoice_ReturnsNotFound()
    {
        var response = await PatchAsync(Guid.NewGuid(), new UpdateInvoiceRequest { Notes = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_SoftDeletedInvoice_ReturnsNotFound()
    {
        var created = await CreateEditableInvoiceAsync();
        await SoftDeleteInvoiceAsync(created.Id);

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { Notes = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_InvoiceOfAnotherOrganization_ReturnsNotFound()
    {
        var created = await CreateEditableInvoiceAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { Notes = "Hacked" }, otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null((await GetOkAsync(created.Id)).Notes);
    }

    [Fact]
    public async Task Patch_OrganizationStaff_ReturnsOk()
    {
        var created = await CreateEditableInvoiceAsync();
        var memberClient = await CreateOrganizationMemberClientAsync(_organizationId);

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { Notes = "By staff" }, memberClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("By staff", (await ReadDataAsync<InvoiceDetailResponse>(response)).Notes);
    }

    [Fact]
    public async Task Patch_PlatformStaff_ReturnsForbidden()
    {
        var created = await CreateEditableInvoiceAsync();
        var staffClient = await CreatePlatformStaffClientAsync();

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { Notes = "x" }, staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patch_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await PatchAsync(Guid.NewGuid(), new UpdateInvoiceRequest(), factory.CreateClient());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_UnpaidInvoice_CancelsKeepsTheNumberAndRecordsTheReason()
    {
        var created = await CreateEditableInvoiceAsync();

        var invoice = await CancelOkAsync(created.Id, "  Wrong meter reading  ");

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(created.InvoiceNumber, invoice.InvoiceNumber);
        Assert.Equal(created.TotalAmount, invoice.TotalAmount);
        Assert.Equal(created.Items.Count, invoice.Items.Count);
        Assert.Equal(InvoiceStatus.Cancelled, (await GetOkAsync(created.Id)).Status);
        var history = await GetLastStatusHistoryAsync(created.Id);
        Assert.Equal(InvoiceStatus.Pending, history.OldStatus);
        Assert.Equal(InvoiceStatus.Cancelled, history.NewStatus);
        Assert.Equal("Wrong meter reading", history.Reason);
        Assert.Equal(_actorId, history.ChangedBy);
    }

    [Fact]
    public async Task Cancel_OverdueUnpaidInvoice_Cancels()
    {
        var created = await CreateEditableInvoiceAsync();
        await SetInvoiceStateAsync(created.Id, InvoiceStatus.Overdue);

        var invoice = await CancelOkAsync(created.Id);

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal(InvoiceStatus.Overdue, (await GetLastStatusHistoryAsync(created.Id)).OldStatus);
    }

    [Fact]
    public async Task Cancel_ThenCreateTheSameMonth_UsesTheNextNumber()
    {
        var lease = await CreateActiveLeaseAsync();
        var cancelled = await CreateOkAsync(Request(lease.Id));
        await CancelOkAsync(cancelled.Id);

        var invoice = await CreateOkAsync(Request(lease.Id));

        Assert.Equal($"INV-{CurrentMonth:yyyyMM}-0002", invoice.InvoiceNumber);
        Assert.Equal(InvoiceStatus.Cancelled, (await GetOkAsync(cancelled.Id)).Status);
    }

    [Fact]
    public async Task Cancel_ThenPatch_ReturnsConflict()
    {
        var created = await CreateEditableInvoiceAsync();
        await CancelOkAsync(created.Id);

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { Notes = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(InvoiceStatus.Partial, 1_000)]
    [InlineData(InvoiceStatus.Paid, 3_050_000)]
    public async Task Cancel_WithConfirmedPayments_ReturnsConflict(InvoiceStatus status, int paidAmount)
    {
        var created = await CreateEditableInvoiceAsync();
        await SetInvoiceStateAsync(created.Id, status, paidAmount);

        var response = await CancelAsync(created.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("An invoice with confirmed payments cannot be cancelled", await ReadTitleAsync(response));
        Assert.Equal(status, (await GetOkAsync(created.Id)).Status);
        Assert.Single(await GetStatusHistoriesAsync(created.Id));
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ReturnsConflict()
    {
        var created = await CreateEditableInvoiceAsync();
        await CancelOkAsync(created.Id);

        var response = await CancelAsync(created.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("The invoice is already cancelled", await ReadTitleAsync(response));
        Assert.Equal(2, (await GetStatusHistoriesAsync(created.Id)).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Cancel_BlankReason_ReturnsBadRequest(string reason)
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await CancelAsync(created.Id, reason);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("reason", out _));
        Assert.Equal(InvoiceStatus.Pending, (await GetOkAsync(created.Id)).Status);
    }

    [Fact]
    public async Task Cancel_ReasonTooLong_ReturnsBadRequest()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await CancelAsync(created.Id, new string('a', Invoice.CancelReasonMaxLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvoiceStatus.Pending, (await GetOkAsync(created.Id)).Status);
    }

    [Fact]
    public async Task Cancel_MissingBody_ReturnsBadRequest()
    {
        var created = await CreateEditableInvoiceAsync();

        var response = await _client.PostAsJsonAsync($"/api/invoices/{created.Id}/cancel", new { }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvoiceStatus.Pending, (await GetOkAsync(created.Id)).Status);
    }

    [Fact]
    public async Task Cancel_OrganizationAdmin_ReturnsOk()
    {
        var created = await CreateEditableInvoiceAsync();
        var adminClient = await CreateOrganizationMemberClientAsync(_organizationId, RoleSlugs.OrganizationAdmin);

        var response = await CancelAsync(created.Id, client: adminClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(InvoiceStatus.Cancelled, (await ReadDataAsync<InvoiceDetailResponse>(response)).Status);
    }

    [Fact]
    public async Task Cancel_OrganizationStaff_ReturnsForbidden()
    {
        var created = await CreateEditableInvoiceAsync();
        var staffClient = await CreateOrganizationMemberClientAsync(_organizationId);

        var response = await CancelAsync(created.Id, client: staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(InvoiceStatus.Pending, (await GetOkAsync(created.Id)).Status);
    }

    [Fact]
    public async Task Cancel_PlatformStaff_ReturnsForbidden()
    {
        var created = await CreateEditableInvoiceAsync();
        var staffClient = await CreatePlatformStaffClientAsync();

        var response = await CancelAsync(created.Id, client: staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(InvoiceStatus.Pending, (await GetOkAsync(created.Id)).Status);
    }

    [Fact]
    public async Task Cancel_InvoiceOfAnotherOrganization_ReturnsNotFound()
    {
        var created = await CreateEditableInvoiceAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await CancelAsync(created.Id, client: otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(InvoiceStatus.Pending, (await GetOkAsync(created.Id)).Status);
    }

    [Fact]
    public async Task Cancel_UnknownInvoice_ReturnsNotFound()
    {
        var response = await CancelAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_SoftDeletedInvoice_ReturnsNotFound()
    {
        var created = await CreateEditableInvoiceAsync();
        await SoftDeleteInvoiceAsync(created.Id);

        var response = await CancelAsync(created.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cancel_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await CancelAsync(Guid.NewGuid(), client: factory.CreateClient());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStatusHistories_ReturnsEveryChangeInOrder()
    {
        var created = await CreateEditableInvoiceAsync();
        await CancelOkAsync(created.Id);

        var histories = await ListStatusHistoriesOkAsync(created.Id);

        Assert.Equal(2, histories.Count);

        Assert.Null(histories[0].OldStatus);
        Assert.Equal(InvoiceStatus.Pending, histories[0].NewStatus);
        Assert.Null(histories[0].Reason);
        Assert.Equal(_actorId, histories[0].ChangedBy);

        Assert.Equal(InvoiceStatus.Pending, histories[1].OldStatus);
        Assert.Equal(InvoiceStatus.Cancelled, histories[1].NewStatus);
        Assert.Equal("Wrong meter reading", histories[1].Reason);
        Assert.Equal(_actorId, histories[1].ChangedBy);
        Assert.True(histories[1].ChangedAt >= histories[0].ChangedAt);
    }

    [Fact]
    public async Task GetStatusHistories_SortsByChangedAtNotInsertOrder()
    {
        var created = await CreateEditableInvoiceAsync();
        var first = (await GetStatusHistoriesAsync(created.Id)).Single();
        var earlier = new InvoiceStatusHistory(
            created.Id, null, InvoiceStatus.Pending, "Imported", changedBy: null, first.ChangedAt.AddMinutes(-5));
        await AddStatusHistoryAsync(earlier);

        var histories = await ListStatusHistoriesOkAsync(created.Id);

        Assert.Equal([earlier.Id, first.Id], histories.Select(h => h.Id));
        Assert.Null(histories[0].ChangedBy);
    }

    [Fact]
    public async Task GetStatusHistories_OnlyReturnsTheRequestedInvoice()
    {
        var created = await CreateEditableInvoiceAsync();
        var other = await CreateOkAsync(Request(created.LeaseId, PreviousMonth));
        await CancelOkAsync(other.Id);

        var histories = await ListStatusHistoriesOkAsync(created.Id);

        Assert.Single(histories);
    }

    [Fact]
    public async Task GetStatusHistories_PlatformStaff_ReturnsOk()
    {
        var created = await CreateEditableInvoiceAsync();
        var staffClient = await CreatePlatformStaffClientAsync();

        Assert.Single(await ListStatusHistoriesOkAsync(created.Id, staffClient));
    }

    [Fact]
    public async Task GetStatusHistories_OrganizationStaff_ReturnsOk()
    {
        var created = await CreateEditableInvoiceAsync();
        var staffClient = await CreateOrganizationMemberClientAsync(_organizationId);

        Assert.Single(await ListStatusHistoriesOkAsync(created.Id, staffClient));
    }

    [Fact]
    public async Task GetStatusHistories_InvoiceOfAnotherOrganization_ReturnsNotFound()
    {
        var created = await CreateEditableInvoiceAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await ListStatusHistoriesAsync(created.Id, otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStatusHistories_UnknownInvoice_ReturnsNotFound()
    {
        var response = await ListStatusHistoriesAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStatusHistories_SoftDeletedInvoice_ReturnsNotFound()
    {
        var created = await CreateEditableInvoiceAsync();
        await SoftDeleteInvoiceAsync(created.Id);

        var response = await ListStatusHistoriesAsync(created.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetStatusHistories_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await ListStatusHistoriesAsync(Guid.NewGuid(), factory.CreateClient());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkOverdueInvoicesJob_PastDueInvoice_BecomesOverdueWithThePropertyLateFee()
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Percent, 5);
        await SetPropertyLateFeeAsync(LateFeeType.Fixed, 100_000);
        var created = await CreateEditableInvoiceAsync();
        await SetDueDateAsync(created.Id, Today.AddDays(-1));

        await RunMarkOverdueInvoicesJobAsync();

        var invoice = await GetOkAsync(created.Id);
        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(100_000, invoice.LateFeeAmount);
        Assert.Equal(3_150_000, invoice.TotalAmount);
        Assert.Equal(3_150_000, invoice.RemainingAmount);

        var history = await GetLastStatusHistoryAsync(created.Id);
        Assert.Equal(InvoiceStatus.Pending, history.OldStatus);
        Assert.Equal(InvoiceStatus.Overdue, history.NewStatus);
        Assert.Equal(Invoice.OverdueReason, history.Reason);
        Assert.Null(history.ChangedBy);
        Assert.Equal(SentinelActors.System, await GetInvoiceUpdatedByAsync(created.Id));
    }

    [Fact]
    public async Task MarkOverdueInvoicesJob_OrganizationLateFeeOnly_ChargesItsPercent()
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Percent, 5);
        var created = await CreateEditableInvoiceAsync();
        await SetDueDateAsync(created.Id, Today.AddDays(-1));

        await RunMarkOverdueInvoicesJobAsync();

        var invoice = await GetOkAsync(created.Id);
        Assert.Equal(152_500, invoice.LateFeeAmount);
        Assert.Equal(3_202_500, invoice.TotalAmount);
    }

    [Fact]
    public async Task MarkOverdueInvoicesJob_NoLateFeeConfigured_BecomesOverdueWithoutAFee()
    {
        var created = await CreateEditableInvoiceAsync();
        await SetDueDateAsync(created.Id, Today.AddDays(-1));

        await RunMarkOverdueInvoicesJobAsync();

        var invoice = await GetOkAsync(created.Id);
        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
        Assert.Equal(created.TotalAmount, invoice.TotalAmount);
    }

    [Fact]
    public async Task MarkOverdueInvoicesJob_PartiallyPaidInvoice_BecomesOverdueKeepingThePaidAmount()
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Fixed, 50_000);
        var created = await CreateEditableInvoiceAsync();
        await SetInvoiceStateAsync(created.Id, InvoiceStatus.Partial, 1_000_000);
        await SetDueDateAsync(created.Id, Today.AddDays(-1));

        await RunMarkOverdueInvoicesJobAsync();

        var invoice = await GetOkAsync(created.Id);
        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(1_000_000, invoice.PaidAmount);
        Assert.Equal(3_100_000, invoice.TotalAmount);
        Assert.Equal(2_100_000, invoice.RemainingAmount);
        Assert.Equal(InvoiceStatus.Partial, (await GetLastStatusHistoryAsync(created.Id)).OldStatus);
    }

    [Fact]
    public async Task MarkOverdueInvoicesJob_RunTwice_AddsTheLateFeeOnce()
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Fixed, 50_000);
        var created = await CreateEditableInvoiceAsync();
        await SetDueDateAsync(created.Id, Today.AddDays(-1));

        await RunMarkOverdueInvoicesJobAsync();
        var updatedBy = await GetInvoiceUpdatedByAsync(created.Id);
        await RunMarkOverdueInvoicesJobAsync();

        var invoice = await GetOkAsync(created.Id);
        Assert.Equal(50_000, invoice.LateFeeAmount);
        Assert.Equal(3_100_000, invoice.TotalAmount);
        Assert.Equal(2, (await GetStatusHistoriesAsync(created.Id)).Count);
        Assert.Equal(updatedBy, await GetInvoiceUpdatedByAsync(created.Id));
    }

    [Fact]
    public async Task MarkOverdueInvoicesJob_NotYetDue_StaysPending()
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Fixed, 50_000);
        var created = await CreateEditableInvoiceAsync();
        await SetDueDateAsync(created.Id, Today);

        await RunMarkOverdueInvoicesJobAsync();

        var invoice = await GetOkAsync(created.Id);
        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
        Assert.Single(await GetStatusHistoriesAsync(created.Id));
    }

    [Theory]
    [InlineData(InvoiceStatus.Cancelled, 0)]
    [InlineData(InvoiceStatus.Paid, 3_050_000)]
    public async Task MarkOverdueInvoicesJob_CancelledOrPaidInvoice_IsLeftAlone(InvoiceStatus status, int paidAmount)
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Fixed, 50_000);
        var created = await CreateEditableInvoiceAsync();
        await SetInvoiceStateAsync(created.Id, status, paidAmount);
        await SetDueDateAsync(created.Id, Today.AddDays(-1));

        await RunMarkOverdueInvoicesJobAsync();

        var invoice = await GetOkAsync(created.Id);
        Assert.Equal(status, invoice.Status);
        Assert.Equal(0, invoice.LateFeeAmount);
    }

    [Fact]
    public async Task MarkOverdueInvoicesJob_SoftDeletedInvoice_IsLeftAlone()
    {
        var created = await CreateEditableInvoiceAsync();
        await SetDueDateAsync(created.Id, Today.AddDays(-1));
        await SoftDeleteInvoiceAsync(created.Id);

        await RunMarkOverdueInvoicesJobAsync();

        Assert.Single(await GetStatusHistoriesAsync(created.Id));
    }

    [Fact]
    public async Task ListIdsToMarkOverdueAsync_ReturnsUnpaidPastDueInvoicesByDueDate()
    {
        var lease = await CreateActiveLeaseAsync();
        var otherLease = await CreateActiveLeaseAsync(roomId: await CreateRoomAsync(_propertyId, "102"));
        var pendingLate = await CreateOkAsync(Request(lease.Id));
        var partialLater = await CreateOkAsync(Request(lease.Id, PreviousMonth));
        var dueToday = await CreateOkAsync(Request(otherLease.Id));
        var overdue = await CreateOkAsync(Request(otherLease.Id, PreviousMonth));
        await SetDueDateAsync(pendingLate.Id, Today.AddDays(-1));
        await SetDueDateAsync(partialLater.Id, Today.AddDays(-3));
        await SetInvoiceStateAsync(partialLater.Id, InvoiceStatus.Partial, 1_000);
        await SetDueDateAsync(dueToday.Id, Today);
        await SetDueDateAsync(overdue.Id, Today.AddDays(-5));
        await SetInvoiceStateAsync(overdue.Id, InvoiceStatus.Overdue);

        var ids = await ListIdsToMarkOverdueAsync();

        Assert.Equal([partialLater.Id, pendingLate.Id], ids);
    }

    [Fact]
    public async Task Patch_DueDateInThePast_MarksTheInvoiceOverdueWithTheLateFee()
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Fixed, 50_000);
        var lease = await CreateActiveLeaseAsync();
        var created = await CreateOkAsync(Request(lease.Id, PreviousMonth));

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest { DueDate = Today.AddDays(-1) });

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(50_000, invoice.LateFeeAmount);
        Assert.Equal(created.TotalAmount + 50_000, invoice.TotalAmount);
        var history = await GetLastStatusHistoryAsync(created.Id);
        Assert.Equal(InvoiceStatus.Pending, history.OldStatus);
        Assert.Equal(InvoiceStatus.Overdue, history.NewStatus);
        Assert.Equal(Invoice.OverdueReason, history.Reason);
        Assert.Equal(_actorId, history.ChangedBy);
    }

    [Fact]
    public async Task Patch_DueDateInThePastWithPropertyLateFee_UsesThePropertyFee()
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Fixed, 50_000);
        await SetPropertyLateFeeAsync(LateFeeType.Fixed, 80_000);
        var lease = await CreateActiveLeaseAsync();
        var created = await CreateOkAsync(Request(lease.Id, PreviousMonth));

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest { DueDate = Today.AddDays(-1) });

        Assert.Equal(80_000, invoice.LateFeeAmount);
    }

    [Fact]
    public async Task Patch_LaterDueDateOnAnOverdueInvoice_MovesItBackToPendingAndKeepsTheLateFee()
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Fixed, 50_000);
        var lease = await CreateActiveLeaseAsync();
        var created = await CreateOkAsync(Request(lease.Id, PreviousMonth));
        await SetDueDateAsync(created.Id, Today.AddDays(-1));
        await RunMarkOverdueInvoicesJobAsync();

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest { DueDate = Today.AddDays(5) });

        Assert.Equal(InvoiceStatus.Pending, invoice.Status);
        Assert.Equal(50_000, invoice.LateFeeAmount);
        Assert.Equal(created.TotalAmount + 50_000, invoice.TotalAmount);
        var history = await GetLastStatusHistoryAsync(created.Id);
        Assert.Equal(InvoiceStatus.Overdue, history.OldStatus);
        Assert.Equal(InvoiceStatus.Pending, history.NewStatus);
        Assert.Null(history.Reason);
        Assert.Equal(_actorId, history.ChangedBy);
    }

    [Fact]
    public async Task Patch_OverdueAgainAfterMovingBack_DoesNotChargeTheLateFeeTwice()
    {
        await SetOrganizationLateFeeAsync(LateFeeType.Fixed, 50_000);
        var lease = await CreateActiveLeaseAsync();
        var created = await CreateOkAsync(Request(lease.Id, PreviousMonth));
        await PatchOkAsync(created.Id, new UpdateInvoiceRequest { DueDate = Today.AddDays(-1) });
        await PatchOkAsync(created.Id, new UpdateInvoiceRequest { DueDate = Today.AddDays(5) });

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest { DueDate = Today.AddDays(-1) });

        Assert.Equal(InvoiceStatus.Overdue, invoice.Status);
        Assert.Equal(50_000, invoice.LateFeeAmount);
        Assert.Equal(created.TotalAmount + 50_000, invoice.TotalAmount);
        Assert.Equal(4, (await GetStatusHistoriesAsync(created.Id)).Count);
    }

    [Fact]
    public async Task Patch_DiscountEqualToTheTotal_MarksTheInvoicePaid()
    {
        var created = await CreateEditableInvoiceAsync();

        var invoice = await PatchOkAsync(created.Id, new UpdateInvoiceRequest
        {
            DiscountAmount = 3_050_000m,
            DiscountReason = "Free month"
        });

        Assert.Equal(0, invoice.TotalAmount);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(InvoiceStatus.Paid, (await GetLastStatusHistoryAsync(created.Id)).NewStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await PatchAsync(created.Id, new UpdateInvoiceRequest { Notes = "x" })).StatusCode);
    }

    [Fact]
    public async Task Patch_InvalidChange_LeavesTheStatusUnchanged()
    {
        var lease = await CreateActiveLeaseAsync();
        var created = await CreateOkAsync(Request(lease.Id, PreviousMonth));

        var response = await PatchAsync(created.Id, new UpdateInvoiceRequest { DueDate = PreviousMonth.AddDays(-1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvoiceStatus.Pending, (await GetOkAsync(created.Id)).Status);
        Assert.Single(await GetStatusHistoriesAsync(created.Id));
    }

    private Task<HttpResponseMessage> GenerateForPropertyAsync(
        Guid propertyId, DateOnly? billingMonth = null, HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync(
            $"/api/properties/{propertyId}/invoices/generate",
            new GenerateInvoicesRequest { BillingMonth = billingMonth ?? CurrentMonth },
            JsonOptions);

    private async Task<InvoiceGenerationRunResponse> GenerateForPropertyOkAsync(DateOnly? billingMonth = null)
    {
        var response = await GenerateForPropertyAsync(_propertyId, billingMonth);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<InvoiceGenerationRunResponse>(response);
    }

    private async Task<List<Invoice>> GetInvoicesOfLeaseAsync(Guid leaseId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Invoices
            .AsNoTracking()
            .Where(i => i.LeaseId == leaseId)
            .ToListAsync();
    }

    private async Task<List<InvoiceGenerationRun>> GetGenerationRunsAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InvoiceGenerationRuns
            .AsNoTracking()
            .OrderBy(r => r.StartedAt)
            .ToListAsync();
    }

    private async Task<PagedResult<InvoiceGenerationRunResponse>> ListGenerationRunsOkAsync(
        string queryString = "", HttpClient? client = null)
    {
        var response = await (client ?? _client).GetAsync($"/api/invoice-generation-runs{queryString}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<PagedResult<InvoiceGenerationRunResponse>>(response);
    }

    // A daily-job run (no property, no actor) written directly: the job itself comes in a later step
    private async Task<Guid> InsertJobRunAsync(Guid organizationId, DateOnly billingMonth)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var startedAt = DateTimeOffset.UtcNow;
        var run = InvoiceGenerationRun.Record(
            organizationId, propertyId: null, BillingMonth.FromFirstDay(billingMonth), triggeredBy: null,
            startedAt, startedAt, createdCount: 0, []);
        context.InvoiceGenerationRuns.Add(run);
        await context.SaveChangesAsync();
        return run.Id;
    }

    // Room 101 pays electricity but has no reading this month; rooms 102 and 103 pay rent only
    private async Task<(LeaseResponse MissingReading, LeaseResponse Room102, LeaseResponse Room103)> CreateThreeLeasesAsync()
    {
        var electricity = await CreateUtilityServiceAsync("Electricity", 3_500, UtilityBillingMethod.Metered, "kWh");
        var missingReading = await CreateActiveLeaseAsync([electricity.Id]);
        var room102 = await CreateActiveLeaseAsync(roomId: await CreateRoomAsync(_propertyId, "102"));
        var room103 = await CreateActiveLeaseAsync(roomId: await CreateRoomAsync(_propertyId, "103"));
        return (missingReading, room102, room103);
    }

    [Fact]
    public async Task GenerateForProperty_BillsEveryLeaseAndReportsTheOnesThatCannotBeBilled()
    {
        var (missingReading, room102, room103) = await CreateThreeLeasesAsync();

        var run = await GenerateForPropertyOkAsync();

        Assert.Equal((_organizationId, _propertyId, CurrentMonth), (run.OrganizationId, run.PropertyId, run.BillingMonth));
        Assert.Equal(_actorId, run.TriggeredBy);
        Assert.Equal((2, 1), (run.CreatedCount, run.SkippedCount));
        var skip = Assert.Single(run.Skipped);
        Assert.Equal((missingReading.Id, missingReading.LeaseCode, "101"), (skip.LeaseId, skip.LeaseCode, skip.RoomNumber));
        var reason = Assert.Single(skip.Reasons);
        Assert.Equal("missingReading", reason.Code);
        Assert.Contains("Electricity", reason.Message);

        Assert.Equal(0, await CountInvoicesAsync(missingReading.Id));
        Assert.Equal(1, await CountInvoicesAsync(room102.Id));
        Assert.Equal(1, await CountInvoicesAsync(room103.Id));
    }

    [Fact]
    public async Task GenerateForProperty_RecordsTheRunWithTheActor()
    {
        await CreateThreeLeasesAsync();

        var response = await GenerateForPropertyOkAsync();

        var saved = Assert.Single(await GetGenerationRunsAsync());
        Assert.Equal(response.Id, saved.Id);
        Assert.Equal((_propertyId, (Guid?)_actorId, 2), (saved.PropertyId, saved.TriggeredBy, saved.CreatedCount));
        Assert.Equal("missingReading", Assert.Single(Assert.Single(saved.Skipped).Reasons).Code);
    }

    // The skipped lease is reported again on every call until it can be billed; billed leases are never duplicated
    [Fact]
    public async Task GenerateForProperty_CalledAgain_ReportsTheSkippedLeaseAgainWithoutDuplicates()
    {
        var (missingReading, room102, _) = await CreateThreeLeasesAsync();
        await GenerateForPropertyOkAsync();

        var second = await GenerateForPropertyOkAsync();

        Assert.Equal((0, 1), (second.CreatedCount, second.SkippedCount));
        Assert.Equal(missingReading.Id, Assert.Single(second.Skipped).LeaseId);
        Assert.Equal(1, await CountInvoicesAsync(room102.Id));
        Assert.Equal(2, (await GetGenerationRunsAsync()).Count);
    }

    [Fact]
    public async Task GenerateForProperty_NumbersTheInvoicesInRoomOrderAsTheActor()
    {
        var room102 = await CreateActiveLeaseAsync(roomId: await CreateRoomAsync(_propertyId, "102"));
        var room101 = await CreateActiveLeaseAsync();

        await GenerateForPropertyOkAsync();

        var month = BillingMonth.FromFirstDay(CurrentMonth);
        var first = Assert.Single(await GetInvoicesOfLeaseAsync(room101.Id));
        var second = Assert.Single(await GetInvoicesOfLeaseAsync(room102.Id));
        Assert.Equal(Invoice.FormatNumber(month, 1), first.InvoiceNumber);
        Assert.Equal(Invoice.FormatNumber(month, 2), second.InvoiceNumber);
        Assert.Equal(_actorId, first.CreatedBy);
        Assert.Equal(_actorId, (await GetLastStatusHistoryAsync(first.Id)).ChangedBy);
    }

    [Fact]
    public async Task GenerateForProperty_AlreadyInvoicedLease_IsLeftOut()
    {
        var lease = await CreateActiveLeaseAsync();
        await CreateOkAsync(Request(lease.Id));

        var run = await GenerateForPropertyOkAsync();

        Assert.Equal((0, 0), (run.CreatedCount, run.SkippedCount));
        Assert.Equal(1, await CountInvoicesAsync(lease.Id));
    }

    [Fact]
    public async Task GenerateForProperty_CancelledInvoice_IsGeneratedAgain()
    {
        var lease = await CreateActiveLeaseAsync();
        var invoice = await CreateOkAsync(Request(lease.Id));
        await CancelOkAsync(invoice.Id);

        var run = await GenerateForPropertyOkAsync();

        Assert.Equal(1, run.CreatedCount);
        Assert.Equal(2, await CountInvoicesAsync(lease.Id));
    }

    [Fact]
    public async Task GenerateForProperty_PreviousMonth_BillsIt()
    {
        var lease = await CreateActiveLeaseAsync();

        var run = await GenerateForPropertyOkAsync(PreviousMonth);

        Assert.Equal((PreviousMonth, 1), (run.BillingMonth, run.CreatedCount));
        Assert.Equal(BillingMonth.FromFirstDay(PreviousMonth), Assert.Single(await GetInvoicesOfLeaseAsync(lease.Id)).BillingMonth);
    }

    [Fact]
    public async Task GenerateForProperty_LeaseTerminatedThisMonth_IsBilledForItsDays()
    {
        var lease = await CreateActiveLeaseAsync();
        await TerminateAsync(lease.Id);

        var run = await GenerateForPropertyOkAsync();

        Assert.Equal(1, run.CreatedCount);
        var invoice = Assert.Single(await GetInvoicesOfLeaseAsync(lease.Id));
        Assert.Equal(BillingMonth.FromFirstDay(CurrentMonth), invoice.BillingMonth);
    }

    // Terminated this month, but the tenants left last month: LastDayOfStay comes from LeftDate, not TerminatedAt
    [Fact]
    public async Task GenerateForProperty_TenantsLeftLastMonth_BillsLastMonthOnly()
    {
        var lease = await CreateActiveLeaseAsync();
        var terminateResponse = await _client.PostAsJsonAsync(
            $"/api/leases/{lease.Id}/terminate",
            new TerminateLeaseRequest { Reason = "Moving out", LeftDate = CurrentMonth.AddDays(-1) },
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, terminateResponse.StatusCode);

        var current = await GenerateForPropertyOkAsync();
        var previous = await GenerateForPropertyOkAsync(PreviousMonth);

        Assert.Equal((0, 0), (current.CreatedCount, current.SkippedCount));
        Assert.Equal(1, previous.CreatedCount);
        Assert.Equal(BillingMonth.FromFirstDay(PreviousMonth), Assert.Single(await GetInvoicesOfLeaseAsync(lease.Id)).BillingMonth);
    }

    [Fact]
    public async Task GenerateForProperty_MonthAfterTheLeaseEnded_DoesNotBillIt()
    {
        var lease = await CreateActiveLeaseAsync();
        await TerminateAsync(lease.Id);

        var run = await GenerateForPropertyOkAsync(CurrentMonth.AddMonths(1));

        Assert.Equal((0, 0), (run.CreatedCount, run.SkippedCount));
        Assert.Equal(0, await CountInvoicesAsync(lease.Id));
    }

    // Still ACTIVE (expire-leases has not run yet) but EndDate is in the previous month. The API refuses to activate a
    // lease that already ended, so the EndDate is moved back in the database
    [Fact]
    public async Task GenerateForProperty_LeaseEndedLastMonthButNotExpiredYet_IsNotBilled()
    {
        var lease = await CreateActiveLeaseAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await context.Leases.SingleAsync(l => l.Id == lease.Id);
            TestEntities.Set(entity, new { EndDate = (DateOnly?)CurrentMonth.AddDays(-1) });
            await context.SaveChangesAsync();
        }

        var run = await GenerateForPropertyOkAsync();

        Assert.Equal((0, 0), (run.CreatedCount, run.SkippedCount));
        Assert.Equal(0, await CountInvoicesAsync(lease.Id));
    }

    [Fact]
    public async Task GenerateForProperty_LeaseStartingNextMonth_IsNotBilledThisMonth()
    {
        var lease = await CreateDraftLeaseAsync(startDate: CurrentMonth.AddMonths(1));
        var activateResponse = await _client.PostAsync($"/api/leases/{lease.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        var run = await GenerateForPropertyOkAsync();

        Assert.Equal((0, 0), (run.CreatedCount, run.SkippedCount));
        Assert.Equal(0, await CountInvoicesAsync(lease.Id));
    }

    [Fact]
    public async Task GenerateForProperty_DraftLease_IsNotBilled()
    {
        var lease = await CreateDraftLeaseAsync();

        var run = await GenerateForPropertyOkAsync();

        Assert.Equal((0, 0), (run.CreatedCount, run.SkippedCount));
        Assert.Equal(0, await CountInvoicesAsync(lease.Id));
    }

    [Fact]
    public async Task GenerateForProperty_LeaseOfAnotherProperty_IsNotBilled()
    {
        var otherPropertyId = await CreatePropertyAsync(_organizationId, "Other Property");
        var otherLease = await CreateActiveLeaseAsync(roomId: await CreateRoomAsync(otherPropertyId, "201"));

        var run = await GenerateForPropertyOkAsync();

        Assert.Equal(0, run.CreatedCount);
        Assert.Equal(0, await CountInvoicesAsync(otherLease.Id));
    }

    [Fact]
    public async Task GenerateForProperty_NoLeases_StillRecordsTheRun()
    {
        var run = await GenerateForPropertyOkAsync();

        Assert.Equal((0, 0), (run.CreatedCount, run.SkippedCount));
        Assert.Single(await GetGenerationRunsAsync());
    }

    [Fact]
    public async Task GenerateForProperty_UnknownProperty_ReturnsNotFound()
    {
        var response = await GenerateForPropertyAsync(Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GenerateForProperty_PropertyOfAnotherOrganization_ReturnsNotFound()
    {
        await CreateActiveLeaseAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await GenerateForPropertyAsync(_propertyId, client: otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await GetGenerationRunsAsync());
    }

    [Fact]
    public async Task GenerateForProperty_OrganizationStaff_ReturnsOk()
    {
        await CreateActiveLeaseAsync();
        var staffClient = await CreateOrganizationMemberClientAsync(_organizationId);

        var response = await GenerateForPropertyAsync(_propertyId, client: staffClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await ReadDataAsync<InvoiceGenerationRunResponse>(response)).CreatedCount);
    }

    [Fact]
    public async Task GenerateForProperty_WithoutInvoiceCreatePermission_ReturnsForbidden()
    {
        var platformStaffClient = await CreatePlatformStaffClientAsync();

        var response = await GenerateForPropertyAsync(_propertyId, client: platformStaffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GenerateForProperty_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await GenerateForPropertyAsync(_propertyId, client: factory.CreateClient());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GenerateForProperty_BillingMonthNotFirstDay_ReturnsBadRequest()
    {
        var response = await GenerateForPropertyAsync(_propertyId, CurrentMonth.AddDays(1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("billingMonth", out _));
    }

    [Fact]
    public async Task GenerateForProperty_BillingMonthTwoMonthsAhead_ReturnsBadRequestWithoutARun()
    {
        var response = await GenerateForPropertyAsync(_propertyId, CurrentMonth.AddMonths(2));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("billingMonth", out _));
        Assert.Empty(await GetGenerationRunsAsync());
    }

    [Fact]
    public async Task GenerateForProperty_WithoutBillingMonth_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/properties/{_propertyId}/invoices/generate", new GenerateInvoicesRequest(), JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("billingMonth", out _));
    }

    [Fact]
    public async Task ListGenerationRuns_ReturnsTheRunsOfTheOrganizationNewestFirstWithTheirSkips()
    {
        await CreateThreeLeasesAsync();
        var first = await GenerateForPropertyOkAsync();
        var second = await GenerateForPropertyOkAsync();

        var runs = await ListGenerationRunsOkAsync($"?organizationId={_organizationId}");

        Assert.Equal([second.Id, first.Id], runs.Items.Select(r => r.Id));
        Assert.Equal((2, 0), (first.CreatedCount, second.CreatedCount));
        Assert.All(runs.Items, r => Assert.Equal("missingReading", Assert.Single(Assert.Single(r.Skipped).Reasons).Code));
    }

    [Fact]
    public async Task ListGenerationRuns_FilterByBillingMonthAndProperty_ReturnsOnlyThoseRuns()
    {
        await GenerateForPropertyOkAsync();
        var nextMonth = await GenerateForPropertyOkAsync(CurrentMonth.AddMonths(1));
        var otherPropertyId = await CreatePropertyAsync(_organizationId, "Other Property");
        Assert.Equal(HttpStatusCode.OK, (await GenerateForPropertyAsync(otherPropertyId, CurrentMonth.AddMonths(1))).StatusCode);

        var runs = await ListGenerationRunsOkAsync(
            $"?organizationId={_organizationId}&propertyId={_propertyId}&billingMonth={CurrentMonth.AddMonths(1):yyyy-MM-dd}");

        Assert.Equal(nextMonth.Id, Assert.Single(runs.Items).Id);
    }

    // Job runs have no property: they are listed with the organization and left out by a property filter
    [Fact]
    public async Task ListGenerationRuns_JobRun_IsListedWithoutPropertyAndExcludedByThePropertyFilter()
    {
        var manual = await GenerateForPropertyOkAsync();
        var jobRunId = await InsertJobRunAsync(_organizationId, CurrentMonth);

        var all = await ListGenerationRunsOkAsync($"?organizationId={_organizationId}");
        var ofProperty = await ListGenerationRunsOkAsync($"?organizationId={_organizationId}&propertyId={_propertyId}");

        var jobRun = Assert.Single(all.Items, r => r.Id == jobRunId);
        Assert.Equal((null, null), (jobRun.PropertyId, jobRun.TriggeredBy));
        Assert.Equal(2, all.TotalItems);
        Assert.Equal(manual.Id, Assert.Single(ofProperty.Items).Id);
    }

    [Fact]
    public async Task ListGenerationRuns_Paging_ReturnsThePageAndTheTotal()
    {
        await GenerateForPropertyOkAsync();
        await GenerateForPropertyOkAsync();
        var newest = await GenerateForPropertyOkAsync();

        var runs = await ListGenerationRunsOkAsync($"?organizationId={_organizationId}&page=1&pageSize=1");

        Assert.Equal((1, 1, 3), (runs.Page, runs.PageSize, runs.TotalItems));
        Assert.Equal(newest.Id, Assert.Single(runs.Items).Id);
    }

    [Fact]
    public async Task ListGenerationRuns_OrganizationHeader_IsUsedWhenTheQueryHasNoOrganization()
    {
        await GenerateForPropertyOkAsync();
        var otherOrganizationId = await CreateOrganizationAsync("Other Org");
        var otherPropertyId = await CreatePropertyAsync(otherOrganizationId, "Other Property");
        Assert.Equal(HttpStatusCode.OK, (await GenerateForPropertyAsync(otherPropertyId)).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invoice-generation-runs");
        request.Headers.Add("X-Organization-Id", otherOrganizationId.ToString());
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var runs = await ReadDataAsync<PagedResult<InvoiceGenerationRunResponse>>(response);
        Assert.Equal(otherOrganizationId, Assert.Single(runs.Items).OrganizationId);
    }

    [Fact]
    public async Task ListGenerationRuns_OrganizationNotInScope_ReturnsNotFound()
    {
        await GenerateForPropertyOkAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await otherClient.GetAsync($"/api/invoice-generation-runs?organizationId={_organizationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListGenerationRuns_MemberWithoutOrganization_SeesOnlyItsOrganizations()
    {
        await GenerateForPropertyOkAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var runs = await ListGenerationRunsOkAsync(client: otherClient);

        Assert.Empty(runs.Items);
    }

    [Fact]
    public async Task ListGenerationRuns_OrganizationStaff_SeesTheRunsOfItsOrganization()
    {
        var run = await GenerateForPropertyOkAsync();
        var staffClient = await CreateOrganizationMemberClientAsync(_organizationId);

        var runs = await ListGenerationRunsOkAsync(client: staffClient);

        Assert.Equal(run.Id, Assert.Single(runs.Items).Id);
    }

    [Fact]
    public async Task ListGenerationRuns_PlatformStaff_ReturnsOk()
    {
        await GenerateForPropertyOkAsync();
        var platformStaffClient = await CreatePlatformStaffClientAsync();

        var runs = await ListGenerationRunsOkAsync(client: platformStaffClient);

        Assert.Single(runs.Items);
    }

    [Fact]
    public async Task ListGenerationRuns_WithoutAuthentication_ReturnsUnauthorized()
    {
        var response = await factory.CreateClient().GetAsync("/api/invoice-generation-runs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListGenerationRuns_BillingMonthNotFirstDay_ReturnsBadRequest()
    {
        var response = await _client.GetAsync($"/api/invoice-generation-runs?billingMonth={CurrentMonth.AddDays(1):yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadErrorsAsync(response)).TryGetProperty("billingMonth", out _));
    }
}
