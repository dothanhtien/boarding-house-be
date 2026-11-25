using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Application.DTOs.TenantPortal;

public record MyUtilityReadingListQuery : PageRequest
{
    public Guid? ServiceId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}
