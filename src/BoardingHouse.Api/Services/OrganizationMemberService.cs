using System.Linq.Expressions;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Organizations;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace BoardingHouse.Api.Services;

public class OrganizationMemberService(
    IOrganizationMemberRepository organizationMemberRepository,
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    AppDbContext context,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<OrganizationMemberService> logger) : IOrganizationMemberService
{
    private static readonly Dictionary<string, Expression<Func<OrganizationMember, object>>> SortableFields =
    new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = m => m.CreatedAt,
        ["createdAt"] = m => m.CreatedAt,
        ["userEmail"] = m => m.User!.Email,
        ["userFullName"] = m => m.User!.FullName,
        ["roleName"] = m => m.Role!.Name
    };

    public async Task<PagedResult<OrganizationMemberResponse>> GetByOrganizationIdAsync(
        Guid organizationId, OrganizationMemberListQuery query, CancellationToken cancellationToken = default)
    {
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var members = context.OrganizationMembers
            .Include(m => m.User)
            .Include(m => m.Role)
            .Where(m => m.OrganizationId == organizationId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = LikePattern.Contains(query.Search.Trim());
            members = members.Where(m =>
                EF.Functions.ILike(m.User!.Email, pattern, LikePattern.EscapeCharacter) ||
                EF.Functions.ILike(m.User!.FullName, pattern, LikePattern.EscapeCharacter));
        }

        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);
        var paged = await members.ToPagedResultAsync(query, sortField, query.SortOrder, cancellationToken);

        return new PagedResult<OrganizationMemberResponse>
        {
            Items = paged.Items.Adapt<List<OrganizationMemberResponse>>(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<OrganizationMemberResponse> AddAsync(
        Guid organizationId,
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundAppException($"User '{request.UserId}' not found");

        var role = await GetOrganizationScopedRoleAsync(request.RoleId, cancellationToken);

        var existing = await organizationMemberRepository.GetByOrganizationAndUserIdAsync(
            organizationId, request.UserId, cancellationToken);
        if (existing is not null)
        {
            logger.LogWarning("Add organization member failed: user already a member ({OrganizationId}, {UserId})", organizationId, request.UserId);
            throw new ConflictAppException("User is already a member of this organization");
        }

        var member = new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = request.UserId,
            RoleId = request.RoleId,
            CreatedBy = currentUserAccessor.RequiredUser.Id
        };

        try
        {
            await organizationMemberRepository.AddAsync(member, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            logger.LogWarning("Add organization member failed: user already a member ({OrganizationId}, {UserId})", organizationId, request.UserId);
            throw new ConflictAppException("User is already a member of this organization");
        }

        logger.LogInformation("Organization member added ({OrganizationId}, {MemberId})", organizationId, member.Id);

        member.User = user;
        member.Role = role;

        return member.Adapt<OrganizationMemberResponse>();
    }

    public async Task<OrganizationMemberResponse> UpdateRoleAsync(
        Guid organizationId,
        Guid memberId,
        UpdateOrganizationMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var member = await context.OrganizationMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundAppException($"Organization member '{memberId}' not found");

        var role = await GetOrganizationScopedRoleAsync(request.RoleId, cancellationToken);

        member.RoleId = request.RoleId;
        member.Role = role;

        organizationMemberRepository.Update(member);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization member role updated ({OrganizationId}, {MemberId})", organizationId, memberId);

        return member.Adapt<OrganizationMemberResponse>();
    }

    public async Task RemoveAsync(Guid organizationId, Guid memberId, CancellationToken cancellationToken = default)
    {
        await EnsureOrganizationExistsAsync(organizationId, cancellationToken);

        var member = await organizationMemberRepository.GetByIdAsync(memberId, cancellationToken);
        if (member is null || member.OrganizationId != organizationId)
        {
            throw new NotFoundAppException($"Organization member '{memberId}' not found");
        }

        organizationMemberRepository.SoftDelete(member);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Organization member removed ({OrganizationId}, {MemberId})", organizationId, memberId);
    }

    private async Task EnsureOrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!await organizationRepository.ExistsAsync(organizationId, cancellationToken))
        {
            throw new NotFoundAppException($"Organization '{organizationId}' not found");
        }
    }

    private async Task<Role> GetOrganizationScopedRoleAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var role = await roleRepository.GetByIdAsync(roleId, cancellationToken)
            ?? throw new NotFoundAppException($"Role '{roleId}' not found");

        if (role.Scope != RoleScope.Organization)
        {
            throw new ValidationAppException("Role must be an organization-scoped role");
        }

        return role;
    }
}
