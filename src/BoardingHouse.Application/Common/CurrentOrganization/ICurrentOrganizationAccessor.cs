namespace BoardingHouse.Application.Common.CurrentOrganization;

public interface ICurrentOrganizationAccessor
{
    Guid? OrganizationId { get; set; }
}
