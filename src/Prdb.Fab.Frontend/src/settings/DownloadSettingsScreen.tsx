import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import {
  readDownloadSettings,
  saveDownloadSettings,
  type DownloadSettingsState,
  type PreferredDownloadQuality,
} from '../api/client.ts'
import { Field, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'

const qualities: readonly PreferredDownloadQuality[] = ['P2160', 'P1080', 'P720', 'P480']

export function DownloadSettingsScreen() {
  const settings = useQuery({
    queryKey: ['download-settings'],
    queryFn: readDownloadSettings,
  })

  return (
    <SettingsPage
      title="Downloads"
      lede="Choose the highest Quality the Download button on a Catalogue card should request."
    >
      <Loaded query={settings} of="the Download settings">
        {(held) => <DownloadSettingsForm held={held} />}
      </Loaded>
    </SettingsPage>
  )
}

/**
 * Mounted only once the read has landed, which is what lets the draft start
 * from the stored answer instead of from a default standing in for it
 * (ADR 0058).
 */
function DownloadSettingsForm({ held }: { held: DownloadSettingsState }) {
  const queryClient = useQueryClient()
  const [preferred, setPreferred] = useState<PreferredDownloadQuality>(held.preferredQuality)
  const [stored, setStored] = useState<PreferredDownloadQuality>(held.preferredQuality)
  const [saved, setSaved] = useState(false)

  const save = useMutation({
    mutationFn: () => saveDownloadSettings(preferred),
    onSuccess: (answer) => {
      setPreferred(answer.preferredQuality)
      setStored(answer.preferredQuality)
      setSaved(true)
      void queryClient.invalidateQueries({ queryKey: ['download-settings'] })
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
      <Field
        label="Preferred highest Quality"
        hint={
          <>
            Direct Download first tries this Quality, then each lower one. It never
            substitutes a known higher Quality. Indexers do not provide a dependable
            Quality field, so fab recognises common 2160p, 4K, UHD, 1080p, FHD, 720p
            and 480p Release-name tags. An unlabelled Release is the final fallback.
          </>
        }
      >
        {(id) => (
          <select
            id={id}
            className={formStyles.field}
            disabled={save.isPending}
            value={preferred}
            onChange={(event) => {
              setPreferred(event.target.value as PreferredDownloadQuality)
              setSaved(false)
            }}
          >
            {qualities.map((quality) => (
              <option key={quality} value={quality}>{labelOf(quality)}</option>
            ))}
          </select>
        )}
      </Field>

      <SaveBar
        label="Save Download settings"
        dirty={preferred !== stored}
        pending={save.isPending}
      >
        {save.isError && (
          <Verdict tone="refusal">
            The preferred Quality could not be saved. Nothing was changed.
          </Verdict>
        )}
        {saved && <Verdict tone="done">The preferred Download Quality has been saved.</Verdict>}
      </SaveBar>
    </form>
  )
}

function labelOf(quality: PreferredDownloadQuality): string {
  return `${quality.slice(1)}p`
}
