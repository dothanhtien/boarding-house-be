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
using BoardingHouse.Application.DTOs.Organizations;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.DTOs.VietQr;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Domain.VietQr;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Persistence.Seed;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Controllers;

public class VietQrControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private const string ActorEmail = "vietqr-actor@test.com";
    private const string Password = "password123";

    private Guid _invoiceId;
    private Guid _organizationId;

    public async Task InitializeAsync()
    {
        await factory.ResetAsync();
        await AuthenticateAsync();

        // Unpaid 2,000,000 invoice INV-2026-11-0001 of a draft lease, in an organization with no settings yet
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (_invoiceId, _, _) = await TestInvoices.SeedAsync(context);
        _organizationId = await context.Invoices
            .Where(i => i.Id == _invoiceId)
            .Select(i => i.OrganizationId)
            .SingleAsync();
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
            FullName = "VietQR Actor"
        });
        var actor = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;

        await factory.GrantPlatformAdminRoleAsync(actor.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = ActorEmail, Password = Password });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
    }

    private async Task<HttpClient> RegisterAndLoginAsync(string fullName)
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
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        return client;
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonOptions))!.Data!;

    private async Task<HttpClient> CreateOrganizationMemberClientAsync(Guid organizationId, string roleSlug)
    {
        var email = $"user-{Guid.NewGuid():N}@test.com";
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = $"09{Random.Shared.Next(10000000, 99999999)}",
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Organization Member"
        });
        var member = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())!.Data!;

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await RbacSeeder.SeedAsync(context);

            var role = await context.Roles.SingleAsync(r => r.Slug == roleSlug);
            context.OrganizationMembers.Add(OrganizationMember.Create(organizationId, member.Id, role, SentinelActors.System));
            await context.SaveChangesAsync();
        }

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));
        return client;
    }

    // Admin of a brand-new organization: can read invoices, but not in _organizationId
    private async Task<HttpClient> CreateOtherOrganizationMemberClientAsync()
    {
        Guid organizationId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var organization = TestEntities.New<Organization>(new { Name = $"Other Org {Guid.NewGuid()}", CreatedBy = SentinelActors.System });
            context.Organizations.Add(organization);
            await context.SaveChangesAsync();
            organizationId = organization.Id;
        }

        return await CreateOrganizationMemberClientAsync(organizationId, RoleSlugs.OrganizationAdmin);
    }

    private static async Task<string?> ReadTitleAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("title").GetString();
    }

    private async Task ConfigureBankAccountAsync(string bankBin = "970422", string accountNumber = "0123456789")
    {
        var response = await _client.PatchAsJsonAsync($"/api/organizations/{_organizationId}/settings", new UpdateOrganizationSettingsRequest
        {
            BankBin = bankBin,
            BankAccountNumber = accountNumber,
            BankAccountName = "NGUYEN VAN A"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SetInvoiceAsync(object values)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await context.Invoices.SingleAsync(i => i.Id == _invoiceId);
        TestEntities.Set(invoice, values);
        await context.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> GetPaymentQrAsync(Guid? invoiceId = null, HttpClient? client = null) =>
        (client ?? _client).GetAsync($"/api/invoices/{invoiceId ?? _invoiceId}/payment-qr");

    [Fact]
    public async Task GetBanks_ReturnsEverySupportedBankInOrder()
    {
        var response = await _client.GetAsync("/api/vietqr/banks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var banks = await ReadDataAsync<List<VietQrBankResponse>>(response);
        Assert.Equal(VietQrBanks.All.Select(b => b.Bin), banks.Select(b => b.Bin));
        Assert.Contains(banks, b => b is { Bin: "970436", Code: "VCB", ShortName: "Vietcombank" });
    }

    [Fact]
    public async Task GetBanks_UsesCamelCaseFields()
    {
        var response = await _client.GetAsync("/api/vietqr/banks");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var first = document.RootElement.GetProperty("data")[0];
        Assert.Equal("970415", first.GetProperty("bin").GetString());
        Assert.Equal("ICB", first.GetProperty("code").GetString());
        Assert.Equal("VietinBank", first.GetProperty("shortName").GetString());
    }

    [Fact]
    public async Task GetBanks_UserWithoutAnyRole_ReturnsOk()
    {
        var client = await RegisterAndLoginAsync("No Role");

        var response = await client.GetAsync("/api/vietqr/banks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetBanks_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/vietqr/banks");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private const string SeededInvoicePayload =
        "00020101021238540010A00000072701240006970422011001234567890208QRIBFTTA5303704540720000005802VN62170813INV2026110001630465D9";

    [Fact]
    public async Task GetPaymentQr_PendingInvoice_ReturnsTheWholeAmount()
    {
        await ConfigureBankAccountAsync();

        var response = await GetPaymentQrAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var qr = await ReadDataAsync<InvoicePaymentQrResponse>(response);
        Assert.Equal(_invoiceId, qr.InvoiceId);
        Assert.Equal("INV-2026-11-0001", qr.InvoiceNumber);
        Assert.Equal(SeededInvoicePayload, qr.Payload);
        Assert.Equal(2_000_000m, qr.Amount);
        Assert.Equal("INV2026110001", qr.TransferContent);
        Assert.Equal(("970422", "MB", "MBBank"), (qr.BankBin, qr.BankCode, qr.BankShortName));
        Assert.Equal(("0123456789", "NGUYEN VAN A"), (qr.AccountNumber, qr.AccountName));
    }

    [Fact]
    public async Task GetPaymentQr_AfterAPartialPayment_ReturnsTheRemainingAmount()
    {
        await ConfigureBankAccountAsync();
        await SetInvoiceAsync(new { PaidAmount = 500_000m, Status = InvoiceStatus.Partial });

        var response = await GetPaymentQrAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var qr = await ReadDataAsync<InvoicePaymentQrResponse>(response);
        Assert.Equal(1_500_000m, qr.Amount);
        Assert.Contains("54071500000", qr.Payload);
    }

    [Fact]
    public async Task GetPaymentQr_DoesNotWriteTheInvoice()
    {
        await ConfigureBankAccountAsync();

        var response = await GetPaymentQrAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await context.Invoices.AsNoTracking().SingleAsync(i => i.Id == _invoiceId);
        Assert.Equal((InvoiceStatus.Pending, (DateTimeOffset?)null), (invoice.Status, invoice.UpdatedAt));
    }

    [Theory]
    [InlineData(InvoiceStatus.Paid, 2_000_000, Invoice.AlreadyPaidMessage)]
    [InlineData(InvoiceStatus.Cancelled, 0, Invoice.CancelledMessage)]
    [InlineData(InvoiceStatus.Partial, 1_999_999.5, Invoice.NothingLeftToPayMessage)]
    public async Task GetPaymentQr_InvoiceThatCannotTakeATransfer_ReturnsConflict(InvoiceStatus status, decimal paidAmount, string title)
    {
        await ConfigureBankAccountAsync();
        await SetInvoiceAsync(new { Status = status, PaidAmount = paidAmount });

        var response = await GetPaymentQrAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(title, await ReadTitleAsync(response));
    }

    [Fact]
    public async Task GetPaymentQr_OrganizationWithoutSettings_ReturnsConflict()
    {
        var response = await GetPaymentQrAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(OrganizationSettings.BankAccountNotConfiguredMessage, await ReadTitleAsync(response));
    }

    [Fact]
    public async Task GetPaymentQr_SettingsWithoutBankBin_ReturnsConflict()
    {
        var settingsResponse = await _client.PatchAsJsonAsync($"/api/organizations/{_organizationId}/settings", new UpdateOrganizationSettingsRequest
        {
            BankAccountNumber = "0123456789",
            BankName = "MB Bank"
        });
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        var response = await GetPaymentQrAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(OrganizationSettings.BankAccountNotConfiguredMessage, await ReadTitleAsync(response));
    }

    [Fact]
    public async Task GetPaymentQr_UnknownInvoice_ReturnsNotFound()
    {
        var id = Guid.NewGuid();

        var response = await GetPaymentQrAsync(id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessages.Invoice(id), await ReadTitleAsync(response));
    }

    [Fact]
    public async Task GetPaymentQr_InvoiceOfAnotherOrganization_ReturnsNotFound()
    {
        await ConfigureBankAccountAsync();
        var client = await CreateOtherOrganizationMemberClientAsync();

        var response = await GetPaymentQrAsync(client: client);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPaymentQr_OrganizationStaff_ReturnsOk()
    {
        await ConfigureBankAccountAsync();
        var client = await CreateOrganizationMemberClientAsync(_organizationId, RoleSlugs.OrganizationStaff);

        var response = await GetPaymentQrAsync(client: client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetPaymentQr_UserWithoutInvoiceRead_ReturnsForbidden()
    {
        await ConfigureBankAccountAsync();
        var client = await RegisterAndLoginAsync("No Role");

        var response = await GetPaymentQrAsync(client: client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPaymentQr_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await GetPaymentQrAsync(client: client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
