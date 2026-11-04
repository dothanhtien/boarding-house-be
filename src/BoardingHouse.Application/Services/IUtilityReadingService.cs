using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.UtilityReadings;

namespace BoardingHouse.Application.Services;

public interface IUtilityReadingService
{
    Task<PagedResult<UtilityReadingRoomListItemResponse>> GetRoomsAsync(
        Guid propertyId, UtilityReadingRoomListQuery query, CancellationToken cancellationToken = default);
    Task<UtilityReadingRoomMetersResponse> GetRoomMetersAsync(
        Guid roomId, UtilityReadingRoomMetersQuery query, CancellationToken cancellationToken = default);
    Task<UtilityReadingRoomMetersResponse> RecordAsync(Guid roomId, RecordRoomUtilityReadingsRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid roomId, Guid readingId, CancellationToken cancellationToken = default);
}
