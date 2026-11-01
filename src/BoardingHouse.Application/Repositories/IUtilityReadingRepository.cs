using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IUtilityReadingRepository
{
    Task<List<UtilityReading>> ListLatestByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default);
    Task<List<EntityWithMedia<UtilityReading>>> ListByRoomAndMonthWithImageAsync(
        Guid roomId, DateOnly readingMonth, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<UtilityReading> readings, CancellationToken cancellationToken = default);
}
