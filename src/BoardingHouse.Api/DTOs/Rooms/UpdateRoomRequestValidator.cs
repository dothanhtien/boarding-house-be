using FluentValidation;

namespace BoardingHouse.Api.DTOs.Rooms;

public class UpdateRoomRequestValidator : AbstractValidator<UpdateRoomRequest>
{
    public UpdateRoomRequestValidator()
    {
        RuleFor(x => x.RoomNumber.Value)
            .NotEmpty().WithMessage("RoomNumber is required")
            .MaximumLength(20).WithMessage("RoomNumber must not exceed 20 characters")
            .When(x => x.RoomNumber.IsSet)
            .OverridePropertyName("RoomNumber");

        RuleFor(x => x.RoomCategory.Value)
            .IsInEnum().WithMessage("RoomCategory is invalid")
            .When(x => x.RoomCategory.IsSet)
            .OverridePropertyName("RoomCategory");

        RuleFor(x => x.RoomStatus.Value)
            .IsInEnum().WithMessage("RoomStatus is invalid")
            .When(x => x.RoomStatus.IsSet)
            .OverridePropertyName("RoomStatus");

        RuleFor(x => x.FloorNumber.Value)
            .GreaterThanOrEqualTo(0).WithMessage("FloorNumber must not be negative")
            .When(x => x.FloorNumber is { IsSet: true, Value: not null })
            .OverridePropertyName("FloorNumber");

        RuleFor(x => x.Area.Value)
            .GreaterThan(0).WithMessage("Area must be greater than 0")
            .PrecisionScale(10, 2, ignoreTrailingZeros: true)
            .WithMessage("Area must not exceed 10 digits in total, with at most 2 decimal places")
            .When(x => x.Area is { IsSet: true, Value: not null })
            .OverridePropertyName("Area");

        RuleFor(x => x.Capacity.Value)
            .GreaterThan(0).WithMessage("Capacity must be greater than 0")
            .When(x => x.Capacity is { IsSet: true, Value: not null })
            .OverridePropertyName("Capacity");

        RuleFor(x => x.MonthlyRent.Value)
            .GreaterThan(0).WithMessage("MonthlyRent must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("MonthlyRent must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.MonthlyRent is { IsSet: true, Value: not null })
            .OverridePropertyName("MonthlyRent");

        RuleFor(x => x.DepositAmount.Value)
            .GreaterThan(0).WithMessage("DepositAmount must be greater than 0")
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("DepositAmount must not exceed 18 digits in total, with at most 2 decimal places")
            .When(x => x.DepositAmount is { IsSet: true, Value: not null })
            .OverridePropertyName("DepositAmount");
    }
}
