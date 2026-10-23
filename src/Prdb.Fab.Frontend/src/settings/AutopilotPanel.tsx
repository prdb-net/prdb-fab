import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { readAutopilot, readAutopilotSelection, switchAutopilot } from '../api/client.ts'
import { CollectionSetupForm } from '../onboarding/CollectionSetupForm.tsx'
import { formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'

export function AutopilotPanel({ controls = false }: { controls?: boolean }) {
  const queries = useQueryClient()
  const state = useQuery({ queryKey: ['autopilot'], queryFn: readAutopilot, refetchInterval: 5000 })
  const [adopting, setAdopting] = useState(false)
  const refresh = () => { void queries.invalidateQueries({ queryKey: ['autopilot'] }); void queries.invalidateQueries({ queryKey: ['status'] }) }
  const change = useMutation({ mutationFn: switchAutopilot, onSuccess: refresh })
  if (state.isError) return <Verdict tone="refusal">Could not read Wanted autopilot status. Reload to try again.</Verdict>
  if (!state.data) return <p>Reading Wanted autopilot status…</p>
  const held = state.data
  return <section aria-label="Recent Wanted autopilot">
    <h2>Recent Wanted autopilot{held.present ? held.enabled ? ' · On' : ' · Off' : ''}</h2>
    <p role="status">{held.detail}</p>
    <p><Link to={held.actionRoute}>{held.actionRoute === '/settings/library/collection' ? 'Open collection preparation' : held.actionRoute === '/status' ? 'Open Status' : 'Autopilot settings'}</Link></p>
    {controls && <>
      <p>Up to 100 newest missing Wanted Videos from the last 90 UTC calendar dates, then new recent Wanted intent. The initial selection is never refilled. All enabled Indexers participate. Older and overflow Videos remain available for manual Download and custom rules.</p>
      {held.present ? <button className={formStyles.button} disabled={change.isPending} onClick={() => change.mutate(!held.enabled)}>{held.enabled ? 'Turn off Recent Wanted autopilot' : 'Turn on Recent Wanted autopilot'}</button>
        : <button className={formStyles.button} onClick={() => setAdopting(!adopting)}>Set up Recent Wanted autopilot…</button>}
      {adopting && !held.present && <CollectionSetupForm onSaved={() => { setAdopting(false); refresh() }} />}
      {held.present && <Selection />}
      {change.data && <Verdict tone={change.data.accepted ? 'done' : 'refusal'}>{change.data.detail}</Verdict>}
      {change.isError && <Verdict tone="refusal">Could not confirm the policy change. Reload to check its state.</Verdict>}
    </>}
  </section>
}

function Selection() {
  const [open, setOpen] = useState(false)
  const [page, setPage] = useState(1)
  const selection = useQuery({ queryKey: ['autopilot-selection', page], queryFn: () => readAutopilotSelection(page), enabled: open, refetchInterval: open ? 5000 : false })
  return <details onToggle={event => setOpen(event.currentTarget.open)}>
    <summary>Initial selection and later admissions</summary>
    {selection.data && <>
      {Number(selection.data.total) === 0 ? <p>No admitted Videos yet.</p> : <ul>{selection.data.videos.map(video => <li key={video.videoId}>
        <strong>{video.title ?? video.videoId}</strong> · {video.initial ? 'Initial selection' : 'Later Wanted intent'} · {video.state}
      </li>)}</ul>}
      <p>Page {page} of {Math.max(1, Math.ceil(Number(selection.data.total) / 25))}</p>
      <button className={formStyles.button} disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</button>{' '}
      <button className={formStyles.button} disabled={page * 25 >= Number(selection.data.total)} onClick={() => setPage(page + 1)}>Next</button>
    </>}
    {selection.isError && <Verdict tone="refusal">Could not read the saved selection.</Verdict>}
  </details>
}
