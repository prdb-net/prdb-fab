import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { readLibrarySettings, saveLibrarySettings, type LibrarySettingsState } from '../api/client.ts'
import { LibraryRootForm } from '../onboarding/LibraryRootForm.tsx'
import { Fieldset, Switch, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

/** ADR 0005's fixed list, which is the reassuring half of the sentence. */
const leftovers = ['.nfo', '.par2', '.sfv', '.srr', '.url', '.txt', '.jpg', '.png']

export function LibrarySettingsScreen() {
  const settings = useQuery({ queryKey: ['library-settings'], queryFn: readLibrarySettings })

  return (
    <SettingsPage
      title="Library"
      lede="Where filing puts things, and what it may remove from a completed download directory once nothing in there is still undecided."
    >
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
          No root has been answered yet, so nothing can be filed.
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
        A new root is used for <strong>everything filed from then on</strong>.
        Entries that are already filed keep the paths they were filed at: nothing
        is moved and no record is rewritten, because a filed path is computed once
        and then recorded. So changing it here is for an installation that has not
        filed anything yet, or one that is keeping the old path mounted. Moving a
        library that already holds something is what restoring a Backup on the new
        machine is for &mdash; that places every recorded path against the roots
        you answer, in one transaction.
      </p>

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
        Only these eight kinds of file are ever removed, and the list is fixed
        rather than something patterns are written into:
      </p>
      <ul className={styles.extensions}>
        {leftovers.map((extension) => (
          <li key={extension}>
            <code>{extension}</code>
          </li>
        ))}
      </ul>
      <p className={styles.detail}>
        Two things are never touched whatever this is set to: a file whose kind is
        not on that list, and the parent directory of a single-file download.
      </p>

      <Fieldset legend="What filing may remove">
        <Switch
          checked={deleteLeftovers}
          onChange={(checked) => { setDeleteLeftovers(checked); setSaved(false) }}
          label="Delete those leftovers from directory-shaped SABnzbd storage"
          hint={
            <>
              Enabled by default, and applied only once nothing in that directory
              is still waiting on a decision. Turning it off leaves the download
              directory to fill up, which is yours to empty.
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
