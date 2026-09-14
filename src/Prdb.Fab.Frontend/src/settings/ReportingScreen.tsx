import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  readReportingSettings,
  saveReportingSettings,
  type ReportingSettingsState,
} from '../api/client.ts'
import { Fieldset, Sends, Switch, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { PublicationChannel } from '../onboarding/PublishingForm.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

export function ReportingScreen() {
  const settings = useQuery({ queryKey: ['reporting-settings'], queryFn: readReportingSettings })

  return (
    <SettingsPage
      title="Reporting"
      lede="What may leave this installation. Three independent channels, all on by default — opting into one is not opting into the other, and the third one publishes in public."
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
  const [publish, setPublish] = useState(held.publishGeneratedPreviews)
  const [stored, setStored] = useState({
    fulfilments: held.reportFulfilments,
    assignments: held.reportConfirmedAssignments,
    publish: held.publishGeneratedPreviews,
  })
  const [saved, setSaved] = useState(false)

  const changed =
    fulfilments !== stored.fulfilments
    || assignments !== stored.assignments
    || publish !== stored.publish

  const save = useMutation({
    mutationFn: () => saveReportingSettings(fulfilments, assignments, publish),
    onSuccess: (answer) => {
      setFulfilments(answer.reportFulfilments)
      setAssignments(answer.reportConfirmedAssignments)
      setPublish(answer.publishGeneratedPreviews)
      setStored({
        fulfilments: answer.reportFulfilments,
        assignments: answer.reportConfirmedAssignments,
        publish: answer.publishGeneratedPreviews,
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

      <Fieldset legend="Published previews">
        <PublicationChannel
          checked={publish}
          onChange={(checked) => { setPublish(checked); setSaved(false) }}
          explained={held.previewPublicationExplained}
        />
      </Fieldset>

      <SaveBar
        label="Save Reporting settings"
        dirty={changed || !held.previewPublicationExplained}
        pending={save.isPending}
        // ADR 0064: an installation that has never saved has something to save
        // even when every switch is exactly as it shipped, and saying a change
        // was made would be a small lie in the one place this is asking to be
        // believed.
        unsaved={changed ? undefined : 'Publishing is waiting to be answered.'}
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
