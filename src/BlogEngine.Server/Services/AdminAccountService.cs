using BlogEngine.Data.Models;
using BlogEngine.Shared.Security;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Server.Services;

/// <summary>
/// Creates the blog's single admin account on first run (design 12.1, O5). Used by the <c>/setup</c> page
/// and by <see cref="AdminSeeder"/>; both are only allowed while no user accounts exist at all.
/// </summary>
public sealed class AdminAccountService(
    UserManager<User> userManager,
    RoleManager<Role> roleManager,
    TimeProvider timeProvider,
    ILogger<AdminAccountService> logger)
{
    /// <summary>Error code returned when an account already exists, so setup must not run again.</summary>
    public const string AlreadySetUpErrorCode = "AdminAlreadyExists";

    // Serializes creation within this process so two simultaneous /setup posts can't both pass the
    // "no users yet" check. The blog runs as a single instance, so an in-process lock is sufficient.
    private static readonly SemaphoreSlim CreateLock = new(1, 1);

    /// <summary>Whether any user account exists; <c>/setup</c> is only available while this is false.</summary>
    public Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken = default)
    {
        return userManager.Users.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the admin account with a confirmed email (there is nobody to confirm it) and assigns the
    /// <see cref="AppRoles.Admin"/> role, creating the role if needed.
    /// </summary>
    /// <returns>
    /// The created user on success. Fails with <see cref="AlreadySetUpErrorCode"/> when any user already
    /// exists, or with Identity's errors when the email or password is rejected.
    /// </returns>
    public async Task<(IdentityResult Result, User? User)> CreateInitialAdminAsync(
        string email, string password, string displayName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        await CreateLock.WaitAsync(cancellationToken);
        try
        {
            if (await AnyUsersExistAsync(cancellationToken))
            {
                return (IdentityResult.Failed(new IdentityError
                {
                    Code = AlreadySetUpErrorCode,
                    Description = "The blog has already been set up."
                }), null);
            }

            var roleResult = await EnsureAdminRoleAsync();
            if (!roleResult.Succeeded)
            {
                return (roleResult, null);
            }

            email = email.Trim();
            var user = new User
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = displayName.Trim(),
                MemberSince = timeProvider.GetUtcNow()
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                return (result, null);
            }

            result = await userManager.AddToRoleAsync(user, AppRoles.Admin);
            if (!result.Succeeded)
            {
                // Don't leave a role-less account behind: it would block /setup without being able to administer anything.
                await userManager.DeleteAsync(user);
                return (result, null);
            }

            logger.LogInformation("Created the admin account {AdminUserId}.", user.Id);
            return (IdentityResult.Success, user);
        }
        finally
        {
            CreateLock.Release();
        }
    }

    /// <summary>Creates the <see cref="AppRoles.Admin"/> role unless it already exists.</summary>
    private async Task<IdentityResult> EnsureAdminRoleAsync()
    {
        if (await roleManager.RoleExistsAsync(AppRoles.Admin))
        {
            return IdentityResult.Success;
        }

        return await roleManager.CreateAsync(new Role { Name = AppRoles.Admin });
    }
}