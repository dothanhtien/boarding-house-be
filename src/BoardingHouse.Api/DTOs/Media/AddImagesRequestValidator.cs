using FluentValidation;

namespace BoardingHouse.Api.DTOs.Media;

public class AddImagesRequestValidator : AbstractValidator<AddImagesRequest>
{
    public AddImagesRequestValidator()
    {
        RuleFor(x => x.Files)
            .NotEmpty().WithMessage("At least one file is required")
            .ValidImageFiles();
    }
}
