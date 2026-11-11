using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Domain.Entities;

public class Room : BaseEntity
{
    public const int MaxAmenities = 20;
    public const int MaxMedia = 20;

    public Guid PropertyId { get; private set; }
    public Property? Property { get; private set; }

    public string RoomNumber { get; private set; } = null!;
    public RoomCategory RoomCategory { get; private set; } = RoomCategory.Standard;
    public RoomStatus RoomStatus { get; private set; } = RoomStatus.Available;
    public int? FloorNumber { get; private set; }
    public decimal? Area { get; private set; }
    public int? Capacity { get; private set; }
    public decimal? MonthlyRent { get; private set; }
    public decimal? DepositAmount { get; private set; }
    public string? Note { get; private set; }

    private readonly List<RoomAmenity> _amenities = [];
    private readonly List<RoomMedia> _media = [];

    public IReadOnlyCollection<RoomAmenity> Amenities => _amenities;
    public IReadOnlyCollection<RoomMedia> Media => _media;

    private bool _coverChangePending;

    public bool HasPendingCoverChange => _coverChangePending;

    public RoomDetails Details => new(RoomNumber, RoomCategory, FloorNumber, Area, Capacity, MonthlyRent, DepositAmount, Note);

    private Room() { }

    public static Room Create(Guid propertyId, RoomDetails details, IReadOnlyList<AmenityChange> amenities, Guid createdBy)
    {
        var room = new Room { PropertyId = propertyId, CreatedBy = createdBy };
        room.Update(details);
        room.ApplyAmenityChanges(amenities, createdBy);
        return room;
    }

    public void Update(RoomDetails details)
    {
        EnsureDetailsValid(details);

        RoomNumber = details.RoomNumber;
        RoomCategory = details.RoomCategory;
        FloorNumber = details.FloorNumber;
        Area = details.Area;
        Capacity = details.Capacity;
        MonthlyRent = details.MonthlyRent;
        DepositAmount = details.DepositAmount;
        Note = details.Note;
    }

    public void ChangeStatus(RoomStatus status, bool hasActiveLease)
    {
        if (status == RoomStatus)
        {
            return;
        }

        if (status == RoomStatus.Occupied)
        {
            throw new DomainException("Room status OCCUPIED is managed by leases");
        }

        if (hasActiveLease)
        {
            throw DomainException.Conflict("Room has an active lease");
        }

        RoomStatus = status;
    }

    public void MarkOccupied()
    {
        if (RoomStatus == RoomStatus.Maintenance)
        {
            throw DomainException.Conflict("Room is under maintenance");
        }

        RoomStatus = RoomStatus.Occupied;
    }

    public void MarkAvailable()
    {
        if (RoomStatus == RoomStatus.Occupied)
        {
            RoomStatus = RoomStatus.Available;
        }
    }

    public IReadOnlyList<RoomAmenity> ApplyAmenityChanges(IReadOnlyList<AmenityChange> changes, Guid actor)
    {
        var existingById = Amenities.ToDictionary(a => a.Id);
        var unknown = changes.FirstOrDefault(c => c.Id is not null && !existingById.ContainsKey(c.Id.Value));
        if (unknown is not null)
        {
            throw DomainException.NotFound($"Room amenity {unknown.Id} not found");
        }

        var deletedCount = changes.Count(c => c.IsDeleted);
        var addedCount = changes.Count(c => c.Id is null);
        if (Amenities.Count - deletedCount + addedCount > MaxAmenities)
        {
            throw new DomainException($"A room must not have more than {MaxAmenities} amenities");
        }

        foreach (var change in changes.Where(c => c.IsDeleted))
        {
            _amenities.Remove(existingById[change.Id!.Value]);
        }

        var added = new List<RoomAmenity>();
        foreach (var change in changes.Where(c => !c.IsDeleted))
        {
            if (change.Id is { } amenityId)
            {
                var amenity = existingById[amenityId];
                if (change.Name is not null)
                {
                    amenity.Rename(change.Name);
                }

                if (change.Quantity.IsSet)
                {
                    amenity.SetQuantity(change.Quantity.Value);
                }

                if (change.Icon.IsSet)
                {
                    amenity.SetIcon(change.Icon.Value);
                }
            }
            else
            {
                added.Add(new RoomAmenity(Id, change.Name, change.Quantity.Value, change.Icon.Value, actor));
            }
        }

        _amenities.AddRange(added);

        return added;
    }

    public void EnsureCanAddMedia(int addedCount)
    {
        if (_media.Count + addedCount > MaxMedia)
        {
            throw new DomainException($"A room must not have more than {MaxMedia} media");
        }
    }

    public IReadOnlyList<RoomMedia> AddMedia(IReadOnlyList<MediaAsset> uploaded, Guid createdBy)
    {
        EnsureCanAddMedia(uploaded.Count);

        var nextSortOrder = _media.Select(m => m.SortOrder).DefaultIfEmpty(-1).Max() + 1;
        var needsCover = !_media.Any(m => m.IsCover);
        var added = uploaded
            .Select((asset, i) => new RoomMedia(Id, asset, nextSortOrder + i, isCover: needsCover && i == 0, createdBy))
            .ToList();
        _media.AddRange(added);

        return added;
    }

    public void ReorderMedia(IReadOnlyList<Guid> order)
    {
        var mediaById = _media.ToDictionary(m => m.MediaAssetId);
        if (order.Count != mediaById.Count || !order.All(mediaById.ContainsKey))
        {
            throw new DomainException("Order must list every media of the room exactly once");
        }

        for (var i = 0; i < order.Count; i++)
        {
            mediaById[order[i]].SetSortOrder(i);
        }
    }

    public void ReleaseCoverFor(Guid mediaAssetId)
    {
        var cover = RequiredMedia(mediaAssetId);
        foreach (var other in _media.Where(m => m.IsCover && m != cover))
        {
            other.SetCover(false);
        }
    }

    public void SetCover(Guid mediaAssetId) => RequiredMedia(mediaAssetId).SetCover(true);

    public RoomMedia RemoveMedia(Guid mediaAssetId)
    {
        var target = RequiredMedia(mediaAssetId);
        _media.Remove(target);
        _coverChangePending = target.IsCover && _media.Count > 0;

        return target;
    }

    public void ApplyPendingCoverChange()
    {
        if (!_coverChangePending)
        {
            throw new InvalidOperationException("No pending cover change: call RemoveMedia on the cover first");
        }

        _coverChangePending = false;
        _media.OrderBy(m => m.SortOrder).First().SetCover(true);
    }

    private static void EnsureDetailsValid(RoomDetails details)
    {
        if (string.IsNullOrWhiteSpace(details.RoomNumber))
        {
            throw new DomainException("RoomNumber is required");
        }

        if (details.FloorNumber < 0)
        {
            throw new DomainException("FloorNumber must not be negative");
        }

        if (details.Area <= 0)
        {
            throw new DomainException("Area must be greater than 0");
        }

        if (details.Capacity <= 0)
        {
            throw new DomainException("Capacity must be greater than 0");
        }

        if (details.MonthlyRent <= 0)
        {
            throw new DomainException("MonthlyRent must be greater than 0");
        }

        if (details.DepositAmount <= 0)
        {
            throw new DomainException("DepositAmount must be greater than 0");
        }
    }

    private RoomMedia RequiredMedia(Guid mediaAssetId) =>
        _media.FirstOrDefault(m => m.MediaAssetId == mediaAssetId)
            ?? throw DomainException.NotFound($"Room media '{mediaAssetId}' not found");
}
