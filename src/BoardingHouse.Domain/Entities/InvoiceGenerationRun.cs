using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Domain.Entities;

// Append-only record of one bulk generation: the daily job (whole organization) or a staff click (one property)
public class InvoiceGenerationRun : Entity
{
    public Guid OrganizationId { get; private set; }

    // Null = the daily job (every property of the organization)
    public Guid? PropertyId { get; private set; }

    public BillingMonth BillingMonth { get; private set; }

    // Null = the daily job
    public Guid? TriggeredBy { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset FinishedAt { get; private set; }
    public int CreatedCount { get; private set; }
    public IReadOnlyList<InvoiceGenerationSkip> Skipped { get; private set; } = [];

    public int SkippedCount => Skipped.Count;

    private InvoiceGenerationRun() { }
}
