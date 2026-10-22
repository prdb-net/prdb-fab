using System.Text.RegularExpressions;

namespace Prdb.Fab.Host.Frontend;

internal static partial class FrontendFiles
{
    // Vite gives a changed asset a new name. The entry document must always be
    // revalidated, including when it is served for a browser deep link.
    public static StaticFileOptions Options => new()
    {
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
            context.Context.Request.Path.StartsWithSegments("/assets")
                && HasContentHash().IsMatch(context.File.Name)
                ? "public, max-age=31536000, immutable"
                : "no-cache",
    };

    [GeneratedRegex(@"-[A-Za-z0-9_-]{8,}\.(?:js|css|woff2?|png|jpe?g|svg|webp|avif)$", RegexOptions.CultureInvariant)]
    private static partial Regex HasContentHash();
}
