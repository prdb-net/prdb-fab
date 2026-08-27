using Microsoft.Extensions.DependencyInjection;

using Prdb.Fab.Core.Access;

namespace Prdb.Fab.Infrastructure.Access;

public static class AccessServiceCollectionExtensions
{
    /// <summary>
    /// ADR 0010's password and sessions. The host adds the cookie and the
    /// endpoints on top of these.
    /// </summary>
    public static IServiceCollection AddFabAccess(this IServiceCollection services)
    {
        services.AddScoped<Installations>();
        services.AddScoped<PasswordGate>();
        services.AddScoped<Sessions>();

        // A singleton, because it counts for the installation rather than per
        // caller and per request — see SignInThrottle for why that is the point
        // rather than a shortcut.
        services.AddSingleton<SignInThrottle>();

        return services;
    }

    /// <summary>
    /// ADR 0010's recovery path: <c>FAB_RESET_PASSWORD=true</c> clears the
    /// password and every session, logs loudly that the variable should now be
    /// removed, and drops the installation back into "set a password".
    /// </summary>
    /// <remarks>
    /// Run at startup, after the migrations and before anything is served.
    /// Nothing else is touched — losing the password costs the password, and
    /// ADR 0037 leaned on exactly that when it refused to derive an encryption
    /// key from it.
    /// </remarks>
    public static async Task ResetPasswordIfAskedAsync(
        this IServiceProvider services,
        bool asked,
        CancellationToken cancellationToken = default)
    {
        if (!asked)
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<PasswordGate>().ClearAsync(cancellationToken);
    }
}
