namespace BoardingHouse.Application.DTOs.Invoices;

public record GenerateInvoicesRequest
{
    public DateOnly? BillingMonth { get; init; }
}
