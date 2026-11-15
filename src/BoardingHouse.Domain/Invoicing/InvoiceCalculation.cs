using BoardingHouse.Domain.Common;

namespace BoardingHouse.Domain.Invoicing;

public sealed record InvoiceCalculation
{
    public required BillingMonth BillingMonth { get; init; }
    public required DateOnly DueDate { get; init; }
    public required decimal VatRate { get; init; }
    public required decimal DiscountAmount { get; init; }
    public required IReadOnlyList<InvoiceLine> Lines { get; init; }
    public required InvoiceTotals Totals { get; init; }
    public required IReadOnlyList<InvoiceIssue> Errors { get; init; }
    public required IReadOnlyList<InvoiceIssue> Warnings { get; init; }

    public bool CanCreate => Errors.Count == 0;
}
