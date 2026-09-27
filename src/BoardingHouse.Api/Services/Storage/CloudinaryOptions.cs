using System.ComponentModel.DataAnnotations;

namespace BoardingHouse.Api.Services.Storage;

public class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    [Required]
    public string CloudName { get; init; } = "";

    [Required]
    public string ApiKey { get; init; } = "";

    [Required]
    public string ApiSecret { get; init; } = "";

    [Required]
    public string RootFolder { get; init; } = "boarding-house";
}
