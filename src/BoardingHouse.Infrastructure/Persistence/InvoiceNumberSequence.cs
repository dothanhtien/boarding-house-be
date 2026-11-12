namespace BoardingHouse.Infrastructure.Persistence;

// Per-organization, per-month invoice counter. Only written by InvoiceRepository.NextNumberAsync (raw SQL)
public class InvoiceNumberSequence
{
    public Guid OrganizationId { get; private set; }
    public DateOnly BillingMonth { get; private set; }
    public int LastNumber { get; private set; }

    private InvoiceNumberSequence() { }
}
