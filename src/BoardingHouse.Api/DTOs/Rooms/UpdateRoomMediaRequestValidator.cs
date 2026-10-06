using BoardingHouse.Api.Entities;
using FluentValidation;

namespace BoardingHouse.Api.DTOs.Rooms;

public class UpdateRoomMediaRequestValidator : AbstractValidator<UpdateRoomMediaRequest>
{
    public UpdateRoomMediaRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.Order.IsSet || x.CoverMediaId.IsSet)
            .WithMessage("Order or CoverMediaId is required")
            .OverridePropertyName("Request");

        RuleFor(x => x.Order.Value)
            .NotNull().WithMessage("Order must not be null")
            .Must(order => order!.Count <= Room.MaxMedia)
            .WithMessage($"Order must not contain more than {Room.MaxMedia} ids")
            .Must(order => order!.Distinct().Count() == order!.Count)
            .WithMessage("Order must not contain duplicate ids")
            .When(x => x.Order.IsSet)
            .OverridePropertyName("Order");

        RuleFor(x => x.CoverMediaId.Value)
            .NotNull().WithMessage("CoverMediaId must not be null")
            .When(x => x.CoverMediaId.IsSet)
            .OverridePropertyName("CoverMediaId");
    }
}
