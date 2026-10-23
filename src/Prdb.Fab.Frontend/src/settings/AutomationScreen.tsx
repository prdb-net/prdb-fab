import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'

import {
  deleteAutomationRule,
  previewDeleteAutomationRule,
  readAutomationSettings,
  saveAutomaticDownloadCap,
  saveAutomationRule,
  saveRetryBudget,
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
import { AutopilotPanel } from './AutopilotPanel.tsx'
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
      lede="Rules and limits for automatic Downloads."
      help="Choose which Wanted Videos may download automatically. A matching enabled rule grants permission; your limits still apply."
    >
      <AutopilotPanel controls />
      <section>
        <div className={styles.sectionHeading}>
          <div>
            <h2>Custom rules · Advanced</h2>
            <p>Custom rules grant independent permission, including older or additional Videos. Turning off the supplied policy does not turn these rules off.</p>
          </div>
          <Link to="/settings/automation/rules/new">Add rule</Link>
        </div>

        <Loaded query={settings} of="the Automation settings">
          {(held) =>
            held.rules.length === 0 ? (
              <p className={styles.empty}>No custom rules. The supplied policy is controlled above.</p>
            ) : (
              <ul className={styles.rules}>
                {held.rules.map((rule) => (
                  <RuleCard key={rule.id} rule={rule} />
                ))}
              </ul>
            )
          }
        </Loaded>
      </section>

      <Loaded query={settings} of="the Automation limits">
        {(held) => <Limits held={held} />}
      </Loaded>
    </SettingsPage>
  )
}

/**
 * One rule, said as a sentence rather than as three grey spans.
 *
 * The enable switch is on the card because it is the control that gets used,
 * and ADR 0007 makes disabling forward-only — it permits nothing new and
 * changes nothing already submitted — so it is safe to offer without a
 * confirmation. Everything else about a rule is behind its own route, which is
 * where ADR 0020 put it.
 */
function RuleCard({ rule }: { rule: AutomationRuleView }) {
  const queries = useQueryClient()
  const [enabled, setEnabled] = useState(rule.enabled)

  const toggle = useMutation({
    mutationFn: (next: boolean) =>
      saveAutomationRule(rule.id, {
        name: rule.name,
        enabled: next,
        minimumSize: rule.minimumSize === null ? null : Number(rule.minimumSize),
        maximumSize: rule.maximumSize === null ? null : Number(rule.maximumSize),
        allowedIndexerIds: rule.allowedIndexers.map((indexer) => indexer.id),
      }),
    onSuccess: (answer) => {
      if (!answer.saved) {
        setEnabled(rule.enabled)
        return
      }

      void queries.invalidateQueries({ queryKey: ['automation-settings'] })
      void queries.invalidateQueries({ queryKey: ['releases'] })
      void queries.invalidateQueries({ queryKey: ['status'] })
    },
  })

  const indexers = rule.allowedIndexers.map((indexer) => indexer.name)

  return (
    <li>
      <div className={styles.rule}>
        <div className={styles.rulePermits}>
          <strong>{rule.name}</strong>
          <p>{permits(rule, indexers)}</p>
          {toggle.data && !toggle.data.saved && (
            <Verdict tone="refusal">{toggle.data.detail}</Verdict>
          )}
          {toggle.isError && (
            <Verdict tone="refusal">Could not confirm the rule change. Reload to check whether it is enabled.</Verdict>
          )}
        </div>
        <div className={styles.ruleActions}>
          <Switch
            checked={enabled}
            disabled={toggle.isPending}
            onChange={(next) => { setEnabled(next); toggle.mutate(next) }}
            label={enabled ? 'Enabled' : 'Disabled'}
          />
          <Link to={`/settings/automation/rules/${rule.id}`}>Edit</Link>
        </div>
      </div>
    </li>
  )
}

/** What a rule permits, in one sentence. */
function permits(rule: AutomationRuleView, indexers: readonly string[]): string {
  if (indexers.length === 0) {
    return 'No Indexers selected. This rule cannot permit a Download.'
  }

  const where = indexers.length === 1 ? indexers[0] : `${indexers.slice(0, -1).join(', ')} and ${indexers.at(-1)}`
  const minimum = rule.minimumSize === null ? null : `${fromBytes(rule.minimumSize)} GiB`
  const maximum = rule.maximumSize === null ? null : `${fromBytes(rule.maximumSize)} GiB`

  const size = minimum && maximum
    ? ` between ${minimum} and ${maximum}`
    : minimum
      ? ` of at least ${minimum}`
      : maximum
        ? ` of at most ${maximum}`
        : ' of any size'

  return `Releases identified as Wanted Videos from ${where},${size}.`
}

/**
 * The two numbers that bound automatic work, together — one across SABnzbd and
 * one per Video — each with the fact behind it. ADR 0020 puts both in this
 * group, and the retry budget had no field at all: the Release view read it and
 * showed a Video's spent attempts against it, and nothing could change the
 * number.
 */
function Limits({ held }: { held: AutomationSettingsState }) {
  return (
    <>
      <h2 className={styles.limitsHeading}>Limits</h2>
      <CapForm held={held} />
      <RetryBudgetForm held={held} />
    </>
  )
}

function RetryBudgetForm({ held }: { held: AutomationSettingsState }) {
  const queryClient = useQueryClient()
  const [budget, setBudget] = useState(String(held.retryBudget))
  const [stored, setStored] = useState(String(held.retryBudget))
  const [saved, setSaved] = useState(false)

  const save = useMutation({
    mutationFn: () => saveRetryBudget(Number(budget)),
    onSuccess: (answer) => {
      setSaved(answer.saved)

      if (answer.saved) {
        setBudget(String(answer.retryBudget))
        setStored(String(answer.retryBudget))
      }

      void queryClient.invalidateQueries({ queryKey: ['automation-settings'] })
      void queryClient.invalidateQueries({ queryKey: ['releases'] })
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
        label="Retry budget, per Video"
        help={
          <>
            Default 3. Every Download attempt counts, across all Indexers and
            regardless of outcome. Changing this limit affects every Video.
            To reset one Video, follow its retry-budget Brake on Status.
          </>
        }
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="number"
            min="1"
            max="10"
            value={budget}
            onChange={(event) => { setBudget(event.target.value); setSaved(false) }}
          />
        )}
      </Field>

      <SaveBar
        label="Save retry budget"
        dirty={budget !== stored}
        pending={save.isPending}
        disabled={budget.trim().length === 0}
        blocked="The retry budget needs a number."
      >
        {save.data && !save.data.saved && <Verdict tone="refusal">{save.data.detail}</Verdict>}
        {save.isError && (
          <Verdict tone="refusal">Could not confirm the retry budget was saved. Reload to check its value.</Verdict>
        )}
        {saved && <Verdict tone="done">{save.data?.detail}</Verdict>}
      </SaveBar>
    </form>
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
        help={
          <>
            Default 20. The maximum number of unfinished automatic Downloads
            in SABnzbd. Additional work waits and resumes as Downloads finish.
          </>
        }
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
          <Verdict tone="refusal">Could not confirm the Download cap was saved. Reload to check its value.</Verdict>
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
        title="Automation Rule unavailable"
        back="/settings/automation"
        backLabel="Automation"
      >
        <Verdict tone="refusal">
          The rule could not be loaded. It may have been deleted; return to Automation to check the current rules.
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
      lede="Choose the Indexers and size limits this rule permits."
      help="Any matching enabled rule can permit a Download. Rules have no priority order. Each Download records all rules that permitted it in its Origin."
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

        {/* One bounded range rather than two unrelated numbers: they are a
            floor and a ceiling on the same quantity, and two labels are not
            where that should have to be worked out. */}
        <Fieldset
          legend="Release size"
          hint="Leave either field empty for no limit."
          help="Sizes are reported by the Indexer for the Release package. They can differ from the final video file size."
        >
          <div className={styles.range}>
            <Field label="At least (GiB)">
              {(id_) => (
                <input
                  id={id_}
                  className={formStyles.field}
                  type="number"
                  min="0"
                  step="0.1"
                  placeholder="Any"
                  value={selected.minimumGiB}
                  onChange={(event) => update({ minimumGiB: event.target.value })}
                />
              )}
            </Field>
            <Field label="At most (GiB)">
              {(id_) => (
                <input
                  id={id_}
                  className={formStyles.field}
                  type="number"
                  min="0"
                  step="0.1"
                  placeholder="Any"
                  value={selected.maximumGiB}
                  onChange={(event) => update({ maximumGiB: event.target.value })}
                />
              )}
            </Field>
          </div>
        </Fieldset>

        <Fieldset legend="Rule status">
          <Switch
            checked={selected.enabled}
            onChange={(checked) => update({ enabled: checked })}
            label="Enabled"
            help="Disabling stops this rule permitting new Downloads. Existing Downloads are unchanged."
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
              {ask.isPending ? 'Checking deletion…' : 'Delete rule…'}
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
            Deleting this rule stops it from permitting new Downloads.
            Existing Downloads continue.
          </p>
          <p>
            {Number(confirming.existingOrigins) > 0
              ? `${confirming.existingOrigins} Download Origin record(s) retain this rule’s name after deletion.`
              : 'This rule has not permitted any Downloads.'}
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

