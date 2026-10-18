using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Repositories;

public sealed record RoomAssetConditionHistoryEntry(RoomAssetConditionHistory History, bool IsLatest);
