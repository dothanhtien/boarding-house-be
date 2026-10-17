namespace BoardingHouse.Api.Common;

public static class NotFoundMessages
{
    public static string Lease(Guid id) => $"Lease '{id}' not found";
    public static string LeaseTenant(Guid id) => $"Lease tenant '{id}' not found";
    public static string Organization(Guid id) => $"Organization '{id}' not found";
    public static string OrganizationMember(Guid id) => $"Organization member '{id}' not found";
    public static string Property(Guid id) => $"Property '{id}' not found";
    public static string Role(Guid id) => $"Role '{id}' not found";
    public static string Room(Guid id) => $"Room '{id}' not found";
    public static string RoomAmenity(Guid id) => $"Room amenity '{id}' not found";
    public static string RoomAsset(Guid id) => $"Room asset '{id}' not found";
    public static string RoomAssetConditionHistory(Guid id) => $"Room asset condition history '{id}' not found";
    public static string RoomAssetConditionPhoto(Guid id) => $"Room asset condition photo '{id}' not found";
    public static string RoomMedia(Guid id) => $"Room media '{id}' not found";
    public static string User(Guid id) => $"User '{id}' not found";
    public static string UtilityService(Guid id) => $"Utility service '{id}' not found";
}
