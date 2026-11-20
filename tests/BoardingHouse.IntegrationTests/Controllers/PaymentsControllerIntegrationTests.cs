using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BoardingHouse.Api.Common;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Auth;
using BoardingHouse.Application.DTOs.Invoices;
using BoardingHouse.Application.DTOs.Leases;
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.DTOs.Payments;
using BoardingHouse.Application.DTOs.Properties;
using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Seed;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class PaymentsControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "payment-actor@test.com";
    private const string Password = "password123";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly CurrentMonth => BillingMonth.Of(Today).FirstDay;
    private static DateOnly PreviousMonth => CurrentMonth.AddMonths(-1);

    private Guid _actorId;
    private Guid _organizationId;
    private Guid _propertyId;
    private int _roomCount;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();
        _organizationId = await CreateOrganizationAsync("Payment Org");
        _propertyId = await CreatePropertyAsync(_organizationId, "Payment Property");
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
            Phone = "0900000094",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Payment Actor"
        });
        var actor = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;

        await factory.GrantPlatformAdminRoleAsync(actor.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = ActorEmail, Password = Password });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        _actorId = actor.Id;
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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;
        return (user.Id, email);
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

    // Rent 3,000,000 for the current month, no deposit, no VAT, due today: PENDING. A new room + active lease each time.
    private async Task<(InvoiceDetailResponse Invoice, Guid TenantId)> CreateInvoiceAsync(Guid? propertyId = null)
    {
        var roomId = await CreateRoomAsync(propertyId ?? _propertyId, $"{101 + _roomCount++}");
        var (tenantId, _) = await RegisterUserAsync("Payment Tenant");

        var leaseResponse = await _client.PostAsJsonAsync("/api/leases", new CreateLeaseRequest
        {
            RoomId = roomId,
            StartDate = PreviousMonth,
            MonthlyRent = 3_000_000,
            DepositAmount = 0,
            Tenants = [new CreateLeaseTenantRequest { UserId = tenantId, IsRepresentative = true }]
        }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, leaseResponse.StatusCode);
        var lease = await ReadDataAsync<LeaseResponse>(leaseResponse);

        var activateResponse = await _client.PostAsync($"/api/leases/{lease.Id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        var invoiceResponse = await _client.PostAsJsonAsync(
            "/api/invoices", new CreateInvoiceRequest { LeaseId = lease.Id, BillingMonth = CurrentMonth }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, invoiceResponse.StatusCode);
        var invoice = await ReadDataAsync<InvoiceDetailResponse>(invoiceResponse);
        Assert.Equal(3_000_000, invoice.TotalAmount);

        return (invoice, tenantId);
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

    // Admin of a brand-new organization: holds every payment permission, but not in _organizationId
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

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    private static async Task<string?> ReadTitleAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("title").GetString();
    }

    private static async Task<JsonElement> ReadErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors").Clone();
    }

    private static RecordPaymentRequest Cash(decimal amount, bool confirm = false) => new()
    {
        Amount = amount,
        Method = PaymentMethod.Cash,
        PaymentDate = DateTimeOffset.UtcNow.AddMinutes(-1),
        Confirm = confirm
    };

    private Task<HttpResponseMessage> RecordAsync(Guid invoiceId, RecordPaymentRequest request, HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync($"/api/invoices/{invoiceId}/payments", request, JsonOptions);

    private async Task<PaymentResponse> RecordOkAsync(Guid invoiceId, RecordPaymentRequest request, HttpClient? client = null)
    {
        var response = await RecordAsync(invoiceId, request, client);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadDataAsync<PaymentResponse>(response);
    }

    private Task<HttpResponseMessage> GetPaymentAsync(Guid id, HttpClient? client = null) =>
        (client ?? _client).GetAsync($"/api/payments/{id}");

    private async Task<PaymentResponse> GetPaymentOkAsync(Guid id)
    {
        var response = await GetPaymentAsync(id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<PaymentResponse>(response);
    }

    private async Task<InvoiceDetailResponse> GetInvoiceOkAsync(Guid id)
    {
        var response = await _client.GetAsync($"/api/invoices/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<InvoiceDetailResponse>(response);
    }

    private async Task<List<InvoiceStatusHistory>> GetStatusHistoriesAsync(Guid invoiceId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InvoiceStatusHistories
            .AsNoTracking()
            .Where(h => h.InvoiceId == invoiceId)
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.Id)
            .ToListAsync();
    }

    private Task<HttpResponseMessage> ConfirmAsync(Guid id, HttpClient? client = null) =>
        (client ?? _client).PostAsync($"/api/payments/{id}/confirm", null);

    private async Task<PaymentResponse> ConfirmOkAsync(Guid id)
    {
        var response = await ConfirmAsync(id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<PaymentResponse>(response);
    }

    private Task<HttpResponseMessage> VoidAsync(Guid id, string reason = "Bounced transfer", HttpClient? client = null) =>
        (client ?? _client).PostAsJsonAsync($"/api/payments/{id}/void", new VoidPaymentRequest { Reason = reason }, JsonOptions);

    private async Task<PaymentResponse> VoidOkAsync(Guid id, string reason = "Bounced transfer")
    {
        var response = await VoidAsync(id, reason);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadDataAsync<PaymentResponse>(response);
    }

    private async Task SetDueDateAsync(Guid invoiceId, DateOnly dueDate)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await context.Invoices.SingleAsync(i => i.Id == invoiceId);
        TestEntities.Set(invoice, new { DueDate = dueDate });
        await context.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> CancelInvoiceAsync(Guid invoiceId) =>
        _client.PostAsJsonAsync(
            $"/api/invoices/{invoiceId}/cancel", new CancelInvoiceRequest { Reason = "Wrong meter reading" }, JsonOptions);

    [Fact]
    public async Task Record_WithoutConfirm_CreatesAPendingPaymentAndLeavesTheInvoiceUnpaid()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var paidAt = DateTimeOffset.UtcNow.AddHours(-2);

        var response = await RecordAsync(invoice.Id, Cash(1_000_000) with
        {
            PaymentDate = paidAt,
            TransactionReference = " RCPT-0001 ",
            Note = "Paid at the office"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await ReadDataAsync<PaymentResponse>(response);
        Assert.Equal($"/api/payments/{payment.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(1_000_000, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.Equal(paidAt.ToUnixTimeMilliseconds(), payment.PaymentDate.ToUnixTimeMilliseconds());
        Assert.Equal("RCPT-0001", payment.TransactionReference);
        Assert.Equal("Paid at the office", payment.Note);
        Assert.Equal(invoice.Id, payment.InvoiceId);
        Assert.Equal(invoice.InvoiceNumber, payment.InvoiceNumber);
        Assert.Equal(invoice.LeaseCode, payment.LeaseCode);
        Assert.Equal(invoice.RoomNumber, payment.RoomNumber);
        Assert.Equal("Payment Property", payment.PropertyName);
        Assert.Equal(_organizationId, payment.OrganizationId);
        Assert.Equal(_actorId, payment.CreatedBy);
        Assert.Null(payment.ConfirmedAt);

        var after = await GetInvoiceOkAsync(invoice.Id);
        Assert.Equal(0, after.PaidAmount);
        Assert.Equal(InvoiceStatus.Pending, after.Status);
    }

    [Fact]
    public async Task Record_WithConfirm_MarksTheInvoicePartialAndRecordsTheChange()
    {
        var (invoice, _) = await CreateInvoiceAsync();

        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(_actorId, payment.ConfirmedBy);
        Assert.NotNull(payment.ConfirmedAt);

        var after = await GetInvoiceOkAsync(invoice.Id);
        Assert.Equal(1_000_000, after.PaidAmount);
        Assert.Equal(2_000_000, after.RemainingAmount);
        Assert.Equal(InvoiceStatus.Partial, after.Status);

        var history = (await GetStatusHistoriesAsync(invoice.Id)).Last();
        Assert.Equal(InvoiceStatus.Pending, history.OldStatus);
        Assert.Equal(InvoiceStatus.Partial, history.NewStatus);
        Assert.Equal(_actorId, history.ChangedBy);
    }

    [Fact]
    public async Task Record_TheWholeAmountWithConfirm_MarksTheInvoicePaid()
    {
        var (invoice, _) = await CreateInvoiceAsync();

        await RecordOkAsync(invoice.Id, Cash(3_000_000, confirm: true));

        var after = await GetInvoiceOkAsync(invoice.Id);
        Assert.Equal(3_000_000, after.PaidAmount);
        Assert.Equal(0, after.RemainingAmount);
        Assert.Equal(InvoiceStatus.Paid, after.Status);
    }

    [Fact]
    public async Task Record_MoreThanTheRemainingAmount_ReturnsBadRequest()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));

        var response = await RecordAsync(invoice.Id, Cash(2_000_001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Amount must not exceed the remaining amount of the invoice (2000000)", await ReadTitleAsync(response));
    }

    [Fact]
    public async Task Record_PendingPaymentsDoNotCountTowardsTheRemainingAmount()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        await RecordOkAsync(invoice.Id, Cash(3_000_000));

        var response = await RecordAsync(invoice.Id, Cash(3_000_000));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Record_ForACancelledInvoice_ReturnsConflict()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        Assert.Equal(HttpStatusCode.OK, (await CancelInvoiceAsync(invoice.Id)).StatusCode);

        var response = await RecordAsync(invoice.Id, Cash(1_000_000));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("A cancelled invoice cannot take payments", await ReadTitleAsync(response));
    }

    [Fact]
    public async Task Record_WithoutPayer_DefaultsToTheRepresentativeTenant()
    {
        var (invoice, tenantId) = await CreateInvoiceAsync();

        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000));

        Assert.Equal(tenantId, payment.PayerUserId);
        Assert.Null(payment.PayerNameOverride);
        Assert.Equal("Payment Tenant", payment.PayerName);
    }

    [Fact]
    public async Task Record_PayerName_KeepsItWithoutAPayerUser()
    {
        var (invoice, _) = await CreateInvoiceAsync();

        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000) with { PayerNameOverride = "Tenant's mother" });

        Assert.Null(payment.PayerUserId);
        Assert.Equal("Tenant's mother", payment.PayerNameOverride);
        Assert.Equal("Tenant's mother", payment.PayerName);
    }

    [Fact]
    public async Task Record_PayerWhoIsNotATenantOfTheLease_ReturnsBadRequest()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var (strangerId, _) = await RegisterUserAsync("Stranger");

        var response = await RecordAsync(invoice.Id, Cash(1_000_000) with { PayerUserId = strangerId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ReadErrorsAsync(response);
        Assert.Equal("PayerUserId must be a tenant of the lease", errors.GetProperty("payerUserId")[0].GetString());
    }

    [Fact]
    public async Task Record_GatewayMethod_ReturnsBadRequest()
    {
        var (invoice, _) = await CreateInvoiceAsync();

        var response = await RecordAsync(invoice.Id, Cash(1_000_000) with { Method = PaymentMethod.VnPay });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Record_UnknownInvoice_ReturnsNotFound()
    {
        var id = Guid.NewGuid();

        var response = await RecordAsync(id, Cash(1_000_000));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessages.Invoice(id), await ReadTitleAsync(response));
    }

    [Fact]
    public async Task Record_InvoiceOfAnotherOrganization_ReturnsNotFound()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await RecordAsync(invoice.Id, Cash(1_000_000), otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_PlatformStaff_ReturnsForbidden()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var staffClient = await CreatePlatformStaffClientAsync();

        var response = await RecordAsync(invoice.Id, Cash(1_000_000), staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // payment:confirm is organization-scoped: platform staff only hold payment:read
    [Fact]
    public async Task Confirm_PlatformStaff_ReturnsForbiddenAndLeavesItPending()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000));
        var staffClient = await CreatePlatformStaffClientAsync();

        var response = await ConfirmAsync(payment.Id, staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentOkAsync(payment.Id)).Status);
        Assert.Equal(0, (await GetInvoiceOkAsync(invoice.Id)).PaidAmount);
    }

    [Fact]
    public async Task Record_OrganizationStaffWithConfirm_ConfirmsThePayment()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var staffClient = await CreateOrganizationMemberClientAsync(_organizationId);

        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true), staffClient);

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
    }

    [Fact]
    public async Task GetById_ReturnsThePayment()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var recorded = await RecordOkAsync(invoice.Id, Cash(1_000_000));

        var payment = await GetPaymentOkAsync(recorded.Id);

        Assert.Equal(recorded, payment);
    }

    [Fact]
    public async Task GetById_UnknownPayment_ReturnsNotFound()
    {
        var id = Guid.NewGuid();

        var response = await GetPaymentAsync(id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessages.Payment(id), await ReadTitleAsync(response));
    }

    [Fact]
    public async Task GetById_PaymentOfAnotherOrganization_ReturnsNotFound()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000));
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await GetPaymentAsync(payment.Id, otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_PlatformStaff_ReturnsThePayment()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000));
        var staffClient = await CreatePlatformStaffClientAsync();

        var response = await GetPaymentAsync(payment.Id, staffClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_TwoPayments_MarkTheInvoicePartialThenPaidWithHistory()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var first = await RecordOkAsync(invoice.Id, Cash(1_000_000));
        var second = await RecordOkAsync(invoice.Id, Cash(2_000_000));

        var confirmed = await ConfirmOkAsync(first.Id);

        Assert.Equal(PaymentStatus.Confirmed, confirmed.Status);
        Assert.Equal(_actorId, confirmed.ConfirmedBy);
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.Equal(InvoiceStatus.Partial, (await GetInvoiceOkAsync(invoice.Id)).Status);

        await ConfirmOkAsync(second.Id);

        var after = await GetInvoiceOkAsync(invoice.Id);
        Assert.Equal(3_000_000, after.PaidAmount);
        Assert.Equal(InvoiceStatus.Paid, after.Status);
        var histories = await GetStatusHistoriesAsync(invoice.Id);
        Assert.Equal(
            [InvoiceStatus.Pending, InvoiceStatus.Partial, InvoiceStatus.Paid],
            histories.Select(h => h.NewStatus));
        Assert.All(histories.Skip(1), h => Assert.Equal(_actorId, h.ChangedBy));
    }

    [Fact]
    public async Task Confirm_ExceedingTheRemainingAmount_ReturnsBadRequestAndLeavesItPending()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var first = await RecordOkAsync(invoice.Id, Cash(2_000_000));
        var second = await RecordOkAsync(invoice.Id, Cash(2_000_000));
        await ConfirmOkAsync(first.Id);

        var response = await ConfirmAsync(second.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Amount must not exceed the remaining amount of the invoice (1000000)", await ReadTitleAsync(response));
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentOkAsync(second.Id)).Status);
        Assert.Equal(2_000_000, (await GetInvoiceOkAsync(invoice.Id)).PaidAmount);
    }

    [Fact]
    public async Task Confirm_TwoPaymentsAtOnceThatTogetherExceedTheInvoice_OnlyOneSucceeds()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var first = await RecordOkAsync(invoice.Id, Cash(2_000_000));
        var second = await RecordOkAsync(invoice.Id, Cash(2_000_000));

        var responses = await Task.WhenAll(ConfirmAsync(first.Id), ConfirmAsync(second.Id));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        var after = await GetInvoiceOkAsync(invoice.Id);
        Assert.Equal(2_000_000, after.PaidAmount);
        Assert.Equal(InvoiceStatus.Partial, after.Status);
    }

    [Fact]
    public async Task Confirm_AlreadyConfirmed_ReturnsConflict()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));

        var response = await ConfirmAsync(payment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Only pending payments can be confirmed", await ReadTitleAsync(response));
        Assert.Equal(1_000_000, (await GetInvoiceOkAsync(invoice.Id)).PaidAmount);
    }

    [Fact]
    public async Task Confirm_UnknownPayment_ReturnsNotFound()
    {
        var id = Guid.NewGuid();

        var response = await ConfirmAsync(id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessages.Payment(id), await ReadTitleAsync(response));
    }

    [Fact]
    public async Task Confirm_PaymentOfAnotherOrganization_ReturnsNotFound()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000));
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await ConfirmAsync(payment.Id, otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentOkAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task Confirm_OrganizationStaff_ReturnsOk()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000));
        var staffClient = await CreateOrganizationMemberClientAsync(_organizationId);

        var response = await ConfirmAsync(payment.Id, staffClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Void_ConfirmedPayment_TakesItOffTheInvoiceAndRecordsTheReason()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));

        var voided = await VoidOkAsync(payment.Id, "  Bounced transfer  ");

        Assert.Equal(PaymentStatus.Voided, voided.Status);
        Assert.Equal("Bounced transfer", voided.VoidReason);
        Assert.Equal(_actorId, voided.VoidedBy);
        Assert.NotNull(voided.VoidedAt);
        Assert.NotNull(voided.ConfirmedAt);

        var after = await GetInvoiceOkAsync(invoice.Id);
        Assert.Equal(0, after.PaidAmount);
        Assert.Equal(InvoiceStatus.Pending, after.Status);
        var history = (await GetStatusHistoriesAsync(invoice.Id)).Last();
        Assert.Equal(InvoiceStatus.Partial, history.OldStatus);
        Assert.Equal(InvoiceStatus.Pending, history.NewStatus);
        Assert.Equal(_actorId, history.ChangedBy);
    }

    [Fact]
    public async Task Void_PaymentOfAPaidInvoicePastItsDueDate_MakesItOverdue()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(3_000_000, confirm: true));
        await SetDueDateAsync(invoice.Id, Today.AddDays(-1));

        await VoidOkAsync(payment.Id);

        var after = await GetInvoiceOkAsync(invoice.Id);
        Assert.Equal(0, after.PaidAmount);
        Assert.Equal(InvoiceStatus.Overdue, after.Status);
    }

    [Fact]
    public async Task Void_PendingPayment_LeavesTheInvoice()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));
        var pending = await RecordOkAsync(invoice.Id, Cash(500_000));

        var voided = await VoidOkAsync(pending.Id, "Typed the wrong amount");

        Assert.Equal(PaymentStatus.Voided, voided.Status);
        Assert.Null(voided.ConfirmedAt);
        var after = await GetInvoiceOkAsync(invoice.Id);
        Assert.Equal(1_000_000, after.PaidAmount);
        Assert.Equal(InvoiceStatus.Partial, after.Status);
    }

    [Fact]
    public async Task Void_Twice_ReturnsConflict()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));
        await VoidOkAsync(payment.Id);

        var response = await VoidAsync(payment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("The payment is already voided", await ReadTitleAsync(response));
        Assert.Equal(0, (await GetInvoiceOkAsync(invoice.Id)).PaidAmount);
    }

    [Fact]
    public async Task Void_ThenConfirm_ReturnsConflict()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000));
        await VoidOkAsync(payment.Id);

        var response = await ConfirmAsync(payment.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Void_BlankReason_ReturnsBadRequest()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000));

        var response = await VoidAsync(payment.Id, "   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentOkAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task Void_OrganizationStaff_ReturnsForbidden()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));
        var staffClient = await CreateOrganizationMemberClientAsync(_organizationId);

        var response = await VoidAsync(payment.Id, client: staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(PaymentStatus.Confirmed, (await GetPaymentOkAsync(payment.Id)).Status);
    }

    [Fact]
    public async Task Void_OrganizationAdmin_ReturnsOk()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));
        var adminClient = await CreateOrganizationMemberClientAsync(_organizationId, RoleSlugs.OrganizationAdmin);

        var response = await VoidAsync(payment.Id, client: adminClient);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Void_PaymentOfAnotherOrganization_ReturnsNotFound()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));
        var otherClient = await CreateOtherOrganizationMemberClientAsync();

        var response = await VoidAsync(payment.Id, client: otherClient);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // A wrong payment is voided and recorded again: the voided amount is free for the new one
    [Fact]
    public async Task Void_ConfirmedPayment_ThenRecordingTheWholeAmountAgain_IsAccepted()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var wrong = await RecordOkAsync(invoice.Id, Cash(3_000_000, confirm: true));
        await VoidOkAsync(wrong.Id, "Recorded on the wrong invoice");

        var again = await RecordOkAsync(invoice.Id, Cash(3_000_000, confirm: true));

        Assert.Equal(PaymentStatus.Confirmed, again.Status);
        var after = await GetInvoiceOkAsync(invoice.Id);
        Assert.Equal(3_000_000, after.PaidAmount);
        Assert.Equal(InvoiceStatus.Paid, after.Status);
    }

    [Fact]
    public async Task Void_UnknownPayment_ReturnsNotFound()
    {
        var id = Guid.NewGuid();

        var response = await VoidAsync(id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessages.Payment(id), await ReadTitleAsync(response));
    }

    [Fact]
    public async Task Void_PlatformStaff_ReturnsForbiddenAndLeavesItConfirmed()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));
        var staffClient = await CreatePlatformStaffClientAsync();

        var response = await VoidAsync(payment.Id, client: staffClient);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(PaymentStatus.Confirmed, (await GetPaymentOkAsync(payment.Id)).Status);
        Assert.Equal(1_000_000, (await GetInvoiceOkAsync(invoice.Id)).PaidAmount);
    }

    [Fact]
    public async Task CancelInvoice_WithPendingPayments_VoidsThem()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var first = await RecordOkAsync(invoice.Id, Cash(1_000_000));
        var second = await RecordOkAsync(invoice.Id, Cash(2_000_000));
        var (otherInvoice, _) = await CreateInvoiceAsync();
        var untouched = await RecordOkAsync(otherInvoice.Id, Cash(1_000_000));

        var response = await CancelInvoiceAsync(invoice.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var id in new[] { first.Id, second.Id })
        {
            var payment = await GetPaymentOkAsync(id);
            Assert.Equal(PaymentStatus.Voided, payment.Status);
            Assert.Equal(Payment.InvoiceCancelledReason, payment.VoidReason);
            Assert.Equal(_actorId, payment.VoidedBy);
        }
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentOkAsync(untouched.Id)).Status);
    }

    [Fact]
    public async Task CancelInvoice_WithAConfirmedPayment_ReturnsConflictAndKeepsThePendingOnes()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));
        var pending = await RecordOkAsync(invoice.Id, Cash(500_000));

        var response = await CancelInvoiceAsync(invoice.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentOkAsync(pending.Id)).Status);
    }

    [Fact]
    public async Task CancelInvoice_AfterVoidingTheConfirmedPayment_Succeeds()
    {
        var (invoice, _) = await CreateInvoiceAsync();
        var payment = await RecordOkAsync(invoice.Id, Cash(1_000_000, confirm: true));
        await VoidOkAsync(payment.Id);

        var response = await CancelInvoiceAsync(invoice.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(InvoiceStatus.Cancelled, (await GetInvoiceOkAsync(invoice.Id)).Status);
    }
}
