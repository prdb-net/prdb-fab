using System.Net;
using System.Text.Json;

using Prdb.Fab.Core.Backup;

using Xunit;

namespace Prdb.Fab.Host.Tests.Backup;

/// <summary>
/// ADR 0009's export as the browser meets it: an authenticated named act that
/// hands back a file, and ADR 0057's file — readable throughout.
/// </summary>
public sealed class BackupRouteTests
{
    /// <summary>
    /// ADR 0010's fallback policy covers this, and the point of asserting it is
    /// that the one endpoint handing out every credential must never be the one
    /// somebody exempts. <c>AnonymousSurfaceTests</c> guards the same thing from
    /// the other end, over the whole routing table.
    /// </summary>
    [Fact]
    public async Task The_export_is_behind_the_password()
    {
        using var application = new FabApplication();
        using var client = application.CreateClient();

        using var response = await client.PostAsync(
            "/api/backup/export", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The whole of what the person gets: a named JSON attachment, a document
    /// that parses, and nothing that may be kept.
    /// </summary>
    [Fact]
    public async Task A_signed_in_caller_is_handed_a_named_json_attachment_that_may_not_be_cached()
    {
        using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        using var response = await client.PostAsync(
            "/api/backup/export", content: null, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        Assert.Equal(BackupFile.MediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);

        var name = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

        Assert.NotNull(name);
        Assert.StartsWith("prdb-fab-backup-", name, StringComparison.Ordinal);
        Assert.EndsWith(".json", name, StringComparison.Ordinal);

        // ADR 0057: the body is readable credentials, so the one thing it must
        // not do is settle anywhere it was not asked to.
        Assert.True(response.Headers.CacheControl?.NoStore);

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var document = BackupJson.Read(text);

        Assert.NotNull(document);
        Assert.Equal(BackupFormat.Version, document.FormatVersion);
        Assert.False(string.IsNullOrWhiteSpace(document.ToolVersion));

        // The name and the envelope agree, which is what makes a directory of
        // these readable as a history.
        Assert.Equal(BackupFile.Name(document.WrittenAt), name);
    }

    /// <summary>
    /// ADR 0057, asserted rather than documented. The secret fields are in the
    /// file as they are stored, and a future build that quietly starts wrapping
    /// them fails here — which is the direction this has to be checked in, since
    /// the documentation now promises a file a person can read.
    /// </summary>
    [Fact]
    public async Task The_credential_in_the_file_is_the_credential_as_it_is_stored()
    {
        using var application = new FabApplication();
        using var client = await application.SignedInClientAsync();

        using var response = await client.PostAsync(
            "/api/backup/export", content: null, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var installation = JsonDocument.Parse(text).RootElement.GetProperty("installation");
        var hash = installation.GetProperty("passwordHash");

        // A string, not an object: an encrypted field would need a nonce beside
        // it, so the shape is what says nothing has been wrapped.
        Assert.Equal(JsonValueKind.String, hash.ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(hash.GetString()));

        // And it is the stored form, which `PasswordHasher<T>` writes as base64
        // beginning with its own version byte.
        Assert.StartsWith("AQAAAA", hash.GetString(), StringComparison.Ordinal);
    }
}
