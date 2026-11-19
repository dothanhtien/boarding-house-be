using BoardingHouse.Domain.Invoicing;

namespace BoardingHouse.Application.Services;

public abstract record InvoiceGenerationResult
{
    private InvoiceGenerationResult() { }

    public sealed record Created(Guid InvoiceId) : InvoiceGenerationResult;

    public sealed record Skipped(IReadOnlyList<InvoiceIssue> Errors) : InvoiceGenerationResult
    {
        public bool AlreadyInvoiced => Errors.Any(e => e.Code == InvoiceIssueCode.AlreadyInvoiced);
    }
}
