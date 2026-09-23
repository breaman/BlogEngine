using BlogEngine.Shared.Contracts;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Shared.Validation;

/// <summary>
/// Registers the FluentValidation validators shared by the server and the WebAssembly client.
/// </summary>
/// <remarks>
/// Validators are registered one by one instead of by assembly scanning: the WebAssembly build is trimmed,
/// and a validator found only through reflection could be removed. They are stateless, so singletons.
/// Blazilla's <c>&lt;FluentValidator /&gt;</c> resolves them as <see cref="IValidator{T}"/>.
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddBlogValidators();
/// </code>
/// </example>
public static class ValidationServiceCollectionExtensions
{
    /// <summary>Adds every shared validator as <see cref="IValidator{T}"/>.</summary>
    public static IServiceCollection AddBlogValidators(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<PostEditDto>, PostEditValidator>();
        services.AddSingleton<IValidator<SiteSettingsDto>, SiteSettingsValidator>();
        services.AddSingleton<IValidator<SocialLinkDto>, SocialLinkValidator>();

        return services;
    }
}
