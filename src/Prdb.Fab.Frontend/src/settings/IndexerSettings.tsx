import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useLocation, useNavigate, useParams } from 'react-router'

import {
  deleteIndexer,
  listIndexers,
  previewDeleteIndexer,
  saveIndexerSettings,
  type ConfiguredIndexer,
  type IndexerDeletePreview,
} from '../api/client.ts'
import { indexersKey, connectionsKey } from '../onboarding/state.ts'
import { IndexerForm } from '../onboarding/IndexerForm.tsx'
import { ConfirmationDialog } from '../ui/ConfirmationDialog.tsx'
import { Field, Fieldset, Switch, formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import styles from './Settings.module.css'

/** The number the backend stores for ADR 0020's unbounded daily budget. */
const unbounded = 100000

/**
 * ADR 0020 gives every indexer its own route, because ADR 0018's Brakes point
 * at one row rather than at a list. The same route adds one, with no row behind
 * it.
 *
 * It holds the whole indexer now. Enabled, the rank and the daily query budget
 * are all read by code that runs unattended — Automation filters on enabled,
 * ADR 0008's total order uses the rank, and the walk divides the budget with
 * the Wanted Sweep — and until ADR 0058 none of the three could be set. The
 * Brake *"…'s daily query budget is spent"* pointed here, at a page on which
 * that budget did not appear.
 */
export function IndexerSettings() {
  const { id } = useParams<{ id: string }>()
  const indexers = useQuery({ queryKey: indexersKey, queryFn: listIndexers })

  return (
    <Loaded query={indexers} of="the configured Indexers">
      {(rows) => {
        const indexer = id ? rows.find((candidate) => candidate.id === id) : undefined

        if (id && !indexer) {
          return (
            <SettingsPage
              title="Indexer not found"
              back="/settings/connections/indexers"
              backLabel="Indexers"
            >
              <Verdict tone="refusal">
                This Indexer is no longer configured. Return to Indexers to choose another.
              </Verdict>
            </SettingsPage>
          )
        }

        return <IndexerMask key={indexer?.id ?? "new"} indexer={indexer} />
      }}
    </Loaded>
  )
}

function IndexerMask({ indexer }: { indexer: ConfiguredIndexer | undefined }) {
  const navigate = useNavigate()
  const { search } = useLocation()
  const [editing, setEditing] = useState(false)
  const [saved, setSaved] = useState(false)

  return (
    <SettingsPage
      title={indexer ? indexer.name : 'Add an indexer'}
      lede="Connect an Indexer and set its query budget."
      help="Before saving, fab tests the API key with a search to verify that it can retrieve Releases."
      back="/settings/connections/indexers"
      backLabel="Indexers"
    >
      {indexer && !editing && <>
        <dl className={styles.connectionSummary}>
          <dt>Address</dt><dd>{indexer.url}</dd>
          <dt>API key</dt><dd>Stored</dd>
          <dt>Categories</dt><dd>{indexer.categories.split(',').join(', ')}</dd>
          <dt>Last checked</dt><dd>{new Date(indexer.lastCheckedAt).toLocaleString()}</dd>
        </dl>
        <button type="button" className={formStyles.button} onClick={() => { setSaved(false); setEditing(true) }}>Edit connection</button>
      </>}
      {(editing || !indexer) && <section aria-label="Connection">
      <IndexerForm
        indexer={indexer}
        onSaved={() => {
          // Adding one lands on the list it was added to; correcting one stays
          // where it is, showing what the check just came back with.
          setEditing(false)
          setSaved(true)
          if (!indexer) {
            void navigate({ pathname: '/settings/connections/indexers', search })
          }
        }}
      />
      {indexer && <button type="button" className={formStyles.button} onClick={() => setEditing(false)}>Cancel editing</button>}
      </section>}

      {saved && <Verdict tone="done">Indexer connection saved.</Verdict>}
      {indexer && (
        <>
          <h2 className={styles.heading}>How it is used</h2>
          <RowSettings indexer={indexer} />

          <h2 className={styles.heading}>Removing it</h2>
          <Removal indexer={indexer} />
        </>
      )}
    </SettingsPage>
  )
}

/**
 * The three things about the row rather than about the connection. Saved
 * without re-running the check: changing a budget is not a reason to spend a
 * query, and an edit that re-checked would make disabling a *broken* indexer
 * impossible — which is exactly when somebody reaches for it.
 */
function RowSettings({ indexer }: { indexer: ConfiguredIndexer }) {
  const queries = useQueryClient()
  const storedBudget = Number(indexer.dailyQueryBudget)
  const [enabled, setEnabled] = useState(indexer.enabled)
  const [budget, setBudget] = useState(storedBudget >= unbounded ? '' : String(storedBudget))
  const [saved, setSaved] = useState(false)

  const wasBudget = storedBudget >= unbounded ? '' : String(storedBudget)

  const save = useMutation({
    mutationFn: () =>
      saveIndexerSettings(indexer.id, {
        enabled,
        dailyQueryBudget: budget.trim() === '' ? null : Number(budget),
      }),
    onSuccess: (answer) => {
      setSaved(answer.saved)
      void queries.invalidateQueries({ queryKey: indexersKey })
      void queries.invalidateQueries({ queryKey: ['automation-settings'] })
      void queries.invalidateQueries({ queryKey: ['status'] })
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
      <Fieldset legend="Whether it is searched">
        <Switch
          checked={enabled}
          onChange={(checked) => { setEnabled(checked); setSaved(false) }}
          label="Enabled"
          help={
            <>
              Disabling stops searches on this Indexer and prevents new automatic
              Downloads from it. Existing Downloads continue. Cached Releases
              remain available for local searches.
            </>
          }
        />
      </Fieldset>

      <Field
        label="Daily query budget"
        hint="Leave empty for no limit. Resets at midnight UTC."
        help={
          <>
            Requests for new and recent Releases take priority. Wanted Sweep and
            Manual Search share the remaining budget before older history is
            fetched.{' '}
            {storedBudget >= unbounded
              ? 'No daily query limit is currently set.'
              : `This indexer currently allows ${storedBudget} requests a day.`}
          </>
        }
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="number"
            min="1"
            max="100000"
            placeholder="No limit"
            value={budget}
            onChange={(event) => { setBudget(event.target.value); setSaved(false) }}
          />
        )}
      </Field>

      <SaveBar
        label="Save Indexer settings"
        dirty={enabled !== indexer.enabled || budget !== wasBudget}
        pending={save.isPending}
      >
        {save.data && !save.data.saved && <Verdict tone="refusal">{save.data.detail}</Verdict>}
        {save.isError && (
          <Verdict tone="refusal">Could not confirm that the Indexer settings were saved. Reload to check the current values.</Verdict>
        )}
        {saved && <Verdict tone="done">{save.data?.detail}</Verdict>}
      </SaveBar>
    </form>
  )
}

function Removal({ indexer }: { indexer: ConfiguredIndexer }) {
  const navigate = useNavigate()
  const { search } = useLocation()
  const queries = useQueryClient()
  const [confirming, setConfirming] = useState<IndexerDeletePreview | null>(null)

  const ask = useMutation({
    mutationFn: () => previewDeleteIndexer(indexer.id),
    onSuccess: (preview) => setConfirming(preview),
  })

  const remove = useMutation({
    mutationFn: () => deleteIndexer(indexer.id),
    onSuccess: () => {
      setConfirming(null)
      void queries.invalidateQueries({ queryKey: indexersKey })
      void queries.invalidateQueries({ queryKey: connectionsKey })
      void queries.invalidateQueries({ queryKey: ['automation-settings'] })
      void queries.invalidateQueries({ queryKey: ['releases'] })
      void queries.invalidateQueries({ queryKey: ['status'] })
      void navigate({ pathname: '/settings/connections/indexers', search })
    },
  })

  return (
    <>
      <p className={styles.detail}>
        Disable an Indexer to pause its searches while keeping its settings and
        cache. Delete it to remove its configuration and cached Releases.
      </p>

      <button
        className={`${formStyles.button} ${formStyles.dangerButton}`}
        type="button"
        disabled={ask.isPending || remove.isPending}
        onClick={() => ask.mutate()}
      >
        {ask.isPending ? 'Checking deletion…' : 'Delete this indexer…'}
      </button>

      {(ask.isError || remove.isError) && (
        <Verdict tone="refusal">The indexer could not be deleted.</Verdict>
      )}

      {confirming && (
        <ConfirmationDialog
          title={`Delete “${confirming.name}”?`}
          confirmLabel="Delete the indexer"
          danger
          busy={remove.isPending}
          onCancel={() => setConfirming(null)}
          onConfirm={() => remove.mutate()}
        >
          <p>
            <strong>{Number(confirming.cachedReleases)} cached Release(s) will be deleted.</strong>
            {' '}Other Indexers keep their own caches.
          </p>
          <p>
            <strong>{Number(confirming.downloads)} Download(s) stay.</strong> Their history, including which Releases were used, is preserved.
          </p>
          <p>
            {Number(confirming.rulesReferencing) === 0 ? (
              <>No Automation Rules use this Indexer.</>
            ) : (
              <>
                <strong>{Number(confirming.rulesReferencing)} Automation Rule(s)</strong> will have this Indexer removed from their allowed list.
                {Number(confirming.rulesLosingTheirLastIndexer) > 0 && (
                  <>
                    {' '}
                    {Number(confirming.rulesLosingTheirLastIndexer)} of them would be left with no
                    permitted Indexer at all, and{' '}
                    <strong>will be disabled</strong>.
                  </>
                )}
              </>
            )}
          </p>
        </ConfirmationDialog>
      )}
    </>
  )
}
