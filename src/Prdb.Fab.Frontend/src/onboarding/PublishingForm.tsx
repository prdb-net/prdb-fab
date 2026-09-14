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

/**
 * ADR 0064's explanation and the switch it gates, as one piece used in two
 * places: the onboarding step, and the third channel of the Reporting form.
 *
 * It is presentational on purpose. Whoever renders it owns the value and owns
 * the save, because the settings route saves three switches at once and the
 * onboarding step saves one while carrying the other two as they stand — and a
 * component that saved for itself would have to know which of those it was in.
 */
export function PublicationChannel({
  checked,
  onChange,
  explained,
}: {
  checked: boolean
  onChange: (checked: boolean) => void
  /**
   * Whether this has been saved once. False is the state ADR 0064 gates on:
   * the switch says yes and nothing has been published, which is worth saying
   * here rather than leaving to the Status page nobody is looking at.
   */
  explained: boolean
}) {
  return (
    <>
      {!explained && (
        <Verdict tone="warning">
          Nothing has been published yet. Generating and sending starts once this
          is saved &mdash; whichever way the switch is left.
        </Verdict>
      )}

      <Switch
        checked={checked}
        onChange={onChange}
        label="Publish generated previews to prdb"
        hint={
          <>
            This one is <strong>public</strong>. A preview published here is
            shown to everyone who looks at that Video on prdb, under your
            account, after a moderator has seen it. Turning this off later stops
            future publications; <strong>prdb has no retraction</strong> for one
            it has already accepted.
          </>
        }
      />

      <Sends summary="What a published preview contains">
        <li>
          One JPEG: a grid of small pictures taken from the video file, and a
          WebVTT saying which picture is which second.
        </li>
        <li>The prdb Video id the file is filed under, and the file&rsquo;s osHash.</li>
        <li>
          That it is a Sprite Sheet, and its position among your previews of that
          Video.
        </li>
        <li>
          <strong>Not</strong> the video file, not its name, not where it is on
          disk, and no sound.
        </li>
      </Sends>

      <p className={formStyles.hint}>
        Only files filed from now on. Videos the library already holds are left
        alone until they are asked for by name.
      </p>
    </>
  )
}

/**
 * The onboarding step: the same channel, with a save of its own.
 *
 * The other two switches are carried through exactly as they stand rather than
 * defaulted, because this form is not about them — saving here must not be a
 * way of silently resetting a channel somebody has already had an opinion
 * about. On a fresh installation that is both of them on, which is ADR 0051's
 * shipped default.
 */
export function PublishingForm({ onSaved }: { onSaved?: () => void }) {
  const settings = useQuery({ queryKey: ['reporting-settings'], queryFn: readReportingSettings })

  return (
    <Loaded query={settings} of="the Reporting settings">
      {(held) => <Form held={held} onSaved={onSaved} />}
    </Loaded>
  )
}

function Form({ held, onSaved }: { held: ReportingSettingsState; onSaved?: () => void }) {
  const queries = useQueryClient()
  const [publish, setPublish] = useState(held.publishGeneratedPreviews)
  const [failure, setFailure] = useState<string | null>(null)

  const save = useMutation({
    mutationFn: () =>
      saveReportingSettings(held.reportFulfilments, held.reportConfirmedAssignments, publish),
    onSuccess: async () => {
      await queries.invalidateQueries({ queryKey: ['reporting-settings'] })
      onSaved?.()
    },
    onError: () =>
      setFailure('The choice could not be sent. The tool may have stopped; the log says.'),
  })

  return (
    <form
      className={formStyles.form}
      onSubmit={(event) => {
        event.preventDefault()
        setFailure(null)
        save.mutate()
      }}
    >
      <Fieldset legend="Publishing previews">
        <PublicationChannel
          checked={publish}
          onChange={setPublish}
          explained={held.previewPublicationExplained}
        />
      </Fieldset>

      {/*
        Always available, never dirty-gated. Leaving the switch exactly as it
        arrived is an answer — it is the shipped default being accepted — and a
        button that waits for a change would make the step unanswerable for
        anybody who agrees with it.
      */}
      <SaveBar
        label="Save and continue"
        dirty
        pending={save.isPending}
        unsaved="Setting up waits here until this is answered."
      >
        {failure && <Verdict tone="refusal">{failure}</Verdict>}
      </SaveBar>
    </form>
  )
}
