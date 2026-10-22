using FluentValidation;
using BoardingHouse.Application.Services.Storage;

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

    public static IRuleBuilderOptions<T, IEnumerable<IFormFile>> ValidImageFiles<T>(
        this IRuleBuilder<T, IEnumerable<IFormFile>?> ruleBuilder) =>
        ruleBuilder
            .Must(files => files is null || files.Count() <= MediaLimits.MaxFilesPerRequest)
            .WithMessage($"At most {MediaLimits.MaxFilesPerRequest} files can be uploaded per request")
            .ForEach(file => file.ValidMediaFile(MediaLimits.AllowedImageMimeTypes))!;
}
