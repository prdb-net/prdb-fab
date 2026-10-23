import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { approveCollection, readCollectionPreview, startCollection, type CollectionPreviewPage } from '../api/client.ts'
import { ConfirmationDialog } from '../ui/ConfirmationDialog.tsx'
import { Field, formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './CollectionScreen.module.css'

export function CollectionScreen() {
  const [page, setPage] = useState(1)
  const [directories, setDirectories] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirm, setConfirm] = useState<{ action: 'all' | 'selected' | 'leave'; revision: CollectionPreviewPage['revision']; ids: string[]; ready: number; unresolved: number } | null>(null)
  const [leaveUnresolved, setLeaveUnresolved] = useState(false)
  const queries = useQueryClient()
  const preview = useQuery({ queryKey: ['collection', page], queryFn: () => readCollectionPreview(page), refetchInterval: 5000 })
  const refresh = () => { setSelected(new Set()); void queries.invalidateQueries({ queryKey: ['collection'] }) }
  const start = useMutation({ mutationFn: () => startCollection(directories.split('\n').map(value => value.trim()).filter(Boolean)), onSuccess: refresh })
  const approve = useMutation({
    mutationFn: () => approveCollection({
      revision: confirm!.revision,
      moveIds: confirm?.action === 'selected' ? confirm!.ids : [],
      leaveIds: confirm?.action === 'leave' ? confirm!.ids : [],
      allReady: confirm?.action === 'all',
      leaveUnresolved: confirm?.action === 'all' && leaveUnresolved,
    }),
    onSuccess: () => { setConfirm(null); refresh() },
  })
  const held = preview.data
  const showConfirmation = (action: 'all' | 'selected' | 'leave') => {
    if (!held) return
    setLeaveUnresolved(false)
    setConfirm({ action, revision: held.revision, ids: [...selected], ready: Number(held.ready), unresolved: Number(held.unresolved) })
  }
  const usable = held?.phase === 'Preview' && !held.error
  return (
    <SettingsPage title="Prepare the existing collection" back="/settings/library" backLabel="Library settings"
      lede="Review the files already on disk before Wanted automation starts.">
      {preview.isPending && <p>Reading collection preparation…</p>}
      {preview.isError && <Verdict tone="refusal">Could not read collection preparation. Reload to try again.</Verdict>}
      {held?.phase === 'NotStarted' && (
        <form onSubmit={event => { event.preventDefault(); start.mutate() }} className={formStyles.form}>
          <p>The Library root is included automatically. Add any other mounted directories containing your existing collection. Scanning leaves files in place.</p>
          <Field label="Additional collection directories" help="Optional. One absolute path per line, as seen by prdb-fab.">
            {id => <textarea id={id} className={formStyles.field} rows={4} value={directories} onChange={event => setDirectories(event.target.value)} />}
          </Field>
          <button className={formStyles.button} disabled={start.isPending}>Scan the existing collection</button>
        </form>
      )}
      {held && held.phase !== 'NotStarted' && <>
        <p role="status"><strong>{phaseName(held.phase)}</strong> · {held.total} files · {held.ready} with a destination · {held.unresolved} need a decision · {held.approved} approved · {held.left} left in place</p>
        {held.error && <Verdict tone="refusal">{held.error}</Verdict>}
        {held.phase === 'Ready' && <Verdict tone="done">Collection preparation is complete. Wanted automation can proceed when its Connections and Wanted list are ready.</Verdict>}
        <p>Approved files move into the sorted Library. Across filesystems, prdb-fab copies, verifies and then deletes the source. Duplicates and unresolved files stay in place until you decide.</p>
        {Number(held.unresolved) > 0 && <p><Link to="/review-queue">Resolve unidentified files in the Review Queue</Link>, or explicitly leave them in place. An unidentified file cannot reliably prevent a later duplicate Download.</p>}
        {usable && <div className={styles.actions}>
          <button className={formStyles.button} disabled={Number(held.ready) === 0 && Number(held.unresolved) === 0} onClick={() => showConfirmation('all')}>Review whole collection…</button>
          <button className={formStyles.button} disabled={selected.size === 0} onClick={() => showConfirmation('selected')}>Import selected…</button>
          <button className={formStyles.button} disabled={selected.size === 0} onClick={() => showConfirmation('leave')}>Leave selected in place…</button>
        </div>}
        <ul className={styles.files}>{held.files.map(file => <li key={file.id}>
          <label className={styles.selection}>
            {usable && !file.left && !file.approved && <input type="checkbox" checked={selected.has(file.id)} onChange={event => {
              const next = new Set(selected); if (event.target.checked) next.add(file.id); else next.delete(file.id); setSelected(next)
            }} />}
            <strong>{file.title ?? 'Unidentified file'}</strong>
          </label>
          <code>{file.sourcePath}</code>
          <p>{file.quality ?? 'Quality unknown'} · {file.confidence ?? 'No identification'} · {(Number(file.sizeBytes) / 1024 ** 3).toFixed(2)} GiB</p>
          {file.destination && <p>Move to <code>{file.destination}</code></p>}
          <p>{file.detail}</p>
        </li>)}</ul>
        <nav className={styles.actions} aria-label="Collection pages">
          <button className={formStyles.button} disabled={page <= 1} onClick={() => { setPage(page - 1); setSelected(new Set()) }}>Previous</button>
          <span>Page {page} of {Math.max(1, Math.ceil(Number(held.total) / Number(held.pageSize)))}</span>
          <button className={formStyles.button} disabled={page * Number(held.pageSize) >= Number(held.total)} onClick={() => { setPage(page + 1); setSelected(new Set()) }}>Next</button>
        </nav>
      </>}
      {start.data && <Verdict tone={start.data.accepted ? 'done' : 'refusal'}>{start.data.detail}</Verdict>}
      {approve.data && <Verdict tone={approve.data.accepted ? 'done' : 'refusal'}>{approve.data.detail}</Verdict>}
      {(start.isError || approve.isError) && <Verdict tone="refusal">Could not confirm the action. Reload to check its current state.</Verdict>}
      {confirm && held && <ConfirmationDialog title={confirm?.action === 'leave' ? 'Leave these files in place?' : 'Approve collection import?'}
        confirmLabel={confirm?.action === 'leave' ? 'Leave selected files' : 'Approve this selection'} busy={approve.isPending}
        onCancel={() => setConfirm(null)} onConfirm={() => approve.mutate()}>
        <p>{confirm?.action === 'all' ? `${confirm.ready} files across the entire preview have a prepared destination.` : `${confirm.ids.length} files are selected.`}</p>
        <p>{confirm?.action === 'leave' ? 'The files stay exactly where they are. This explicitly dismisses their identification and candidate holds.' : 'Approved files leave their original location. Sources and destinations are checked again before moving; changed files require an updated preview.'}</p>
        {confirm?.action === 'all' && confirm.unresolved > 0 && <label className={styles.selection}>
          <input type="checkbox" checked={leaveUnresolved} onChange={event => setLeaveUnresolved(event.target.checked)} />
          Explicitly leave the {confirm.unresolved} unresolved files in place. Unidentified files may still be downloaded again.
        </label>}
        <p>Import waits until every file has an import or leave decision. No additional rule setup is needed for the supplied Wanted policy.</p>
      </ConfirmationDialog>}
    </SettingsPage>
  )
}

function phaseName(phase: string): string {
  return ({ Inventory: 'Scanning directories', Probing: 'Reading video files', Identifying: 'Identifying with prdb',
    Planning: 'Preparing destinations', Preview: 'Awaiting your decisions', Validating: 'Checking the approved selection',
    Importing: 'Importing approved files', Ready: 'Collection ready' } as Record<string, string>)[phase] ?? phase
}
