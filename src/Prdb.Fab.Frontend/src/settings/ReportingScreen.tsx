import { useState } from 'react'
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

export function ReportingScreen() {
  const settings = useQuery({ queryKey: ['reporting-settings'], queryFn: readReportingSettings })

  return (
    <SettingsPage
      title="Reporting"
      lede="Both channels are enabled by default, remain independently configurable, and use the same governed background routine."
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
      <Fieldset legend="What may be sent back to prdb">
        <Switch
          checked={fulfilments}
          onChange={(checked) => { setFulfilments(checked); setSaved(false) }}
          label="Report Fulfilments"
          hint={
            <>
              {held.fulfilmentBacklog} local Fulfilment change(s) are waiting.
              Enabling sends the Video, held state, real filing time and the highest
              prdb Quality the Library Entry truthfully clears. Deliberately deleting
              a Library Entry retracts that state. Quality below 720p is left
              unstated; the application is Other and no external ID is sent. Turning
              this off stops future reports. It does not retract anything already at
              prdb; a missing file or mount never retracts a Fulfilment either.
            </>
          }
        />

        <Switch
          checked={assignments}
          onChange={(checked) => { setAssignments(checked); setSaved(false) }}
          label="Report Confirmed Assignments"
          hint={
            <>
              {held.confirmedAssignmentBacklog} assignment(s) confirmed in the Review
              Queue are waiting. Enabling sends the Video, osHash, file size, recorded
              runtime, width, height and video codec, the arrival file name and
              Release name, marked UserConfirmed. Files are not probed again. Turning
              this off stops future submissions. prdb has no retraction for an
              assignment already sent.
            </>
          }
        />
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
