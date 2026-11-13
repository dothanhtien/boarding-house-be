namespace BoardingHouse.Domain.Invoicing;

public enum InvoiceIssueCode
{
    // Errors: the invoice cannot be created
    LeaseNotBillable,
    AlreadyInvoiced,
    MonthBeforeStart,
    MonthAfterEnd,
    UnsupportedCurrency,
    MissingReading,
    NoItems,
    NegativeTotal,

    // Warnings: the invoice can be created, a line was left out
    ReadingWithoutConsumption,
    ServiceNotOnLease,
    ReadingBilledElsewhere
}

public sealed record InvoiceIssue(InvoiceIssueCode Code, string Message, Guid? ServiceId = null);
