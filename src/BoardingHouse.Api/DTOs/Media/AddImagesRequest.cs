namespace BoardingHouse.Api.DTOs.Media;

public record AddImagesRequest
{
    public List<IFormFile>? Files { get; init; }
}
