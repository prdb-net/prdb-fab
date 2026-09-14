using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;

using Prdb.Fab.Core.Sync;

namespace Prdb.Fab.Infrastructure.Sync;

/// <summary>
/// The body of ADR 0064's submission: six parts, and the one place they are
/// named.
/// </summary>
/// <remarks>
/// <para>
/// A class of its own rather than a few lines inside the routine, because what
/// it is for is to be the only thing that decides what leaves this
/// installation. ADR 0064 enumerates the payload instead of describing it, and
/// <c>docs/privacy.md</c> states that enumeration to a person — so a part added
/// anywhere else would make both of those documents quietly wrong.
/// </para>
/// <para>
/// <strong>The filenames are constants and never the file's own.</strong> A
/// multipart filename is the one field of a body that carries a local name by
/// accident, and the source Video File never leaves — including what it is
/// called.
/// </para>
/// </remarks>
public static class PreviewPublicationBody
{
    /// <summary>
    /// Builds the body for one generated pair.
    /// </summary>
    /// <param name="videoId">The prdb Video the file is filed under.</param>
    /// <param name="osHash">The osHash the Probe recorded for those bytes.</param>
    /// <param name="sheet">The generated JPEG, positioned at its start.</param>
    /// <param name="vtt">The generated WebVTT, positioned at its start.</param>
    public static MultipartBody For(Guid videoId, string osHash, Stream sheet, Stream vtt)
    {
        var body = new MultipartBody { RequestAdapter = Writes };

        body.AddOrReplacePart("File", "image/jpeg", sheet, PreviewPublicationContract.SheetFilename);
        body.AddOrReplacePart("VttFile", "text/vtt", vtt, PreviewPublicationContract.VttFilename);

        // The Video rather than the local Video File: what prdb is being told
        // is which of its own rows this picture is of.
        body.AddOrReplacePart("VideoId", "text/plain", videoId.ToString("D"));

        // The bytes the picture was made from, which is what binds the two
        // together and the only thing in the body derived from the file.
        body.AddOrReplacePart("BasedOnFileWithOsHash", "text/plain", osHash);

        body.AddOrReplacePart(
            "PreviewImageType",
            "text/plain",
            PreviewPublicationContract.PreviewImageType);
        body.AddOrReplacePart(
            "DisplayOrder",
            "text/plain",
            PreviewPublicationContract.DisplayOrder.ToString());

        return body;
    }

    /// <summary>
    /// What a <see cref="MultipartBody"/> asks for and this body never uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kiota requires a body to hold a request adapter, and reads a
    /// serialization writer off it for any part that is a model. Every part
    /// here is a string or a stream, so nothing is ever asked of it — and the
    /// client's own adapter is not reachable from outside the generated builder
    /// anyway.
    /// </para>
    /// <para>
    /// So this is a placeholder rather than a transport, and every member that
    /// would send something refuses. The request itself goes out through
    /// <c>PrdbGateway</c>'s client, on the one transport the governor is on —
    /// which is the property that must not be quietly escapable, and an adapter
    /// here that could send would be exactly that escape.
    /// </para>
    /// </remarks>
    private static readonly IRequestAdapter Writes = new NoTransport();

    private sealed class NoTransport : IRequestAdapter
    {
        public ISerializationWriterFactory SerializationWriterFactory =>
            SerializationWriterFactoryRegistry.DefaultInstance;

        public string? BaseUrl { get; set; }

        public void EnableBackingStore(IBackingStoreFactory backingStoreFactory) => throw Refused();

        public Task<ModelType?> SendAsync<ModelType>(
            RequestInformation requestInfo,
            ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
            CancellationToken cancellationToken = default)
            where ModelType : IParsable => throw Refused();

        public Task<IEnumerable<ModelType>?> SendCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
            CancellationToken cancellationToken = default)
            where ModelType : IParsable => throw Refused();

        public Task<IEnumerable<ModelType>?> SendPrimitiveCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
            CancellationToken cancellationToken = default) => throw Refused();

        public Task<ModelType?> SendPrimitiveAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
            CancellationToken cancellationToken = default) => throw Refused();

        public Task SendNoContentAsync(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = null,
            CancellationToken cancellationToken = default) => throw Refused();

        public Task<T?> ConvertToNativeRequestAsync<T>(
            RequestInformation requestInfo,
            CancellationToken cancellationToken = default) => throw Refused();

        private static NotSupportedException Refused() => new(
            "This adapter exists to hold a multipart body together. Every prdb request goes "
            + "through PrdbGateway, so that the governor sees it (ADR 0014).");
    }
}
