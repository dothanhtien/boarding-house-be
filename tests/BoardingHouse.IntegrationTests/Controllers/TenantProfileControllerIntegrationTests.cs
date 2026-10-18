using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.TenantProfiles;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Services;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardingHouse.IntegrationTests.Controllers;

public class TenantProfileControllerIntegrationTests(PostgresApiFactory factory)
    : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    private const string Url = "/api/auth/me/tenant-profile";
    private const string Password = "password123";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string ExtractAccessTokenCookie(HttpResponseMessage response)
    {
        var setCookieHeader = response.Headers.GetValues("Set-Cookie")
            .Single(h => h.StartsWith("accessToken=", StringComparison.Ordinal));

        return setCookieHeader.Split(';')[0]["accessToken=".Length..];
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync(string email, string phone)
    {
        var client = factory.CreateClient();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email,
            Phone = phone,
            Password = Password,
            PasswordConfirmation = Password,
            FullName = "Tenant User"
        });
        var user = (await registerResponse.Content.ReadFromJsonAsync<ApiResponse<UserResponse>>())?.Data;

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Identifier = email, Password = Password });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ExtractAccessTokenCookie(loginResponse));

        return (client, user!.Id);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, UpdateTenantProfileRequest request) =>
        await client.PatchAsJsonAsync(Url, request, JsonOptions);

    private static async Task<TenantProfileResponse> ReadProfileAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResponse<TenantProfileResponse>>(JsonOptions))!.Data!;

    private async Task<int> CountProfilesAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.TenantProfiles.CountAsync(t => t.UserId == userId);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        var response = await factory.CreateClient().GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task AssertNoProfileAsync(HttpClient client)
    {
        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<TenantProfileResponse?>>(JsonOptions);
        Assert.Null(body!.Data);
    }

    [Fact]
    public async Task Get_NoProfileYet_Returns200WithNullData()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");

        await AssertNoProfileAsync(client);
    }

    [Fact]
    public async Task Update_NoProfileYet_CreatesProfileForCaller()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");

        var response = await PatchAsync(client, new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.Passport,
            IdentityNumber = "b1234567",
            IdentityIssuedDate = new DateOnly(2020, 1, 15),
            IdentityExpiryDate = new DateOnly(2030, 1, 15),
            DateOfBirth = new DateOnly(1995, 6, 20),
            Gender = Gender.Female,
            PermanentAddress = "12 Nguyen Trai, Ha Noi",
            EmergencyContactName = "Mother",
            EmergencyContactPhone = "0911111111"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await ReadProfileAsync(response);
        Assert.Equal(userId, created.UserId);
        Assert.Equal(IdentityType.Passport, created.IdentityType);
        Assert.Equal("B1234567", created.IdentityNumber);
        Assert.Equal(Gender.Female, created.Gender);
        Assert.Equal(new DateOnly(1995, 6, 20), created.DateOfBirth);

        var fetched = await ReadProfileAsync(await client.GetAsync(Url));
        Assert.Equal(created.Id, fetched.Id);
        Assert.Equal("B1234567", fetched.IdentityNumber);
        Assert.Equal("Mother", fetched.EmergencyContactName);
    }

    [Fact]
    public async Task Update_ExistingProfile_UpdatesSameRowAndKeepsOmittedFields()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");
        var created = await ReadProfileAsync(await PatchAsync(client, new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.CitizenIdentityCard,
            IdentityNumber = "001095012345",
            PermanentAddress = "Old address",
            EmergencyContactName = "Mother"
        }));

        var response = await PatchAsync(client, new UpdateTenantProfileRequest
        {
            PermanentAddress = "New address",
            EmergencyContactName = null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadProfileAsync(response);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("New address", updated.PermanentAddress);
        Assert.Null(updated.EmergencyContactName);
        Assert.Equal(IdentityType.CitizenIdentityCard, updated.IdentityType);
        Assert.Equal("001095012345", updated.IdentityNumber);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(1, await CountProfilesAsync(userId));
    }

    [Fact]
    public async Task Update_ClearIdentity_ClearsDocumentAndIssuanceDetails()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");
        await PatchAsync(client, new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.CitizenIdentityCard,
            IdentityNumber = "001095012345",
            IdentityIssuedDate = new DateOnly(2021, 5, 1),
            IdentityIssuedPlace = "Ha Noi",
            IdentityExpiryDate = new DateOnly(2036, 5, 1),
            PermanentAddress = "12 Nguyen Trai"
        });

        var response = await PatchAsync(client, new UpdateTenantProfileRequest { IdentityType = null, IdentityNumber = null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadProfileAsync(response);
        Assert.Null(updated.IdentityType);
        Assert.Null(updated.IdentityNumber);
        Assert.Null(updated.IdentityIssuedDate);
        Assert.Null(updated.IdentityIssuedPlace);
        Assert.Null(updated.IdentityExpiryDate);
        Assert.Equal("12 Nguyen Trai", updated.PermanentAddress);
    }

    [Fact]
    public async Task Update_IssuanceDetailsWithoutIdentityDocument_Returns400()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");

        var response = await PatchAsync(client, new UpdateTenantProfileRequest { IdentityIssuedPlace = "Ha Noi" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountProfilesAsync(userId));
    }

    [Fact]
    public async Task Update_ExistingProfileClearingEveryField_Returns400AndKeepsProfile()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");
        await PatchAsync(client, new UpdateTenantProfileRequest { PermanentAddress = "Old address", Gender = Gender.Male });

        var response = await PatchAsync(client, new UpdateTenantProfileRequest { PermanentAddress = null, Gender = null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await ReadProfileAsync(await client.GetAsync(Url));
        Assert.Equal("Old address", stored.PermanentAddress);
        Assert.Equal(Gender.Male, stored.Gender);
    }

    [Fact]
    public async Task Update_IdentityTypeWithoutNumber_Returns400()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");

        var response = await PatchAsync(client, new UpdateTenantProfileRequest { IdentityType = IdentityType.CitizenIdentityCard });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountProfilesAsync(userId));
    }

    [Fact]
    public async Task Update_ExpiryNotAfterStoredIssuedDate_Returns400()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");
        await PatchAsync(client, new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.CitizenIdentityCard,
            IdentityNumber = "001095012345",
            IdentityIssuedDate = new DateOnly(2021, 5, 1)
        });

        var response = await PatchAsync(client, new UpdateTenantProfileRequest { IdentityExpiryDate = new DateOnly(2021, 5, 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var stored = await ReadProfileAsync(await client.GetAsync(Url));
        Assert.Null(stored.IdentityExpiryDate);
    }

    [Fact]
    public async Task Update_IdentityAlreadyUsedByAnotherUser_Returns409()
    {
        var (clientA, _) = await CreateAuthenticatedClientAsync("a@test.com", "0900000001");
        var (clientB, userB) = await CreateAuthenticatedClientAsync("b@test.com", "0900000002");
        await PatchAsync(clientA, new UpdateTenantProfileRequest { IdentityType = IdentityType.Passport, IdentityNumber = "B1234567" });

        var response = await PatchAsync(clientB, new UpdateTenantProfileRequest { IdentityType = IdentityType.Passport, IdentityNumber = "b1234567" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountProfilesAsync(userB));
    }

    [Fact]
    public async Task Update_SameNumberDifferentIdentityType_Returns409()
    {
        var (clientA, _) = await CreateAuthenticatedClientAsync("a@test.com", "0900000001");
        var (clientB, userB) = await CreateAuthenticatedClientAsync("b@test.com", "0900000002");
        await PatchAsync(clientA, new UpdateTenantProfileRequest { IdentityType = IdentityType.CitizenIdentityCard, IdentityNumber = "123456789012" });

        var response = await PatchAsync(clientB, new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.Other,
            IdentityNumber = "123456789012"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountProfilesAsync(userB));
    }

    [Fact]
    public async Task Update_IdentityNumberChangedTooOften_Returns429()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");
        for (var i = 0; i < TenantProfileService.IdentityNumberChangeLimit; i++)
        {
            var allowed = await PatchAsync(client, new UpdateTenantProfileRequest
            {
                IdentityType = IdentityType.CitizenIdentityCard,
                IdentityNumber = $"00109501234{i}"
            });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var response = await PatchAsync(client, new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.CitizenIdentityCard,
            IdentityNumber = "001095012349"
        });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var stored = await ReadProfileAsync(await client.GetAsync(Url));
        Assert.Equal($"00109501234{TenantProfileService.IdentityNumberChangeLimit - 1}", stored.IdentityNumber);

        // Edits that keep the same number are not throttled.
        var sameNumber = await PatchAsync(client, new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.CitizenIdentityCard,
            IdentityNumber = stored.IdentityNumber,
            PermanentAddress = "New address"
        });
        Assert.Equal(HttpStatusCode.OK, sameNumber.StatusCode);
    }

    [Fact]
    public async Task Update_OnlyTouchesCallersOwnProfile()
    {
        var (clientA, userA) = await CreateAuthenticatedClientAsync("a@test.com", "0900000001");
        var (clientB, userB) = await CreateAuthenticatedClientAsync("b@test.com", "0900000002");
        await PatchAsync(clientB, new UpdateTenantProfileRequest { PermanentAddress = "B address" });

        await AssertNoProfileAsync(clientA);

        var createdA = await ReadProfileAsync(await PatchAsync(clientA, new UpdateTenantProfileRequest { PermanentAddress = "A address" }));

        var fetchedB = await ReadProfileAsync(await clientB.GetAsync(Url));
        Assert.Equal(userA, createdA.UserId);
        Assert.Equal(userB, fetchedB.UserId);
        Assert.Equal("B address", fetchedB.PermanentAddress);
        Assert.Equal(1, await CountProfilesAsync(userA));
        Assert.Equal(1, await CountProfilesAsync(userB));
    }

    [Fact]
    public async Task Update_NoProfileYetAndEmptyRequest_Returns400()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");

        var response = await PatchAsync(client, new UpdateTenantProfileRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountProfilesAsync(userId));
    }

    [Fact]
    public async Task Update_NoProfileYetAndOnlyNullOrBlankValues_Returns400()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");

        var response = await PatchAsync(client, new UpdateTenantProfileRequest
        {
            DateOfBirth = null,
            PermanentAddress = "   ",
            EmergencyContactName = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountProfilesAsync(userId));
    }

    [Fact]
    public async Task Update_ExistingProfileAndEmptyRequest_Returns200UnchangedWithoutTouchingAudit()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");
        var created = await ReadProfileAsync(await PatchAsync(client, new UpdateTenantProfileRequest { PermanentAddress = "Old address" }));

        var response = await PatchAsync(client, new UpdateTenantProfileRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadProfileAsync(response);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Old address", updated.PermanentAddress);
        Assert.Null(updated.UpdatedAt);
        Assert.Equal(1, await CountProfilesAsync(userId));
    }

    [Fact]
    public async Task Update_SameValuesAsStored_DoesNotTouchAudit()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");
        await PatchAsync(client, new UpdateTenantProfileRequest { PermanentAddress = "Old address" });

        var response = await PatchAsync(client, new UpdateTenantProfileRequest { PermanentAddress = " Old address " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await ReadProfileAsync(response)).UpdatedAt);
    }

    [Fact]
    public async Task Update_StringFields_AreTrimmedAndBlankStoredAsNull()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("tenant@test.com", "0900000001");
        await PatchAsync(client, new UpdateTenantProfileRequest { EmergencyContactName = "Mother" });

        var response = await PatchAsync(client, new UpdateTenantProfileRequest
        {
            IdentityType = IdentityType.CitizenIdentityCard,
            IdentityNumber = " 001095012345 ",
            PermanentAddress = "  12 Nguyen Trai  ",
            EmergencyContactName = "   ",
            EmergencyContactPhone = ""
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadProfileAsync(response);
        Assert.Equal("001095012345", updated.IdentityNumber);
        Assert.Equal("12 Nguyen Trai", updated.PermanentAddress);
        Assert.Null(updated.EmergencyContactName);
        Assert.Null(updated.EmergencyContactPhone);
    }
}
