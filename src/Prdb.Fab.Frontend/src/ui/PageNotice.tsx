import { Link, useLocation } from 'react-router'

/** Recovery keeps filters and Preview state on the list's own address. */
export function PageNotice({ page, pages, pending = false, previousPage, unavailable = page > pages }: {
  page: number
  pages: number
  pending?: boolean
  previousPage?: number
  unavailable?: boolean
}) {
  const location = useLocation()
  const next = new URLSearchParams(location.search)
  if (pages === 1) next.delete('page')
  else next.set('page', String(pages))

  return <>
    <p role="status" aria-live="polite">
      {pending ? `Loading page ${page}…${previousPage === undefined ? '' : ` Showing previous results from page ${previousPage} until it arrives.`}` : ''}
    </p>
    {unavailable && <div role="status">
      <p>Page {page} is no longer available. {pages === 1 ? 'Return to the first page to see the current results.' : `There are ${pages} pages now.`}</p>
      <Link to={{ pathname: location.pathname, search: next.toString() }}>Go to {pages === 1 ? 'first' : 'last'} page</Link>
    </div>}
  </>
}
