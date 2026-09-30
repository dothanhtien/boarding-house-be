using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;

namespace BoardingHouse.Api.Services;

public class RoomAssetConditionHistoryService(
    IRoomAssetRepository roomAssetRepository,
    IRoomAssetConditionHistoryRepository roomAssetConditionHistoryRepository,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<RoomAssetConditionHistoryService> logger) : IRoomAssetConditionHistoryService
{
    public async Task<List<RoomAssetConditionHistoryResponse>> GetAllAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await roomAssetRepository.GetByIdWithRoomAsync(roomId, assetId, cancellationToken)
            ?? throw new AppNotFoundException($"Room asset '{assetId}' not found");

        await organizationScopeAccessor.EnsureScopeAsync(
            asset.Room!.Property!.OrganizationId, "room", "read",
            $"Room asset '{assetId}' not found", cancellationToken);

        var histories = await roomAssetConditionHistoryRepository.ListByAssetIdAsync(assetId, cancellationToken);
        return histories.Adapt<List<RoomAssetConditionHistoryResponse>>();
    }

    public async Task<RoomAssetConditionHistoryResponse> CreateAsync(
        Guid roomId,
        Guid assetId,
        CreateRoomAssetConditionHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var history = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var asset = await roomAssetRepository.GetByIdWithRoomForUpdateAsync(roomId, assetId, ct)
                ?? throw new AppNotFoundException($"Room asset '{assetId}' not found");

            await organizationScopeAccessor.EnsureScopeAsync(
                asset.Room!.Property!.OrganizationId, "room", "update",
                $"Room asset '{assetId}' not found", ct);

            var created = new RoomAssetConditionHistory
            {
                AssetId = assetId,
                OldCondition = asset.Condition,
                NewCondition = request.NewCondition,
                Note = request.Note,
                ChangedBy = currentUserAccessor.RequiredUser.Id
            };

            asset.Condition = request.NewCondition;
            roomAssetRepository.Update(asset);
            await roomAssetConditionHistoryRepository.AddAsync(created, ct);

            return created;
        }, cancellationToken);

        logger.LogInformation(
            "Room asset condition recorded ({RoomAssetId}), history {RoomAssetConditionHistoryId}", assetId, history.Id);

        return history.Adapt<RoomAssetConditionHistoryResponse>();
    }
}
