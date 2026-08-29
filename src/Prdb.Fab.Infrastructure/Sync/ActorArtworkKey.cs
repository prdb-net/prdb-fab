using System.Security.Cryptography;

namespace Prdb.Fab.Infrastructure.Sync;

internal static class ActorArtworkKey
{
    public static Guid Of(Guid actorId)
    {
        Span<byte> input = stackalloc byte[17];
        input[0] = 0xA7;
        actorId.TryWriteBytes(input[1..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }
}
