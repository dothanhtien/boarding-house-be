using Microsoft.EntityFrameworkCore;
using Npgsql;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Persistence;

public class InvoiceGenerationRunPersistenceTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly BillingMonth November = BillingMonth.FromFirstDay(new DateOnly(2026, 11, 1));
    private static readonly DateTimeOffset StartedAt = new(2026, 11, 16, 0, 30, 0, TimeSpan.Zero);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task JobRun_WithSkippedLeases_RoundTripsThroughJsonb()
    {
        var (organizationId, _, _) = await SeedAsync();
        var leaseId = Guid.NewGuid();
        var run = NewRun(organizationId, skipped:
        [
            new InvoiceGenerationSkip(leaseId, "HD-001", "101",
            [
                new InvoiceGenerationSkipReason("missingReading", "Electricity reading for 2026-11 is missing"),
                new InvoiceGenerationSkipReason("failed", "Unexpected error, see the server logs")
            ]),
            new InvoiceGenerationSkip(Guid.NewGuid(), "HD-002", "102",
                [new InvoiceGenerationSkipReason("noItems", "Nothing to bill")])
        ]);
        await InsertAsync(run);

        await using var context = fixture.CreateContext();
        var saved = await context.InvoiceGenerationRuns.SingleAsync(r => r.Id == run.Id);

        Assert.Equal(organizationId, saved.OrganizationId);
        Assert.Null(saved.PropertyId);
        Assert.Null(saved.TriggeredBy);
        Assert.Equal(November, saved.BillingMonth);
        Assert.Equal(StartedAt, saved.StartedAt);
        Assert.Equal(StartedAt.AddSeconds(5), saved.FinishedAt);
        Assert.Equal(3, saved.CreatedCount);
        Assert.Equal(2, saved.SkippedCount);
        var first = saved.Skipped[0];
        Assert.Equal((leaseId, "HD-001", "101"), (first.LeaseId, first.LeaseCode, first.RoomNumber));
        Assert.Equal(run.Skipped[0].Reasons, first.Reasons);
        Assert.Equal("HD-002", saved.Skipped[1].LeaseCode);
    }

    [Fact]
    public async Task Skipped_IsStoredAsJsonbWithCamelCaseKeys()
    {
        var (organizationId, _, _) = await SeedAsync();
        var run = NewRun(organizationId, skipped:
            [new InvoiceGenerationSkip(Guid.NewGuid(), "HD-001", "101", [new InvoiceGenerationSkipReason("missingReading", "Missing")])]);
        await InsertAsync(run);

        await using var context = fixture.CreateContext();
        var row = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT jsonb_typeof(skipped) || '|' || (skipped->0->>'leaseCode') || '|' || (skipped->0->>'roomNumber')
                    || '|' || (skipped->0->'reasons'->0->>'code') AS "Value"
                FROM invoice_generation_runs WHERE id = {0}
                """,
                run.Id)
            .SingleAsync();

        Assert.Equal("array|HD-001|101|missingReading", row);
    }

    [Fact]
    public async Task ManualRun_WithPropertyAndUserAndNoSkips_StoresAnEmptyArray()
    {
        var (organizationId, propertyId, userId) = await SeedAsync();
        var run = NewRun(organizationId, propertyId, userId, createdCount: 0, skipped: []);
        await InsertAsync(run);

        await using var context = fixture.CreateContext();
        var saved = await context.InvoiceGenerationRuns.SingleAsync(r => r.Id == run.Id);
        var json = await context.Database
            .SqlQueryRaw<string>("SELECT skipped::text AS \"Value\" FROM invoice_generation_runs WHERE id = {0}", run.Id)
            .SingleAsync();

        Assert.Equal(propertyId, saved.PropertyId);
        Assert.Equal(userId, saved.TriggeredBy);
        Assert.Equal(0, saved.CreatedCount);
        Assert.Empty(saved.Skipped);
        Assert.Equal(0, saved.SkippedCount);
        Assert.Equal("[]", json);
    }

    [Fact]
    public async Task LoadedRun_IsNotDetectedAsModified()
    {
        var (organizationId, _, _) = await SeedAsync();
        var run = NewRun(organizationId, skipped:
            [new InvoiceGenerationSkip(Guid.NewGuid(), "HD-001", "101", [new InvoiceGenerationSkipReason("missingReading", "Missing")])]);
        await InsertAsync(run);

        await using var context = fixture.CreateContext();
        var saved = await context.InvoiceGenerationRuns.SingleAsync(r => r.Id == run.Id);
        context.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Unchanged, context.Entry(saved).State);
        Assert.Equal(0, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task NegativeCreatedCount_ViolatesCheckConstraint()
    {
        var (organizationId, _, _) = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            InsertAsync(NewRun(organizationId, createdCount: -1)));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, "ck_invoice_generation_runs_created_count");
    }

    [Fact]
    public async Task FinishedBeforeStarted_ViolatesCheckConstraint()
    {
        var (organizationId, _, _) = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            InsertAsync(NewRun(organizationId, finishedAt: StartedAt.AddSeconds(-1))));

        AssertViolates(exception.InnerException, PostgresErrorCodes.CheckViolation, "ck_invoice_generation_runs_finished_at");
    }

    [Fact]
    public async Task FinishedAtEqualToStartedAt_IsAllowed()
    {
        var (organizationId, _, _) = await SeedAsync();

        await InsertAsync(NewRun(organizationId, finishedAt: StartedAt));
    }

    // BillingMonth cannot hold a day other than 1, so the row is written with raw SQL
    [Fact]
    public async Task BillingMonthNotOnTheFirstDay_ViolatesCheckConstraint()
    {
        var (organizationId, _, _) = await SeedAsync();

        await using var context = fixture.CreateContext();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO invoice_generation_runs (organization_id, billing_month, started_at, finished_at, created_count, skipped)
            VALUES ({0}, DATE '2026-11-15', now(), now(), 0, '[]'::jsonb)
            """,
            organizationId));

        AssertViolates(exception, PostgresErrorCodes.CheckViolation, "ck_invoice_generation_runs_billing_month");
    }

    [Fact]
    public async Task UnknownOrganization_ViolatesForeignKey()
    {
        await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewRun(Guid.NewGuid())));

        AssertViolates(exception.InnerException, PostgresErrorCodes.ForeignKeyViolation,
            "fk_invoice_generation_runs_organizations_organization_id");
    }

    [Fact]
    public async Task UnknownProperty_ViolatesForeignKey()
    {
        var (organizationId, _, userId) = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            InsertAsync(NewRun(organizationId, propertyId: Guid.NewGuid(), triggeredBy: userId)));

        AssertViolates(exception.InnerException, PostgresErrorCodes.ForeignKeyViolation,
            "fk_invoice_generation_runs_properties_property_id");
    }

    [Fact]
    public async Task UnknownTriggeringUser_ViolatesForeignKey()
    {
        var (organizationId, propertyId, _) = await SeedAsync();

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            InsertAsync(NewRun(organizationId, propertyId, triggeredBy: Guid.NewGuid())));

        AssertViolates(exception.InnerException, PostgresErrorCodes.ForeignKeyViolation,
            "fk_invoice_generation_runs_users_triggered_by");
    }

    [Fact]
    public async Task OrganizationSettings_AutoGenerateInvoices_DefaultsToFalse()
    {
        var (organizationId, _, _) = await SeedAsync();

        await using (var context = fixture.CreateContext())
        {
            context.OrganizationSettings.Add(OrganizationSettings.DefaultsFor(organizationId));
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var settings = await readContext.OrganizationSettings.SingleAsync(s => s.OrganizationId == organizationId);

        Assert.False(settings.AutoGenerateInvoices);
    }

    // Rows written before the migration (or without the column) get the database default
    [Fact]
    public async Task OrganizationSettings_RowWithoutTheColumn_GetsTheDatabaseDefaultFalse()
    {
        var (organizationId, _, _) = await SeedAsync();

        await using var context = fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO organization_settings (organization_id) VALUES ({0})", organizationId);
        var value = await context.Database
            .SqlQueryRaw<bool>(
                "SELECT auto_generate_invoices AS \"Value\" FROM organization_settings WHERE organization_id = {0}",
                organizationId)
            .SingleAsync();

        Assert.False(value);
    }

    [Fact]
    public async Task OrganizationSettings_AutoGenerateInvoicesTrue_IsPersisted()
    {
        var (organizationId, _, _) = await SeedAsync();

        await using (var context = fixture.CreateContext())
        {
            var settings = TestEntities.Set(OrganizationSettings.DefaultsFor(organizationId), new { AutoGenerateInvoices = true });
            context.OrganizationSettings.Add(settings);
            await context.SaveChangesAsync();
        }

        await using var readContext = fixture.CreateContext();
        var saved = await readContext.OrganizationSettings.SingleAsync(s => s.OrganizationId == organizationId);

        Assert.True(saved.AutoGenerateInvoices);
    }

    // Step 1 has no factory yet (InvoiceGenerationRun.Record comes with IInvoiceGenerator): build the row the way EF does
    private static InvoiceGenerationRun NewRun(
        Guid organizationId,
        Guid? propertyId = null,
        Guid? triggeredBy = null,
        int createdCount = 3,
        DateTimeOffset? finishedAt = null,
        IReadOnlyList<InvoiceGenerationSkip>? skipped = null) =>
        TestEntities.New<InvoiceGenerationRun>(new
        {
            OrganizationId = organizationId,
            PropertyId = propertyId,
            BillingMonth = November,
            TriggeredBy = triggeredBy,
            StartedAt = StartedAt,
            FinishedAt = finishedAt ?? StartedAt.AddSeconds(5),
            CreatedCount = createdCount,
            Skipped = skipped ?? []
        });

    private async Task InsertAsync(InvoiceGenerationRun run)
    {
        await using var context = fixture.CreateContext();
        context.InvoiceGenerationRuns.Add(run);
        await context.SaveChangesAsync();
    }

    private static void AssertViolates(Exception? exception, string sqlState, string constraintName)
    {
        var postgresException = Assert.IsType<PostgresException>(exception);
        Assert.Equal(sqlState, postgresException.SqlState);
        Assert.Equal(constraintName, postgresException.ConstraintName);
    }

    // A staff user, an organization and one of its properties
    private async Task<(Guid OrganizationId, Guid PropertyId, Guid UserId)> SeedAsync()
    {
        await using var context = fixture.CreateContext();

        var staff = TestEntities.New<User>(new { FullName = "Staff", PasswordHash = "hash", CreatedBy = SentinelActors.System });
        var organization = TestEntities.New<Organization>(new { Name = "Org", CreatedBy = staff.Id });
        var property = TestEntities.New<Property>(new { OrganizationId = organization.Id, Name = "Property", CreatedBy = staff.Id });
        context.AddRange(staff, organization, property);
        await context.SaveChangesAsync();

        return (organization.Id, property.Id, staff.Id);
    }
}
