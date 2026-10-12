namespace BoardingHouse.Api.Common;

public interface IUpdateAuditable
{
    DateTimeOffset? UpdatedAt { get; set; }
    Guid? UpdatedBy { get; set; }
}
