/**
 * Where prdb is, for a link out of a card.
 *
 * The public API document names `https://api.prdb.net` and no address for the
 * site a person reads, and `VideoDetailDto` carries no canonical URL the way
 * `SiteDto` carries the producer's — so there is nothing to build a deep link
 * out of that would not be a guess. Until prdb publishes one this points at the
 * front door, which is honest and useless in the same measure.
 *
 * Deliberately one function rather than a string in a component: when the URL
 * arrives, this is the whole of the change.
 */
export const prdbSite = 'https://prdb.net'

/** Where a video lives on prdb, as far as anything documented says. */
export function prdbVideoUrl(_prdbId: string): string {
  return prdbSite
}
