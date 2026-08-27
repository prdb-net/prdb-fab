/**
 * Where prdb is, for a link out of a card.
 *
 * The address form is `/videos/<id>`, and the id is the one the public API
 * already hands out — so a client that holds a video holds everything the link
 * needs and looks nothing up. It is not in the OpenAPI document: `VideoDetailDto`
 * carries no canonical URL the way `SiteDto` carries the producer's, and the
 * document names only the API host. So this is an arrangement rather than a
 * contract, which is exactly why it lives in one function: if the form ever
 * changes, this is the whole of the change.
 */
export const prdbSite = 'https://prdb.net'

/** Where a video lives on prdb. */
export function prdbVideoUrl(prdbId: string): string {
  return `${prdbSite}/videos/${prdbId}`
}
