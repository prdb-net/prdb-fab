using System.Net;
using System.Net.Http.Json;

using Xunit;

namespace Prdb.Fab.Host.Tests.Catalogue;

/// <summary>
/// The wanted list as a route: where setting up now ends, and read-only.
/// </summary>
public sealed class WantedRouteTests
{
    /// <summary>
    /// A fresh installation, before prdb has been asked anything. The list is
    /// empty and says which kind of empty it is — a page that has not arrived
    /// rather than an account with nothing on it.
    /// </summary>
    [Fact]
    public async Task A_fresh_installation_says_its_list_has_not_been_read_yet()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        using var answer = await client.GetAsync(
            "/api/catalogue/wanted",
            TestContext.Current.CancellationToken);

        answer.EnsureSuccessStatusCode();

        var list = await answer.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken);

        Assert.NotNull(list);
        Assert.Empty(list.Videos);
        Assert.False(list.FeedHasRun);

        // The Recent Window starts without a page visit, and its first pass is
        // said out loud rather than left to look like nothing is happening.
        Assert.True(list.RecentWindowFilling);
    }

    /// <summary>
    /// ADR 0007 makes prdb the only source of intent, so there is no route here
    /// that writes to the list. Asking for one is a 404 rather than a 405,
    /// because <c>Program.cs</c> answers an unknown API path with <em>there is
    /// no such thing</em> rather than letting it fall through to the page.
    /// </summary>
    [Fact]
    public async Task Nothing_here_writes_to_the_list()
    {
        await using var application = new FabApplication();

        using var client = await application.SignedInClientAsync();

        using var answer = await client.PostAsync(
            "/api/catalogue/wanted",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    /// <summary>ADR 0010: everything is behind the password unless it says otherwise.</summary>
    [Fact]
    public async Task The_surface_is_behind_the_password()
    {
        await using var application = new FabApplication();

        using var client = application.CreateClient();

        using var answer = await client.GetAsync(
            "/api/catalogue/wanted",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
    }

    private sealed record Answer(
        IReadOnlyList<object> Videos,
        int Page,
        int PageSize,
        int Total,
        bool FeedHasRun,
        bool RecentWindowFilling);
}
