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
import styles from './Settings.module.css'

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
      lede="Two gates over the same ladder of named answers. Both are fixed sets — ADR 0006 makes this set membership rather than an order, which is why there is no slider and no free choice of members."
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
      {/*
        The relationship between the two is the whole point of ADR 0006 and the
        reason ADR 0020 gave them a page together, and the page never said it.
      */}
      <p className={styles.detail}>
        The <strong>first</strong> gate is checked before a Download is submitted
        without being asked. The <strong>second</strong> is checked before an
        arrival is filed into the library. The first is meant to be the{' '}
        <strong>looser</strong> of the two: a wrong download costs bandwidth and
        can be thrown away, while a wrong filing puts the wrong file in the
        library under the right name.
      </p>

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
          Saving only queues local reconsideration. It submits no Download in this
          request, and it moves no file: what changes is which Releases the
          background routines may act on from now on.
        </Verdict>
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
        These two are the wrong way round. The gate before a Download is stricter
        than the one before filing, so the tool will refuse to fetch things it
        would have been willing to file &mdash; and the error it is being careful
        about there costs only bandwidth. That is allowed, and it is worth being
        sure it is what you meant.
      </Verdict>
    )
  }

  if (difference === 0) {
    return (
      <Verdict tone="warning">
        Both gates admit the same answers, so the first one decides nothing that
        the second would not have decided anyway. Nothing is wrong with it; it
        simply means arrivals are never held back by a check the download already
        passed.
      </Verdict>
    )
  }

  return null
}
