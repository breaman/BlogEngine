using BlogEngine.Shared.Contracts;

namespace BlogEngine.Shared.Services;

/// <summary>
/// Reads and saves the blog-wide settings (design 13).
/// </summary>
/// <remarks>
/// The server implementation reads the single <c>SiteSettings</c> row through a cache and evicts it on
/// save, so settings are cheap to read on every request.
/// </remarks>
/// <example>
/// <code>
/// var settings = await settingsService.GetAsync(cancellationToken);
/// settings.PostsPerPage = 20;
/// await settingsService.SaveAsync(settings, cancellationToken);
/// </code>
/// </example>
public interface ISettingsService
{
    /// <summary>
    /// Gets the current settings. Each call returns a new instance, so callers may modify it freely.
    /// </summary>
    Task<SiteSettingsDto> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates and saves the settings, making them visible to subsequent <see cref="GetAsync"/> calls.
    /// </summary>
    /// <exception cref="FluentValidation.ValidationException">
    /// The settings are invalid, for example an unknown time zone.
    /// </exception>
    Task SaveAsync(SiteSettingsDto settings, CancellationToken cancellationToken = default);
}
