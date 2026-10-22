using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;

using Microsoft.AspNetCore.Hosting;

using Xunit;

namespace Prdb.Fab.Host.Tests;

public sealed class FrontendFilesTests
{
    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    public async Task Public_assets_are_compressed_and_only_hashed_files_are_immutable(string encoding)
    {
        var root = Path.Combine(Path.GetTempPath(), "prdb-fab-static", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        var script = string.Concat(Enumerable.Repeat("console.log('frontend');\n", 1000));
        await File.WriteAllTextAsync(Path.Combine(root, "assets", "index-Abcd1234.js"), script, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "assets", "plain.js"), script, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<html>first release</html>", TestContext.Current.CancellationToken);
        try
        {
            using var application = new FabApplication();
            using var host = application.WithWebHostBuilder(builder => builder.UseEnvironment("Production").UseWebRoot(root));
            using var client = host.CreateClient();
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd(encoding);
            using var asset = await client.GetAsync("/assets/index-Abcd1234.js", TestContext.Current.CancellationToken);
            asset.EnsureSuccessStatusCode();
            Assert.Contains(encoding, asset.Content.Headers.ContentEncoding);
            Assert.True(asset.Headers.CacheControl?.Public);
            Assert.Equal(TimeSpan.FromDays(365), asset.Headers.CacheControl?.MaxAge);
            Assert.Contains(asset.Headers.CacheControl!.Extensions, value => value.Name == "immutable");
            Assert.Contains("Accept-Encoding", asset.Headers.Vary);
            var bytes = await asset.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
            Assert.True(bytes.Length < Encoding.UTF8.GetByteCount(script) / 2);
            using var input = new MemoryStream(bytes);
            using Stream decoded = encoding == "br" ? new BrotliStream(input, CompressionMode.Decompress) : new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(decoded);
            Assert.Equal(script, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));

            foreach (var path in new[] { "/", "/index.html", "/settings/account", "/assets/plain.js" })
            {
                using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
                response.EnsureSuccessStatusCode();
                Assert.True(response.Headers.CacheControl?.NoCache);
            }

            // A revalidated entry document points at the current release.
            await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<html>second release with changed assets</html>", TestContext.Current.CancellationToken);
            client.DefaultRequestHeaders.AcceptEncoding.Clear();
            Assert.Contains("second release", await client.GetStringAsync("/settings/account", TestContext.Current.CancellationToken));

            using var api = await client.GetAsync("/api/access/state", TestContext.Current.CancellationToken);
            Assert.Empty(api.Content.Headers.ContentEncoding);
            Assert.Null(api.Headers.CacheControl);
            using var backup = await client.PostAsync("/api/backup/export", null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, backup.StatusCode);
            Assert.Empty(backup.Content.Headers.ContentEncoding);
            using var password = await client.PostAsJsonAsync("/api/access/password", new { password = "a long enough password" }, TestContext.Current.CancellationToken);
            password.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd(encoding);
            using var export = await client.PostAsync("/api/backup/export", null, TestContext.Current.CancellationToken);
            export.EnsureSuccessStatusCode();
            Assert.True(export.Headers.CacheControl?.NoStore);
            Assert.Empty(export.Content.Headers.ContentEncoding);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
