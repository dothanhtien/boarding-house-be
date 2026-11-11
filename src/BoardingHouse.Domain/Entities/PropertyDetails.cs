namespace BoardingHouse.Domain.Entities;

public sealed record PropertyDetails(
    string Name,
    string? Description,
    string? Province,
    string? District,
    string? Ward,
    string? Address,
    bool IsActive,
    int? DefaultBillingDay,
    LateFeePolicy LateFee);
