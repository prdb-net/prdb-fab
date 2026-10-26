using Prdb.Fab.Infrastructure.Filing;
using Xunit;

namespace Prdb.Fab.Infrastructure.Tests.Filing;

public sealed class StorageSpaceTests
{
    [Fact]
    public void Capacity_is_read_for_an_existing_filesystem_and_insufficient_capacity_refuses_the_copy()
    {
        var destination = Path.Combine(Path.GetTempPath(), "fab-capacity-test", "file.part");
        var available = StorageSpace.AvailableAt(destination);
        Assert.NotNull(available);
        Assert.True(available >= 0);
        Assert.Throws<InsufficientStorageException>(() => StorageSpace.RequireCopyCapacity(destination, long.MaxValue));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public void Capacity_is_a_hint_and_unknown_capacity_is_not_fabricated_as_zero()
    {
        Assert.False(StorageSpace.IsInsufficient(100, null));
        Assert.False(StorageSpace.IsInsufficient(100, 100));
        Assert.True(StorageSpace.IsInsufficient(101, 100));
    }
}
