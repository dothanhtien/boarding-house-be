using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;

namespace BoardingHouse.Api.Services;

public class OrganizationService(
    IOrganizationRepository organizationRepository,
    AppDbContext context,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<OrganizationService> logger) : IOrganizationService
{
    public async Task<List<OrganizationResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var organizations = await organizationRepository.GetAllAsync(cancellationToken);
        return organizations.Adapt<List<OrganizationResponse>>();
    }

    public async Task<OrganizationResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"Organization '{id}' not found");

        return organization.Adapt<OrganizationResponse>();
    }

    public async Task<OrganizationResponse> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var organization = new Organization
        {
            Name = request.Name,
            TaxCode = request.TaxCode,
            Phone = request.Phone,
            Email = request.Email,
            Province = request.Province,
            District = request.District,
            Ward = request.Ward,
            Address = request.Address,
            CreatedBy = currentUserAccessor.RequiredUser.Id
        };

        await organizationRepository.AddAsync(organization, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization created ({OrganizationId})", organization.Id);

        return organization.Adapt<OrganizationResponse>();
    }

    public async Task<OrganizationResponse> UpdateAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"Organization '{id}' not found");

        organization.Name = request.Name;
        organization.TaxCode = request.TaxCode;
        organization.Phone = request.Phone;
        organization.Email = request.Email;
        organization.Province = request.Province;
        organization.District = request.District;
        organization.Ward = request.Ward;
        organization.Address = request.Address;
        organization.IsActive = request.IsActive;

        organizationRepository.Update(organization);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization updated ({OrganizationId})", organization.Id);

        return organization.Adapt<OrganizationResponse>();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var organization = await organizationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundAppException($"Organization '{id}' not found");

        organizationRepository.SoftDelete(organization);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization soft-deleted ({OrganizationId})", organization.Id);
    }
}
