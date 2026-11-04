using BoardingHouse.Application.Common;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public interface IUtilityReadingRepository
{
    Task<UtilityReading?> GetByIdAsync(Guid roomId, Guid readingId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByServiceIdAsync(Guid serviceId, CancellationToken cancellationToken = default);
    Task<Guid> GetLatestIdOfMeterAsync(Guid roomId, Guid serviceId, CancellationToken cancellationToken = default);
    Task<List<UtilityReading>> ListLatestByRoomIdAsync(Guid roomId, CancellationToken cancellationToken = default);
    Task<List<UtilityReading>> ListLastBilledOfMonthByRoomIdsAsync(
        IReadOnlyCollection<Guid> roomIds, DateOnly readingMonth, CancellationToken cancellationToken = default);
    Task<List<EntityWithMedia<UtilityReading>>> ListByRoomAndMonthWithImageAsync(
        Guid roomId, DateOnly readingMonth, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<UtilityReading> readings, CancellationToken cancellationToken = default);
    void SoftDelete(UtilityReading reading);
}
