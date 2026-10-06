namespace BoardingHouse.Api.DTOs.Rooms;

public record AddRoomMediaRequest
{
    public List<IFormFile>? Files { get; init; }
}
