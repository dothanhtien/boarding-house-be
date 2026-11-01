using BoardingHouse.Application.DTOs.UtilityReadings;

namespace BoardingHouse.Application.Services;

public interface IUtilityReadingService
{
    Task<UtilityReadingRoomMetersResponse> RecordAsync(Guid roomId, RecordRoomUtilityReadingsRequest request, CancellationToken cancellationToken = default);
}
