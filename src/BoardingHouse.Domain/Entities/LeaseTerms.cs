namespace BoardingHouse.Domain.Entities;

public sealed record LeaseTerms(
    Guid? PreviousLeaseId,
    string LeaseCode,
    DateOnly StartDate,
    DateOnly? EndDate,
    int? BillingDay,
    decimal MonthlyRent,
    decimal DepositAmount);
