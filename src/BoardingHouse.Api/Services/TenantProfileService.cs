using Mapster;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Services.Throttling;
using BoardingHouse.Application.DTOs.TenantProfiles;
using BoardingHouse.Application.Mappings;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Api.Services;

public class TenantProfileService(
    ITenantProfileRepository tenantProfileRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUserAccessor,
    IAttemptLimiter attemptLimiter,
    ILogger<TenantProfileService> logger) : ITenantProfileService
{
    public const int IdentityNumberChangeLimit = 5;
    public static readonly TimeSpan IdentityNumberChangeWindow = TimeSpan.FromDays(1);

    public async Task<TenantProfileResponse?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var profile = await tenantProfileRepository.GetByUserIdAsync(userId, cancellationToken);
        return profile?.Adapt<TenantProfileResponse>();
    }

    public async Task<TenantProfileResponse> UpsertAsync(Guid userId, UpdateTenantProfileRequest request, CancellationToken cancellationToken = default)
    {
        var profile = await tenantProfileRepository.GetByUserIdAsync(userId, cancellationToken);

        var isNew = profile is null;
        profile ??= new TenantProfile
        {
            UserId = userId,
            CreatedBy = currentUserAccessor.RequiredUser.Id
        };
        var previousIdentityNumber = profile.IdentityNumber;

        request.Adapt(profile, TenantProfileMappingConfig.UpdateConfig);

        if (profile.IsEmpty())
        {
            throw new AppValidationException("Tenant profile must contain at least one field");
        }

        if (profile.IdentityNumber is null
            && (profile.IdentityIssuedDate is not null || profile.IdentityIssuedPlace is not null || profile.IdentityExpiryDate is not null))
        {
            throw new AppValidationException("Identity issued date, issued place and expiry date require an identity document");
        }

        if (profile is { IdentityIssuedDate: { } issued, IdentityExpiryDate: { } expiry } && expiry <= issued)
        {
            throw new AppValidationException("Identity expiry date must be after the identity issued date");
        }

        if (profile.IdentityNumber is not null && profile.IdentityNumber != previousIdentityNumber
            && !await attemptLimiter.TryAcquireAsync(
                $"tenant-profile:identity-number:{userId}",
                IdentityNumberChangeLimit,
                IdentityNumberChangeWindow,
                cancellationToken))
        {
            logger.LogWarning("Upsert tenant profile rejected: identity number change limit reached ({UserId})", userId);
            throw new AppTooManyRequestsException("Too many identity number changes, please try again later");
        }

        if (isNew)
        {
            await tenantProfileRepository.AddAsync(profile, cancellationToken);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.TenantProfileIdentityNumber)
        {
            logger.LogWarning("Upsert tenant profile failed: identity document already in use ({UserId})", userId);
            throw new AppConflictException("This identity document is already used by another tenant profile");
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.TenantProfileUserId)
        {
            logger.LogWarning("Upsert tenant profile failed: profile created by a concurrent request ({UserId})", userId);
            throw new AppConflictException("Tenant profile was modified by another request, please retry");
        }

        logger.LogInformation("Tenant profile {Action} ({UserId})", isNew ? "created" : "updated", userId);

        return profile.Adapt<TenantProfileResponse>();
    }
}
