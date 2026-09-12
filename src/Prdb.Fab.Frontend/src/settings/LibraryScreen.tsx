import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { readLibrarySettings, saveLibrarySettings, type LibrarySettingsState } from '../api/client.ts'
import { LibraryRootForm } from '../onboarding/LibraryRootForm.tsx'
import { Fieldset, Switch, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'

export function LibrarySettingsScreen() {
  const settings = useQuery({ queryKey: ['library-settings'], queryFn: readLibrarySettings })

  return (
    <SettingsPage
      title="Library"
      lede="The Library root and what filing may remove from a completed download directory. Changes apply to the next tidy-up pass."
    >
      <LibraryRootForm submitLabel="Save Library root" />

      <Loaded query={settings} of="the Library settings">
        {(held) => <LeftoverForm held={held} />}
      </Loaded>
    </SettingsPage>
  )
}

function LeftoverForm({ held }: { held: LibrarySettingsState }) {
  const queries = useQueryClient()
  const [deleteLeftovers, setDeleteLeftovers] = useState(held.deleteLeftovers)
  const [stored, setStored] = useState(held.deleteLeftovers)
  const [saved, setSaved] = useState(false)

  const save = useMutation({
    mutationFn: () => saveLibrarySettings(deleteLeftovers),
    onSuccess: (answer) => {
      setDeleteLeftovers(answer.deleteLeftovers)
      setStored(answer.deleteLeftovers)
      setSaved(true)
      void queries.invalidateQueries({ queryKey: ['library-settings'] })
    },
  })

  return (
    <form
      className={formStyles.form}
      onSubmit={(event) => {
        event.preventDefault()
        setSaved(false)
        save.mutate()
      }}
    >
      <Fieldset legend="After filing">
        <Switch
          checked={deleteLeftovers}
          onChange={(checked) => { setDeleteLeftovers(checked); setSaved(false) }}
          label="Delete known leftover files from directory-shaped SABnzbd storage"
          hint={
            <>
              Enabled by default. Only .nfo, .par2, .sfv, .srr, .url, .txt, .jpg and
              .png are removed. Unsupported files and non-empty directories remain,
              and the parent directory of a single-file download is never tidied.
            </>
          }
        />
      </Fieldset>

      <SaveBar
        label="Save Library settings"
        dirty={deleteLeftovers !== stored}
        pending={save.isPending}
      >
        {save.isError && (
          <Verdict tone="refusal">
            The Library settings could not be saved. Nothing was changed.
          </Verdict>
        )}
        {saved && <Verdict tone="done">Library settings saved.</Verdict>}
      </SaveBar>
    </form>
  )
}
