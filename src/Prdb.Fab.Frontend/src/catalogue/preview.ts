import { useCallback } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router'

/**
 * Which Video is open over which page of which list (ADR 0060).
 *
 * ADR 0036 puts what is linkable in the address bar, and that is what this is:
 * a parameter on whatever list address the person is already at, rather than
 * state inside a screen. It is also what makes the browser's back button close
 * the sheet instead of leaving the list — the gesture a phone offers first.
 */
export const previewParameter = 'preview'

/** Whether this address was reached by opening a Preview from a grid. */
type PreviewHistory = { preview?: boolean }

export function usePreview() {
  const [parameters] = useSearchParams()
  const navigate = useNavigate()
  const location = useLocation()
  const open = parameters.get(previewParameter)

  const openPreview = useCallback(
    (prdbId: string) => {
      const next = new URLSearchParams(parameters)
      next.set(previewParameter, prdbId)

      // One history entry for the sheet, not one per Video looked at. Stepping
      // from card to card replaces it, so the back button leaves the Preview
      // rather than walking back through everything that was glanced at.
      navigate(
        { search: `?${next}` },
        { replace: open !== null, state: { preview: true } satisfies PreviewHistory },
      )
    },
    [navigate, open, parameters],
  )

  const closePreview = useCallback(() => {
    if ((location.state as PreviewHistory | null)?.preview) {
      // Going back is what leaves no forward entry behind, so closing the sheet
      // and pressing back are the same act rather than two.
      navigate(-1)
      return
    }

    // Arrived at this address directly — a link somebody sent, or a reload with
    // the sheet open. There is nothing behind it to go back to, so the
    // parameter is dropped in place.
    const next = new URLSearchParams(parameters)
    next.delete(previewParameter)
    navigate({ search: next.size > 0 ? `?${next}` : '' }, { replace: true })
  }, [location.state, navigate, parameters])

  return { open, openPreview, closePreview }
}
