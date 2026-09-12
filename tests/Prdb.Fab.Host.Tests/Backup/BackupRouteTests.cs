using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    /// <summary>
    /// The whole loop over HTTP, with nothing outside this process involved:
    /// one installation writes a file, a second one takes it before it has a
    /// password, and the credential that comes back is the one that signs in.
    /// </summary>
    /// <remarks>
    /// The public CI has to be able to run exactly this without external
    /// services, which is why it is at the Host level and deliberately thin on
    /// data — the rich round trip over all fifteen tables is in the
    /// Infrastructure suite, where an installation can be populated without
    /// standing up a prdb and four indexers first.
    /// </remarks>
    [Fact]
    public async Task A_backup_carries_an_installation_from_one_container_to_another()
    {
        const string password = "the password from the file";

        byte[] file;

        using (var written = new FabApplication())
        using (var client = await written.SignedInClientAsync(password))
        using (var export = await client.PostAsync(
            "/api/backup/export", content: null, TestContext.Current.CancellationToken))
        {
            export.EnsureSuccessStatusCode();
            file = await export.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        }

        using var fresh = new FabApplication();
        using var anonymous = fresh.CreateClient();

        // The first call: read the file and say what it needs. Nothing is
        // written, so the window is still open afterwards.
        using (var read = await anonymous.PostAsJsonAsync(
            "/api/backup/restore",
            new { document = Encoding.UTF8.GetString(file), roots = (object?)null },
            TestContext.Current.CancellationToken))
        {
            read.EnsureSuccessStatusCode();

            var verdict = await read.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken);

            Assert.Equal("RootsNeeded", verdict?.Outcome);
        }

        using (var restored = await anonymous.PostAsJsonAsync(
            "/api/backup/restore",
            new { document = Encoding.UTF8.GetString(file), roots = new { library = (string?)null, downloads = (string?)null } },
            TestContext.Current.CancellationToken))
        {
            restored.EnsureSuccessStatusCode();

            var verdict = await restored.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken);

            Assert.Equal("Restored", verdict?.Outcome);
        }

        // ADR 0010: the restored credential closes both unauthenticated writes,
        // and a restored installation ends on the sign-in screen.
        using (var state = await anonymous.GetAsync("/api/access/state", TestContext.Current.CancellationToken))
        {
            var answer = await state.Content.ReadFromJsonAsync<State>(TestContext.Current.CancellationToken);

            Assert.True(answer?.PasswordSet);
            Assert.False(answer?.SignedIn);
        }

        using (var signIn = await anonymous.PostAsJsonAsync(
            "/api/access/sign-in", new { password }, TestContext.Current.CancellationToken))
        {
            var answer = await signIn.Content.ReadFromJsonAsync<Verdict>(TestContext.Current.CancellationToken);

            Assert.Equal("SignedIn", answer?.Outcome);
        }
    }

    private sealed record Verdict(string Outcome);

    private sealed record State(bool PasswordSet, bool SignedIn);
}
