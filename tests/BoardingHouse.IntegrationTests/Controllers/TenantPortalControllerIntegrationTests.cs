using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.TenantPortal;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class TenantPortalControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string Password = "password123";

    private Guid _staffId;
    private Guid _organizationId;
    private Guid _propertyId;
    private Room _room = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();

        // lease_status_histories.changed_by references users, so the actor must be a real user
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staff = TestEntities.New<User>(new { FullName = "Staff", PasswordHash = "hash", CreatedBy = SentinelActors.System });
        var organization = TestEntities.New<Organization>(new { Name = "Org A", CreatedBy = staff.Id });
        var property = TestEntities.New<Property>(new { OrganizationId = organization.Id, Name = "Property A", CreatedBy = staff.Id });
        _room = TestRooms.New(propertyId: property.Id, roomNumber: "101", capacity: 4, createdBy: staff.Id);
        context.AddRange(staff, organization, property, _room);
        await context.SaveChangesAsync();

        _staffId = staff.Id;
        _organizationId = organization.Id;
        _propertyId = property.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string ExtractAccessTokenCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("accessToken=", StringComparison.Ordinal))
            .Split(';')[0]["accessToken=".Length..];

    private async Task<(HttpClient Client, Guid UserId)> RegisterAndLoginAsync(string fullName)
    {
        var email = $"user-{Guid.NewGuid():N}@test.com";
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = fullName
        });
        var user = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        return (client, user.Id);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    // A lease in _room with the given tenants (first one is the representative), moved to `status`
    private Task<Guid> SeedLeaseAsync(
        LeaseStatus status, string leaseCode, DateOnly startDate, params Guid[] tenantUserIds) =>
        SeedLeaseAsync(status, leaseCode, startDate, services: [], tenantUserIds);

    // Same, with utility services of _propertyId created and attached to the lease
    private async Task<Guid> SeedLeaseAsync(
        LeaseStatus status,
        string leaseCode,
        DateOnly startDate,
        IReadOnlyList<(UtilityType Type, UtilityServiceDetails Details, decimal? UnitPriceOverride)> services,
        params Guid[] tenantUserIds)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await context.Users.Where(u => tenantUserIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        var room = await context.Rooms.SingleAsync(r => r.Id == _room.Id);
        var selections = services
            .Select(s => new LeaseUtilityServiceSelection(
                UtilityService.Create(_propertyId, s.Type, s.Details, _staffId), s.UnitPriceOverride, Quantity: 1))
            .ToList();
        context.UtilityServices.AddRange(selections.Select(s => s.Service));

        var lease = Lease.CreateDraft(
            _organizationId, room, previousLeaseId: null, leaseCode, startDate, startDate.AddYears(1), billingDay: null,
            monthlyRent: 3_000_000, depositAmount: 3_000_000,
            tenantUserIds.Select((id, index) => new TenantChange { UserId = id, IsRepresentative = index == 0 }).ToList(),
            users, selections, DateTimeOffset.UtcNow, _staffId);
        TestEntities.Set(lease, new { Status = status });
        context.Leases.Add(lease);
        await context.SaveChangesAsync();
        return lease.Id;
    }

    private async Task DeactivateUtilityServiceAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await context.UtilityServices.SingleAsync(s => s.Name == name);
        TestEntities.Set(service, new { IsActive = false });
        await context.SaveChangesAsync();
    }

    private async Task SetTenantDatesAsync(Guid leaseId, Guid userId, DateOnly? joinedDate, DateOnly? leftDate)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await context.LeaseTenants.SingleAsync(t => t.LeaseId == leaseId && t.UserId == userId);
        TestEntities.Set(tenant, new { JoinedDate = joinedDate, LeftDate = leftDate });
        await context.SaveChangesAsync();
    }

    // The tenant moves out on `leftDate`, then comes back to the same lease on `rejoinedDate`: a second lease_tenants row
    private async Task RejoinAsync(Guid leaseId, Guid userId, DateOnly leftDate, DateOnly rejoinedDate)
    {
        await SetTenantDatesAsync(leaseId, userId, joinedDate: null, leftDate);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.LeaseTenants.Add(TestEntities.New<LeaseTenant>(new
        {
            LeaseId = leaseId,
            UserId = userId,
            IsRepresentative = false,
            JoinedDate = rejoinedDate,
            CreatedBy = _staffId
        }));
        await context.SaveChangesAsync();
    }

    private async Task<Guid> SeedInvoiceAsync(
        Guid leaseId, DateOnly billingMonth, InvoiceStatus status = InvoiceStatus.Pending, decimal paidAmount = 0)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = TestEntities.New<Invoice>(new
        {
            OrganizationId = _organizationId,
            LeaseId = leaseId,
            // unique per organization: a random suffix could collide and make the test flaky
            InvoiceNumber = $"INV-{billingMonth:yyyy-MM}-{Guid.NewGuid():N}"[..24],
            BillingMonth = BillingMonth.FromFirstDay(billingMonth),
            DueDate = billingMonth.AddDays(9),
            Subtotal = 3_000_000m,
            TotalAmount = 3_000_000m,
            PaidAmount = paidAmount,
            Status = status,
            Notes = "internal note",
            CreatedBy = _staffId
        });
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        return invoice.Id;
    }

    private async Task AddInvoiceItemAsync(
        Guid invoiceId, string description, decimal amount, int sortOrder, InvoiceItemSourceType sourceType, Guid? sourceId = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.InvoiceItems.Add(TestEntities.New<InvoiceItem>(new
        {
            InvoiceId = invoiceId,
            Description = description,
            Quantity = 1m,
            UnitPrice = amount,
            Amount = amount,
            SortOrder = sortOrder,
            SourceType = sourceType,
            SourceId = sourceId ?? Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        }));
        await context.SaveChangesAsync();
    }

    private async Task<Guid> SeedElectricityServiceAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = TestEntities.New<UtilityService>(new
        {
            PropertyId = _propertyId,
            Name = "Electricity",
            Type = UtilityType.Electricity,
            Unit = "kWh",
            BillingMethod = UtilityBillingMethod.Metered,
            DefaultUnitPrice = 3_500m,
            CreatedBy = _staffId
        });
        context.UtilityServices.Add(service);
        await context.SaveChangesAsync();
        return service.Id;
    }

    // latest: null makes each month an independent row (unique index is (room_id, reading_month, service_id))
    private async Task<Guid> SeedReadingAsync(Guid leaseId, Guid serviceId, DateOnly month)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reading = UtilityReading.Record(
            _room.Id, serviceId, readOn: month, BillingMonth.FromFirstDay(month), latest: null,
            initialPreviousValue: 100, currentValue: 150, billedLeaseId: leaseId, DateTimeOffset.UtcNow, _staffId);
        context.UtilityReadings.Add(reading);
        await context.SaveChangesAsync();
        return reading.Id;
    }

    private async Task<string> AttachReadingImageAsync(Guid readingId)
    {
        var storageKey = $"test/utility-readings/{Guid.NewGuid():N}";
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MediaAssets.Add(TestEntities.New<MediaAsset>(new
        {
            OrganizationId = _organizationId,
            FileName = "meter.png",
            StorageProvider = StorageProvider.Cloudinary,
            StorageKey = storageKey,
            FileUrl = $"https://storage.test/{storageKey}",
            MimeType = "image/png",
            FileSize = 3L,
            EntityType = MediaAssetEntityType.UtilityReadingImage,
            EntityId = readingId,
            CreatedBy = SentinelActors.System
        }));
        await context.SaveChangesAsync();
        return storageKey;
    }

    private async Task SoftDeleteReadingAsync(Guid readingId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reading = await context.UtilityReadings.SingleAsync(r => r.Id == readingId);
        context.UtilityReadings.Remove(reading);
        await context.SaveChangesAsync();
    }

    private async Task<Guid> SeedWaterServiceAsync()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = TestEntities.New<UtilityService>(new
        {
            PropertyId = _propertyId,
            Name = "Water",
            Type = UtilityType.Water,
            Unit = "m3",
            BillingMethod = UtilityBillingMethod.Metered,
            DefaultUnitPrice = 20_000m,
            CreatedBy = _staffId
        });
        context.UtilityServices.Add(service);
        await context.SaveChangesAsync();
        return service.Id;
    }

    private async Task<string> GetInvoiceNumberAsync(Guid invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Invoices.Where(i => i.Id == invoiceId).Select(i => i.InvoiceNumber).SingleAsync();
    }

    // Payment has no factory outside Invoice.RecordPayment (needs the loaded aggregate): build the row the way EF does,
    // like PaymentPersistenceTests.NewPayment. A voided payment gets the void columns its check constraint requires.
    private async Task<Guid> SeedPaymentAsync(
        Guid invoiceId,
        PaymentStatus status = PaymentStatus.Pending,
        DateTimeOffset? paymentDate = null,
        Guid? payerUserId = null,
        string? payerNameOverride = null,
        string? note = null)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var payment = TestEntities.New<Payment>(new
        {
            InvoiceId = (Guid?)invoiceId,
            PayerUserId = payerUserId,
            PayerNameOverride = payerNameOverride,
            Amount = 500_000m,
            Method = PaymentMethod.Cash,
            Status = status,
            PaymentDate = paymentDate ?? now,
            ConfirmedBy = status == PaymentStatus.Pending ? null : (Guid?)_staffId,
            ConfirmedAt = status == PaymentStatus.Pending ? null : (DateTimeOffset?)now,
            VoidedBy = status == PaymentStatus.Voided ? (Guid?)_staffId : null,
            VoidedAt = status == PaymentStatus.Voided ? (DateTimeOffset?)now : null,
            VoidReason = status == PaymentStatus.Voided ? "staff void reason" : null,
            Note = note,
            CreatedBy = _staffId
        });
        context.Payments.Add(payment);
        await context.SaveChangesAsync();
        return payment.Id;
    }

    private async Task SoftDeleteLeaseAsync(Guid leaseId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lease = await context.Leases.SingleAsync(l => l.Id == leaseId);
        context.Leases.Remove(lease);
        await context.SaveChangesAsync();
    }

    private async Task SoftDeleteTenantRowAsync(Guid leaseId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await context.LeaseTenants.SingleAsync(t => t.LeaseId == leaseId && t.UserId == userId);
        context.LeaseTenants.Remove(tenant);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetTenancy_UserWithoutLeases_IsZero()
    {
        var (client, _) = await RegisterAndLoginAsync("Staff");

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        Assert.Equal(0, tenancy.TotalLeaseCount);
        Assert.Equal(0, tenancy.ActiveLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_ActiveAndPastLeases_CountsThem_IgnoringDraft()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        await SeedLeaseAsync(LeaseStatus.Active, "HD-ACTIVE", new DateOnly(2026, 1, 1), tenantA);
        await SeedLeaseAsync(LeaseStatus.Expired, "HD-EXPIRED", new DateOnly(2025, 1, 1), tenantA);
        await SeedLeaseAsync(LeaseStatus.Draft, "HD-DRAFT", new DateOnly(2027, 1, 1), tenantA);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        // no tenant_profiles row is seeded: tenancy comes from lease_tenants only
        Assert.Equal(1, tenancy.ActiveLeaseCount);
        Assert.Equal(2, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_LeasesOfOtherUsers_NotCounted()
    {
        var (client, _) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        await SeedLeaseAsync(LeaseStatus.Active, "HD-B", new DateOnly(2026, 1, 1), tenantB);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        Assert.Equal(0, tenancy.ActiveLeaseCount);
        Assert.Equal(0, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_TenantWhoLeftActiveLease_IsNotActive()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        await SetTenantDatesAsync(leaseId, tenantA, joinedDate: null, leftDate: yesterday);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        Assert.Equal(0, tenancy.ActiveLeaseCount);
        Assert.Equal(1, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_TenantLeavingToday_IsStillActive()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SetTenantDatesAsync(leaseId, tenantA, joinedDate: null, leftDate: today);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        // left_date is the last day of the stay
        Assert.Equal(1, tenancy.ActiveLeaseCount);
        Assert.Equal(1, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_TenantWhoRejoinedSameLease_CountedOnce()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // the old stay ends in the future too, so a row-based count would give ActiveLeaseCount 2
        await RejoinAsync(leaseId, tenantA, leftDate: today.AddDays(1), rejoinedDate: today.AddDays(2));

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        Assert.Equal(1, tenancy.ActiveLeaseCount);
        Assert.Equal(1, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task GetTenancy_SoftDeletedLeaseOrTenantRow_NotCounted()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var deletedLeaseId = await SeedLeaseAsync(LeaseStatus.Expired, "HD-DELETED", new DateOnly(2024, 1, 1), tenantA);
        var removedFromLeaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-REMOVED", new DateOnly(2026, 1, 1), tenantB, tenantA);
        await SoftDeleteLeaseAsync(deletedLeaseId);
        await SoftDeleteTenantRowAsync(removedFromLeaseId, tenantA);

        var tenancy = await ReadDataAsync<TenancyResponse>(await client.GetAsync("/api/me/tenancy"));

        // StaysOf uses IgnoreQueryFilters: this fails if its explicit DeletedAt checks are dropped
        Assert.Equal(0, tenancy.ActiveLeaseCount);
        Assert.Equal(0, tenancy.TotalLeaseCount);
    }

    [Fact]
    public async Task AdminEndpoint_CalledByTenantOnly_Returns403()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);

        var response = await client.GetAsync("/api/leases");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTenancy_Anonymous_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/me/tenancy");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetLeases_ReturnsOnlyCallersNonDraftLeases()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var mine = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        await SeedLeaseAsync(LeaseStatus.Draft, "HD-A-DRAFT", new DateOnly(2026, 6, 1), tenantA);
        await SeedLeaseAsync(LeaseStatus.Expired, "HD-B", new DateOnly(2025, 1, 1), tenantB);

        var response = await client.GetAsync("/api/me/leases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadDataAsync<PagedResult<MyLeaseResponse>>(response);
        var lease = Assert.Single(page.Items);
        Assert.Equal(mine, lease.Id);
        Assert.Equal("Org A", lease.OrganizationName);
        Assert.Equal("101", lease.RoomNumber);
        Assert.True(lease.IsRepresentative);
        var stay = Assert.Single(lease.MyStays);
        Assert.Equal(new DateOnly(2026, 1, 1), stay.JoinedDate); // JoinedDate null = lease start date
        Assert.Null(stay.LeftDate);
    }

    [Fact]
    public async Task GetLeases_FilterByStatus_NewestFirst()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var active = await SeedLeaseAsync(LeaseStatus.Active, "HD-ACTIVE", new DateOnly(2026, 1, 1), tenantA);
        var older = await SeedLeaseAsync(LeaseStatus.Expired, "HD-2024", new DateOnly(2024, 1, 1), tenantA);
        var newer = await SeedLeaseAsync(LeaseStatus.Expired, "HD-2025", new DateOnly(2025, 1, 1), tenantA);

        var all = await ReadDataAsync<PagedResult<MyLeaseResponse>>(await client.GetAsync("/api/me/leases"));
        var expired = await ReadDataAsync<PagedResult<MyLeaseResponse>>(await client.GetAsync("/api/me/leases?status=expired"));

        Assert.Equal([active, newer, older], all.Items.Select(l => l.Id));
        Assert.Equal([newer, older], expired.Items.Select(l => l.Id));
        Assert.Equal(2, expired.TotalItems);
    }

    [Fact]
    public async Task GetLeases_TenantWhoRejoined_ListsBothStays()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        await RejoinAsync(leaseId, tenantA, leftDate: new DateOnly(2026, 3, 10), rejoinedDate: new DateOnly(2026, 5, 20));

        var leases = await ReadDataAsync<PagedResult<MyLeaseResponse>>(await client.GetAsync("/api/me/leases"));
        var detail = await client.GetAsync($"/api/me/leases/{leaseId}");

        var lease = Assert.Single(leases.Items);
        Assert.Equal(
            [(new DateOnly(2026, 1, 1), (DateOnly?)new DateOnly(2026, 3, 10)), (new DateOnly(2026, 5, 20), null)],
            lease.MyStays.Select(s => (s.JoinedDate, s.LeftDate)));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task GetLease_OfAnotherTenant_Returns404()
    {
        var (client, _) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var othersLease = await SeedLeaseAsync(LeaseStatus.Active, "HD-B", new DateOnly(2026, 1, 1), tenantB);

        var response = await client.GetAsync($"/api/me/leases/{othersLease}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLease_Draft_Returns404()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var draft = await SeedLeaseAsync(LeaseStatus.Draft, "HD-DRAFT", new DateOnly(2026, 1, 1), tenantA);

        var response = await client.GetAsync($"/api/me/leases/{draft}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLease_RoommatesExposeNameOnly()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantA, tenantB);

        var response = await client.GetAsync($"/api/me/leases/{leaseId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tenants = document.RootElement.GetProperty("data").GetProperty("tenants");
        Assert.Equal(2, tenants.GetArrayLength());
        foreach (var tenant in tenants.EnumerateArray())
        {
            Assert.Equal(
                ["fullName", "isRepresentative", "joinedDate", "leftDate"],
                tenant.EnumerateObject().Select(p => p.Name).Order().ToArray());
        }
    }

    [Fact]
    public async Task GetLease_UtilityServices_OnlyBillableOnes_WithEffectivePrice()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(
            LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1),
            [
                (UtilityType.Water, new UtilityServiceDetails("Water", UtilityBillingMethod.Metered, "m3", 20_000, IsActive: true), 15_000m),
                (UtilityType.Internet, new UtilityServiceDetails("Internet", UtilityBillingMethod.Fixed, "month", 100_000, IsActive: true), null)
            ],
            tenantA);
        await DeactivateUtilityServiceAsync("Internet");

        var lease = await ReadDataAsync<MyLeaseDetailResponse>(await client.GetAsync($"/api/me/leases/{leaseId}"));

        // the deactivated price-list entry is not billable (LeaseUtilityService.IsBillable); the override wins
        var service = Assert.Single(lease.UtilityServices);
        Assert.Equal("Water", service.Name);
        Assert.Equal(UtilityBillingMethod.Metered, service.BillingMethod);
        Assert.Equal(15_000m, service.UnitPrice);
        Assert.Equal(1, service.Quantity);
    }

    [Fact]
    public async Task GetLeases_StaffWhoIsAlsoTenantElsewhere_SeesOnlyOwnLeases_IgnoringOrganizationHeader()
    {
        var (client, staffTenant) = await RegisterAndLoginAsync("Staff Tenant");
        await factory.GrantPlatformAdminRoleAsync(staffTenant);
        var (_, other) = await RegisterAndLoginAsync("Other");
        var mine = await SeedLeaseAsync(LeaseStatus.Active, "HD-MINE", new DateOnly(2026, 1, 1), staffTenant);
        await SeedLeaseAsync(LeaseStatus.Expired, "HD-OTHER", new DateOnly(2025, 1, 1), other);
        client.DefaultRequestHeaders.Add("X-Organization-Id", _organizationId.ToString());

        var page = await ReadDataAsync<PagedResult<MyLeaseResponse>>(await client.GetAsync("/api/me/leases"));

        Assert.Equal([mine], page.Items.Select(l => l.Id));
    }

    [Fact]
    public async Task GetLeases_StaffWhoIsNotTenant_ReturnsEmptyPage()
    {
        var (client, staff) = await RegisterAndLoginAsync("Staff");
        await factory.GrantPlatformAdminRoleAsync(staff);

        var response = await client.GetAsync("/api/me/leases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadDataAsync<PagedResult<MyLeaseResponse>>(response)).Items);
    }

    [Fact]
    public async Task GetInvoices_TenantWhoMovedOut_DoesNotSeeLaterMonths()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        await SetTenantDatesAsync(leaseId, tenantA, joinedDate: null, leftDate: new DateOnly(2026, 6, 15));
        var june = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 6, 1));
        var july = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 7, 1));

        var list = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync("/api/me/invoices"));

        Assert.Equal([june], list.Items.Select(i => i.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/me/invoices/{july}")).StatusCode);
    }

    [Fact]
    public async Task GetInvoices_TenantWhoJoinedLater_DoesNotSeeEarlierMonths()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        await SetTenantDatesAsync(leaseId, tenantA, joinedDate: new DateOnly(2026, 8, 20), leftDate: null);
        await SeedInvoiceAsync(leaseId, new DateOnly(2026, 7, 1));
        var august = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 8, 1));

        var list = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync("/api/me/invoices"));

        Assert.Equal([august], list.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetInvoices_TenantWhoRejoined_SeesOnlyMonthsLivedThere()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        await RejoinAsync(leaseId, tenantA, leftDate: new DateOnly(2026, 3, 10), rejoinedDate: new DateOnly(2026, 5, 20));
        var march = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 3, 1));
        await SeedInvoiceAsync(leaseId, new DateOnly(2026, 4, 1));
        var may = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 5, 1));

        var invoices = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync("/api/me/invoices"));

        // April (moved out) is not visible
        Assert.Equal([may, march], invoices.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetInvoices_StayBoundaries_OneDayInTheMonthIsEnough()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        // moved in on the last day of January, left on the first day of March
        await SetTenantDatesAsync(leaseId, tenantA, joinedDate: new DateOnly(2026, 1, 31), leftDate: new DateOnly(2026, 3, 1));
        await SeedInvoiceAsync(leaseId, new DateOnly(2025, 12, 1));
        var january = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 1, 1));
        var february = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 2, 1));
        var march = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 3, 1));
        await SeedInvoiceAsync(leaseId, new DateOnly(2026, 4, 1));

        var invoices = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync("/api/me/invoices"));

        // same rule as Domain StayPeriod.Covers (StayPeriodTests), here as SQL
        Assert.Equal([march, february, january], invoices.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetInvoices_OfDraftLease_NotVisible()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var draft = await SeedLeaseAsync(LeaseStatus.Draft, "HD-DRAFT", new DateOnly(2026, 1, 1), tenantA);
        var invoiceId = await SeedInvoiceAsync(draft, new DateOnly(2026, 1, 1));

        var invoices = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync("/api/me/invoices"));

        Assert.Empty(invoices.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/me/invoices/{invoiceId}")).StatusCode);
    }

    [Fact]
    public async Task GetInvoices_FilterByLeaseAndBillingMonth()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var older = await SeedLeaseAsync(LeaseStatus.Expired, "HD-2025", new DateOnly(2025, 1, 1), tenantA);
        var current = await SeedLeaseAsync(LeaseStatus.Active, "HD-2026", new DateOnly(2026, 1, 1), tenantA);
        var others = await SeedLeaseAsync(LeaseStatus.Expired, "HD-B", new DateOnly(2024, 1, 1), tenantB);
        var olderJune = await SeedInvoiceAsync(older, new DateOnly(2025, 6, 1));
        var currentJune = await SeedInvoiceAsync(current, new DateOnly(2026, 6, 1));
        await SeedInvoiceAsync(current, new DateOnly(2026, 7, 1));
        await SeedInvoiceAsync(others, new DateOnly(2024, 6, 1));

        var byLease = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync($"/api/me/invoices?leaseId={older}"));
        var byMonth = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync("/api/me/invoices?billingMonth=2026-06-01"));
        var othersLease = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync($"/api/me/invoices?leaseId={others}"));

        Assert.Equal([olderJune], byLease.Items.Select(i => i.Id));
        Assert.Equal([currentJune], byMonth.Items.Select(i => i.Id));
        Assert.Empty(othersLease.Items);
    }

    [Fact]
    public async Task GetInvoices_HidesCancelledUnlessAskedFor()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        var cancelled = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 2, 1), InvoiceStatus.Cancelled);
        var pending = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 3, 1));

        var all = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync("/api/me/invoices"));
        var onlyCancelled = await ReadDataAsync<PagedResult<MyInvoiceResponse>>(await client.GetAsync("/api/me/invoices?status=cancelled"));

        Assert.Equal([pending], all.Items.Select(i => i.Id));
        Assert.Equal([cancelled], onlyCancelled.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetInvoices_BillingMonthNotFirstDay_Returns400()
    {
        var (client, _) = await RegisterAndLoginAsync("Tenant A");

        var response = await client.GetAsync("/api/me/invoices?billingMonth=2026-06-15");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetInvoice_ReturnsItemsInOrder_AndRemainingAmount()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        var invoiceId = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 2, 1), InvoiceStatus.Partial, paidAmount: 1_000_000m);
        await AddInvoiceItemAsync(invoiceId, "Water", 200_000m, sortOrder: 2, InvoiceItemSourceType.Utility);
        await AddInvoiceItemAsync(invoiceId, "Rent", 2_800_000m, sortOrder: 1, InvoiceItemSourceType.Rent);

        var invoice = await ReadDataAsync<MyInvoiceDetailResponse>(await client.GetAsync($"/api/me/invoices/{invoiceId}"));

        Assert.Equal(new DateOnly(2026, 2, 1), invoice.BillingMonth);
        Assert.Equal("HD-A", invoice.LeaseCode);
        Assert.Equal("Property A", invoice.PropertyName);
        Assert.Equal(2_000_000m, invoice.RemainingAmount);
        Assert.Equal(["Rent", "Water"], invoice.Items.Select(i => i.Description));
    }

    [Fact]
    public async Task GetInvoice_DoesNotExposeInternalFields()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        var invoiceId = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 2, 1));
        await AddInvoiceItemAsync(invoiceId, "Rent", 3_000_000m, sortOrder: 1, InvoiceItemSourceType.Rent);

        var json = await (await client.GetAsync($"/api/me/invoices/{invoiceId}")).Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        Assert.False(data.TryGetProperty("notes", out _));
        Assert.False(data.TryGetProperty("createdBy", out _));
        Assert.DoesNotContain("internal note", json);
        // no item id / sourceId (staff InvoiceItemResponse has them)
        var item = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(
            ["amount", "description", "quantity", "sourceType", "unitPrice"],
            item.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task GetUtilityReadings_TenantWhoMovedOut_SeesOnlyMonthsLivedThere()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        await SetTenantDatesAsync(leaseId, tenantA, joinedDate: null, leftDate: new DateOnly(2026, 6, 15));
        var electricity = await SeedElectricityServiceAsync();
        var june = await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 6, 1));
        await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 7, 1));

        var response = await client.GetAsync($"/api/me/leases/{leaseId}/utility-readings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadDataAsync<PagedResult<MyUtilityReadingResponse>>(response);
        var reading = Assert.Single(page.Items);
        Assert.Equal(june, reading.Id);
        Assert.Equal(("Electricity", 50m), (reading.ServiceName, reading.Consumption));
        Assert.Null(reading.ImageUrl);
        Assert.Null(reading.InvoiceNumber);
    }

    [Fact]
    public async Task GetUtilityReadings_TenantWhoRejoined_DoesNotSeeMonthsBetweenStays()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        await RejoinAsync(leaseId, tenantA, leftDate: new DateOnly(2026, 3, 10), rejoinedDate: new DateOnly(2026, 5, 20));
        var electricity = await SeedElectricityServiceAsync();
        var march = await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 3, 1));
        await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 4, 1));
        var may = await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 5, 1));

        var page = await ReadDataAsync<PagedResult<MyUtilityReadingResponse>>(
            await client.GetAsync($"/api/me/leases/{leaseId}/utility-readings"));

        Assert.Equal([may, march], page.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task GetUtilityReadings_WithImageAndInvoice_ReturnsUrlAndLiveInvoiceNumber()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        var electricity = await SeedElectricityServiceAsync();
        var readingId = await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 2, 1));
        var storageKey = await AttachReadingImageAsync(readingId);
        // billed on an invoice that was cancelled, then on its replacement (cancelled ones are not "live")
        var cancelled = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 2, 1), InvoiceStatus.Cancelled);
        await AddInvoiceItemAsync(cancelled, "Electricity", 175_000m, sortOrder: 1, InvoiceItemSourceType.Utility, sourceId: readingId);
        var replacement = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 2, 1));
        await AddInvoiceItemAsync(replacement, "Electricity", 175_000m, sortOrder: 1, InvoiceItemSourceType.Utility, sourceId: readingId);
        var replacementNumber = await GetInvoiceNumberAsync(replacement);

        var page = await ReadDataAsync<PagedResult<MyUtilityReadingResponse>>(
            await client.GetAsync($"/api/me/leases/{leaseId}/utility-readings"));

        var reading = Assert.Single(page.Items);
        Assert.Equal($"https://storage.test/{storageKey}", reading.ImageUrl);
        Assert.Equal(replacementNumber, reading.InvoiceNumber);
    }

    [Fact]
    public async Task GetUtilityReadings_FilterByServiceAndMonths()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        var electricity = await SeedElectricityServiceAsync();
        var water = await SeedWaterServiceAsync();
        await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 1, 1));
        var february = await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 2, 1));
        var march = await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 3, 1));
        await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 4, 1));
        await SeedReadingAsync(leaseId, water, new DateOnly(2026, 2, 1));

        var page = await ReadDataAsync<PagedResult<MyUtilityReadingResponse>>(await client.GetAsync(
            $"/api/me/leases/{leaseId}/utility-readings?serviceId={electricity}&from=2026-02-01&to=2026-03-01"));

        Assert.Equal([march, february], page.Items.Select(r => r.Id));
    }

    [Fact]
    public async Task GetUtilityReadings_SoftDeletedReading_NotListed()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        var electricity = await SeedElectricityServiceAsync();
        var deleted = await SeedReadingAsync(leaseId, electricity, new DateOnly(2026, 2, 1));
        await SoftDeleteReadingAsync(deleted);

        var page = await ReadDataAsync<PagedResult<MyUtilityReadingResponse>>(
            await client.GetAsync($"/api/me/leases/{leaseId}/utility-readings"));

        // the query uses IgnoreQueryFilters: this fails if its explicit DeletedAt check is dropped
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task GetUtilityReadings_OfAnotherTenantsLease_Returns404()
    {
        var (client, _) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-B", new DateOnly(2026, 1, 1), tenantB);

        var response = await client.GetAsync($"/api/me/leases/{leaseId}/utility-readings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUtilityReadings_OfDraftLease_Returns404()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var draft = await SeedLeaseAsync(LeaseStatus.Draft, "HD-DRAFT", new DateOnly(2026, 1, 1), tenantA);

        var response = await client.GetAsync($"/api/me/leases/{draft}/utility-readings");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUtilityReadings_ToBeforeFrom_Returns400()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);

        var response = await client.GetAsync($"/api/me/leases/{leaseId}/utility-readings?from=2026-06-01&to=2026-05-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPayments_OnlyPaymentsOfVisibleInvoices()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseA = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        var leaseB = await SeedLeaseAsync(LeaseStatus.Expired, "HD-B", new DateOnly(2025, 1, 1), tenantB);
        var mine = await SeedPaymentAsync(await SeedInvoiceAsync(leaseA, new DateOnly(2026, 2, 1)), note: "internal note");
        await SeedPaymentAsync(await SeedInvoiceAsync(leaseB, new DateOnly(2025, 2, 1)));

        var response = await client.GetAsync("/api/me/payments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var page = JsonSerializer.Deserialize<ApiResponse<PagedResult<MyPaymentResponse>>>(json, JsonOptions)!.Data!;
        var payment = Assert.Single(page.Items);
        Assert.Equal(mine, payment.Id);
        Assert.Equal(("HD-A", "Property A", "101"), (payment.LeaseCode, payment.PropertyName, payment.RoomNumber));
        Assert.DoesNotContain("internal note", json);
    }

    [Fact]
    public async Task GetPayments_TenantWhoMovedOut_DoesNotSeePaymentsOfLaterMonths()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var (_, tenantB) = await RegisterAndLoginAsync("Tenant B");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-AB", new DateOnly(2026, 1, 1), tenantB, tenantA);
        await SetTenantDatesAsync(leaseId, tenantA, joinedDate: null, leftDate: new DateOnly(2026, 6, 15));
        var june = await SeedPaymentAsync(await SeedInvoiceAsync(leaseId, new DateOnly(2026, 6, 1)));
        await SeedPaymentAsync(await SeedInvoiceAsync(leaseId, new DateOnly(2026, 7, 1)));

        var page = await ReadDataAsync<PagedResult<MyPaymentResponse>>(await client.GetAsync("/api/me/payments"));

        Assert.Equal([june], page.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task GetPayments_FilterByInvoiceAndStatus_NewestFirst()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        var february = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 2, 1));
        var march = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 3, 1));
        var day = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var older = await SeedPaymentAsync(february, PaymentStatus.Confirmed, paymentDate: day);
        var newer = await SeedPaymentAsync(february, PaymentStatus.Confirmed, paymentDate: day.AddDays(5));
        var voided = await SeedPaymentAsync(february, PaymentStatus.Voided, paymentDate: day.AddDays(1));
        await SeedPaymentAsync(march, PaymentStatus.Confirmed, paymentDate: day.AddDays(10));

        var confirmed = await ReadDataAsync<PagedResult<MyPaymentResponse>>(
            await client.GetAsync($"/api/me/payments?invoiceId={february}&status=confirmed"));
        var all = await ReadDataAsync<PagedResult<MyPaymentResponse>>(
            await client.GetAsync($"/api/me/payments?invoiceId={february}"));

        Assert.Equal([newer, older], confirmed.Items.Select(p => p.Id));
        Assert.Equal([newer, voided, older], all.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task GetPayments_PayerName_OverrideOrUserName()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        var invoiceId = await SeedInvoiceAsync(leaseId, new DateOnly(2026, 2, 1));
        var day = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedPaymentAsync(invoiceId, paymentDate: day, payerUserId: tenantA);
        await SeedPaymentAsync(invoiceId, paymentDate: day.AddDays(1), payerNameOverride: "Mother of Tenant A");
        await SeedPaymentAsync(invoiceId, paymentDate: day.AddDays(2));

        var page = await ReadDataAsync<PagedResult<MyPaymentResponse>>(await client.GetAsync("/api/me/payments"));

        Assert.Equal([null, "Mother of Tenant A", "Tenant A"], page.Items.Select(p => p.PayerName));
    }

    [Fact]
    public async Task GetPayments_DoesNotExposeStaffFields()
    {
        var (client, tenantA) = await RegisterAndLoginAsync("Tenant A");
        var leaseId = await SeedLeaseAsync(LeaseStatus.Active, "HD-A", new DateOnly(2026, 1, 1), tenantA);
        await SeedPaymentAsync(await SeedInvoiceAsync(leaseId, new DateOnly(2026, 2, 1)), PaymentStatus.Voided, note: "internal note");

        var json = await (await client.GetAsync("/api/me/payments")).Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);
        var payment = Assert.Single(document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        foreach (var hidden in new[] { "note", "voidReason", "confirmedBy", "voidedBy", "createdBy", "payerUserId" })
        {
            Assert.False(payment.TryGetProperty(hidden, out _), hidden);
        }

        Assert.DoesNotContain("staff void reason", json);
    }
}
