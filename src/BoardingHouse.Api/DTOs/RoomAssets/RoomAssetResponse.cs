using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.DTOs.RoomAssets;

public record RoomAssetResponse
{
    public required Guid Id { get; init; }
    public required Guid RoomId { get; init; }
    public required string Name { get; init; }
    public required int Quantity { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchaseUnitPrice { get; init; }
    public required AssetCondition Condition { get; init; }
    public string? Note { get; init; }
    public Guid? SplitFromAssetId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}
