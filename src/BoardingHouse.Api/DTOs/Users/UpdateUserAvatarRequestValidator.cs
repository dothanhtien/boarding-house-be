using BoardingHouse.Api.DTOs.Media;
using BoardingHouse.Api.Services.Storage;
using FluentValidation;

namespace BoardingHouse.Api.DTOs.Users;

public class UpdateUserAvatarRequestValidator : AbstractValidator<UpdateUserAvatarRequest>
{
    public UpdateUserAvatarRequestValidator()
    {
        RuleFor(x => x.File)
            .NotNull().WithMessage("File is required")
            .ValidMediaFile(MediaLimits.AllowedImageMimeTypes);
    }
}
