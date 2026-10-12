using System.Data.Common;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BoardingHouse.IntegrationTests.Repositories;

public class RoomAssetConditionHistoryRepositoryIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ListCurrentPhotosByAssetIdsAsync_PicksLatestEntryWithLivePhotos()
    {
        Guid assetId, deskId, photographedId;
        await using (var context = fixture.CreateContext())
        {
            var room = AddRoom(context, "101");
            var asset = AddAsset(context, room, "Bed");
            var photographed = AddHistory(context, asset, AssetCondition.Good, T0);
            AddPhoto(context, photographed);
            var onlyDeletedPhoto = AddHistory(context, asset, AssetCondition.Damaged, T0.AddDays(1));
            AddPhoto(context, onlyDeletedPhoto, deleted: true);
            AddHistory(context, asset, AssetCondition.Broken, T0.AddDays(2));
            var desk = AddAsset(context, room, "Desk");
            await context.SaveChangesAsync();

            (assetId, deskId, photographedId) = (asset.Id, desk.Id, photographed.Id);
        }

        await using var readContext = fixture.CreateContext();
        var photos = await new RoomAssetConditionHistoryRepository(readContext).ListCurrentPhotosByAssetIdsAsync([assetId, deskId]);

        var (key, current) = Assert.Single(photos);
        Assert.Equal(assetId, key);
        Assert.Equal(photographedId, current.History.Id);
        Assert.False(current.IsLatest);
        Assert.NotNull(Assert.Single(current.History.Media).MediaAsset);
    }

    [Fact]
    public async Task ListCurrentPhotosByAssetIdsAsync_LatestEntryHasPhotos_IsLatest()
    {
        Guid assetId;
        await using (var context = fixture.CreateContext())
        {
            var room = AddRoom(context, "101");
            var asset = AddAsset(context, room, "Bed");
            AddPhoto(context, AddHistory(context, asset, AssetCondition.Good, T0));
            AddPhoto(context, AddHistory(context, asset, AssetCondition.Damaged, T0.AddDays(1)));
            await context.SaveChangesAsync();
            assetId = asset.Id;
        }

        await using var readContext = fixture.CreateContext();
        var current = Assert.Single(await new RoomAssetConditionHistoryRepository(readContext).ListCurrentPhotosByAssetIdsAsync([assetId])).Value;

        Assert.Equal(AssetCondition.Damaged, current.History.NewCondition);
        Assert.True(current.IsLatest);
    }

    [Fact]
    public async Task ListCurrentPhotosByAssetIdsAsync_SingleQueryRegardlessOfAssetCount()
    {
        var oneAsset = await SeedRoomWithPhotographedAssetsAsync("101", 1);
        var manyAssets = await SeedRoomWithPhotographedAssetsAsync("102", 5);

        Assert.Equal(1, await CountQueriesAsync(oneAsset));
        Assert.Equal(1, await CountQueriesAsync(manyAssets));
        Assert.Equal(0, await CountQueriesAsync([]));
    }

    [Fact]
    public async Task ListCurrentPhotosByAssetIdsAsync_SameChangedAt_TieBrokenById()
    {
        Guid assetId, photographedId;
        await using (var context = fixture.CreateContext())
        {
            var room = AddRoom(context, "101");
            var asset = AddAsset(context, room, "Bed");
            var first = AddHistory(context, asset, AssetCondition.Good, T0);
            var second = AddHistory(context, asset, AssetCondition.Damaged, T0);
            var photographed = first.Id.CompareTo(second.Id) < 0 ? first : second;
            AddPhoto(context, photographed);
            await context.SaveChangesAsync();

            (assetId, photographedId) = (asset.Id, photographed.Id);
        }

        await using var readContext = fixture.CreateContext();
        var current = Assert.Single(await new RoomAssetConditionHistoryRepository(readContext).ListCurrentPhotosByAssetIdsAsync([assetId])).Value;

        Assert.Equal(photographedId, current.History.Id);
        Assert.False(current.IsLatest);
    }

    private async Task<int> CountQueriesAsync(IReadOnlyCollection<Guid> assetIds)
    {
        var counter = new CommandCounter();
        await using var context = fixture.CreateContext(counter);

        var photos = await new RoomAssetConditionHistoryRepository(context).ListCurrentPhotosByAssetIdsAsync(assetIds);

        Assert.Equal(assetIds.Count, photos.Count);
        return counter.Count;
    }

    private async Task<List<Guid>> SeedRoomWithPhotographedAssetsAsync(string roomNumber, int assetCount)
    {
        await using var context = fixture.CreateContext();
        var room = AddRoom(context, roomNumber);
        var assetIds = new List<Guid>();
        for (var i = 0; i < assetCount; i++)
        {
            var asset = AddAsset(context, room, $"Asset {i}");
            AddPhoto(context, AddHistory(context, asset, AssetCondition.Good, T0));
            assetIds.Add(asset.Id);
        }

        await context.SaveChangesAsync();
        return assetIds;
    }

    private static Room AddRoom(AppDbContext context, string roomNumber)
    {
        var organization = new Organization { Name = $"Org {roomNumber}", CreatedBy = SentinelActors.System };
        var property = new Property { OrganizationId = organization.Id, Name = $"Property {roomNumber}", CreatedBy = SentinelActors.System };
        var room = new Room { PropertyId = property.Id, RoomNumber = roomNumber, CreatedBy = SentinelActors.System };
        context.AddRange(organization, property, room);
        return room;
    }

    private static RoomAsset AddAsset(AppDbContext context, Room room, string name)
    {
        var asset = new RoomAsset { RoomId = room.Id, Name = name, CreatedBy = SentinelActors.System };
        context.RoomAssets.Add(asset);
        return asset;
    }

    private static RoomAssetConditionHistory AddHistory(
        AppDbContext context, RoomAsset asset, AssetCondition condition, DateTimeOffset changedAt)
    {
        var history = new RoomAssetConditionHistory { AssetId = asset.Id, NewCondition = condition, ChangedAt = changedAt };
        context.RoomAssetConditionHistories.Add(history);
        return history;
    }

    private static void AddPhoto(AppDbContext context, RoomAssetConditionHistory history, bool deleted = false)
    {
        var mediaAsset = new MediaAsset
        {
            FileName = "photo.png",
            StorageProvider = StorageProvider.Cloudinary,
            StorageKey = $"test/{Guid.NewGuid():N}",
            FileUrl = "https://storage.test/photo.png",
            MimeType = "image/png",
            FileSize = 16,
            EntityType = MediaAssetEntityType.RoomAssetConditionPhoto,
            CreatedBy = SentinelActors.System
        };
        context.MediaAssets.Add(mediaAsset);
        context.RoomAssetConditionHistoryMedia.Add(new RoomAssetConditionHistoryMedia
        {
            ConditionHistoryId = history.Id,
            MediaAssetId = mediaAsset.Id,
            CreatedBy = SentinelActors.System,
            DeletedAt = deleted ? T0 : null,
            DeletedBy = deleted ? SentinelActors.System : null
        });
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
