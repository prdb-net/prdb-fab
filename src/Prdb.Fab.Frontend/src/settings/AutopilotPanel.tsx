import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useNavigate } from 'react-router'
import { readAutopilot, readAutopilotSelection, switchAutopilot } from '../api/client.ts'
import { CollectionSetupForm } from '../onboarding/CollectionSetupForm.tsx'
import { formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import styles from './AutopilotPanel.module.css'

export function AutopilotPanel({ controls = false, compact = false }: { controls?: boolean; compact?: boolean }) {
  const queries = useQueryClient()
  const navigate = useNavigate()
  const location = useLocation()
  const state = useQuery({ queryKey: ['autopilot'], queryFn: readAutopilot, refetchInterval: 5000 })
  const refresh = () => {
    for (const key of ['autopilot', 'automation-settings', 'status', 'collection'])
      void queries.invalidateQueries({ queryKey: [key] })
  }
  const change = useMutation({ mutationFn: switchAutopilot, onSuccess: refresh })
  if (state.isError) return <Verdict tone="refusal">Could not read automatic download status. Reload to try again.</Verdict>
  if (!state.data) return <p>Reading automatic download status…</p>
  const held = state.data
  const step = ['NotAdopted', 'PreparationRequired', 'Inventory', 'Probing', 'Identifying', 'Planning'].includes(held.phase) ? 0
    : ['Preview', 'Validating', 'Importing'].includes(held.phase) ? 1 : 2
  const phaseLabel = ({ NotAdopted: 'Not started', Off: 'Off', Preview: 'Your review is needed', Active: 'On',
    Inventory: 'Checking your collection', Probing: 'Reading files', Identifying: 'Identifying files', Planning: 'Preparing your preview',
    Importing: 'Importing approved files', Validating: 'Checking approved files', WantedBaseline: 'Waiting for Wanted sync',
    Connections: 'Connection needed', Setup: 'Setup needed' } as Record<string, string>)[held.phase] ?? 'Preparing'
  const action = held.phase === 'Active' ? { to: '/wanted', label: 'Open Wanted' }
    : held.actionRoute === '/settings/library/collection' ? { to: '/settings/library/collection?from=/settings/automation', label: held.phase === 'Preview' ? 'Review collection' : 'View collection progress' }
    : held.phase === 'Connections' ? { to: held.actionRoute, label: 'Check connection' }
    : held.phase === 'Setup' ? { to: held.actionRoute, label: 'Finish setup' }
    : held.phase === 'WantedBaseline' ? { to: '/status', label: 'View sync status' }
    : { to: '/settings/automation', label: held.present ? 'Manage automatic downloads' : 'Set up automatic downloads' }

  if (compact && !controls) return <details className={styles.compact}>
    <summary>Automatic Wanted downloads <span className={styles.badge}>{phaseLabel}</span></summary>
    <p role="status">{held.detail}</p>
    <Link to={action.to === location.pathname ? '/settings/automation' : action.to}>
      {action.to === location.pathname ? 'Manage automatic downloads' : action.label}
    </Link>
  </details>

  return <section className={styles.panel} aria-label="Automatic Wanted downloads">
    <div className={styles.heading}>
      <h2>Automatic Wanted downloads</h2>
      <span className={styles.badge}>{phaseLabel}</span>
    </div>
    {controls && held.phase !== 'Off' && <ol className={styles.steps} aria-label="Automatic download setup">
      {['Check collection', 'Review and import', 'Download Wanted'].map((label, index) => <li key={label} aria-current={step === index ? 'step' : undefined}>
        <span className={styles.stepNumber}>{index + 1}</span><span>{label}</span>
      </li>)}
    </ol>}
    <p role="status">{!held.present
      ? 'Start by checking what you already have. Review any files to import, then let prdb-fab download missing Wanted Videos automatically. No custom rules needed.'
      : held.detail}</p>
    {controls && <>
      <p className={styles.scope}>Starts with up to <strong>100 missing Wanted Videos</strong> released in the <strong>last 90 days</strong>, then follows new recent Wanted additions. Finishing that selection does not start downloading the older backlog.</p>
      {!held.present ? <CollectionSetupForm guided onSaved={() => {
        refresh()
        navigate('/settings/library/collection?from=/settings/automation')
      }} /> : <>
        <div className={styles.actions}>
          {action.to !== location.pathname && <Link className={`${formStyles.button} ${formStyles.primaryButton}`} to={action.to}>{action.label}</Link>}
          <button className={formStyles.button} disabled={change.isPending} onClick={() => change.mutate(!held.enabled)}>
            {change.isPending ? 'Saving…' : held.enabled ? 'Turn off automatic Wanted downloads' : 'Resume automatic Wanted downloads'}
          </button>
        </div>
        {held.phase === 'Off' && <p><Link to="/settings/library/collection?from=/settings/automation">View collection preparation</Link></p>}
        {(Number(held.initialAdmitted) > 0 || Number(held.admitted) > 0 || held.phase === 'Active') && <Selection />}
      </>}
      {change.data && <Verdict tone={change.data.accepted ? 'done' : 'refusal'}>{change.data.detail}</Verdict>}
      {change.isError && <Verdict tone="refusal">Could not confirm the change. Reload to check its state.</Verdict>}
    </>}
    {!controls && action.to !== location.pathname && <Link to={action.to}>{action.label}</Link>}
  </section>
}

function Selection() {
  const [open, setOpen] = useState(false)
  const [page, setPage] = useState(1)
  const selection = useQuery({ queryKey: ['autopilot-selection', page], queryFn: () => readAutopilotSelection(page), enabled: open, refetchInterval: open ? 5000 : false })
  return <details className={styles.selection} onToggle={event => setOpen(event.currentTarget.open)}>
    <summary>View selected Videos</summary>
    {selection.isPending && open && <p>Reading selected Videos…</p>}
    {selection.data && <>
      {Number(selection.data.total) === 0 ? <p>No eligible missing Videos have been selected yet.</p> : <ul>{selection.data.videos.map(video => <li key={video.videoId}>
        <strong>{video.title ?? video.videoId}</strong> · {video.initial ? 'Initial selection' : 'Added later'} · {video.state}
      </li>)}</ul>}
      {Number(selection.data.total) > 25 && <div className={styles.actions}>
        <button className={formStyles.button} disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</button>
        <span>Page {page} of {Math.max(1, Math.ceil(Number(selection.data.total) / 25))}</span>
        <button className={formStyles.button} disabled={page * 25 >= Number(selection.data.total)} onClick={() => setPage(page + 1)}>Next</button>
      </div>}
    </>}
    {selection.isError && <Verdict tone="refusal">Could not read the saved selection.</Verdict>}
  </details>
}
