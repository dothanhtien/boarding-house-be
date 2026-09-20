namespace BoardingHouse.Api.Common.CurrentOrganization;

public interface ICurrentOrganizationAccessor
{
    Guid? OrganizationId { get; set; }
}
