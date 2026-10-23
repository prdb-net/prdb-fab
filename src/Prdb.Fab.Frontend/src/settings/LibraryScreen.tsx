import { Link } from 'react-router'
import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { readLibrarySettings, saveLibrarySettings, type LibrarySettingsState } from '../api/client.ts'
import { LibraryRootForm } from '../onboarding/LibraryRootForm.tsx'
import { Fieldset, Switch, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { InfoHelp } from '../ui/InfoHelp.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

/** ADR 0005's fixed list, which is the reassuring half of the sentence. */
const leftovers = ['.nfo', '.par2', '.sfv', '.srr', '.url', '.txt', '.jpg', '.png']

export function LibrarySettingsScreen() {
  const settings = useQuery({ queryKey: ['library-settings'], queryFn: readLibrarySettings })

  return (
    <SettingsPage
      title="Library"
      lede="Choose where Videos are filed and how leftovers are cleaned up."
    >
      <p><Link to="/settings/library/collection">Prepare or review the existing collection</Link></p>
      <Loaded query={settings} of="the Library settings">
        {(held) => (
          <>
            <RootInForce held={held} />
            <LeftoverForm held={held} />
          </>
        )}
      </Loaded>
    </SettingsPage>
  )
}

/**
 * The root in force, named, and the form opening with it.
 *
 * `LibraryRootForm` opens with `useState('')` on both its entry points, which is
 * right in onboarding — there is no answer yet — and wrong here, where there is
 * one and an empty field offers to replace something it does not name.
 * `ConnectionsState` and `LibrarySettingsState` have both carried the root all
 * along and nothing read it.
 */
function RootInForce({ held }: { held: LibrarySettingsState }) {
  return (
    <>
      <h2 className={styles.heading}>Where the library is</h2>

      {held.libraryRoot ? (
        <code className={formStyles.path}>{held.libraryRoot}</code>
      ) : (
        <p className={styles.detail}>
          Set a Library root before filing Videos.
        </p>
      )}

      {/*
        What replacing it does, said before it is replaced. ADR 0017 computes a
        filed path once and then records it: `LibraryEntryRow.EntryDirectory`
        and `VideoFileRow.FiledPath` are stored whole, against the root that was
        in force at the time of filing. Nothing here moves a file or rewrites a
        record, and the page used to say nothing at all about that.

        The tool can re-root — a restored Backup carries every path relative to
        its roots and places them all in one transaction — but that is the
        Restore path and this is not it.
      */}
      <p className={styles.detail}>
        Changing the root affects future files only. Existing files stay where they are;
        keep their old path mounted.
      </p>
      <InfoHelp label="Moving an existing Library">
        Filed paths are recorded when each file arrives. Changing this setting does not
        move files or rewrite those records. To move the Library to another machine,
        restore a Backup there and supply the new roots during setup.
      </InfoHelp>

      <LibraryRootForm
        submitLabel="Save Library root"
        initialPath={held.libraryRoot ?? ''}
      />
    </>
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
      <h2 className={styles.heading}>After filing</h2>

      {/*
        ADR 0005 fixed this list rather than letting patterns be typed into it,
        and that is the reassuring part of the sentence — so it is in the open
        rather than buried in a hint.
      */}
      <p className={styles.detail}>
        Cleanup only removes these file types:
      </p>
      <ul className={styles.extensions}>
        {leftovers.map((extension) => (
          <li key={extension}>
            <code>{extension}</code>
          </li>
        ))}
      </ul>
      <p className={styles.detail}>
        Other file types and the parent directory of a single-file Download are kept.
      </p>

      <Fieldset legend="What filing may remove">
        <Switch
          checked={deleteLeftovers}
          onChange={(checked) => { setDeleteLeftovers(checked); setSaved(false) }}
          label="Delete leftovers after filing"
          hint="Only after all files in the download directory are resolved."
          help={
            <>
              Enabled by default. Cleanup waits until all files in the download
              directory have been filed or resolved in the Review Queue.
              When disabled, you must remove leftover files yourself.
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
            Could not confirm that the Library settings were saved. Reload to check the current values.
          </Verdict>
        )}
        {saved && <Verdict tone="done">Library settings saved.</Verdict>}
      </SaveBar>
    </form>
  )
}
