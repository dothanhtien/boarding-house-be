using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public sealed record UtilityServiceDetails(
    string Name,
    UtilityBillingMethod BillingMethod,
    string Unit,
    decimal DefaultUnitPrice,
    bool IsActive);
