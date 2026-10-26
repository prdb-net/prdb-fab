import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { readNotificationSettings, saveNotificationSettings, type NotificationSettingsState } from '../api/client.ts'
import { Field, Switch, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'

export function NotificationsScreen() {
  const settings = useQuery({ queryKey: ['notification-settings'], queryFn: readNotificationSettings, refetchInterval: 10000 })
  return <SettingsPage title="Notifications" lede="Receive grouped messages when the installation needs attention, and when it recovers.">
    <p>The destination receives a JSON POST with source, state, stages (stage and gaps), and reviewFiles. Connect a webhook receiver that accepts this format. No Video titles, paths, artwork or API keys are sent. The destination URL is included in Backups.</p>
    <p>Changes are grouped into at most one message every 30 minutes. Unchanged conditions do not repeat. Brakes are excluded because they reflect configured behaviour.</p>
    <Loaded query={settings} of="notification settings">{held => <>
      <NotificationForm held={held} />
      <p>Last delivered: {held.lastDeliveredAt ? new Date(held.lastDeliveredAt).toLocaleString() : 'No delivery recorded'}.</p>
      {held.error && <Verdict tone="refusal">{held.error}</Verdict>}
    </>}</Loaded>
  </SettingsPage>
}

function NotificationForm({ held }: { held: NotificationSettingsState }) {
  const [enabled, setEnabled] = useState(held.enabled)
  const [stored, setStored] = useState(held.enabled)
  const [url, setUrl] = useState('')
  const queries = useQueryClient()
  const save = useMutation({
    mutationFn: () => saveNotificationSettings(enabled, url || null),
    onSuccess: answer => {
      if (!answer.accepted) return
      setStored(enabled)
      setUrl('')
      void queries.invalidateQueries({ queryKey: ['notification-settings'] })
    },
  })
  return <form className={formStyles.form} onSubmit={event => { event.preventDefault(); save.mutate() }}>
    <Switch checked={enabled} onChange={setEnabled} label="Send notifications" />
    <Field label="Webhook URL" hint={held.configured ? 'A URL is stored. Leave blank to keep it.' : 'An HTTP or HTTPS destination that accepts JSON POST requests.'}>
      {id => <input id={id} className={formStyles.field} type="url" autoComplete="off" value={url} onChange={event => setUrl(event.target.value)} />}
    </Field>
    <SaveBar label="Save notifications" dirty={enabled !== stored || url.length > 0} pending={save.isPending}>
      {save.data && <Verdict tone={save.data.accepted ? 'done' : 'refusal'}>{save.data.detail}</Verdict>}
      {save.isError && <Verdict tone="refusal">Could not save notification settings.</Verdict>}
    </SaveBar>
  </form>
}
