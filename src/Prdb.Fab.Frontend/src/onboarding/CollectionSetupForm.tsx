import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { readAutopilot, startAutopilot } from '../api/client.ts'
import { Field, formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'

export function CollectionSetupForm({ onSaved }: { onSaved?: () => void }) {
  const [enabled, setEnabled] = useState(true)
  const [directories, setDirectories] = useState('')
  const state = useQuery({ queryKey: ['autopilot'], queryFn: readAutopilot })
  const save = useMutation({ mutationFn: () => startAutopilot(enabled, directories.split('\n').map(value => value.trim()).filter(Boolean)),
    onSuccess: answer => { if (answer.accepted) onSaved?.() } })
  return <form className={formStyles.form} onSubmit={event => { event.preventDefault(); save.mutate() }}>
    <p>The Library root is scanned for existing Video Files. Scanning leaves everything in place. After setup, review a preview and approve the files to move into the sorted Library. Empty storage needs no import confirmation.</p>
    {!state.data?.present && <>
      <Field label="Additional collection directories" help="Optional. One absolute mounted path per line, as seen by prdb-fab. SABnzbd's download directory is excluded.">
        {id => <textarea id={id} className={formStyles.field} rows={3} value={directories} onChange={event => setDirectories(event.target.value)} />}
      </Field>
      <label><input type="checkbox" checked={enabled} onChange={event => setEnabled(event.target.checked)} /> Start Recent Wanted autopilot after collection preparation</label>
    </>}
    <p>The supplied policy selects up to the 100 newest missing Wanted Videos released within the last 90 UTC calendar dates, then admits new recent Wanted intent. Unknown and future dates are excluded. The initial selection is never refilled. All enabled Indexers participate.</p>
    <p>At most {state.data?.downloadCap ?? 20} unfinished automatic Downloads and {state.data?.retryBudget ?? 3} attempts per Video. You can turn this policy off in Settings. Custom rules are separate permissions.</p>
    {state.data?.present && <Verdict tone="done">Your saved choice is {state.data.enabled ? 'on' : 'off'} and will be preserved.</Verdict>}
    <button className={formStyles.button} disabled={save.isPending || state.isPending || state.isError}>{state.data?.present ? 'Continue with saved choice' : 'Save and prepare collection'}</button>
    {save.data && <Verdict tone={save.data.accepted ? 'done' : 'refusal'}>{save.data.detail}</Verdict>}
    {(save.isError || state.isError) && <Verdict tone="refusal">Could not confirm collection setup. Reload to check its state.</Verdict>}
  </form>
}
