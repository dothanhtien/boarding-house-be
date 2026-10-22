using BoardingHouse.Application.Common.CurrentOrganization;

namespace BoardingHouse.Api.Common.CurrentOrganization;

public class CurrentOrganizationAccessor : ICurrentOrganizationAccessor
{
    public Guid? OrganizationId { get; set; }
}
