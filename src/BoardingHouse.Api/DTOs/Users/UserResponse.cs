namespace BoardingHouse.Api.DTOs.Users;

public record UserResponse
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public DateTimeOffset? EmailVerifiedAt { get; init; }
    public string? Phone { get; init; }
    public required string FullName { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
    public required bool IsActive { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
