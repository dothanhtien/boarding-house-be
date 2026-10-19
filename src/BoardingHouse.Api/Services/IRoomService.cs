using BoardingHouse.Application.DTOs.Rooms;
using BoardingHouse.Api.DTOs.Media;
using BoardingHouse.Application.Common.Listing;

namespace BoardingHouse.Api.Services;

public interface IRoomService
{
    Task<PagedResult<RoomResponse>> GetAllAsync(RoomListQuery query, CancellationToken cancellationToken = default);
    Task<RoomDetailsResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RoomDetailsResponse> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken = default);
    Task<RoomDetailsResponse> UpdateAsync(Guid id, UpdateRoomRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<RoomMediaResponse>> AddMediaAsync(Guid roomId, AddImagesRequest request, CancellationToken cancellationToken = default);
    Task<List<RoomMediaResponse>> UpdateMediaAsync(Guid roomId, UpdateRoomMediaRequest request, CancellationToken cancellationToken = default);
    Task DeleteMediaAsync(Guid roomId, Guid mediaId, CancellationToken cancellationToken = default);
}
