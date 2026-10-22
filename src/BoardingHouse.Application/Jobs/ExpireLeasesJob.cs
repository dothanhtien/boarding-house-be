using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Application.Jobs;

/// <summary>
/// Moves every <c>ACTIVE</c> lease whose <c>EndDate</c> is before today (UTC) to <c>EXPIRED</c>. Scheduled daily by
/// <c>RecurringJobRegistry</c> (Infrastructure); safe to re-run — each lease is re-checked under its lock and handled
/// in its own transaction, so one failure doesn't stop the batch.
/// </summary>
public class ExpireLeasesJob(
    ILeaseRepository leaseRepository,
    ILeaseStatusHistoryRepository leaseStatusHistoryRepository,
    IRoomRepository roomRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<ExpireLeasesJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        currentUserAccessor.IsSystemActor = true;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var leaseIds = await leaseRepository.ListIdsToExpireAsync(today, cancellationToken);

        var expired = 0;
        var failed = 0;

        foreach (var leaseId in leaseIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await TryExpireAsync(leaseId, today, cancellationToken))
                {
                    expired++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "Expire lease failed ({LeaseId})", leaseId);
            }
            finally
            {
                unitOfWork.DiscardChanges();
            }
        }

        logger.LogInformation(
            "Expire leases finished: {Expired} expired, {Skipped} skipped, {Failed} failed",
            expired, leaseIds.Count - expired - failed, failed);
    }

    private Task<bool> TryExpireAsync(Guid leaseId, DateOnly today, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var roomId = await leaseRepository.GetRoomIdAsync(leaseId, ct);
            if (roomId is null) return false;

            if (await roomRepository.GetByIdWithOrganizationIdForUpdateAsync(roomId.Value, ct) is null) return false;

            var lease = await leaseRepository.GetByIdWithRoomForUpdateAsync(leaseId, ct);

            if (lease is null
                || lease.RoomId != roomId
                || lease.Status != LeaseStatus.Active
                || lease.EndDate is not { } endDate
                || endDate >= today)
            {
                return false;
            }

            await leaseRepository.LoadTenantsAsync(lease, ct);

            var history = lease.Expire(DateTimeOffset.UtcNow);

            await leaseStatusHistoryRepository.AddAsync(history, ct);

            logger.LogInformation("Lease expired ({LeaseId})", leaseId);

            return true;
        }, cancellationToken);
}
