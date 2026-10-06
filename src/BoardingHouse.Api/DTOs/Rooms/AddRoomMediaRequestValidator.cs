using BoardingHouse.Api.DTOs.Media;
using BoardingHouse.Api.Services.Storage;
using FluentValidation;

namespace BoardingHouse.Api.DTOs.Rooms;

public class AddRoomMediaRequestValidator : AbstractValidator<AddRoomMediaRequest>
{
    public AddRoomMediaRequestValidator()
    {
        RuleFor(x => x.Files)
            .NotEmpty().WithMessage("At least one file is required")
            .Must(files => files!.Count <= MediaLimits.MaxFilesPerRequest)
            .WithMessage($"At most {MediaLimits.MaxFilesPerRequest} files can be uploaded per request");

        RuleForEach(x => x.Files)
            .ValidMediaFile(MediaLimits.AllowedImageMimeTypes);
    }
}
