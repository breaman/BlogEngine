using BlogEngine.Shared.Contracts;

namespace BlogEngine.Client.Services;

/// <summary>
/// Compares the author-editable fields of two <see cref="PostEditDto"/> values, for the editor's
/// "unsaved changes" and "not live yet" states (design 10.2, Q3).
/// </summary>
/// <remarks>
/// A blank slug or summary means "generate it", so <see langword="null"/> and empty compare equal. Tags compare
/// in order, because the editor shows them in order.
/// </remarks>
public static class PostEdits
{
    /// <summary>Whether every editable field is the same.</summary>
    public static bool AreEqual(PostEditDto left, PostEditDto right)
    {
        return ContentEquals(left, right) && DetailsEqual(left, right);
    }

    /// <summary>Whether the title and Markdown are the same: the fields an autosave of a published post stages.</summary>
    public static bool ContentEquals(PostEditDto left, PostEditDto right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return left.Title == right.Title && left.ContentMarkdown == right.ContentMarkdown;
    }

    /// <summary>
    /// Whether everything except the title and Markdown is the same. On a published post these only change on
    /// <b>Update</b>, because autosave stages the content alone.
    /// </summary>
    public static bool DetailsEqual(PostEditDto left, PostEditDto right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return TextEquals(left.Slug, right.Slug)
            && TextEquals(left.Summary, right.Summary)
            && TextEquals(left.MetaTitle, right.MetaTitle)
            && TextEquals(left.MetaDescription, right.MetaDescription)
            && left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal)
            && left.AllowComments == right.AllowComments
            && left.IsFeatured == right.IsFeatured;
    }

    private static bool TextEquals(string? left, string? right)
    {
        return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
    }
}
