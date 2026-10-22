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
import { InfoHelp } from '../ui/InfoHelp.tsx'

/** How much each named set admits. Higher is stricter. */
const strictness: Record<string, number> = {
  ThroughProbable: 0,
  ExactAndStrong: 1,
  ExactOnly: 2,
}

export function IdentificationScreen() {
  const settings = useQuery({
    queryKey: ['identification-settings'],
    queryFn: readIdentificationSettings,
  })

  return (
    <SettingsPage
      title="Identification"
      lede="Choose which identifications allow automatic downloading and filing."
    >
      <Loaded query={settings} of="the identification settings">
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
      {/*
        The relationship between the two is the whole point of ADR 0006 and the
        reason ADR 0020 gave them a page together, and the page never said it.
      */}
      <InfoHelp label="Identification before and after downloading">
        Before downloading, prdb identifies the Release from its name. After
        downloading, the file itself is checked. Requiring stronger evidence
        before filing helps prevent a file being stored with another Video&apos;s
        title and artwork. Files that do not meet the filing requirement wait
        in the Review Queue.
      </InfoHelp>

      <Fieldset legend="Identification required for automatic Downloads">
        <Choice
          name="before-download"
          value="ThroughProbable"
          selected={before}
          onSelect={(value) => { setBefore(value); setSaved(false) }}
          label="Exact, Strong, or Probable identification"
          hint="Default. Accepts these results for Wanted Videos; an enabled Automation Rule must also permit the Download."
        />
        <Choice
          name="before-download"
          value="ExactAndStrong"
          selected={before}
          onSelect={(value) => { setBefore(value); setSaved(false) }}
          label="Exact or Strong identification"
          hint="Releases identified as Probable are not downloaded automatically."
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

      <Fieldset legend="Identification required for filing">
        <Choice
          name="after-download"
          value="ExactAndStrong"
          selected={after}
          onSelect={(value) => { setAfter(value); setSaved(false) }}
          label="Exact or Strong identification"
          hint="Default. These results pass the identification check for filing. Other file checks still apply."
        />
        <Choice
          name="after-download"
          value="ExactOnly"
          selected={after}
          onSelect={(value) => { setAfter(value); setSaved(false) }}
          label="Exact identification only"
          hint="Files without an Exact identification wait in the Review Queue as Unidentified."
        />
      </Fieldset>

      <Pairing before={before} after={after} />

      <SaveBar
        label="Save identification settings"
        dirty={before !== stored.before || after !== stored.after}
        pending={save.isPending}
      >
        {/*
          The most consequential sentence on the page, and it used to be in the
          lede in the same grey as everything else.
        */}
        <Verdict tone="warning">
          Saving schedules a background check of cached Releases under these settings.
          Eligible Releases may then download automatically. Saving does not
          immediately submit a Download or move a file.
        </Verdict>
        {save.isError && (
          <Verdict tone="refusal">
            Could not confirm that the identification settings were saved. Reload to check the current values.
          </Verdict>
        )}
        {saved && <Verdict tone="done">Identification settings saved.</Verdict>}
      </SaveBar>
    </form>
  )
}

/**
 * Where the chosen pair inverts ADR 0006's intent, said beside the choice — as
 * an observation and not a refusal.
 *
 * ADR 0020 made the floor structural by offering fixed sets rather than free
 * membership, and what remains is a combination a person may have meant. A tool
 * that refused it would be claiming to know an installation it cannot see.
 */
function Pairing({ before, after }: { before: string; after: string }) {
  const difference = strictness[before] - strictness[after]

  if (difference > 0) {
    return (
      <Verdict tone="warning">
        Automatic downloading requires an Exact identification, while filing also
        accepts Strong. A Release identified as Strong will therefore need a
        manual Download, even though a file identified as Strong can pass the
        filing check.
      </Verdict>
    )
  }

  if (difference === 0) {
    return (
      <Verdict tone="warning">
        Both settings accept the same identification results. Files are still
        checked after downloading and may receive a different result from the
        Release name.
      </Verdict>
    )
  }

  return null
}
