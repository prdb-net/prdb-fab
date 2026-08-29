import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'

import {
  deleteAutomationRule,
  previewDeleteAutomationRule,
  readAutomationSettings,
  saveAutomaticDownloadCap,
  saveAutomationRule,
  type AutomationRuleView,
} from '../api/client.ts'
import formStyles from '../onboarding/Onboarding.module.css'
import styles from './AutomationScreen.module.css'
import { SettingsPage } from './SettingsPage.tsx'

export function AutomationScreen() {
  const { id } = useParams()
  return id ? <AutomationRuleScreen id={id} /> : <AutomationOverview />
}

function AutomationOverview() {
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['automation-settings'], queryFn: readAutomationSettings })
  const [cap, setCap] = useState('')
  const [saved, setSaved] = useState(false)
  const currentCap = cap || String(settings.data?.automaticDownloadCap ?? 20)
  const saveCap = useMutation({
    mutationFn: () => saveAutomaticDownloadCap(Number(currentCap)),
    onSuccess: (answer) => {
      setSaved(answer.saved)
      if (answer.saved) setCap(String(answer.automaticDownloadCap))
      void queryClient.invalidateQueries({ queryKey: ['automation-settings'] })
      void queryClient.invalidateQueries({ queryKey: ['status'] })
    },
  })

  return (
    <SettingsPage
      title="Automation"
      lede="Enabled rules are independent permissions over matched Wanted Releases. If any rule permits a Release, the bounded background Decide routine may submit it."
    >
      <section>
        <div className={styles.sectionHeading}>
          <div>
            <h2>Rules</h2>
            <p>No enabled rule is the global off state.</p>
          </div>
          <Link to="/settings/automation/rules/new">Add rule</Link>
        </div>
        {settings.isPending && <p>Loading Automation Rules…</p>}
        {settings.isError && <p className={formStyles.refusal}>Automation settings could not be read.</p>}
        {settings.data && settings.data.rules.length === 0 && (
          <p className={styles.empty}>No rules yet. Automatic Downloads are off.</p>
        )}
        {settings.data && settings.data.rules.length > 0 && (
          <ul className={styles.rules}>
            {settings.data.rules.map((rule) => (
              <li key={rule.id}>
                <div>
                  <strong>{rule.name}</strong>
                  <span>{rule.enabled ? 'Enabled' : 'Disabled'} · {ruleSize(rule)}</span>
                  <span>{rule.allowedIndexers.map((indexer) => indexer.name).join(', ') || 'No allowed Indexers'}</span>
                </div>
                <Link to={`/settings/automation/rules/${rule.id}`}>Edit</Link>
              </li>
            ))}
          </ul>
        )}
      </section>

      <form
        className={formStyles.form}
        onSubmit={(event) => {
          event.preventDefault()
          setSaved(false)
          saveCap.mutate()
        }}
      >
        <label className={formStyles.label} htmlFor="automatic-download-cap">
          Unfinished automatic Download cap
        </label>
        <input
          className={formStyles.field}
          id="automatic-download-cap"
          name="automatic-download-cap"
          type="number"
          min="1"
          max="1000"
          required
          value={currentCap}
          onChange={(event) => { setCap(event.target.value); setSaved(false) }}
        />
        <p className={formStyles.hint}>
          Default 20. Work above this SABnzbd in-flight limit waits in the durable work set.
        </p>
        {saveCap.data && !saveCap.data.saved && <p className={formStyles.refusal}>{saveCap.data.detail}</p>}
        {saveCap.isError && <p className={formStyles.refusal}>The cap could not be saved.</p>}
        {saved && <p className={formStyles.done}>{saveCap.data?.detail}</p>}
        <button className={formStyles.button} type="submit" disabled={settings.isPending || saveCap.isPending}>
          Save cap
        </button>
      </form>
    </SettingsPage>
  )
}

function AutomationRuleScreen({ id }: { id: string }) {
  const creating = id === 'new'
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['automation-settings'], queryFn: readAutomationSettings })
  const existing = creating ? null : settings.data?.rules.find((rule) => rule.id === id)
  const [draft, setDraft] = useState<RuleDraft | null>(null)
  const selected = draft ?? (existing ? fromRule(existing) : emptyRule())

  useEffect(() => {
    if (existing && draft === null) setDraft(fromRule(existing))
  }, [draft, existing])

  const save = useMutation({
    mutationFn: () => saveAutomationRule(creating ? null : id, {
      name: selected.name,
      enabled: selected.enabled,
      minimumSize: toBytes(selected.minimumGiB),
      maximumSize: toBytes(selected.maximumGiB),
      allowedIndexerIds: [...selected.allowedIndexerIds],
    }),
    onSuccess: (answer) => {
      if (!answer.saved || !answer.ruleId) return
      void queryClient.invalidateQueries({ queryKey: ['automation-settings'] })
      void queryClient.invalidateQueries({ queryKey: ['releases'] })
      void queryClient.invalidateQueries({ queryKey: ['status'] })
      void navigate('/settings/automation')
    },
  })
  const remove = useMutation({
    mutationFn: async () => {
      const preview = await previewDeleteAutomationRule(id)
      const suffix = preview.existingOrigins
        ? `\n\n${preview.existingOrigins} existing Download Origin member(s) keep the copied rule name.`
        : ''
      if (!window.confirm(`Delete Automation Rule “${preview.name}”?${suffix}`)) return null
      return deleteAutomationRule(id)
    },
    onSuccess: (answer) => {
      if (!answer) return
      void queryClient.invalidateQueries({ queryKey: ['automation-settings'] })
      void queryClient.invalidateQueries({ queryKey: ['releases'] })
      void queryClient.invalidateQueries({ queryKey: ['downloads'] })
      void queryClient.invalidateQueries({ queryKey: ['status'] })
      void navigate('/settings/automation')
    },
  })

  const indexers = settings.data?.indexers ?? []
  const noEnabledIndexers = useMemo(() => indexers.every((indexer) => !indexer.enabled), [indexers])
  if (settings.isPending) return <SettingsPage title="Automation Rule"><p>Loading rule…</p></SettingsPage>
  if (settings.isError || (!creating && !existing)) {
    return <SettingsPage title="That Automation Rule is not here" back="/settings/automation" backLabel="Automation"><p>The rule could not be read.</p></SettingsPage>
  }

  const update = (change: Partial<RuleDraft>) => setDraft({ ...selected, ...change })
  return (
    <SettingsPage
      title={creating ? 'Add Automation Rule' : existing!.name}
      back="/settings/automation"
      backLabel="Automation"
      lede="A rule grants permission only. Rules are unordered, and every permitting rule is copied onto a Download's Origin."
    >
      <form
        className={formStyles.form}
        onSubmit={(event) => { event.preventDefault(); save.mutate() }}
      >
        <label className={formStyles.label} htmlFor="automation-rule-name">Name</label>
        <input className={formStyles.field} id="automation-rule-name" required value={selected.name} onChange={(event) => update({ name: event.target.value })} />

        <label className={formStyles.label}>Allowed Indexers</label>
        {indexers.map((indexer) => (
          <label key={indexer.id}>
            <input
              type="checkbox"
              checked={selected.allowedIndexerIds.has(indexer.id)}
              disabled={!indexer.enabled && !selected.allowedIndexerIds.has(indexer.id)}
              onChange={(event) => {
                const next = new Set(selected.allowedIndexerIds)
                if (event.target.checked) next.add(indexer.id)
                else next.delete(indexer.id)
                update({ allowedIndexerIds: next })
              }}
            />{' '}
            {indexer.name}{!indexer.enabled && ' (disabled)'}
          </label>
        ))}
        {noEnabledIndexers && <p className={formStyles.hint}>Configure and enable an Indexer before enabling a rule.</p>}

        <label className={formStyles.label} htmlFor="automation-minimum-size">Minimum size (GiB, optional)</label>
        <input className={formStyles.field} id="automation-minimum-size" type="number" min="0" step="0.1" value={selected.minimumGiB} onChange={(event) => update({ minimumGiB: event.target.value })} />
        <label className={formStyles.label} htmlFor="automation-maximum-size">Maximum size (GiB, optional)</label>
        <input className={formStyles.field} id="automation-maximum-size" type="number" min="0" step="0.1" value={selected.maximumGiB} onChange={(event) => update({ maximumGiB: event.target.value })} />

        <label className={styles.enabled}>
          <input type="checkbox" checked={selected.enabled} onChange={(event) => update({ enabled: event.target.checked })} />{' '}
          Enabled
        </label>
        <p className={formStyles.hint}>Disabling is forward-only and does not change existing Downloads.</p>

        {save.data && !save.data.saved && <p className={formStyles.refusal}>{save.data.detail}</p>}
        {save.isError && <p className={formStyles.refusal}>The Automation Rule could not be saved.</p>}
        <div className={styles.actions}>
          <button className={formStyles.button} type="submit" disabled={save.isPending || remove.isPending}>Save rule</button>
          {!creating && <button className={styles.danger} type="button" disabled={save.isPending || remove.isPending} onClick={() => remove.mutate()}>Delete rule</button>}
        </div>
        {remove.isError && <p className={formStyles.refusal}>The Automation Rule could not be deleted.</p>}
      </form>
    </SettingsPage>
  )
}

interface RuleDraft {
  name: string
  enabled: boolean
  minimumGiB: string
  maximumGiB: string
  allowedIndexerIds: Set<string>
}

function emptyRule(): RuleDraft {
  return { name: '', enabled: false, minimumGiB: '', maximumGiB: '', allowedIndexerIds: new Set() }
}

function fromRule(rule: AutomationRuleView): RuleDraft {
  return {
    name: rule.name,
    enabled: rule.enabled,
    minimumGiB: fromBytes(rule.minimumSize),
    maximumGiB: fromBytes(rule.maximumSize),
    allowedIndexerIds: new Set(rule.allowedIndexers.map((indexer) => indexer.id)),
  }
}

function fromBytes(bytes: number | string | null): string {
  return bytes === null ? '' : String(Number(bytes) / 1024 ** 3)
}

function toBytes(gib: string): number | null {
  return gib.trim() === '' ? null : Math.round(Number(gib) * 1024 ** 3)
}

function ruleSize(rule: AutomationRuleView): string {
  const minimum = rule.minimumSize === null ? 'any' : `${fromBytes(rule.minimumSize)} GiB`
  const maximum = rule.maximumSize === null ? 'any' : `${fromBytes(rule.maximumSize)} GiB`
  return `${minimum}–${maximum}`
}
