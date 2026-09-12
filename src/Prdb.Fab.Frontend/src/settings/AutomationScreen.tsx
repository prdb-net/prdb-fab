import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'

import {
  deleteAutomationRule,
  previewDeleteAutomationRule,
  readAutomationSettings,
  saveAutomaticDownloadCap,
  saveAutomationRule,
  type AutomationRuleDeletePreview,
  type AutomationRuleView,
  type AutomationSettingsState,
} from '../api/client.ts'
import { ConfirmationDialog } from '../ui/ConfirmationDialog.tsx'
import { Field, Fieldset, Switch, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import styles from './AutomationScreen.module.css'
import { SettingsPage } from './SettingsPage.tsx'

export function AutomationScreen() {
  const { id } = useParams()
  return id ? <AutomationRuleScreen id={id} /> : <AutomationOverview />
}

function AutomationOverview() {
  const settings = useQuery({ queryKey: ['automation-settings'], queryFn: readAutomationSettings })

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

        <Loaded query={settings} of="the Automation settings">
          {(held) =>
            held.rules.length === 0 ? (
              <p className={styles.empty}>No rules yet. Automatic Downloads are off.</p>
            ) : (
              <ul className={styles.rules}>
                {held.rules.map((rule) => (
                  <li key={rule.id}>
                    <div>
                      <strong>{rule.name}</strong>
                      <span>{rule.enabled ? 'Enabled' : 'Disabled'} · {ruleSize(rule)}</span>
                      <span>
                        {rule.allowedIndexers.map((indexer) => indexer.name).join(', ')
                          || 'No allowed Indexers'}
                      </span>
                    </div>
                    <Link to={`/settings/automation/rules/${rule.id}`}>Edit</Link>
                  </li>
                ))}
              </ul>
            )
          }
        </Loaded>
      </section>

      <Loaded query={settings} of="the Automation limits">
        {(held) => <CapForm held={held} />}
      </Loaded>
    </SettingsPage>
  )
}

function CapForm({ held }: { held: AutomationSettingsState }) {
  const queryClient = useQueryClient()

  // A string, and only a string. The old `cap || String(stored)` put the stored
  // value straight back the moment the field was cleared, so the cap could be
  // prefixed and never replaced.
  const [cap, setCap] = useState(String(held.automaticDownloadCap))
  const [stored, setStored] = useState(String(held.automaticDownloadCap))
  const [saved, setSaved] = useState(false)

  const save = useMutation({
    mutationFn: () => saveAutomaticDownloadCap(Number(cap)),
    onSuccess: (answer) => {
      setSaved(answer.saved)

      if (answer.saved) {
        setCap(String(answer.automaticDownloadCap))
        setStored(String(answer.automaticDownloadCap))
      }

      void queryClient.invalidateQueries({ queryKey: ['automation-settings'] })
      void queryClient.invalidateQueries({ queryKey: ['status'] })
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
        label="Unfinished automatic Download cap"
        hint="Default 20. Work above this SABnzbd in-flight limit waits in the durable work set."
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="number"
            min="1"
            max="1000"
            value={cap}
            onChange={(event) => { setCap(event.target.value); setSaved(false) }}
          />
        )}
      </Field>

      <SaveBar
        label="Save cap"
        dirty={cap !== stored}
        pending={save.isPending}
        disabled={cap.trim().length === 0}
        blocked="The cap needs a number."
      >
        {save.data && !save.data.saved && <Verdict tone="refusal">{save.data.detail}</Verdict>}
        {save.isError && (
          <Verdict tone="refusal">The cap could not be saved. Nothing was changed.</Verdict>
        )}
        {saved && <Verdict tone="done">{save.data?.detail}</Verdict>}
      </SaveBar>
    </form>
  )
}

function AutomationRuleScreen({ id }: { id: string }) {
  const creating = id === 'new'
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const settings = useQuery({ queryKey: ['automation-settings'], queryFn: readAutomationSettings })
  const existing = creating ? null : settings.data?.rules.find((rule) => rule.id === id)
  const [draft, setDraft] = useState<RuleDraft | null>(null)
  const [confirming, setConfirming] = useState<AutomationRuleDeletePreview | null>(null)
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

  // ADR 0058: a destructive act is confirmed in the application. The preview is
  // fetched first because it is what the dialog has to say — window.confirm
  // could not carry it, and the old `\n\n`-joined string is what that looked
  // like.
  const ask = useMutation({
    mutationFn: () => previewDeleteAutomationRule(id),
    onSuccess: (preview) => setConfirming(preview),
  })

  const remove = useMutation({
    mutationFn: () => deleteAutomationRule(id),
    onSuccess: () => {
      setConfirming(null)
      void queryClient.invalidateQueries({ queryKey: ['automation-settings'] })
      void queryClient.invalidateQueries({ queryKey: ['releases'] })
      void queryClient.invalidateQueries({ queryKey: ['downloads'] })
      void queryClient.invalidateQueries({ queryKey: ['status'] })
      void navigate('/settings/automation')
    },
  })

  const indexers = settings.data?.indexers ?? []
  const noEnabledIndexers = useMemo(() => indexers.every((indexer) => !indexer.enabled), [indexers])

  if (settings.isPending) {
    return (
      <SettingsPage title="Automation Rule">
        <p className={formStyles.reading}>Reading the Automation Rule…</p>
      </SettingsPage>
    )
  }

  if (settings.isError || (!creating && !existing)) {
    return (
      <SettingsPage
        title="That Automation Rule is not here"
        back="/settings/automation"
        backLabel="Automation"
      >
        <Verdict tone="refusal">
          There is no Automation Rule with that address in this installation.
        </Verdict>
      </SettingsPage>
    )
  }

  const update = (change: Partial<RuleDraft>) => setDraft({ ...selected, ...change })
  const unchanged = existing != null && same(selected, fromRule(existing))

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
        <Field label="Name">
          {(id_) => (
            <input
              id={id_}
              className={formStyles.field}
              required
              value={selected.name}
              onChange={(event) => update({ name: event.target.value })}
            />
          )}
        </Field>

        <Fieldset
          legend="Allowed Indexers"
          hint={noEnabledIndexers ? 'Configure and enable an Indexer before enabling a rule.' : undefined}
        >
          {indexers.map((indexer) => (
            <Switch
              key={indexer.id}
              checked={selected.allowedIndexerIds.has(indexer.id)}
              disabled={!indexer.enabled && !selected.allowedIndexerIds.has(indexer.id)}
              onChange={(checked) => {
                const next = new Set(selected.allowedIndexerIds)
                if (checked) next.add(indexer.id)
                else next.delete(indexer.id)
                update({ allowedIndexerIds: next })
              }}
              label={`${indexer.name}${indexer.enabled ? '' : ' (disabled)'}`}
            />
          ))}
        </Fieldset>

        <Field label="Minimum size (GiB, optional)">
          {(id_) => (
            <input
              id={id_}
              className={formStyles.field}
              type="number"
              min="0"
              step="0.1"
              value={selected.minimumGiB}
              onChange={(event) => update({ minimumGiB: event.target.value })}
            />
          )}
        </Field>

        <Field label="Maximum size (GiB, optional)">
          {(id_) => (
            <input
              id={id_}
              className={formStyles.field}
              type="number"
              min="0"
              step="0.1"
              value={selected.maximumGiB}
              onChange={(event) => update({ maximumGiB: event.target.value })}
            />
          )}
        </Field>

        <Fieldset legend="Whether it acts">
          <Switch
            checked={selected.enabled}
            onChange={(checked) => update({ enabled: checked })}
            label="Enabled"
            hint="Disabling is forward-only and does not change existing Downloads."
          />
        </Fieldset>

        <SaveBar
          label="Save rule"
          dirty={!unchanged}
          pending={save.isPending}
          disabled={selected.name.trim().length === 0 || remove.isPending}
          blocked={remove.isPending ? 'The rule is being deleted.' : 'A rule needs a name.'}
        >
          {save.data && !save.data.saved && <Verdict tone="refusal">{save.data.detail}</Verdict>}
          {save.isError && (
            <Verdict tone="refusal">The Automation Rule could not be saved.</Verdict>
          )}
          {(ask.isError || remove.isError) && (
            <Verdict tone="refusal">The Automation Rule could not be deleted.</Verdict>
          )}
        </SaveBar>

        {!creating && (
          <div className={styles.actions}>
            <button
              className={`${formStyles.button} ${formStyles.dangerButton}`}
              type="button"
              disabled={save.isPending || ask.isPending || remove.isPending}
              onClick={() => ask.mutate()}
            >
              {ask.isPending ? 'Reading what it holds…' : 'Delete rule…'}
            </button>
          </div>
        )}
      </form>

      {confirming && (
        <ConfirmationDialog
          title={`Delete “${confirming.name}”?`}
          confirmLabel="Delete the rule"
          danger
          busy={remove.isPending}
          onCancel={() => setConfirming(null)}
          onConfirm={() => remove.mutate()}
        >
          <p>
            The rule stops permitting anything from now on. Downloads it already
            permitted are untouched.
          </p>
          <p>
            {Number(confirming.existingOrigins) > 0
              ? `${confirming.existingOrigins} existing Download Origin member(s) keep the copied rule
                 name, so "why is this on my disk" still has an answer after the rule is gone.`
              : 'No Download has been permitted by it yet, so nothing carries its name.'}
          </p>
        </ConfirmationDialog>
      )}
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

function same(one: RuleDraft, other: RuleDraft): boolean {
  return one.name === other.name
    && one.enabled === other.enabled
    && one.minimumGiB === other.minimumGiB
    && one.maximumGiB === other.maximumGiB
    && one.allowedIndexerIds.size === other.allowedIndexerIds.size
    && [...one.allowedIndexerIds].every((id) => other.allowedIndexerIds.has(id))
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
