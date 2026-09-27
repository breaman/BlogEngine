using System.Text;

using BlogEngine.Shared.Common;

namespace BlogEngine.Shared.Markdown;

/// <summary>How wide a library image is shown in a post (design 9.5).</summary>
public enum MediaImageSize
{
    /// <summary>Its natural width, up to the width of the post.</summary>
    Full,

    /// <summary>About two thirds of the post width (<c>.img-medium</c>).</summary>
    Medium,

    /// <summary>About a third of the post width (<c>.img-small</c>).</summary>
    Small
}

/// <summary>
/// Builds the standard Markdown the media picker inserts (design 9.5, T2.11), so posts stay portable:
/// <c>![alt](/media/{publicId}/{fileName} "caption"){.img-medium .img-center}</c>.
/// </summary>
public static class MediaMarkdown
{
    /// <summary>Size hint class for <see cref="MediaImageSize.Medium"/>.</summary>
    public const string MediumClass = "img-medium";

    /// <summary>Size hint class for <see cref="MediaImageSize.Small"/>.</summary>
    public const string SmallClass = "img-small";

    /// <summary>Alignment class that centers an image.</summary>
    public const string CenterClass = "img-center";

    /// <summary>Builds the Markdown for one image.</summary>
    /// <param name="publicId">The item's public id.</param>
    /// <param name="fileName">The item's file name.</param>
    /// <param name="altText">Alt text; brackets and backslashes are escaped.</param>
    /// <param name="caption">Optional caption, written as the link title.</param>
    /// <param name="size">Size hint.</param>
    /// <param name="center">Whether to center the image.</param>
    /// <example>
    /// <code>
    /// MediaMarkdown.Image("ab12cd34ef56", "sunset.jpg", "Sunset", "At dusk", MediaImageSize.Medium, center: true);
    /// // ![Sunset](/media/ab12cd34ef56/sunset.jpg "At dusk"){.img-medium .img-center}
    /// </code>
    /// </example>
    public static string Image(string publicId, string fileName, string? altText, string? caption,
        MediaImageSize size = MediaImageSize.Full, bool center = false)
    {
        var markdown = new StringBuilder("![")
            .Append(Escape(Collapse(altText), "\\[]"))
            .Append("](")
            .Append(MediaPaths.Item(publicId, fileName));

        if (!string.IsNullOrWhiteSpace(caption))
        {
            markdown.Append(" \"").Append(Escape(Collapse(caption), "\\\"")).Append('"');
        }

        markdown.Append(')');

        var classes = new List<string>();
        switch (size)
        {
            case MediaImageSize.Medium:
                classes.Add(MediumClass);
                break;
            case MediaImageSize.Small:
                classes.Add(SmallClass);
                break;
        }

        if (center)
        {
            classes.Add(CenterClass);
        }

        if (classes.Count > 0)
        {
            markdown.Append('{').AppendJoin(' ', classes.Select(c => "." + c)).Append('}');
        }

        return markdown.ToString();
    }

    /// <summary>Line breaks would end the image syntax, so text is collapsed to one line.</summary>
    private static string Collapse(string? text)
    {
        return string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Escape(string text, string characters)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (characters.Contains(c))
            {
                escaped.Append('\\');
            }

            escaped.Append(c);
        }

        return escaped.ToString();
    }
}