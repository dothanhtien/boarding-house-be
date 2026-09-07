using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Persistence;

namespace BoardingHouse.Api.Repositories;

public class OrganizationRepository(AppDbContext context)
    : Repository<Organization>(context), IOrganizationRepository;
