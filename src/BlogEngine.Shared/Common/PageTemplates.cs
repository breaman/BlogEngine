namespace BlogEngine.Shared.Common;

/// <summary>
/// Starting points for standalone pages (design 6.7, 12.3, A17), offered by the admin pages list. The author edits the
/// result like any other page; nothing is created until it is saved.
/// </summary>
public static class PageTemplates
{
    /// <summary>A starting point for a page in the admin editor.</summary>
    /// <param name="Key">The value of the editor's <c>?template=</c> query parameter.</param>
    /// <param name="Title">The page title.</param>
    /// <param name="Slug">The suggested slug.</param>
    /// <param name="Summary">The meta description.</param>
    /// <param name="Markdown">The page content.</param>
    public sealed record Template(string Key, string Title, string Slug, string Summary, string Markdown);

    /// <summary>
    /// A privacy page describing what this blog engine actually does with visitors' data (design 12.3): no third-party
    /// analytics, commenters' IP addresses kept only as salted hashes, emails never shown. Bracketed text is for the
    /// author to fill in.
    /// </summary>
    public static Template Privacy { get; } = new(
        "privacy",
        "Privacy",
        "privacy",
        "What this site collects about visitors and commenters, and why.",
        """
        This page explains what this site collects when you read it or leave a comment. The short version: as little as
        possible, and nothing is sold or shared for advertising.

        ## Reading

        - There are **no third-party analytics, ads or tracking scripts** on this site.
        - Like any web server, the server may keep technical logs (such as request paths and times) to keep the site
          running and secure. [Say how long logs are kept.]
        - Your choice of light or dark theme is stored in your own browser (`localStorage`) and is never sent to the
          server.

        ## Comments

        When you comment, the site stores:

        - the **name**, **email address** and optional **website** you enter. Your name and website are shown with your
          comment; your email address is never shown.
        - the comment itself.
        - a **salted hash of your IP address** and your browser's user agent, only to fight spam and abuse. The IP
          address itself isn't stored.

        If you tick "Remember me", your name, email and website are saved in your own browser so the form is filled in
        next time. They aren't sent anywhere until you post another comment.

        [If avatars are enabled: commenter avatars come from Gravatar, which receives a hash of your email address when
        the page loads. See Gravatar's privacy policy.]

        Every comment is reviewed before it appears. You can ask for your comments to be removed at any time.

        ## Cookies

        Readers get no cookies. The only cookies are the sign-in cookies the site's author uses to manage the blog, and a
        security cookie on pages with a form, which protects the comment form from forgery.

        ## Contact

        To ask what is stored about you, or to have it removed, contact [your name] at [your email address].

        *Last updated: [date].*
        """);

    /// <summary>Every template, by <see cref="Template.Key"/>.</summary>
    public static IReadOnlyList<Template> All { get; } = [Privacy];

    /// <summary>The template with this key (case-insensitive), or <see langword="null"/>.</summary>
    public static Template? Find(string? key)
    {
        return All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}