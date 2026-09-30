using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.RoomAssets;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Mappings;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;

namespace BoardingHouse.Api.Services;

public class RoomAssetService(IRoomRepository roomRepository,
    IRoomAssetRepository roomAssetRepository,
    IRoomAssetConditionHistoryRepository roomAssetConditionHistoryRepository,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<RoomAssetService> logger) : IRoomAssetService
{
    public async Task<RoomAssetResponse> CreateAsync(Guid roomId, CreateRoomAssetRequest request, CancellationToken cancellationToken = default)
    {
        var asset = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var room = await roomRepository.GetByIdWithPropertyForShareAsync(roomId, ct)
                ?? throw new AppNotFoundException($"Room '{roomId}' not found");

            await organizationScopeAccessor.EnsureScopeAsync(
                room.Property!.OrganizationId, "room", "update", $"Room '{roomId}' not found", ct);

            var actorId = currentUserAccessor.RequiredUser.Id;
            var created = new RoomAsset
            {
                RoomId = roomId,
                Name = request.Name,
                Quantity = request.Quantity,
                PurchaseDate = request.PurchaseDate,
                PurchaseUnitPrice = request.PurchaseUnitPrice,
                Condition = AssetCondition.Good,
                Note = request.Note,
                CreatedBy = actorId
            };
            await roomAssetRepository.AddAsync(created, ct);

            await roomAssetConditionHistoryRepository.AddAsync(new RoomAssetConditionHistory
            {
                AssetId = created.Id,
                OldCondition = null,
                NewCondition = created.Condition,
                ChangedBy = actorId
            }, ct);

            return created;
        }, cancellationToken);

        logger.LogInformation("Room asset created ({RoomAssetId})", asset.Id);

        return asset.Adapt<RoomAssetResponse>();
    }

    public async Task<RoomAssetResponse> UpdateAsync(Guid roomId, Guid assetId, UpdateRoomAssetRequest request, CancellationToken cancellationToken = default)
    {
        var asset = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existing = await roomAssetRepository.GetByIdWithRoomForUpdateAsync(roomId, assetId, ct)
                ?? throw new AppNotFoundException($"Room asset '{assetId}' not found");

            await organizationScopeAccessor.EnsureScopeAsync(
                existing.Room!.Property!.OrganizationId, "room", "update", $"Room asset '{assetId}' not found", ct);

            request.Adapt(existing, RoomAssetMappingConfig.UpdateConfig);

            roomAssetRepository.Update(existing);

            return existing;
        }, cancellationToken);

        logger.LogInformation("Room asset updated ({RoomAssetId})", asset.Id);

        return asset.Adapt<RoomAssetResponse>();
    }

    public async Task DeleteAsync(Guid roomId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existing = await roomAssetRepository.GetByIdWithRoomForUpdateAsync(roomId, assetId, ct)
                ?? throw new AppNotFoundException($"Room asset '{assetId}' not found");

            await organizationScopeAccessor.EnsureScopeAsync(
                existing.Room!.Property!.OrganizationId, "room", "update", $"Room asset '{assetId}' not found", ct);

            roomAssetRepository.SoftDelete(existing);

            return existing;
        }, cancellationToken);

        logger.LogInformation("Room asset soft-deleted ({RoomAssetId})", asset.Id);
    }

    public async Task<SplitRoomAssetResponse> SplitAsync(Guid roomId, Guid assetId, SplitRoomAssetRequest request, CancellationToken cancellationToken = default)
    {
        var (original, split) = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            _ = await roomRepository.GetByIdWithPropertyForShareAsync(roomId, ct)
                ?? throw new AppNotFoundException($"Room '{roomId}' not found");

            var existing = await roomAssetRepository.GetByIdWithRoomForUpdateAsync(roomId, assetId, ct)
                ?? throw new AppNotFoundException($"Room asset '{assetId}' not found");

            await organizationScopeAccessor.EnsureScopeAsync(
                existing.Room!.Property!.OrganizationId, "room", "update", $"Room asset '{assetId}' not found", ct);

            if (request.Quantity >= existing.Quantity)
            {
                throw new AppValidationException(
                    $"Quantity must be less than the asset's quantity ({existing.Quantity}); to change the condition of the whole row, record a condition history entry instead");
            }

            var actorId = currentUserAccessor.RequiredUser.Id;
            var created = new RoomAsset
            {
                RoomId = roomId,
                Name = existing.Name,
                Quantity = request.Quantity,
                PurchaseDate = existing.PurchaseDate,
                PurchaseUnitPrice = existing.PurchaseUnitPrice,
                Condition = request.NewCondition,
                Note = existing.Note,
                SplitFromAssetId = existing.Id,
                CreatedBy = actorId
            };

            existing.Quantity -= request.Quantity;
            roomAssetRepository.Update(existing);
            await roomAssetRepository.AddAsync(created, ct);

            await roomAssetConditionHistoryRepository.AddAsync(new RoomAssetConditionHistory
            {
                AssetId = created.Id,
                OldCondition = existing.Condition,
                NewCondition = request.NewCondition,
                Note = request.Note,
                ChangedBy = actorId
            }, ct);

            return (existing, created);
        }, cancellationToken);

        logger.LogInformation(
            "Room asset split ({RoomAssetId} -> {SplitRoomAssetId}, quantity {Quantity})", original.Id, split.Id, request.Quantity);

        return new SplitRoomAssetResponse
        {
            Original = original.Adapt<RoomAssetResponse>(),
            Split = split.Adapt<RoomAssetResponse>()
        };
    }
}
