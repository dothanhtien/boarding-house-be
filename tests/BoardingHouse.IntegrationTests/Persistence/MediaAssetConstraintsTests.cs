using BoardingHouse.Api.Common;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BoardingHouse.IntegrationTests.Persistence;

public class MediaAssetConstraintsTests(PostgresApiFactory factory) : IClassFixture<PostgresApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static MediaAsset NewAsset(MediaAssetEntityType? entityType, Guid? entityId) => new()
    {
        FileName = "file.png",
        StorageProvider = StorageProvider.Cloudinary,
        StorageKey = $"test/{Guid.NewGuid():N}",
        FileUrl = "https://storage.test/file.png",
        MimeType = "image/png",
        FileSize = 1,
        EntityType = entityType,
        EntityId = entityId,
        CreatedBy = SentinelActors.System
    };

    private async Task InsertAsync(MediaAsset asset)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.MediaAssets.Add(asset);
        await context.SaveChangesAsync();
    }

    private async Task SoftDeleteAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var asset = await context.MediaAssets.SingleAsync(m => m.Id == id);
        asset.DeletedAt = DateTimeOffset.UtcNow;
        asset.DeletedBy = SentinelActors.System;
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task SingleSlotType_TwoLiveAssetsForSameEntity_ViolatesUniqueIndex()
    {
        var userId = Guid.NewGuid();
        await InsertAsync(NewAsset(MediaAssetEntityType.UserAvatar, userId));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            InsertAsync(NewAsset(MediaAssetEntityType.UserAvatar, userId)));

        Assert.True(exception.IsUniqueViolation("ix_media_assets_entity_type_entity_id"));
    }

    [Fact]
    public async Task SingleSlotType_PreviousAssetSoftDeleted_AllowsNewAsset()
    {
        var userId = Guid.NewGuid();
        var previous = NewAsset(MediaAssetEntityType.UserAvatar, userId);
        await InsertAsync(previous);
        await SoftDeleteAsync(previous.Id);

        await InsertAsync(NewAsset(MediaAssetEntityType.UserAvatar, userId));
    }

    [Fact]
    public async Task MultiAssetType_SeveralLiveAssetsWithoutEntityId_AreAllowed()
    {
        await InsertAsync(NewAsset(MediaAssetEntityType.PropertyMedia, null));
        await InsertAsync(NewAsset(MediaAssetEntityType.PropertyMedia, null));
    }

    [Fact]
    public async Task UnlinkedUploads_SeveralLiveAssets_AreAllowed()
    {
        await InsertAsync(NewAsset(null, null));
        await InsertAsync(NewAsset(null, null));
    }

    [Fact]
    public async Task EntityIdWithoutEntityType_ViolatesCheckConstraint()
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => InsertAsync(NewAsset(null, Guid.NewGuid())));

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, postgresException.SqlState);
        Assert.Equal("CK_MediaAssets_EntityLink", postgresException.ConstraintName);
    }
}
