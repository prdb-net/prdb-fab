import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  readIdentificationSettings,
  saveIdentificationSettings,
  type AfterDownloadGateChoice,
} from '../api/client.ts'
import { SettingsPage } from './SettingsPage.tsx'
import formStyles from '../onboarding/Onboarding.module.css'

export function IdentificationScreen() {
  const queryClient = useQueryClient()
  const settings = useQuery({
    queryKey: ['identification-settings'],
    queryFn: readIdentificationSettings,
  })
  const [choice, setChoice] = useState<AfterDownloadGateChoice | null>(null)
  const [saved, setSaved] = useState(false)

  const selected = choice ?? settings.data?.afterDownload ?? 'ExactAndStrong'
  const save = useMutation({
    mutationFn: () => saveIdentificationSettings(selected),
    onSuccess: (answer) => {
      setChoice(answer.afterDownload)
      setSaved(true)
      void queryClient.invalidateQueries({ queryKey: ['identification-settings'] })
    },
  })

  return (
    <SettingsPage
      title="Identification"
      lede="The after-download gate is a set of names, not a score. It is applied again to identified files that are still waiting when this changes."
    >
      <form
        className={formStyles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setSaved(false)
          save.mutate()
        }}
      >
        <label className={formStyles.label}>Proceed to filing after</label>
        <label>
          <input
            type="radio"
            name="after-download"
            value="ExactAndStrong"
            checked={selected === 'ExactAndStrong'}
            onChange={() => { setChoice('ExactAndStrong'); setSaved(false) }}
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
            checked={selected === 'ExactOnly'}
            onChange={() => { setChoice('ExactOnly'); setSaved(false) }}
          />{' '}
          Exact identification only
        </label>
        <p className={formStyles.hint}>
          Strong and every other answer wait as Unidentified.
        </p>

        {settings.isError && <p className={formStyles.refusal}>{String(settings.error)}</p>}
        {save.isError && <p className={formStyles.refusal}>{String(save.error)}</p>}
        {saved && <p className={formStyles.done}>The after-download gate has been saved.</p>}

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
