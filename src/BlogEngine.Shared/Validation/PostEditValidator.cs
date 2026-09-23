using System.ComponentModel.DataAnnotations;

using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Text;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Validates a <see cref="PostEditDto"/> the same way in the editor (WebAssembly) and on the server.
/// </summary>
/// <remarks>
/// Runs the data annotations on the DTO (title required, lengths, slug format) plus the tag rules from
/// <see cref="TagNormalizer"/>, which annotations can't express. The server always re-validates; the
/// client runs it only to show errors early.
/// </remarks>
/// <example>
/// <code>
/// var errors = PostEditValidator.Validate(post);
/// if (errors.Count &gt; 0)
/// {
///     return new PostInvalid(errors);
/// }
/// </code>
/// </example>
public static class PostEditValidator
{
    /// <summary>Most tags a single post may have; keeps the tag list meaningful and the save bounded.</summary>
    public const int MaxTags = 20;

    /// <summary>Validates the post.</summary>
    /// <returns>Error messages keyed by property name; empty when the post is valid.</returns>
    public static IReadOnlyDictionary<string, string[]> Validate(PostEditDto post)
    {
        ArgumentNullException.ThrowIfNull(post);

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(post, new ValidationContext(post), results, validateAllProperties: true);

        var errors = results
            .SelectMany(r => r.MemberNames.DefaultIfEmpty(string.Empty), (r, member) => (member, r.ErrorMessage ?? "Invalid value."))
            .GroupBy(e => e.member, e => e.Item2)
            .ToDictionary(g => g.Key, g => g.ToList());

        var tagErrors = ValidateTags(post.Tags);
        if (tagErrors.Count > 0)
        {
            errors[nameof(PostEditDto.Tags)] = tagErrors;
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    /// <summary>Checks every tag name and the number of distinct tags.</summary>
    private static List<string> ValidateTags(IReadOnlyCollection<string>? tags)
    {
        var errors = new List<string>();
        if (tags is null)
        {
            return errors;
        }

        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            var result = TagNormalizer.Normalize(tag);
            if (!result.IsValid)
            {
                errors.Add(string.IsNullOrEmpty(result.Name) ? result.Error! : $"'{result.Name}': {result.Error}");
                continue;
            }

            distinct.Add(result.NormalizedName);
        }

        if (distinct.Count > MaxTags)
        {
            errors.Add($"A post can have at most {MaxTags} tags.");
        }

        return errors;
    }
}
