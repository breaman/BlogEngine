using Microsoft.Extensions.Options;

namespace BlogEngine.Server.Services;

/// <summary>
/// Creates the admin account at startup from <see cref="AdminSeedOptions"/> when no user exists yet, as an
/// alternative to the <c>/setup</c> page (design 12.1). Does nothing, and never touches the database,
/// unless both an email and a password are configured.
/// </summary>
public sealed class AdminSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<AdminSeedOptions> options,
    ILogger<AdminSeeder> logger) : IHostedService
{
    private const string DefaultDisplayName = "Admin";

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.Email) || string.IsNullOrWhiteSpace(seed.Password))
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var accounts = scope.ServiceProvider.GetRequiredService<AdminAccountService>();

            if (await accounts.AnyUsersExistAsync(cancellationToken))
            {
                logger.LogDebug("Skipping admin seeding because user accounts already exist.");
                return;
            }

            var displayName = string.IsNullOrWhiteSpace(seed.DisplayName) ? DefaultDisplayName : seed.DisplayName;
            var (result, _) = await accounts.CreateInitialAdminAsync(seed.Email, seed.Password, displayName, cancellationToken);
            if (!result.Succeeded)
            {
                logger.LogError("Could not seed the admin account: {Errors}",
                    string.Join(" ", result.Errors.Select(e => e.Description)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A seeding failure shouldn't take the whole site down; /setup remains available as a fallback.
            logger.LogError(ex, "Admin seeding failed.");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
