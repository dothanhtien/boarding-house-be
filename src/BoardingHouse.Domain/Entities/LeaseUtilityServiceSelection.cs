namespace BoardingHouse.Domain.Entities;

public sealed record LeaseUtilityServiceSelection(UtilityService Service, decimal? UnitPriceOverride, int Quantity);
