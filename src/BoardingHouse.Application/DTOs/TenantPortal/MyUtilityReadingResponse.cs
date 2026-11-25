namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyUtilityReadingResponse
{
    public required Guid Id { get; init; }
    public required Guid ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required string Unit { get; init; }
    public required DateOnly ReadOn { get; init; }
    public required DateOnly ReadingMonth { get; init; }
    public decimal? PreviousValue { get; init; }
    public required decimal CurrentValue { get; init; }
    public decimal? Consumption { get; init; }
    public string? ImageUrl { get; init; }
    public string? InvoiceNumber { get; init; }
}
