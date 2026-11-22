using Microsoft.EntityFrameworkCore;
using Npgsql;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Persistence;

public class OrganizationSettingsPersistenceTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private const string BankBinAccountConstraint = "ck_organization_settings_bank_bin_account";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BankBinWithAccountNumber_RoundTrips()
    {
        var organizationId = await SeedOrganizationAsync();
        await InsertAsync(TestEntities.Set(
            OrganizationSettings.DefaultsFor(organizationId),
            new { BankBin = "970436", BankAccountNumber = "0011001234567", BankAccountName = "NGUYEN VAN A" }));

        var saved = await LoadAsync(organizationId);

        Assert.Equal(("970436", "0011001234567", "NGUYEN VAN A"), (saved.BankBin, saved.BankAccountNumber, saved.BankAccountName));
    }

    [Fact]
    public async Task NewSettings_BankBinIsNull()
    {
        var organizationId = await SeedOrganizationAsync();
        await InsertAsync(OrganizationSettings.DefaultsFor(organizationId));

        var saved = await LoadAsync(organizationId);

        Assert.Null(saved.BankBin);
    }

    [Fact]
    public async Task BankBinWithoutAccountNumber_ViolatesCheckConstraint()
    {
        var organizationId = await SeedOrganizationAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(TestEntities.Set(
            OrganizationSettings.DefaultsFor(organizationId), new { BankBin = "970436" })));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, BankBinAccountConstraint);
    }

    [Fact]
    public async Task AccountNumberWithoutBankBin_IsAllowed()
    {
        var organizationId = await SeedOrganizationAsync();

        await InsertAsync(TestEntities.Set(
            OrganizationSettings.DefaultsFor(organizationId), new { BankAccountNumber = "0011 0012 34567" }));

        var saved = await LoadAsync(organizationId);
        Assert.Equal(("0011 0012 34567", (string?)null), (saved.BankAccountNumber, saved.BankBin));
    }

    [Fact]
    public async Task ClearingTheAccountNumberOfAnExistingRowWithABankBin_ViolatesCheckConstraint()
    {
        var organizationId = await SeedOrganizationAsync();
        await InsertAsync(TestEntities.Set(
            OrganizationSettings.DefaultsFor(organizationId), new { BankBin = "970436", BankAccountNumber = "0011001234567" }));

        await using var context = fixture.CreateContext();
        var settings = await context.OrganizationSettings.SingleAsync(s => s.OrganizationId == organizationId);
        TestEntities.Set(settings, new { BankAccountNumber = (string?)null });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, BankBinAccountConstraint);
    }

    [Fact]
    public async Task ClearingBothBankBinAndAccountNumber_IsAllowed()
    {
        var organizationId = await SeedOrganizationAsync();
        await InsertAsync(TestEntities.Set(
            OrganizationSettings.DefaultsFor(organizationId), new { BankBin = "970436", BankAccountNumber = "0011001234567" }));

        await using (var context = fixture.CreateContext())
        {
            var settings = await context.OrganizationSettings.SingleAsync(s => s.OrganizationId == organizationId);
            TestEntities.Set(settings, new { BankBin = (string?)null, BankAccountNumber = (string?)null });
            await context.SaveChangesAsync();
        }

        var saved = await LoadAsync(organizationId);
        Assert.Equal(((string?)null, (string?)null), (saved.BankBin, saved.BankAccountNumber));
    }

    [Fact]
    public async Task BankBinLongerThanSixCharacters_IsRejected()
    {
        var organizationId = await SeedOrganizationAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(TestEntities.Set(
            OrganizationSettings.DefaultsFor(organizationId), new { BankBin = "9704361", BankAccountNumber = "0011001234567" })));

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, postgresException.SqlState);
    }

    private async Task<Guid> SeedOrganizationAsync()
    {
        await using var context = fixture.CreateContext();
        var organization = TestEntities.New<Organization>(new { Name = "Org", CreatedBy = SentinelActors.System });
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        return organization.Id;
    }

    private async Task InsertAsync(OrganizationSettings settings)
    {
        await using var context = fixture.CreateContext();
        context.OrganizationSettings.Add(settings);
        await context.SaveChangesAsync();
    }

    private async Task<OrganizationSettings> LoadAsync(Guid organizationId)
    {
        await using var context = fixture.CreateContext();
        return await context.OrganizationSettings.AsNoTracking().SingleAsync(s => s.OrganizationId == organizationId);
    }

    private static void AssertViolates(Exception? exception, string sqlState, string constraintName)
    {
        var postgresException = Assert.IsType<PostgresException>(exception);
        Assert.Equal(sqlState, postgresException.SqlState);
        Assert.Equal(constraintName, postgresException.ConstraintName);
    }
}
