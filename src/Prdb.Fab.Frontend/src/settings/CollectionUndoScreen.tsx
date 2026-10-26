import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { readCollectionUndo, requestCollectionUndo, resumeCollectionUndo, type CollectionUndoPreview } from '../api/client.ts'
import { ConfirmationDialog } from '../ui/ConfirmationDialog.tsx'
import { formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './CollectionScreen.module.css'

export function CollectionUndoScreen() {
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<string[]>([])
  const [confirm, setConfirm] = useState<{ all: boolean; revision: CollectionUndoPreview['revision']; evidence: string; ids: string[]; count: number } | null>(null)
  const [resume, setResume] = useState<boolean | null>(null)
  const queries = useQueryClient()
  const preview = useQuery({ queryKey: ['collection-undo', page], queryFn: () => readCollectionUndo(page), refetchInterval: 5000 })
  const refresh = () => {
    setSelected([])
    void queries.invalidateQueries({ queryKey: ['collection-undo'] })
    void queries.invalidateQueries({ queryKey: ['collection'] })
    void queries.invalidateQueries({ queryKey: ['library'] })
  }
  const request = useMutation({
    mutationFn: () => requestCollectionUndo(confirm!.revision, confirm!.evidence, confirm!.ids, confirm!.all),
    onSuccess: () => { setConfirm(null); refresh() },
  })
  const continueWork = useMutation({
    mutationFn: () => resumeCollectionUndo(resume!),
    onSuccess: () => { setResume(null); refresh() },
  })
  const held = preview.data
  const usable = held?.phase === 'Ready' || held?.phase === 'UndoComplete'
  const pages = Math.max(1, Math.ceil(Math.max(Number(held?.available ?? 0), Number(held?.requested ?? 0)) / 50))
  return <SettingsPage title="Return imported files" back="/settings/library/collection" backLabel="Collection preparation"
    lede="Move imported files back to their recorded original paths, after reviewing and approving the return plan.">
    <p>This returns files moved during collection import, including files reorganised inside the Library root. Downloads and files whose path did not change are excluded. Existing files are never overwritten. Work resumes after a restart; a Restore requires another confirmation of the return paths.</p>
    <p>Automatic acquisition waits during returns and afterwards until you explicitly resume it. Returned Videos leave the Library; their prdb Fulfilment is not retracted automatically.</p>
    {preview.isError && <Verdict tone="refusal">The return preview could not be read.</Verdict>}
    {held && <>
      <p role="status">{held.available} files available to return. {held.completed} of {held.requested} approved returns completed.</p>
      {usable && Number(held.available) > 0 && <div className={styles.actions}>
        <button className={formStyles.button} disabled={selected.length === 0} onClick={() => setConfirm({ all: false, revision: held.revision, evidence: held.evidence, ids: [...selected], count: selected.length })}>Return selected…</button>
        <button className={formStyles.button} onClick={() => setConfirm({ all: true, revision: held.revision, evidence: held.evidence, ids: [], count: Number(held.available) })}>Return all {held.available} files…</button>
      </div>}
      <ul className={styles.files}>{held.files.map(file => <li key={file.id}>
        <label className={styles.selection}>
          {usable && <input type="checkbox" aria-label={`Return ${file.destinationPath}`} checked={selected.includes(file.id)} onChange={event => setSelected(ids => event.target.checked ? [...ids, file.id] : ids.filter(id => id !== file.id))} />}
          <strong>{file.destinationPath.split(/[\\/]/).at(-1)} · {(Number(file.sizeBytes) / 1024 ** 3).toFixed(2)} GiB</strong>
        </label>
        <p>From <code>{file.sourcePath}</code></p><p>Return to <code>{file.destinationPath}</code></p>
        {file.conflict && <Verdict tone="refusal">{file.conflict}</Verdict>}
      </li>)}</ul>
      {Number(held.requested) > 0 && <>
        <h2>Approved returns</h2>
        <ul className={styles.files}>{held.progress.map(file => <li key={file.id}>
          <strong>{file.state}</strong><p><code>{file.sourcePath}</code> → <code>{file.destinationPath}</code></p>
          {file.error && <Verdict tone="refusal">{file.error}</Verdict>}
        </li>)}</ul>
      </>}
      {held.progress.some(file => file.state === 'Awaiting confirmation after Restore') && <button className={formStyles.button} onClick={() => setResume(true)}>Confirm restored return paths…</button>}
      {held.phase === 'UndoComplete' && <button className={formStyles.button} onClick={() => setResume(false)}>Resume automatic acquisition…</button>}
      {pages > 1 && <nav className={styles.actions} aria-label="Return preview pages">
        <button className={formStyles.button} disabled={page <= 1} onClick={() => { setPage(page - 1); setSelected([]) }}>Previous</button>
        <span>Page {page} of {pages}</span>
        <button className={formStyles.button} disabled={page >= pages} onClick={() => { setPage(page + 1); setSelected([]) }}>Next</button>
      </nav>}
    </>}
    {(request.isError || continueWork.isError) && <Verdict tone="refusal">Could not confirm the action. Reload to check its state.</Verdict>}
    {request.data && <Verdict tone={request.data.accepted ? 'done' : 'refusal'}>{request.data.detail}</Verdict>}
    {continueWork.data && <Verdict tone={continueWork.data.accepted ? 'done' : 'refusal'}>{continueWork.data.detail}</Verdict>}
    {confirm && <ConfirmationDialog title="Return imported files?" confirmLabel={`Return ${confirm.count} files`} busy={request.isPending}
      onCancel={() => setConfirm(null)} onConfirm={() => request.mutate()}>
      <p>{confirm.count} files will leave the Library and return to their original paths{confirm.all ? ', across every page of this preview' : ''}. Conflicting paths wait for you to resolve them. Each file is checked again before it moves.</p>
      <p>Automatic acquisition stays paused until you explicitly resume it after the returns finish.</p>
    </ConfirmationDialog>}
    {resume !== null && <ConfirmationDialog title={resume ? 'Confirm restored return paths?' : 'Resume automatic acquisition?'} confirmLabel="Confirm" busy={continueWork.isPending}
      onCancel={() => setResume(null)} onConfirm={() => continueWork.mutate()}>
      <p>{resume ? 'Confirm every unfinished return shown across the preview pages on this installation. The recorded original paths must still be the intended destinations.' : 'Returned Videos are no longer held in the Library. Wanted Videos may be downloaded again where an Automation Rule permits it.'}</p>
    </ConfirmationDialog>}
  </SettingsPage>
}
