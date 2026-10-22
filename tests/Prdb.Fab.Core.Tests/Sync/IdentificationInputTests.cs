using Prdb.Fab.Core.ReleaseDiscovery;
using Xunit;
namespace Prdb.Fab.Core.Tests.Sync;
public sealed class IdentificationInputTests
{
    [Fact]
    public void Every_evidence_field_participates_in_exact_input_identity()
    {
        var input = new IdentificationInput("Exact.Name");
        Assert.Equal(input, new IdentificationInput("Exact.Name"));
        Assert.NotEqual(input, input with { Filename = "Exact Name" });
        Assert.NotEqual(input, input with { Filesize = 123 });
        Assert.NotEqual(input, input with { OsHash = "0000000000000001" });
        Assert.NotEqual(input, input with { PHash = "1" });
        Assert.NotEqual(input, input with { Md5 = "1" });
    }
}
