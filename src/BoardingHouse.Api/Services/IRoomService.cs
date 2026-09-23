using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Rooms;

namespace BoardingHouse.Api.Services;

public interface IRoomService
{
    Task<PagedResult<RoomResponse>> GetAllAsync(RoomListQuery query, CancellationToken cancellationToken = default);
    Task<RoomResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RoomResponse> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken = default);
    Task<RoomResponse> UpdateAsync(Guid id, UpdateRoomRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
