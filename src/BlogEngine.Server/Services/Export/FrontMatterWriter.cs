using System.Globalization;
using System.Text;

namespace BlogEngine.Server.Services.Export;

/// <summary>
/// Builds a Markdown file with YAML front matter in the Hugo and Jekyll style (design 17), for the export zip.
/// </summary>
/// <remarks>
/// <para>
/// Only the few YAML shapes front matter needs are written: strings, booleans, integers, timestamps and lists of strings.
/// Every string is double-quoted and escaped, so a title such as <c>C#: "tips" &amp; tricks</c>, one starting with a YAML
/// indicator (<c>-</c>, <c>*</c>, <c>[</c>), or one that looks like another type (<c>yes</c>, <c>2026</c>) always reads
/// back as the same string. A hand-written emitter avoids a YAML dependency for what is a handful of lines.
/// </para>
/// <para>
/// Keys are written in the order they are added. <see langword="null"/> values are left out, as Hugo and Jekyll treat a
/// missing key as unset.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var markdown = new FrontMatterWriter()
///     .Add("title", "Hello")
///     .Add("tags", ["C#", ".NET"])
///     .Add("draft", false)
///     .ToDocument("Post body…");
/// // ---
/// // title: "Hello"
/// // tags:
/// //   - "C#"
/// //   - ".NET"
/// // draft: false
/// // ---
/// //
/// // Post body…
/// </code>
/// </example>
public sealed class FrontMatterWriter
{
    private const string Fence = "---";

    private readonly StringBuilder _yaml = new();

    /// <summary>Adds a string, double-quoted; skipped when <see langword="null"/>.</summary>
    public FrontMatterWriter Add(string key, string? value)
    {
        return value is null ? this : AddLine(key, Quote(value));
    }

    /// <summary>Adds a boolean as <c>true</c> or <c>false</c>.</summary>
    public FrontMatterWriter Add(string key, bool value)
    {
        return AddLine(key, value ? "true" : "false");
    }

    /// <summary>Adds an integer.</summary>
    public FrontMatterWriter Add(string key, int value)
    {
        return AddLine(key, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Adds an ISO 8601 timestamp with its offset (<c>2026-09-22T14:30:00+00:00</c>), which YAML, Hugo and Jekyll read as
    /// a date; skipped when <see langword="null"/>.
    /// </summary>
    public FrontMatterWriter Add(string key, DateTimeOffset? value)
    {
        return value is { } date
            ? AddLine(key, date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture))
            : this;
    }

    /// <summary>Adds a list of strings as a block sequence, or <c>[]</c> when it is empty.</summary>
    public FrontMatterWriter Add(string key, IReadOnlyCollection<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            return AddLine(key, "[]");
        }

        _yaml.Append(key).Append(":\n");
        foreach (var value in values)
        {
            _yaml.Append("  - ").Append(Quote(value)).Append('\n');
        }

        return this;
    }

    /// <summary>The finished file: the front matter between <c>---</c> fences, a blank line, then <paramref name="body"/>.</summary>
    public string ToDocument(string body)
    {
        var document = new StringBuilder(_yaml.Length + body.Length + 16)
            .Append(Fence).Append('\n')
            .Append(_yaml)
            .Append(Fence).Append('\n');

        if (body.Length > 0)
        {
            document.Append('\n').Append(body.ReplaceLineEndings("\n"));
            if (!body.EndsWith('\n'))
            {
                document.Append('\n');
            }
        }

        return document.ToString();
    }

    /// <summary>
    /// A YAML double-quoted scalar: backslashes and quotes are escaped, and so is every character YAML would otherwise
    /// fold or reject (control characters, and the Unicode line and paragraph separators).
    /// </summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var quoted = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            _ = c switch
            {
                '\\' => quoted.Append(@"\\"),
                '"' => quoted.Append("\\\""),
                '\n' => quoted.Append(@"\n"),
                '\r' => quoted.Append(@"\r"),
                '\t' => quoted.Append(@"\t"),
                _ when char.IsControl(c) || c is (char)0x2028 or (char)0x2029 =>
                    quoted.Append(@"\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)),
                _ => quoted.Append(c)
            };
        }

        return quoted.Append('"').ToString();
    }

    private FrontMatterWriter AddLine(string key, string value)
    {
        _yaml.Append(key).Append(": ").Append(value).Append('\n');
        return this;
    }
}
