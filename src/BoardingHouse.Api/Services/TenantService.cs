using Mapster;
using Microsoft.EntityFrameworkCore;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Common.CurrentOrganization;
using BoardingHouse.Api.DTOs.TenantProfiles;
using BoardingHouse.Api.DTOs.Tenants;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Persistence.Configurations;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Throttling;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Api.Services;

public class TenantService(
    IUserRepository userRepository,
    ITenantProfileRepository tenantProfileRepository,
    ILeaseRepository leaseRepository,
    IOrganizationRepository organizationRepository,
    ITenantProfileService tenantProfileService,
    IUnitOfWork unitOfWork,
    IOrganizationScopeAccessor organizationScopeAccessor,
    ICurrentUserAccessor currentUserAccessor,
    ICurrentOrganizationAccessor currentOrganizationAccessor,
    IAttemptLimiter attemptLimiter,
    ILogger<TenantService> logger) : ITenantService
{
    public const int LookupLimit = 60;
    public static readonly TimeSpan LookupWindow = TimeSpan.FromHours(1);

    private const string DuplicateUserMessage = "A user with this phone or email already exists - use tenant lookup";

    public async Task<TenantLookupResponse> LookupAsync(TenantLookupQuery query, CancellationToken cancellationToken = default)
    {
        var loggedInUserId = currentUserAccessor.RequiredUser.Id;

        if (!await attemptLimiter.TryAcquireAsync($"tenant:lookup:{loggedInUserId}", LookupLimit, LookupWindow, cancellationToken))
        {
            logger.LogWarning("Tenant lookup rejected: lookup limit reached ({UserId})", loggedInUserId);
            throw new AppTooManyRequestsException("Too many tenant lookups, please try again later");
        }

        var phone = PhoneNumber.Normalize(query.Phone);
        var user = (phone is not null
            ? await userRepository.GetByPhoneAsync(phone, cancellationToken)
            : await userRepository.GetByEmailAsync(EmailAddress.Normalize(query.Email)!, cancellationToken)) ?? throw new AppNotFoundException("No user matches this phone or email");

        return new TenantLookupResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            MaskedPhone = string.IsNullOrEmpty(user.Phone) ? null : PhoneNumber.Mask(user.Phone),
            MaskedEmail = user.Email is null ? null : EmailAddress.Mask(user.Email),
            HasTenantProfile = await tenantProfileRepository.ExistsByUserIdAsync(user.Id, cancellationToken),
            IsActive = user.IsActive
        };
    }

    public async Task<TenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken = default)
    {
        var organizationId = request.OrganizationId ?? currentOrganizationAccessor.OrganizationId
            ?? throw new AppValidationException("OrganizationId is required — pass it explicitly or set the X-Organization-Id header");

        if (!await organizationRepository.ExistsAsync(organizationId, cancellationToken))
        {
            throw new AppNotFoundException(NotFoundMessages.Organization(organizationId));
        }

        await organizationScopeAccessor.EnsureScopeAsync(
            organizationId, PermissionResources.Tenant, PermissionActions.Create, cancellationToken: cancellationToken);

        var phone = PhoneNumber.Normalize(request.Phone)!;
        var email = EmailAddress.Normalize(request.Email);

        if (await userRepository.ExistsByEmailOrPhoneAsync(email, phone, cancellationToken))
        {
            logger.LogWarning("Create tenant failed: phone or email already in use (organization {OrganizationId})", organizationId);
            throw new AppConflictException(DuplicateUserMessage);
        }

        try
        {
            var (user, profile) = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var created = new User
                {
                    Email = email,
                    Phone = phone,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
                    FullName = request.FullName,
                    CreatedBy = currentUserAccessor.RequiredUser.Id
                };

                await userRepository.AddAsync(created, ct);

                var createdProfile = request.Profile is null
                    ? null
                    : await tenantProfileService.UpsertAsync(created.Id, request.Profile, ct);

                return (created, createdProfile);
            }, cancellationToken);

            logger.LogInformation("Tenant created ({UserId}, organization {OrganizationId})", user.Id, organizationId);

            return user.Adapt<TenantResponse>() with { Profile = profile };
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(UserConfiguration.EmailUniqueIndex)
            || ex.IsUniqueViolation(UserConfiguration.PhoneUniqueIndex))
        {
            logger.LogWarning("Create tenant failed: phone or email already in use (organization {OrganizationId})", organizationId);
            throw new AppConflictException(DuplicateUserMessage);
        }
    }

    public async Task<TenantResponse> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Tenant(userId));

        var scope = await organizationScopeAccessor.GetScopeAsync(PermissionResources.Tenant, PermissionActions.Read, cancellationToken);

        var leases = await leaseRepository.ListByTenantUserIdAsync(userId, scope.IsUnrestricted ? null : scope.OrganizationIds, cancellationToken);

        if (leases.Count == 0 && !scope.IsUnrestricted)
        {
            throw new AppNotFoundException(NotFoundMessages.Tenant(userId));
        }

        var profile = await tenantProfileService.GetByUserIdAsync(userId, cancellationToken);

        return user.Adapt<TenantResponse>() with
        {
            Profile = profile,
            Leases = leases.Adapt<List<TenantLeaseResponse>>()
        };
    }

    public async Task<TenantProfileResponse> UpdateProfileAsync(
        Guid userId, UpdateTenantProfileRequest request, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.Tenant(userId));

        var scope = await organizationScopeAccessor.GetScopeAsync(
            PermissionResources.Tenant, PermissionActions.Update, cancellationToken);

        if (!scope.IsUnrestricted
            && !await leaseRepository.ExistsTenantInOrganizationsAsync(userId, scope.OrganizationIds, cancellationToken))
        {
            throw new AppNotFoundException(NotFoundMessages.Tenant(userId));
        }

        var profile = await tenantProfileService.UpsertAsync(userId, request, cancellationToken);

        logger.LogInformation("Tenant profile updated by staff ({UserId})", userId);

        return profile;
    }
}
