using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.Services.Storage;

namespace BoardingHouse.Application.Services;

public interface IUserService
{
    Task<PagedResult<UserResponse>> GetAllAsync(UserListQuery query, CancellationToken cancellationToken = default);
    Task<UserResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserResponse> UpdateAvatarAsync(Guid id, IFileUpload file, CancellationToken cancellationToken = default);
    Task DeleteAvatarAsync(Guid id, CancellationToken cancellationToken = default);
}
