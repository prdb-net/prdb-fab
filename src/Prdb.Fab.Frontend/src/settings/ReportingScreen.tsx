import { useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  readReportingSettings,
  saveReportingSettings,
  type ReportingSettingsState,
} from '../api/client.ts'
import { Fieldset, Switch, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

export function ReportingScreen() {
  const settings = useQuery({ queryKey: ['reporting-settings'], queryFn: readReportingSettings })

  return (
    <SettingsPage
      title="Reporting"
      lede="What may leave this installation. Two independent channels, both on by default, sharing one governed background routine — opting into one is not opting into the other."
    >
      <Loaded query={settings} of="the Reporting settings">
        {(held) => <ReportingForm held={held} />}
      </Loaded>
    </SettingsPage>
  )
}

function ReportingForm({ held }: { held: ReportingSettingsState }) {
  const queries = useQueryClient()
  const [fulfilments, setFulfilments] = useState(held.reportFulfilments)
  const [assignments, setAssignments] = useState(held.reportConfirmedAssignments)
  const [stored, setStored] = useState({
    fulfilments: held.reportFulfilments,
    assignments: held.reportConfirmedAssignments,
  })
  const [saved, setSaved] = useState(false)

  const save = useMutation({
    mutationFn: () => saveReportingSettings(fulfilments, assignments),
    onSuccess: (answer) => {
      setFulfilments(answer.reportFulfilments)
      setAssignments(answer.reportConfirmedAssignments)
      setStored({
        fulfilments: answer.reportFulfilments,
        assignments: answer.reportConfirmedAssignments,
      })
      setSaved(true)
      void queries.invalidateQueries({ queryKey: ['reporting-settings'] })
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
      <Fieldset legend="Fulfilments">
        {/*
          ADR 0019 requires the count be shown *before* the switch is thrown —
          VISION.md's "stated plainly" can only change a decision beforehand —
          and it used to be the first clause of a five-line grey hint. It is not
          rendered at all until it is read: this form is mounted after that, so
          a confident zero is not a state it can be in.
        */}
        <Backlog
          count={Number(held.fulfilmentBacklog)}
          one="local Fulfilment change is waiting to be sent."
          many="local Fulfilment changes are waiting to be sent."
          none="Nothing is waiting to be sent."
        />

        <Switch
          checked={fulfilments}
          onChange={(checked) => { setFulfilments(checked); setSaved(false) }}
          label="Report which wanted Videos are held"
          hint={
            <>
              Turning this off stops future reports. It does not retract anything
              already at prdb: only a person retracts a Fulfilment, and a missing
              file or an unmounted library never does.
            </>
          }
        />

        <Sends summary="What a Fulfilment report contains">
          <li>The prdb Video id.</li>
          <li>Whether it is held.</li>
          <li>When it was really filed.</li>
          <li>
            The highest prdb Quality the Library Entry truthfully clears. Below
            720p is left unstated rather than guessed at.
          </li>
          <li>The application, as Other. No external identifier is sent.</li>
        </Sends>
      </Fieldset>

      <Fieldset legend="Confirmed Assignments">
        <Backlog
          count={Number(held.confirmedAssignmentBacklog)}
          one="assignment confirmed in the Review Queue is waiting to be sent."
          many="assignments confirmed in the Review Queue are waiting to be sent."
          none="Nothing is waiting to be sent."
        />

        <Switch
          checked={assignments}
          onChange={(checked) => { setAssignments(checked); setSaved(false) }}
          label="Report the hash-to-Video answers you confirm by hand"
          hint={
            <>
              Turning this off stops future submissions. <strong>prdb has no
              retraction for an assignment already sent</strong> &mdash; unlike a
              Fulfilment, there is nothing to undo it with.
            </>
          }
        />

        <Sends summary="What an assignment report contains">
          <li>The prdb Video id and the file&rsquo;s osHash.</li>
          <li>Its size, and its recorded runtime, width, height and video codec.</li>
          <li>The name it arrived under, and the Release name.</li>
          <li>The marker UserConfirmed. The file is not probed again to send it.</li>
        </Sends>
      </Fieldset>

      <SaveBar
        label="Save Reporting settings"
        dirty={fulfilments !== stored.fulfilments || assignments !== stored.assignments}
        pending={save.isPending}
      >
        {save.isError && (
          <Verdict tone="refusal">
            The Reporting settings could not be saved. Neither channel was changed.
          </Verdict>
        )}
        {saved && <Verdict tone="done">Reporting settings saved.</Verdict>}
      </SaveBar>
    </form>
  )
}

/** The count, as a fact beside the decision rather than inside a paragraph. */
function Backlog({
  count,
  one,
  many,
  none,
}: {
  count: number
  one: string
  many: string
  none: string
}) {
  return (
    <p className={styles.backlog}>
      {count === 0 ? none : <><strong>{count}</strong> {count === 1 ? one : many}</>}
    </p>
  )
}

/**
 * What a channel sends: available, and out of the way.
 *
 * It was a wall of prose at exactly the moment somebody is deciding, which is
 * the moment they will not read it. Folded, the sentence that matters is the
 * one at the switch.
 */
function Sends({ summary, children }: { summary: string; children: ReactNode }) {
  return (
    <details className={styles.sends}>
      <summary>{summary}</summary>
      <ul>{children}</ul>
    </details>
  )
}
