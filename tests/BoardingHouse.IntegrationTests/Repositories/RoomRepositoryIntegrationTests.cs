using Microsoft.EntityFrameworkCore;
using BoardingHouse.IntegrationTests.Fixtures;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Infrastructure.Persistence;
using BoardingHouse.Infrastructure.Repositories;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.IntegrationTests.Repositories;

public class RoomRepositoryIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    private static readonly DateOnly Month = new(2026, 10, 1);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SearchForUtilityReadingAsync_CountsTheMonthsReadingsAndThoseOnMeteredServices()
    {
        var propertyId = await SeedAsync();

        var rooms = await SearchAsync(propertyId, recorded: null);

        Assert.Equal(["101", "102", "103"], rooms.Select(r => r.Room.RoomNumber));
        Assert.Equal([2, 2, 0], rooms.Select(r => r.RecordedCount));
        // 102's reading of the inactive service counts as an extra meter
        Assert.Equal([2, 3, 2], rooms.Select(r => r.MeterCount));
    }

    [Fact]
    public async Task SearchForUtilityReadingAsync_FiltersOnRecorded()
    {
        var propertyId = await SeedAsync();

        var rooms = await SearchAsync(propertyId, recorded: false);

        Assert.Equal(["102", "103"], rooms.Select(r => r.Room.RoomNumber));
    }

    [Fact]
    public async Task GetAggregateForUpdateAsync_LoadsAmenitiesAndMediaWithAsset()
    {
        Guid roomId;
        await using (var seed = fixture.CreateContext())
        {
            var organization = TestEntities.New<Organization>(new { Name = "Org", CreatedBy = SentinelActors.System });
            var property = TestEntities.New<Property>(new { OrganizationId = organization.Id, Name = "Property", CreatedBy = SentinelActors.System });
            seed.AddRange(organization, property);
            var room = AddRoom(seed, property, "101");
            room.ApplyAmenityChanges([new AmenityChange { Name = "Fan" }], SentinelActors.System);
            var mediaAsset = TestEntities.New<MediaAsset>(new
            {
                FileName = "photo.png",
                StorageProvider = StorageProvider.Cloudinary,
                StorageKey = $"test/{Guid.NewGuid():N}",
                FileUrl = "https://storage.test/photo.png",
                MimeType = "image/png",
                FileSize = 16,
                EntityType = MediaAssetEntityType.RoomMedia,
                CreatedBy = SentinelActors.System
            });
            seed.MediaAssets.Add(mediaAsset);
            room.AddMedia([mediaAsset], SentinelActors.System);
            await seed.SaveChangesAsync();
            roomId = room.Id;
        }

        await using var context = fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var (loaded, organizationId) = (await new RoomRepository(context).GetAggregateForUpdateAsync(roomId))!;

        Assert.NotEqual(Guid.Empty, organizationId);
        Assert.Equal("Fan", Assert.Single(loaded.Amenities).Name);
        Assert.NotNull(Assert.Single(loaded.Media).MediaAsset);
    }

    private async Task<List<RoomWithRecordedMeterCount>> SearchAsync(
        Guid propertyId, bool? recorded)
    {
        await using var context = fixture.CreateContext();
        var paged = await new RoomRepository(context).SearchForUtilityReadingAsync(
            propertyId, BillingMonth.Of(Month), recorded, search: null,
            new PageRequest(), r => r.Room.RoomNumber, SortOrder.Asc);
        return paged.Items.ToList();
    }

    // 101: both meters recorded · 102: one meter, plus a reading of an inactive service and one of last month ·
    // 103: nothing
    private async Task<Guid> SeedAsync()
    {
        await using var context = fixture.CreateContext();
        var organization = TestEntities.New<Organization>(new { Name = "Org", CreatedBy = SentinelActors.System });
        var property = TestEntities.New<Property>(new { OrganizationId = organization.Id, Name = "Property", CreatedBy = SentinelActors.System });
        context.AddRange(organization, property);

        var electricity = AddService(context, property, "Electricity");
        var water = AddService(context, property, "Water");
        var retired = AddService(context, property, "Gas");
        TestEntities.Set(retired, new { IsActive = false });

        var room101 = AddRoom(context, property, "101");
        var room102 = AddRoom(context, property, "102");
        AddRoom(context, property, "103");

        AddReading(context, room101, electricity, Month);
        AddReading(context, room101, water, Month);
        AddReading(context, room102, electricity, Month);
        AddReading(context, room102, retired, Month);
        AddReading(context, room102, water, Month.AddMonths(-1));

        await context.SaveChangesAsync();
        return property.Id;
    }

    private static UtilityService AddService(AppDbContext context, Property property, string name)
    {
        var service = TestEntities.New<UtilityService>(new
        {
            PropertyId = property.Id,
            Name = name,
            Unit = "unit",
            BillingMethod = UtilityBillingMethod.Metered,
            CreatedBy = SentinelActors.System
        });
        context.UtilityServices.Add(service);
        return service;
    }

    private static Room AddRoom(AppDbContext context, Property property, string roomNumber)
    {
        var room = TestRooms.New(propertyId: property.Id, roomNumber: roomNumber);
        context.Rooms.Add(room);
        return room;
    }

    private static void AddReading(AppDbContext context, Room room, UtilityService service, DateOnly readingMonth) =>
        context.UtilityReadings.Add(UtilityReading.Record(
            room.Id, service.Id, readOn: readingMonth, BillingMonth.FromFirstDay(readingMonth),
            latest: null, initialPreviousValue: null, currentValue: 10, billedLeaseId: null,
            DateTimeOffset.UtcNow, SentinelActors.System));
}
