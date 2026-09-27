namespace BoardingHouse.Api.Services.Storage;

public static class MediaSignatures
{
    public const int HeaderLength = 12;

    public static string? DetectMimeType(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (header.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        return null;
    }
}
