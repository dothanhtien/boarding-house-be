using BoardingHouse.Application.Services.Storage;

namespace BoardingHouse.Application.DTOs.RoomAssets;

public record CreateRoomAssetInput
{
    public required string Name { get; init; }
    public required int Quantity { get; init; }
    public DateOnly? PurchaseDate { get; init; }
    public decimal? PurchaseUnitPrice { get; init; }
    public string? Note { get; init; }
    public IReadOnlyList<IFileUpload> Files { get; init; } = [];
}
