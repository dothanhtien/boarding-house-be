using BoardingHouse.Api.Services.Storage;
using FluentValidation;

namespace BoardingHouse.Api.DTOs.Media;

public static class MediaFileRuleExtensions
{
    public static IRuleBuilderOptions<T, TFile> ValidMediaFile<T, TFile>(
        this IRuleBuilder<T, TFile> ruleBuilder,
        IReadOnlySet<string> allowedMimeTypes) where TFile : IFormFile? =>
        ruleBuilder
            .Must(f => f!.Length > 0).WithMessage("File must not be empty")
            .Must(f => f!.Length <= MediaLimits.MaxFileSizeBytes)
            .WithMessage($"File must not exceed {MediaLimits.MaxFileSizeBytes / 1024 / 1024} MB")
            .Must(f => allowedMimeTypes.Contains(f!.ContentType))
            .WithMessage($"File type must be one of: {string.Join(", ", allowedMimeTypes)}")
            .Must(f => Path.GetFileName(f!.FileName).Length <= MediaLimits.MaxFileNameLength)
            .WithMessage($"File name must not exceed {MediaLimits.MaxFileNameLength} characters");
}
