using BlogEngine.Shared.Contracts;
using BlogEngine.Shared.Services;

using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.IntegrationTests.Infrastructure;

/// <summary>
/// Changes the site settings for one test and puts them back afterwards. Tests using it must run with
/// <c>[NotInParallel(TestConstraints.SiteSettings)]</c>, since the settings row is shared.
/// </summary>
public static class SettingsTestData
{
    /// <summary>Applies <paramref name="change"/> to the settings; disposing the result restores the previous values.</summary>
    /// <example>
    /// <code>
    /// await using var _ = await SettingsTestData.ChangeAsync(factory, s => s.CloseCommentsAfterDays = 7);
    /// </code>
    /// </example>
    public static async Task<IAsyncDisposable> ChangeAsync(BlogEngineWebApplicationFactory factory, Action<SiteSettingsDto> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var original = await GetAsync(factory);
        var changed = await GetAsync(factory);
        change(changed);
        await SaveAsync(factory, changed);

        return new Restore(factory, original);
    }

    private static async Task<SiteSettingsDto> GetAsync(BlogEngineWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync();
    }

    private static async Task SaveAsync(BlogEngineWebApplicationFactory factory, SiteSettingsDto settings)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsService>().SaveAsync(settings);
    }

    /// <summary>Saves the settings as they were.</summary>
    private sealed class Restore(BlogEngineWebApplicationFactory factory, SiteSettingsDto original) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await SaveAsync(factory, original);
        }
    }
}
