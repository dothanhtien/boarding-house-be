namespace BoardingHouse.Application.Persistence;

/// <summary>
/// Database names of the unique indexes that handlers match on when catching
/// <see cref="Exceptions.UniqueConstraintViolationException"/>. The EF configurations in Infrastructure reference
/// these constants, so the name is declared once.
/// </summary>
public static class UniqueIndexNames
{
    public const string LeaseOrganizationIdLeaseCode = "ix_leases_organization_id_lease_code";
    public const string LeasePreviousLeaseId = "ix_leases_previous_lease_id";
    public const string LeaseRoomIdActive = "ix_leases_room_id_active";
    public const string MediaAssetEntityTypeEntityId = "ix_media_assets_entity_type_entity_id";
    public const string RoomPropertyIdRoomNumber = "ix_rooms_property_id_room_number";
    public const string TenantProfileIdentityNumber = "ix_tenant_profiles_identity_number";
    public const string TenantProfileUserId = "ix_tenant_profiles_user_id";
    public const string UserEmail = "ix_users_email";
    public const string UserPhone = "ix_users_phone";
    public const string UtilityServicePropertyIdName = "ix_utility_services_property_id_name";
}
