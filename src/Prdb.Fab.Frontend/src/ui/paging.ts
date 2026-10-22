/** URL input must fit the API's positive Int32 page parameter. */
export function readPage(parameters: URLSearchParams): number {
  const page = Number(parameters.get('page') ?? '1')
  return Number.isInteger(page) && page > 0 && page <= 2_147_483_647 ? page : 1
}
