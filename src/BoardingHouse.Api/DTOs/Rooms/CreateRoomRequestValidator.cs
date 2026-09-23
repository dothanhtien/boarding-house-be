using FluentValidation;

namespace BoardingHouse.Api.DTOs.Rooms;

public class CreateRoomRequestValidator : AbstractValidator<CreateRoomRequest>
{
    public CreateRoomRequestValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty().WithMessage("PropertyId is required");

        RuleFor(x => x.RoomNumber)
            .NotEmpty().WithMessage("RoomNumber is required")
            .MaximumLength(20).WithMessage("RoomNumber must not exceed 20 characters");

        RuleFor(x => x.RoomCategory).IsInEnum().WithMessage("RoomCategory is invalid");

        RuleFor(x => x.FloorNumber)
            .GreaterThanOrEqualTo(0).WithMessage("FloorNumber must not be negative")
            .When(x => x.FloorNumber is not null);

        RuleFor(x => x.Area)
            .GreaterThan(0).WithMessage("Area must be greater than 0")
            .PrecisionScale(10, 2, ignoreTrailingZeros: true)
            .WithMessage("Area must not exceed 10 digits in total, with at most 2 decimal places")
            .When(x => x.Area is not null);

        RuleFor(x => x.Capacity)
            .GreaterThan(0).WithMessage("Capacity must be greater than 0")
            .When(x => x.Capacity is not null);

        RuleFor(x => x.MonthlyRent)
            .GreaterThan(0).WithMessage("MonthlyRent must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("MonthlyRent must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.MonthlyRent is not null);

        RuleFor(x => x.DepositAmount)
            .GreaterThan(0).WithMessage("DepositAmount must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("DepositAmount must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.DepositAmount is not null);
    }
}
