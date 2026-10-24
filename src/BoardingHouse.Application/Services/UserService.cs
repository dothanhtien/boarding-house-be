using System.Linq.Expressions;
using Mapster;
using BoardingHouse.Application.Common;
using BoardingHouse.Application.DTOs.Users;
using BoardingHouse.Application.Exceptions;
using BoardingHouse.Application.Repositories;
using BoardingHouse.Application.Persistence;
using BoardingHouse.Application.Services.Caching;
using BoardingHouse.Application.Services.Storage;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.Common.CurrentUser;

namespace BoardingHouse.Application.Services;

public class UserService(
    IUserRepository userRepository,
    ITenantProfileRepository tenantProfileRepository,
    IUnitOfWork unitOfWork,
    IUserCache userCache,
    IMediaAttachmentService mediaAttachmentService,
    IStorageProvider storageProvider,
    ICurrentUserAccessor currentUserAccessor,
    ILogger<UserService> logger) : IUserService
{
    private static readonly Dictionary<string, Expression<Func<User, object>>> SortableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [""] = u => u.CreatedAt,
        ["email"] = u => u.Email!,
        ["fullName"] = u => u.FullName,
        ["createdAt"] = u => u.CreatedAt,
        ["isActive"] = u => u.IsActive
    };

    public async Task<PagedResult<UserResponse>> GetAllAsync(UserListQuery query, CancellationToken cancellationToken = default)
    {
        var sortField = SortableFields.GetValueOrDefault(query.SortBy ?? "", SortableFields[""]);
        var paged = await userRepository.SearchAsync(query.IsActive, query.Search, query, sortField, query.SortOrder, cancellationToken);

        return new PagedResult<UserResponse>
        {
            Items = paged.Items
                .Select(u => u.Entity.Adapt<UserResponse>() with
                {
                    AvatarUrl = u.Media is null ? null : storageProvider.GetUrl(u.Media.StorageKey, u.Media.MimeType)
                })
                .ToList(),
            Page = paged.Page,
            PageSize = paged.PageSize,
            TotalItems = paged.TotalItems
        };
    }

    public async Task<UserResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var (user, avatar) = await userRepository.GetByIdWithAvatarAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.User(id));

        return user.Adapt<UserResponse>() with
        {
            AvatarUrl = avatar is null ? null : storageProvider.GetUrl(avatar.StorageKey, avatar.MimeType)
        };
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var phone = PhoneNumber.Normalize(request.Phone);
        var maskedEmail = EmailAddress.Mask(email);
        var maskedPhone = PhoneNumber.Mask(phone ?? "");

        if (await userRepository.ExistsByEmailOrPhoneAsync(email, phone, cancellationToken))
        {
            logger.LogWarning("Create user failed: email or phone already in use ({Email}, {Phone})", maskedEmail, maskedPhone);
            throw new AppConflictException("Email or phone already in use");
        }

        var user = new User
        {
            Email = email,
            Phone = phone,
            PasswordHash = PasswordHasher.Hash(request.Password),
            FullName = request.FullName,
            CreatedBy = currentUserAccessor.User?.Id ?? SentinelActors.System
        };

        try
        {
            await userRepository.AddAsync(user, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            logger.LogWarning("Create user failed: email or phone already in use ({Email}, {Phone})", maskedEmail, maskedPhone);
            throw new AppConflictException("Email or phone already in use");
        }

        logger.LogInformation("User created ({UserId}, {Email}, {Phone})", user.Id, maskedEmail, maskedPhone);

        return user.Adapt<UserResponse>();
    }

    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var (user, avatar) = await userRepository.GetByIdWithAvatarAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.User(id));

        if (request.Email.IsSet)
        {
            var email = request.Email.Value!.Trim().ToLowerInvariant();
            var maskedEmail = EmailAddress.Mask(email);

            if (email != user.Email &&
                await userRepository.ExistsByEmailExcludingUserAsync(email, user.Id, cancellationToken))
            {
                logger.LogWarning("Update user failed: email already in use ({Email})", maskedEmail);
                throw new AppConflictException("Email already in use");
            }

            user.Email = email;
        }

        if (request.Phone.IsSet)
        {
            var phone = PhoneNumber.Normalize(request.Phone.Value);

            if (phone != user.Phone &&
                phone is not null &&
                await userRepository.ExistsByPhoneExcludingUserAsync(phone, user.Id, cancellationToken))
            {
                logger.LogWarning("Update user failed: phone already in use ({UserId})", id);
                throw new AppConflictException("Phone already in use");
            }

            user.Phone = phone;
        }

        request.FullName.ApplyIfSet(v => user.FullName = v!);
        request.IsActive.ApplyIfSet(v => user.IsActive = v);

        try
        {
            userRepository.Update(user);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            logger.LogWarning("Update user failed: email or phone already in use ({UserId})", id);
            throw new AppConflictException("Email or phone already in use");
        }

        await userCache.InvalidateAsync(user.Id, cancellationToken);

        logger.LogInformation("User updated ({UserId})", user.Id);

        return user.Adapt<UserResponse>() with
        {
            AvatarUrl = avatar is null ? null : storageProvider.GetUrl(avatar.StorageKey, avatar.MimeType)
        };
    }

    public async Task<UserResponse> UpdateAvatarAsync(Guid id, IFileUpload file, CancellationToken cancellationToken = default)
    {
        var (user, _) = await userRepository.GetByIdWithRolesAndOrganizationsAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.User(id));

        var change = await mediaAttachmentService.StageReplaceAsync(
            MediaAssetEntityType.UserAvatar,
            user.Id,
            file,
            $"users/{user.Id}",
            organizationId: null,
            cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.MediaAssetEntityTypeEntityId)
        {
            await mediaAttachmentService.DiscardAsync(change);
            logger.LogWarning("Update user avatar failed: avatar changed concurrently ({UserId})", user.Id);
            throw new AppConflictException("Avatar was changed by another request, please try again");
        }
        catch
        {
            await mediaAttachmentService.DiscardAsync(change);
            throw;
        }

        await mediaAttachmentService.CompleteAsync(change);

        logger.LogInformation("User avatar updated ({UserId})", user.Id);

        return user.Adapt<UserResponse>() with
        {
            AvatarUrl = storageProvider.GetUrl(change.Added!.StorageKey, change.Added.MimeType)
        };
    }

    public async Task DeleteAvatarAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await userRepository.ExistsAsync(id, cancellationToken))
        {
            throw new AppNotFoundException(NotFoundMessages.User(id));
        }

        var change = await mediaAttachmentService.StageRemoveAsync(MediaAssetEntityType.UserAvatar, id, cancellationToken);
        if (change == MediaAttachmentChange.None)
        {
            return;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == UniqueIndexNames.MediaAssetEntityTypeEntityId)
        {
            await mediaAttachmentService.DiscardAsync(change);
            logger.LogWarning("Remove user avatar failed: avatar changed concurrently ({UserId})", id);
            throw new AppConflictException("Avatar was changed by another request, please try again");
        }
        catch
        {
            await mediaAttachmentService.DiscardAsync(change);
            throw;
        }

        await mediaAttachmentService.CompleteAsync(change);

        logger.LogInformation("User avatar removed ({UserId})", id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new AppNotFoundException(NotFoundMessages.User(id));

        userRepository.SoftDelete(user);

        var tenantProfile = await tenantProfileRepository.GetByUserIdAsync(user.Id, cancellationToken);
        if (tenantProfile is not null)
        {
            tenantProfileRepository.SoftDelete(tenantProfile);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await userCache.InvalidateAsync(user.Id, cancellationToken);

        logger.LogInformation("User soft-deleted ({UserId})", user.Id);
    }
}
