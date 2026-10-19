using BoardingHouse.Api.DTOs.Media;
using FluentValidation;
using BoardingHouse.Application.Services.Storage;

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
