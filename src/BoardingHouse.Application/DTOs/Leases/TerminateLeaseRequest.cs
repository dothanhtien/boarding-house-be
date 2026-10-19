namespace BoardingHouse.Application.DTOs.Leases;

public record TerminateLeaseRequest
{
    public required string Reason { get; init; }
    public DateOnly? LeftDate { get; init; }
}
