using System.Net.Http.Headers;

using BlogEngine.Server.Services.Media;
using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Security;

using Microsoft.Extensions.DependencyInjection;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Fixture images and upload helpers for the media tests. Images are generated with ImageSharp: the left half red,
/// the right half blue, so rotations are visible in the pixels.
/// </summary>
public static class MediaTestFiles
{
    /// <summary>Path of the admin upload endpoint.</summary>
    public const string MediaApi = "/api/admin/media";

    /// <summary>A PNG of the given size.</summary>
    public static byte[] Png(int width = 40, int height = 20)
    {
        return Encode(width, height, new PngEncoder());
    }

    /// <summary>
    /// A JPEG as phones produce it: stored sideways with EXIF orientation 6 and a GPS location, which the upload
    /// pipeline must apply and remove.
    /// </summary>
    public static byte[] SidewaysJpegWithGps(int storedWidth = 400, int storedHeight = 200)
    {
        return Encode(storedWidth, storedHeight, new JpegEncoder { Quality = 95 }, image =>
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            exif.SetValue(ExifTag.GPSLatitude, [new Rational(47, 1), new Rational(36, 1), new Rational(3, 1)]);
            image.Metadata.ExifProfile = exif;
        });
    }

    /// <summary>Adds a file straight through <see cref="ServerMediaService"/>, as test setup.</summary>
    public static async Task<MediaItemDto> AddAsync(BlogEngineWebApplicationFactory factory, string fileName, byte[]? bytes = null)
    {
        bytes ??= Png();
        await using var scope = factory.Services.CreateAsyncScope();
        var media = scope.ServiceProvider.GetRequiredService<ServerMediaService>();
        using var content = new MemoryStream(bytes);

        var result = await media.UploadAsync(fileName, content, bytes.Length, CancellationToken.None);
        return result.Item ?? throw new InvalidOperationException($"Uploading {fileName} failed: {result.Error}");
    }

    /// <summary>Posts files to the upload endpoint as a browser form would.</summary>
    public static Task<HttpResponseMessage> UploadAsync(HttpClient client, string? antiforgeryToken, params (string Name, byte[] Bytes)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (name, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(part, "files", name);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, MediaApi) { Content = form };
        if (antiforgeryToken is not null)
        {
            request.Headers.Add(AntiforgeryHeaders.RequestToken, antiforgeryToken);
        }

        return client.SendAsync(request);
    }

    /// <summary>Reads a stored object's bytes, or null when it doesn't exist.</summary>
    public static async Task<byte[]?> ReadStoredAsync(BlogEngineWebApplicationFactory factory, string key)
    {
        var storage = factory.Services.GetRequiredService<BlogEngine.Server.Storage.IMediaStorage>();
        await using var stream = await storage.OpenReadAsync(key, CancellationToken.None);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    /// <summary>A short unique token for file names and titles.</summary>
    public static string Token()
    {
        return Guid.NewGuid().ToString("N")[..10];
    }

    private static byte[] Encode(int width, int height, IImageEncoder encoder, Action<Image>? configure = null)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = x < width / 2 ? Color.Red : Color.Blue;
            }
        }

        configure?.Invoke(image);
        using var output = new MemoryStream();
        image.Save(output, encoder);
        return output.ToArray();
    }
}