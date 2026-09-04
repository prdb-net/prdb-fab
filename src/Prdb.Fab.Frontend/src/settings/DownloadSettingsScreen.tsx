import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  readDownloadSettings,
  saveDownloadSettings,
  type PreferredDownloadQuality,
} from '../api/client.ts'
import formStyles from '../onboarding/Onboarding.module.css'
import { SettingsPage } from './SettingsPage.tsx'

const qualities: readonly PreferredDownloadQuality[] = ['P2160', 'P1080', 'P720', 'P480']

export function DownloadSettingsScreen() {
  const queryClient = useQueryClient()
  const settings = useQuery({
    queryKey: ['download-settings'],
    queryFn: readDownloadSettings,
  })
  const [choice, setChoice] = useState<PreferredDownloadQuality | null>(null)
  const [saved, setSaved] = useState(false)
  const preferred = choice ?? settings.data?.preferredQuality ?? 'P2160'
  const save = useMutation({
    mutationFn: () => saveDownloadSettings(preferred),
    onSuccess: (answer) => {
      setChoice(answer.preferredQuality)
      setSaved(true)
      void queryClient.invalidateQueries({ queryKey: ['download-settings'] })
    },
  })

  return (
    <SettingsPage
      title="Downloads"
      lede="Choose the highest Quality the Download button on a Catalogue card should request."
    >
      <form
        className={formStyles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setSaved(false)
          save.mutate()
        }}
      >
        <label className={formStyles.label} htmlFor="preferred-download-quality">
          Preferred highest Quality
        </label>
        <select
          className={formStyles.field}
          disabled={settings.isPending || save.isPending}
          id="preferred-download-quality"
          value={preferred}
          onChange={(event) => {
            setChoice(event.target.value as PreferredDownloadQuality)
            setSaved(false)
          }}
        >
          {qualities.map((quality) => (
            <option key={quality} value={quality}>{labelOf(quality)}</option>
          ))}
        </select>
        <p className={formStyles.hint}>
          Direct Download first tries this Quality, then each lower one. It never
          substitutes a known higher Quality. Indexers do not provide a dependable
          Quality field, so fab recognises common 2160p, 4K, UHD, 1080p, FHD, 720p
          and 480p Release-name tags. An unlabelled Release is the final fallback.
        </p>

        {settings.isError && <p className={formStyles.refusal}>{String(settings.error)}</p>}
        {save.isError && <p className={formStyles.refusal}>{String(save.error)}</p>}
        {saved && <p className={formStyles.done}>The preferred Download Quality has been saved.</p>}

        <button
          className={formStyles.button}
          disabled={settings.isPending || save.isPending}
          type="submit"
        >
          Save Download settings
        </button>
      </form>
    </SettingsPage>
  )
}

function labelOf(quality: PreferredDownloadQuality): string {
  return `${quality.slice(1)}p`
}
