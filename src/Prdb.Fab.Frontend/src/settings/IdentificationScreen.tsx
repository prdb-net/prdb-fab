import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  readIdentificationSettings,
  saveIdentificationSettings,
  type AfterDownloadGateChoice,
  type BeforeDownloadGateChoice,
} from '../api/client.ts'
import { SettingsPage } from './SettingsPage.tsx'
import formStyles from '../onboarding/Onboarding.module.css'

export function IdentificationScreen() {
  const queryClient = useQueryClient()
  const settings = useQuery({
    queryKey: ['identification-settings'],
    queryFn: readIdentificationSettings,
  })
  const [beforeChoice, setBeforeChoice] = useState<BeforeDownloadGateChoice | null>(null)
  const [afterChoice, setAfterChoice] = useState<AfterDownloadGateChoice | null>(null)
  const [saved, setSaved] = useState(false)

  const before = beforeChoice ?? settings.data?.beforeDownload ?? 'ThroughProbable'
  const after = afterChoice ?? settings.data?.afterDownload ?? 'ExactAndStrong'
  const save = useMutation({
    mutationFn: () => saveIdentificationSettings(before, after),
    onSuccess: (answer) => {
      setBeforeChoice(answer.beforeDownload)
      setAfterChoice(answer.afterDownload)
      setSaved(true)
      void queryClient.invalidateQueries({ queryKey: ['identification-settings'] })
    },
  })

  return (
    <SettingsPage
      title="Identification"
      lede="Both gates are fixed sets of named identification answers, not numeric scores. Saving only queues local reconsideration; it never submits a Download in this request."
    >
      <form
        className={formStyles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setSaved(false)
          save.mutate()
        }}
      >
        <label className={formStyles.label}>Allow an automatic Download after</label>
        <GateChoice
          name="before-download"
          value="ThroughProbable"
          selected={before}
          setSelected={(value) => { setBeforeChoice(value); setSaved(false) }}
          label="Exact, Strong, or Probable identification"
          hint="The default. Every matched Wanted Release through Probable may reach an Automation Rule."
        />
        <GateChoice
          name="before-download"
          value="ExactAndStrong"
          selected={before}
          setSelected={(value) => { setBeforeChoice(value); setSaved(false) }}
          label="Exact or Strong identification"
          hint="Probable and every other answer are held before any automatic submission."
        />
        <GateChoice
          name="before-download"
          value="ExactOnly"
          selected={before}
          setSelected={(value) => { setBeforeChoice(value); setSaved(false) }}
          label="Exact identification only"
          hint="Only an Exact match may be submitted automatically."
        />

        <label className={formStyles.label}>Proceed to filing after</label>
        <label>
          <input
            type="radio"
            name="after-download"
            value="ExactAndStrong"
            checked={after === 'ExactAndStrong'}
            onChange={() => { setAfterChoice('ExactAndStrong'); setSaved(false) }}
          />{' '}
          Exact or Strong identification
        </label>
        <p className={formStyles.hint}>
          The default. Both named answers may proceed without review.
        </p>

        <label>
          <input
            type="radio"
            name="after-download"
            value="ExactOnly"
            checked={after === 'ExactOnly'}
            onChange={() => { setAfterChoice('ExactOnly'); setSaved(false) }}
          />{' '}
          Exact identification only
        </label>
        <p className={formStyles.hint}>
          Strong and every other answer wait as Unidentified.
        </p>

        {settings.isError && <p className={formStyles.refusal}>{String(settings.error)}</p>}
        {save.isError && <p className={formStyles.refusal}>{String(save.error)}</p>}
        {saved && <p className={formStyles.done}>Both identification gates have been saved.</p>}

        <button
          className={formStyles.button}
          type="submit"
          disabled={settings.isPending || save.isPending}
        >
          Save identification settings
        </button>
      </form>
    </SettingsPage>
  )
}

function GateChoice({
  name,
  value,
  selected,
  setSelected,
  label,
  hint,
}: {
  name: string
  value: BeforeDownloadGateChoice
  selected: BeforeDownloadGateChoice
  setSelected: (choice: BeforeDownloadGateChoice) => void
  label: string
  hint: string
}) {
  return (
    <>
      <label>
        <input
          type="radio"
          name={name}
          value={value}
          checked={selected === value}
          onChange={() => setSelected(value)}
        />{' '}
        {label}
      </label>
      <p className={formStyles.hint}>{hint}</p>
    </>
  )
}
