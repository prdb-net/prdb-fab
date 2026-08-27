using Prdb.Fab.Infrastructure.Connections;

using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Connections;

/// <summary>
/// The three questions onboarding asks about a directory, against a real one.
/// ADR 0042 already put these tests on a real temporary directory rather than
/// behind an abstraction, because what is being checked is what the filesystem
/// does.
/// </summary>
public sealed class DirectoryTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), "prdb-fab-tests", Guid.NewGuid().ToString("n"));

    public DirectoryTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void A_directory_that_is_there_is_there()
    {
        Assert.True(Directories.Exists(directory));
        Assert.False(Directories.Exists(Path.Combine(directory, "nothing")));
    }

    [Fact]
    public void An_empty_directory_is_still_readable() => Assert.True(Directories.IsReadable(directory));

    [Fact]
    public void Writable_is_answered_by_writing_and_leaves_nothing_behind()
    {
        Assert.True(Directories.IsWritable(directory));
        Assert.Empty(Directory.GetFileSystemEntries(directory));
    }

    [Fact]
    public void Neither_question_is_answered_yes_about_a_directory_that_is_not_there()
    {
        var missing = Path.Combine(directory, "nothing");

        Assert.False(Directories.IsReadable(missing));
        Assert.False(Directories.IsWritable(missing));
    }

    /// <summary>
    /// Two directories on one volume, which is what CI has. ADR 0042 put the
    /// kernel's own cross-device refusal on the list of what is not tested, and
    /// this is the half that can be: the answer is <em>yes</em> rather than
    /// <em>we could not tell</em>.
    /// </summary>
    [Fact]
    public void Two_directories_on_one_volume_share_a_filesystem()
    {
        var second = Path.Combine(directory, "second");
        Directory.CreateDirectory(second);

        Assert.True(Directories.OnTheSameFilesystem(directory, second));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }
}
