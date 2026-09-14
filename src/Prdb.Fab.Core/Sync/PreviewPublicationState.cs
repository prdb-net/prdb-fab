namespace Prdb.Fab.Core.Sync;

/// <summary>
/// Where one generated preview stands between being owed and being finished
/// with (ADR 0064).
/// </summary>
/// <remarks>
/// <para>
/// The states are the ADR's, written down whole rather than added to as each
/// half of the work lands, because what they are for is to be read together:
/// <see cref="Uncertain"/> only means anything beside <see cref="Sent"/> and
/// <see cref="Refused"/>, and the argument for it is the argument for not
/// collapsing the three.
/// </para>
/// <para>
/// <strong>Deferral is not among them.</strong> A run the governor turned away
/// leaves the row exactly as it was and comes back, which is ADR 0014's fourth
/// case and the reason it is an absence rather than a value here.
/// </para>
/// </remarks>
public enum PreviewPublicationState
{
    /// <summary>
    /// Owed: a file was filed, and a sheet is to be made from it. Nothing has
    /// been decoded and nothing is on disk.
    /// </summary>
    Intended,

    /// <summary>
    /// Generated and validated, waiting to be sent. The bytes are under
    /// <c>publications/</c>, and the count of rows in this state is what
    /// ADR 0064 bounds at <see cref="PreviewPublicationContract.MostWaiting"/>.
    /// </summary>
    Ready,

    /// <summary>prdb accepted the submission. Not the same as visible.</summary>
    Sent,

    /// <summary>
    /// prdb considered the submission and declined it. Final: never
    /// regenerated, never resubmitted.
    /// </summary>
    Refused,

    /// <summary>
    /// The request left and nothing came back that says what became of it. The
    /// bytes are kept and a person decides, because the read endpoints show
    /// only what moderation has made public and absence there proves nothing.
    /// </summary>
    Uncertain,

    /// <summary>
    /// Nothing was sent and nothing will be: the file is gone, its bytes
    /// changed, the account changed, the switch went off, the intent expired,
    /// or prdb already shows a preview made from this exact file.
    /// </summary>
    Dropped,
}
