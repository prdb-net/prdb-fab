import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  readIdentificationSettings,
  saveIdentificationSettings,
  type AfterDownloadGateChoice,
  type BeforeDownloadGateChoice,
  type IdentificationSettingsState,
} from '../api/client.ts'
import { Choice, Fieldset, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'

export function IdentificationScreen() {
  const settings = useQuery({
    queryKey: ['identification-settings'],
    queryFn: readIdentificationSettings,
  })

  return (
    <SettingsPage
      title="Identification"
      lede="Both gates are fixed sets of named identification answers, not numeric scores. Saving only queues local reconsideration; it never submits a Download in this request."
    >
      <Loaded query={settings} of="the identification gates">
        {(held) => <IdentificationForm held={held} />}
      </Loaded>
    </SettingsPage>
  )
}

function IdentificationForm({ held }: { held: IdentificationSettingsState }) {
  const queryClient = useQueryClient()
  const [before, setBefore] = useState<BeforeDownloadGateChoice>(held.beforeDownload)
  const [after, setAfter] = useState<AfterDownloadGateChoice>(held.afterDownload)
  const [stored, setStored] = useState({ before: held.beforeDownload, after: held.afterDownload })
  const [saved, setSaved] = useState(false)

  const save = useMutation({
    mutationFn: () => saveIdentificationSettings(before, after),
    onSuccess: (answer) => {
      setBefore(answer.beforeDownload)
      setAfter(answer.afterDownload)
      setStored({ before: answer.beforeDownload, after: answer.afterDownload })
      setSaved(true)
      void queryClient.invalidateQueries({ queryKey: ['identification-settings'] })
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
      <Fieldset legend="Allow an automatic Download after">
        <Choice
          name="before-download"
          value="ThroughProbable"
          selected={before}
          onSelect={(value) => { setBefore(value); setSaved(false) }}
          label="Exact, Strong, or Probable identification"
          hint="The default. Every matched Wanted Release through Probable may reach an Automation Rule."
        />
        <Choice
          name="before-download"
          value="ExactAndStrong"
          selected={before}
          onSelect={(value) => { setBefore(value); setSaved(false) }}
          label="Exact or Strong identification"
          hint="Probable and every other answer are held before any automatic submission."
        />
        <Choice
          name="before-download"
          value="ExactOnly"
          selected={before}
          onSelect={(value) => { setBefore(value); setSaved(false) }}
          label="Exact identification only"
          hint="Only an Exact match may be submitted automatically."
        />
      </Fieldset>

      <Fieldset legend="Proceed to filing after">
        <Choice
          name="after-download"
          value="ExactAndStrong"
          selected={after}
          onSelect={(value) => { setAfter(value); setSaved(false) }}
          label="Exact or Strong identification"
          hint="The default. Both named answers may proceed without review."
        />
        <Choice
          name="after-download"
          value="ExactOnly"
          selected={after}
          onSelect={(value) => { setAfter(value); setSaved(false) }}
          label="Exact identification only"
          hint="Strong and every other answer wait as Unidentified."
        />
      </Fieldset>

      <SaveBar
        label="Save identification settings"
        dirty={before !== stored.before || after !== stored.after}
        pending={save.isPending}
      >
        {save.isError && (
          <Verdict tone="refusal">
            The identification gates could not be saved. Neither was changed.
          </Verdict>
        )}
        {saved && <Verdict tone="done">Both identification gates have been saved.</Verdict>}
      </SaveBar>
    </form>
  )
}
