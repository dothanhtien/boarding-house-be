using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Repositories;

public sealed record RoomAssetConditionHistoryEntry(RoomAssetConditionHistory History, bool IsLatest);
