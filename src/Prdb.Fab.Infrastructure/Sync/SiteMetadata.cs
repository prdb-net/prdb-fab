using System.Text.Json;
using Prdb.Fab.Infrastructure.Persistence;

namespace Prdb.Fab.Infrastructure.Sync;

public sealed record CatalogueLink(string? Label, string? Url);
public sealed record SiteMetadata(string? Network, string? Url, Guid? PlatformId, string? PlatformKey,
    string? PlatformTitle, string? AccountHandle, IReadOnlyList<CatalogueLink> Links)
{
    public static SiteMetadata Of(CatalogueSiteRow row) => new(row.Network, ExternalUrl(row.Url), row.PlatformId,
        row.PlatformKey, row.PlatformTitle, row.AccountHandle,
        (row.LinksJson is null ? [] : JsonSerializer.Deserialize<List<CatalogueLink>>(row.LinksJson) ?? [])
            .Where(link => ExternalUrl(link.Url) is not null)
            .Select(link => link with { Url = ExternalUrl(link.Url) }).ToArray());

    /// <summary>Only absolute web addresses without credentials or control characters reach anchors.</summary>
    public static string? ExternalUrl(string? value) => !string.IsNullOrWhiteSpace(value)
        && !value.Any(char.IsControl)
        && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
}
