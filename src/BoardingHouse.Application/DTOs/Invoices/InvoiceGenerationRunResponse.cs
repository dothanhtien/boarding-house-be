namespace BoardingHouse.Application.DTOs.Invoices;

public record InvoiceGenerationRunResponse
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public Guid? PropertyId { get; init; }
    public required DateOnly BillingMonth { get; init; }
    public Guid? TriggeredBy { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset FinishedAt { get; init; }
    public required int CreatedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required List<InvoiceGenerationSkipResponse> Skipped { get; init; }
}

public record InvoiceGenerationSkipResponse
{
    public required Guid LeaseId { get; init; }
    public required string LeaseCode { get; init; }
    public required string RoomNumber { get; init; }
    public required List<InvoiceGenerationSkipReasonResponse> Reasons { get; init; }
}

public record InvoiceGenerationSkipReasonResponse
{
    public required string Code { get; init; }
    public required string Message { get; init; }
}
