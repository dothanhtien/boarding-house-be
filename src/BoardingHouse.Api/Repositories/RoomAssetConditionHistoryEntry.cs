using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Repositories;

public sealed record RoomAssetConditionHistoryEntry(RoomAssetConditionHistory History, bool IsLatest);
