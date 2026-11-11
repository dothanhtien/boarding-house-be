using BoardingHouse.UnitTests.Common;
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
            _roomRepository.Object,
            _unitOfWork.Object,
            _currentUserAccessor,
            NullLogger<ExpireLeasesJob>.Instance);
    }

    private Lease SetupLease(LeaseStatus status, DateOnly? endDate)
    {
        var room = TestRooms.New();
        var tenant = TestEntities.New<User>(new { FullName = "Tenant", PasswordHash = "hash", CreatedBy = SentinelActors.System });
        var startDate = Today.AddMonths(-6);
        var now = DateTimeOffset.UtcNow;
        var lease = Lease.CreateDraft(
            Guid.NewGuid(), room, previousLeaseId: null, "HD-TEST", startDate, endDate, billingDay: null,
            monthlyRent: 1, depositAmount: 1, [new TenantChange { UserId = tenant.Id, IsRepresentative = true }],
            new Dictionary<Guid, User> { [tenant.Id] = tenant }, utilityServices: [], now, SentinelActors.System);
        lease.Activate(now, startDate, SentinelActors.System);

        switch (status)
        {
            case LeaseStatus.Active:
                break;
            case LeaseStatus.Terminated:
                lease.Terminate(now, Today, SentinelActors.System, leftDate: null, "Moved out");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Only Active / Terminated are used here");
        }

        _leaseRepository.Setup(r => r.GetRoomIdAsync(lease.Id, It.IsAny<CancellationToken>())).ReturnsAsync(room.Id);
        _roomRepository
            .Setup(r => r.GetByIdWithOrganizationIdForUpdateAsync(room.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityWithOrganizationId<Room>(room, Guid.NewGuid()));
        _leaseRepository
            .Setup(r => r.GetAggregateForUpdateAsync(lease.Id, It.IsAny<CancellationToken>()))
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
        var history = lease.StatusHistories.Last();
        Assert.Equal(LeaseStatus.Expired, history.NewStatus);
        Assert.Null(history.ChangedBy);
        Assert.Equal(Lease.ExpiredReason, history.Reason);
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
        Assert.DoesNotContain(lease.StatusHistories, h => h.NewStatus == LeaseStatus.Expired);
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
            .Setup(r => r.GetAggregateForUpdateAsync(failing.Id, It.IsAny<CancellationToken>()))
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
