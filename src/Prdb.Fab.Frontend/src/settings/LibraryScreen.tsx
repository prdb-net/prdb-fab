import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { readLibrarySettings, saveLibrarySettings } from '../api/client.ts'
import { LibraryRootForm } from '../onboarding/LibraryRootForm.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import formStyles from '../onboarding/Onboarding.module.css'

export function LibrarySettingsScreen() {
  const queries = useQueryClient()
  const settings = useQuery({ queryKey: ['library-settings'], queryFn: readLibrarySettings })
  const [choice, setChoice] = useState<boolean | null>(null)
  const [saved, setSaved] = useState(false)
  const selected = choice ?? settings.data?.deleteLeftovers ?? true
  const save = useMutation({
    mutationFn: () => saveLibrarySettings(selected),
    onSuccess: (answer) => { setChoice(answer.deleteLeftovers); setSaved(true); void queries.invalidateQueries({ queryKey: ['library-settings'] }) },
  })
  return <SettingsPage title="Library" lede="The Library root and what filing may remove from a completed download directory. Changes apply to the next tidy-up pass.">
    <LibraryRootForm submitLabel="Save Library root" />
    <form className={formStyles.form} onSubmit={(event) => { event.preventDefault(); setSaved(false); save.mutate() }}>
      <label className={formStyles.label}>After filing</label>
      <label><input id="delete-library-leftovers" name="deleteLeftovers" type="checkbox" checked={selected} onChange={(event) => { setChoice(event.target.checked); setSaved(false) }} /> Delete known leftover files from directory-shaped SABnzbd storage</label>
      <p className={formStyles.hint}>Enabled by default. Only .nfo, .par2, .sfv, .srr, .url, .txt, .jpg and .png are removed. Unsupported files and non-empty directories remain, and the parent directory of a single-file download is never tidied.</p>
      {saved && <p className={formStyles.done}>Library settings saved.</p>}
      {save.isError && <p className={formStyles.refusal}>{String(save.error)}</p>}
      <button className={formStyles.button} type="submit" disabled={settings.isPending || save.isPending}>Save Library settings</button>
    </form>
  </SettingsPage>
}
