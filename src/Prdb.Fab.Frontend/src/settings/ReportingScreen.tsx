import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { readReportingSettings, saveReportingSettings } from '../api/client.ts'
import { SettingsPage } from './SettingsPage.tsx'
import formStyles from '../onboarding/Onboarding.module.css'

export function ReportingScreen() {
  const queries = useQueryClient()
  const settings = useQuery({ queryKey: ['reporting-settings'], queryFn: readReportingSettings })
  const [fulfilments, setFulfilments] = useState<boolean | null>(null)
  const [assignments, setAssignments] = useState<boolean | null>(null)
  const [saved, setSaved] = useState(false)
  const reportFulfilments = fulfilments ?? settings.data?.reportFulfilments ?? true
  const reportConfirmedAssignments = assignments
    ?? settings.data?.reportConfirmedAssignments
    ?? true
  const save = useMutation({
    mutationFn: () => saveReportingSettings(reportFulfilments, reportConfirmedAssignments),
    onSuccess: (answer) => {
      setFulfilments(answer.reportFulfilments)
      setAssignments(answer.reportConfirmedAssignments)
      setSaved(true)
      void queries.invalidateQueries({ queryKey: ['reporting-settings'] })
    },
  })

  return (
    <SettingsPage
      title="Reporting"
      lede="Both channels are enabled by default, remain independently configurable, and use the same governed background routine."
    >
      <form
        className={formStyles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setSaved(false)
          save.mutate()
        }}
      >
        <label>
          <input
            type="checkbox"
            checked={reportFulfilments}
            onChange={(event) => { setFulfilments(event.target.checked); setSaved(false) }}
          />{' '}
          Report Fulfilments
        </label>
        <p className={formStyles.hint}>
          {settings.data?.fulfilmentBacklog ?? 0} held Wanted Video(s) are waiting. Enabling
          sends the Video, held state, real filing time and the highest prdb Quality the
          Library Entry truthfully clears. Quality below 720p is left unstated; the
          application is Other and no external ID is sent.
        </p>
        <p className={formStyles.hint}>
          Turning this off stops future reports. It does not retract anything already at
          prdb; a missing file or mount never retracts a Fulfilment either.
        </p>

        <label>
          <input
            type="checkbox"
            checked={reportConfirmedAssignments}
            onChange={(event) => { setAssignments(event.target.checked); setSaved(false) }}
          />{' '}
          Report Confirmed Assignments
        </label>
        <p className={formStyles.hint}>
          {settings.data?.confirmedAssignmentBacklog ?? 0} assignment(s) confirmed in the
          Review Queue are waiting. Enabling sends the Video, osHash, file size, recorded
          runtime, width, height and video codec, the arrival file name and Release name,
          marked UserConfirmed. Files are not probed again.
        </p>
        <p className={formStyles.hint}>
          Turning this off stops future submissions. prdb has no retraction for an
          assignment already sent.
        </p>

        {settings.isError && <p className={formStyles.refusal}>{String(settings.error)}</p>}
        {save.isError && <p className={formStyles.refusal}>{String(save.error)}</p>}
        {saved && <p className={formStyles.done}>Reporting settings saved.</p>}

        <button
          className={formStyles.button}
          type="submit"
          disabled={settings.isPending || save.isPending}
        >
          Save Reporting settings
        </button>
      </form>
    </SettingsPage>
  )
}
