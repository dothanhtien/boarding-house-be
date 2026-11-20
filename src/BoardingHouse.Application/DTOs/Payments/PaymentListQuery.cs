using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.DTOs.Payments;

public record PaymentListQuery : ListQuery
{
    public Guid? OrganizationId { get; init; }
    public Guid? PropertyId { get; init; }
    public PaymentMethod? Method { get; init; }
    public PaymentStatus? Status { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}
