using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Services;

public sealed record InvoiceDraft(
    Guid LeaseId,
    BillingMonth BillingMonth,
    DateOnly Today,
    IReadOnlyList<InvoiceLine> CustomLines,
    decimal DiscountAmount,
    string? DiscountReason,
    string? Notes)
{
    public static InvoiceDraft Monthly(Guid leaseId, BillingMonth billingMonth, DateOnly today) =>
        new(leaseId, billingMonth, today, [], 0, null, null);
}
