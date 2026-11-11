using Mapster;
using BoardingHouse.Application.Common.CurrentUser;
using BoardingHouse.Application.DTOs.TenantProfiles;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Services.Throttling;
using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Services;

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
        var previousIdentityNumber = profile?.IdentityNumber;
        var details = ToDetails(request, profile?.Details ?? TenantProfileDetails.Empty);

        if (profile is null)
        {
            profile = TenantProfile.Create(userId, details, currentUserAccessor.RequiredUser.Id);
        }
        else
        {
            profile.Update(details);
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

    private static TenantProfileDetails ToDetails(UpdateTenantProfileRequest request, TenantProfileDetails current)
    {
        var details = new TenantProfileDetails(
            request.IdentityType.OrKeep(current.IdentityType),
            request.IdentityNumber.OrKeep(current.IdentityNumber),
            request.IdentityIssuedDate.OrKeep(current.IdentityIssuedDate),
            request.IdentityIssuedPlace.OrKeep(current.IdentityIssuedPlace),
            request.IdentityExpiryDate.OrKeep(current.IdentityExpiryDate),
            request.DateOfBirth.OrKeep(current.DateOfBirth),
            request.Gender.OrKeep(current.Gender),
            request.PermanentAddress.OrKeep(current.PermanentAddress),
            request.EmergencyContactName.OrKeep(current.EmergencyContactName),
            request.EmergencyContactPhone.OrKeep(current.EmergencyContactPhone));

        return request.IdentityNumber.IsSet && string.IsNullOrWhiteSpace(request.IdentityNumber.Value)
            ? details with { IdentityIssuedDate = null, IdentityIssuedPlace = null, IdentityExpiryDate = null }
            : details;
    }
}
