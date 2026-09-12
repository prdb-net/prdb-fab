import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router'

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
              title="That indexer is not here"
              back="/settings/connections"
              backLabel="Connections"
            >
              <Verdict tone="refusal">
                There is no indexer with that address in this installation.
              </Verdict>
            </SettingsPage>
          )
        }

        return <IndexerMask indexer={indexer} />
      }}
    </Loaded>
  )
}

function IndexerMask({ indexer }: { indexer: ConfiguredIndexer | undefined }) {
  const navigate = useNavigate()

  return (
    <SettingsPage
      title={indexer ? indexer.name : 'Add an indexer'}
      lede="Checked with a real search before anything is stored, because most indexers answer a capabilities call to anybody."
      back="/settings/connections"
      backLabel="Connections"
    >
      <IndexerForm
        indexer={indexer}
        onSaved={() => {
          // Adding one lands on the list it was added to; correcting one stays
          // where it is, showing what the check just came back with.
          if (!indexer) {
            void navigate('/settings/connections')
          }
        }}
      />

      {indexer && (
        <>
          <p className={styles.note}>
            Searching: {indexer.categories.split(',').join(', ')}. Last checked{' '}
            {new Date(indexer.lastCheckedAt).toLocaleString()}.
          </p>

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
          hint={
            <>
              Disabling is forward-only: nothing already submitted changes, and no
              Automation Rule may permit a Release from it from then on. What it
              has already cached stays and is still searched locally.
            </>
          }
        />
      </Fieldset>

      <Field
        label="Daily query budget"
        hint={
          <>
            The window is <strong>UTC midnight</strong>, not local midnight and not
            a rolling day. Half of it, up to 480 requests, is reserved for the
            Wanted Sweep — the only route by which an older wanted Video is ever
            found — and the rest is shared by Manual Search and the continuous
            walk. Empty means unbounded.{' '}
            {storedBudget >= unbounded
              ? 'This indexer is currently unbounded.'
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
            placeholder="Unbounded"
            value={budget}
            onChange={(event) => { setBudget(event.target.value); setSaved(false) }}
          />
        )}
      </Field>

      <SaveBar
        label="Save how it is used"
        dirty={enabled !== indexer.enabled || budget !== wasBudget}
        pending={save.isPending}
      >
        {save.data && !save.data.saved && <Verdict tone="refusal">{save.data.detail}</Verdict>}
        {save.isError && (
          <Verdict tone="refusal">That could not be saved. Nothing was changed.</Verdict>
        )}
        {saved && <Verdict tone="done">{save.data?.detail}</Verdict>}
      </SaveBar>
    </form>
  )
}

function Removal({ indexer }: { indexer: ConfiguredIndexer }) {
  const navigate = useNavigate()
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
      void navigate('/settings/connections')
    },
  })

  return (
    <>
      <p className={styles.detail}>
        Disabling stops it being searched and keeps everything it has. Deleting is
        for an indexer you no longer have an account with.
      </p>

      <button
        className={`${formStyles.button} ${formStyles.dangerButton}`}
        type="button"
        disabled={ask.isPending || remove.isPending}
        onClick={() => ask.mutate()}
      >
        {ask.isPending ? 'Reading what it holds…' : 'Delete this indexer…'}
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
            <strong>Its cache goes with it</strong> — {Number(confirming.cachedReleases)} cached
            Release(s). That is disposable by design and refills itself from any
            indexer you still have.
          </p>
          <p>
            <strong>{Number(confirming.downloads)} Download(s) stay.</strong> The Download record
            is what says a Release was used up and what answers &ldquo;why is this on my
            disk&rdquo;, so it outlives the indexer it came from.
          </p>
          <p>
            {Number(confirming.rulesReferencing) === 0 ? (
              <>No Automation Rule permits it, so none is affected.</>
            ) : (
              <>
                <strong>{Number(confirming.rulesReferencing)} Automation Rule(s)</strong> permit it
                today and lose that permission.
                {Number(confirming.rulesLosingTheirLastIndexer) > 0 && (
                  <>
                    {' '}
                    {Number(confirming.rulesLosingTheirLastIndexer)} of them would be left with no
                    permitted Indexer at all, and{' '}
                    <strong>come back disabled</strong> rather than quietly permitting nothing — a
                    disabled rule shows on Status, an inert one does not.
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
