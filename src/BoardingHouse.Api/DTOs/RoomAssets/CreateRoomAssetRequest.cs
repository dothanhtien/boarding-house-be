namespace BoardingHouse.Api.DTOs.RoomAssets;

public record CreateRoomAssetRequest
{
    public required string Name { get; init; }
    public int Quantity { get; init; } = 1;
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchaseUnitPrice { get; init; }
    public string? Note { get; init; }
}
