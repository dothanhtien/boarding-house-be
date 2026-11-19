using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.UnitTests.Entities;

public class InvoiceGenerationRunTests
{
    [Fact]
    public void Record_KeepsTheRunAndACopyOfTheSkips()
    {
        var organizationId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var month = BillingMonth.FromFirstDay(new DateOnly(2026, 6, 1));
        var startedAt = new DateTimeOffset(2026, 6, 5, 0, 30, 0, TimeSpan.Zero);
        var finishedAt = startedAt.AddSeconds(3);
        var skip = new InvoiceGenerationSkip(
            Guid.NewGuid(), "HD-001", "101", [new InvoiceGenerationSkipReason("missingReading", "No reading")]);
        var skipped = new List<InvoiceGenerationSkip> { skip };

        var run = InvoiceGenerationRun.Record(organizationId, propertyId, month, actorId, startedAt, finishedAt, 2, skipped);
        skipped.Clear();

        Assert.Equal((organizationId, propertyId, month, actorId), (run.OrganizationId, run.PropertyId, run.BillingMonth, run.TriggeredBy));
        Assert.Equal((startedAt, finishedAt, 2), (run.StartedAt, run.FinishedAt, run.CreatedCount));
        Assert.Same(skip, Assert.Single(run.Skipped));
        Assert.Equal(1, run.SkippedCount);
    }

    [Fact]
    public void Record_JobRunWithoutSkips_HasNoPropertyNorActor()
    {
        var month = BillingMonth.FromFirstDay(new DateOnly(2026, 6, 1));
        var at = new DateTimeOffset(2026, 6, 5, 0, 30, 0, TimeSpan.Zero);

        var run = InvoiceGenerationRun.Record(Guid.NewGuid(), propertyId: null, month, triggeredBy: null, at, at, 0, []);

        Assert.Null(run.PropertyId);
        Assert.Null(run.TriggeredBy);
        Assert.NotEqual(Guid.Empty, run.Id);
        Assert.Empty(run.Skipped);
        Assert.Equal(0, run.SkippedCount);
    }
}
