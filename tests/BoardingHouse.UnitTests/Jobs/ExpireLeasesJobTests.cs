using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using BoardingHouse.Api.Common.CurrentUser;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Jobs;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.Jobs;

public class ExpireLeasesJobTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly Mock<ILeaseRepository> _leaseRepository = new();
    private readonly Mock<ILeaseStatusHistoryRepository> _leaseStatusHistoryRepository = new();
    private readonly Mock<IRoomRepository> _roomRepository = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly CurrentUserAccessor _currentUserAccessor = new();
    private readonly ExpireLeasesJob _job;

    public ExpireLeasesJobTests()
    {
        _unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<bool>> operation, CancellationToken ct) => operation(ct));

        _job = new ExpireLeasesJob(
            _leaseRepository.Object,
            _leaseStatusHistoryRepository.Object,
            _roomRepository.Object,
            _unitOfWork.Object,
            _currentUserAccessor,
            NullLogger<ExpireLeasesJob>.Instance);
    }

    private Lease SetupLease(LeaseStatus status, DateOnly? endDate)
    {
        var room = new Room { RoomNumber = "101", RoomStatus = RoomStatus.Occupied, CreatedBy = SentinelActors.System };
        var lease = new Lease
        {
            LeaseCode = "HD-TEST",
            CreatedBy = SentinelActors.System,
            RoomId = room.Id,
            Room = room,
            StartDate = Today.AddMonths(-6),
            EndDate = endDate,
            Status = status
        };

        _leaseRepository.Setup(r => r.GetRoomIdAsync(lease.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room.Id);
        _roomRepository
            .Setup(r => r.GetByIdWithOrganizationIdForUpdateAsync(room.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityWithOrganizationId<Room>(room, Guid.NewGuid()));
        _leaseRepository
            .Setup(r => r.GetByIdWithRoomForUpdateAsync(lease.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(lease);

        return lease;
    }

    private void SetupLeaseIdsToExpire(params Guid[] ids) =>
        _leaseRepository
            .Setup(r => r.ListIdsToExpireAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ids.ToList());

    [Fact]
    public async Task RunAsync_OverdueActiveLease_ExpiresFreesRoomAndWritesSystemHistory()
    {
        var lease = SetupLease(LeaseStatus.Active, Today.AddDays(-1));
        SetupLeaseIdsToExpire(lease.Id);

        await _job.RunAsync();

        Assert.Equal(LeaseStatus.Expired, lease.Status);
        Assert.Equal(RoomStatus.Available, lease.Room!.RoomStatus);
        Assert.True(_currentUserAccessor.IsSystemActor);
        _leaseStatusHistoryRepository.Verify(r => r.AddAsync(
            It.Is<LeaseStatusHistory>(h =>
                h.LeaseId == lease.Id
                && h.NewStatus == LeaseStatus.Expired
                && h.ChangedBy == null
                && h.Reason == Lease.ExpiredReason),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(LeaseStatus.Terminated, -1)]
    [InlineData(LeaseStatus.Active, 0)]
    [InlineData(LeaseStatus.Active, 1)]
    public async Task RunAsync_LeaseNoLongerDueUnderLock_Skips(LeaseStatus status, int endDateOffsetDays)
    {
        var lease = SetupLease(status, Today.AddDays(endDateOffsetDays));
        SetupLeaseIdsToExpire(lease.Id);

        await _job.RunAsync();

        Assert.Equal(status, lease.Status);
        _leaseStatusHistoryRepository.Verify(
            r => r.AddAsync(It.IsAny<LeaseStatusHistory>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_OpenEndedLease_Skips()
    {
        var lease = SetupLease(LeaseStatus.Active, endDate: null);
        SetupLeaseIdsToExpire(lease.Id);

        await _job.RunAsync();

        Assert.Equal(LeaseStatus.Active, lease.Status);
    }

    [Fact]
    public async Task RunAsync_OneLeaseFails_StillExpiresTheOthersAndDiscardsChangesEachTime()
    {
        var failing = SetupLease(LeaseStatus.Active, Today.AddDays(-1));
        var ok = SetupLease(LeaseStatus.Active, Today.AddDays(-1));
        _leaseRepository
            .Setup(r => r.GetByIdWithRoomForUpdateAsync(failing.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        SetupLeaseIdsToExpire(failing.Id, ok.Id);

        await _job.RunAsync();

        Assert.Equal(LeaseStatus.Active, failing.Status);
        Assert.Equal(LeaseStatus.Expired, ok.Status);
        _unitOfWork.Verify(u => u.DiscardChanges(), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_Cancelled_Throws()
    {
        var lease = SetupLease(LeaseStatus.Active, Today.AddDays(-1));
        SetupLeaseIdsToExpire(lease.Id);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _job.RunAsync(new CancellationToken(canceled: true)));
    }
}
