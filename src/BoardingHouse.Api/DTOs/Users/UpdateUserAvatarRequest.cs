namespace BoardingHouse.Api.DTOs.Users;

public record UpdateUserAvatarRequest
{
    public IFormFile? File { get; init; }
}
