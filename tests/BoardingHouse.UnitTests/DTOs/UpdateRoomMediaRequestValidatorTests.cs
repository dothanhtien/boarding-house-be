using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateRoomMediaRequestValidatorTests
{
    private readonly UpdateRoomMediaRequestValidator _validator = new();

    [Fact]
    public void Validate_OrderAndCover_IsValid()
    {
        var id = Guid.NewGuid();

        var result = _validator.Validate(new UpdateRoomMediaRequest { Order = new List<Guid> { id, Guid.NewGuid() }, CoverMediaId = id });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyOrder_IsValid()
    {
        var result = _validator.Validate(new UpdateRoomMediaRequest { Order = new List<Guid>() });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NothingSent_HasRequestError()
    {
        var result = _validator.Validate(new UpdateRoomMediaRequest());

        Assert.Contains(result.Errors, e => e.PropertyName == "Request" && e.ErrorMessage == "Order or CoverMediaId is required");
    }

    [Fact]
    public void Validate_OrderNull_HasOrderError()
    {
        var result = _validator.Validate(new UpdateRoomMediaRequest { Order = (List<Guid>?)null });

        Assert.Contains(result.Errors, e => e.PropertyName == "Order" && e.ErrorMessage == "Order must not be null");
    }

    [Fact]
    public void Validate_OrderWithDuplicates_HasOrderError()
    {
        var id = Guid.NewGuid();

        var result = _validator.Validate(new UpdateRoomMediaRequest { Order = new List<Guid> { id, id } });

        Assert.Contains(result.Errors, e => e.PropertyName == "Order" && e.ErrorMessage == "Order must not contain duplicate ids");
    }

    [Fact]
    public void Validate_OrderOverRoomLimit_HasOrderError()
    {
        var order = Enumerable.Range(0, Room.MaxMedia + 1).Select(_ => Guid.NewGuid()).ToList();

        var result = _validator.Validate(new UpdateRoomMediaRequest { Order = order });

        Assert.Contains(result.Errors, e => e.PropertyName == "Order");
    }

    [Fact]
    public void Validate_CoverNull_HasCoverError()
    {
        var result = _validator.Validate(new UpdateRoomMediaRequest { CoverMediaId = (Guid?)null });

        Assert.Contains(result.Errors, e => e.PropertyName == "CoverMediaId" && e.ErrorMessage == "CoverMediaId must not be null");
    }
}
