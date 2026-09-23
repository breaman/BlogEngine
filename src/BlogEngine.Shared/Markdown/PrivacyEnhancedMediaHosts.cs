using System.Diagnostics.CodeAnalysis;

using Markdig.Extensions.MediaLinks;

namespace BlogEngine.Shared.Markdown;

/// <summary>
/// Media-link settings that embed only YouTube and Vimeo, with YouTube served from the privacy-enhanced
/// <c>youtube-nocookie.com</c> domain (design 10.1).
/// </summary>
/// <remarks>
/// Markdig's built-in providers already understand every YouTube and Vimeo URL shape (watch, youtu.be,
/// embed, playlists, start times), so they are reused and only their output host is rewritten. The other
/// built-in hosts are dropped because the post sanitizer only allows YouTube and Vimeo iframes.
/// </remarks>
internal static class PrivacyEnhancedMediaHosts
{
    /// <summary>CSS class Markdig gives YouTube embeds; identifies the YouTube providers.</summary>
    private const string YouTubeClass = "youtube";

    /// <summary>CSS class Markdig gives Vimeo embeds; identifies the Vimeo provider.</summary>
    private const string VimeoClass = "vimeo";

    /// <summary>Creates media options restricted to YouTube (no-cookie) and Vimeo embeds.</summary>
    public static MediaOptions CreateOptions()
    {
        var options = new MediaOptions();
        var builtIn = options.Hosts.ToList();
        options.Hosts.Clear();

        foreach (var host in builtIn)
        {
            switch (host.Class)
            {
                case YouTubeClass:
                    options.Hosts.Add(new NoCookieYouTubeHostProvider(host));
                    break;
                case VimeoClass:
                    options.Hosts.Add(host);
                    break;
            }
        }

        return options;
    }

    /// <summary>Wraps a built-in YouTube provider and moves its embed URL to <c>youtube-nocookie.com</c>.</summary>
    private sealed class NoCookieYouTubeHostProvider(IHostProvider inner) : IHostProvider
    {
        private const string StandardHost = "www.youtube.com/embed";
        private const string NoCookieHost = "www.youtube-nocookie.com/embed";

        /// <inheritdoc />
        public string? Class => inner.Class;

        /// <inheritdoc />
        public bool AllowFullScreen => inner.AllowFullScreen;

        /// <inheritdoc />
        public bool TryHandle(Uri mediaUri, bool isSchemaRelative, [NotNullWhen(true)] out string? iframeUrl)
        {
            if (!inner.TryHandle(mediaUri, isSchemaRelative, out iframeUrl) || iframeUrl is null)
            {
                return false;
            }

            iframeUrl = iframeUrl.Replace(StandardHost, NoCookieHost, StringComparison.OrdinalIgnoreCase);
            return true;
        }
    }
}