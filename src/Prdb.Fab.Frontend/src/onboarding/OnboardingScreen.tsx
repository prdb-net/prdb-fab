import { useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Navigate, useParams } from 'react-router'

import {
  readAccessState,
  readConnections,
  skipOnboardingStep,
  takeOnboardingStep,
  type ConnectionsState,
  type OnboardingStep,
  type OnboardingVerdict,
} from '../api/client.ts'
import { accessStateKey } from '../access/state.ts'
import { connectionsKey } from './state.ts'
import { IndexerForm } from './IndexerForm.tsx'
import { IndexerList } from './IndexerList.tsx'
import { LibraryRootForm } from './LibraryRootForm.tsx'
import { PrdbForm } from './PrdbForm.tsx'
import { SabnzbdForm } from './SabnzbdForm.tsx'
import styles from './Onboarding.module.css'

/**
 * ADR 0010's path, strung together: the four forms in their order, the marker
 * that says which of them is next, and the two acts that move it.
 *
 * The page shows the step the installation is actually on and nothing else.
 * Every step is its own address (ADR 0036) and an address that is not the
 * current step lands on the one that is — which is what makes a closed tab, a
 * restarted container and a second window all behave the same way, without any
 * of them being a case handled somewhere.
 *
 * Going back is not offered. Nothing here is stored without having been checked
 * against the service it names, so there is no wrong answer sitting behind the
 * user; corrections are the settings routes ADR 0020 builds out of these same
 * forms.
 */
export function OnboardingScreen() {
  const { step: slug } = useParams<{ step: string }>()
  const queries = useQueryClient()
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })
  const [asking, setAsking] = useState(false)
  const [verdict, setVerdict] = useState<OnboardingVerdict | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  // The marker moved, so the page re-decides what to show and lands on the next
  // step by the same rule that brought it here. Nothing navigates by hand.
  const move = useMutation({
    mutationFn: ({ act, step }: { act: 'take' | 'skip'; step: OnboardingStep }) =>
      act === 'take' ? takeOnboardingStep(step) : skipOnboardingStep(step),
    onSuccess: async (answer) => {
      setVerdict(answer)
      setAsking(false)

      await queries.invalidateQueries({ queryKey: accessStateKey })
    },
    onError: (error) => setFailure(String(error)),
  })

  const here = state.data?.nextStep
  const showing = steps.find((candidate) => candidate.slug === slug)

  if (state.isPending) {
    return null
  }

  if (!showing || showing.step !== here) {
    return <Navigate to={routeFor(here)} replace />
  }

  const taken = () => move.mutate({ act: 'take', step: showing.step })

  // Whether the form in front of this step has stored anything. The same
  // question the backend refuses a step on, asked here only to decide whether
  // there is a button — a browser that got this wrong would be told.
  const answered = isAnswered(showing.step, connections.data)

  return (
    <main className={styles.screen}>
      <h1>Setting up</h1>

      <Path here={showing.step} skipped={connections.data} />

      <h2 className={styles.heading}>{showing.title}</h2>
      <p className={styles.lede}>{showing.description}</p>

      {showing.form(taken)}

      {/* Three of the four forms continue by themselves the moment they store
          something. This is for the two moments they cannot: an indexer step
          that may take several, and a library root stored with a warning that
          has to stay on screen long enough to be read. */}
      {answered && (
        <button className={styles.button} type="button" disabled={move.isPending} onClick={taken}>
          Continue
        </button>
      )}

      {showing.consequence && (
        <div className={styles.skip}>
          {asking ? (
            <>
              <p className={styles.warning}>{showing.consequence}</p>
              <button
                className={styles.button}
                type="button"
                disabled={move.isPending}
                onClick={() => move.mutate({ act: 'skip', step: showing.step })}
              >
                Skip it anyway
              </button>{' '}
              <button className={styles.quiet} type="button" onClick={() => setAsking(false)}>
                Go back
              </button>
            </>
          ) : (
            <button className={styles.quiet} type="button" onClick={() => setAsking(true)}>
              Skip this step
            </button>
          )}
        </div>
      )}

      {verdict && verdict.outcome !== 'Taken' && verdict.outcome !== 'Skipped' && (
        <p className={styles.refusal}>{verdict.detail}</p>
      )}
      {failure && <p className={styles.refusal}>{failure}</p>}
    </main>
  )
}

/** What each step asks for, as the browser side can see it. */
function isAnswered(step: OnboardingStep, connections: ConnectionsState | undefined): boolean {
  switch (step) {
    case 'PrdbKey':
      return connections?.prdbConfigured === true
    case 'Sabnzbd':
      return connections?.sabnzbdConfigured === true
    case 'Indexers':
      return Number(connections?.indexerCount ?? 0) > 0
    case 'LibraryRoot':
      return Boolean(connections?.libraryRoot)
    default:
      return false
  }
}

/** Where the path has got to, and where it still has to go. */
function Path({ here, skipped }: { here: OnboardingStep; skipped: ConnectionsState | undefined }) {
  const position = steps.findIndex((candidate) => candidate.step === here)

  return (
    <ol className={styles.path}>
      {steps.map((candidate, index) => {
        const passed =
          candidate.step === 'Sabnzbd'
            ? skipped?.sabnzbdSkipped
            : candidate.step === 'Indexers'
              ? skipped?.indexersSkipped
              : false

        return (
          <li
            key={candidate.slug}
            className={index === position ? styles.now : index < position ? styles.behind : undefined}
            aria-current={index === position ? 'step' : undefined}
          >
            {candidate.title}
            {index < position && <span className={styles.stepState}>{passed ? 'Skipped' : 'Done'}</span>}
          </li>
        )
      })}
    </ol>
  )
}

/**
 * ADR 0010's path, minus the two states that are not forms: the password, which
 * ADR 0010 puts in front of the path rather than on it, and the end of it.
 */
export const steps: ReadonlyArray<{
  step: OnboardingStep
  slug: string
  title: string
  description: string
  /** What skipping this step costs, said before it is skipped. Only the two that may be. */
  consequence?: string
  form: (taken: () => void) => ReactNode
}> = [
  {
    step: 'PrdbKey',
    slug: 'prdb',
    title: 'The prdb API key',
    description:
      'Mandatory. Without it there is no identification, no wanted list, no artwork and no duplicate detection.',
    form: (taken) => <PrdbForm onSaved={taken} />,
  },
  {
    step: 'Sabnzbd',
    slug: 'sabnzbd',
    title: 'SABnzbd',
    description:
      'Skippable. A tool that cannot download is still a tool that holds a library.',
    consequence:
      'Without SABnzbd nothing is downloaded: releases can be found and nothing can be fetched. '
      + 'The library still works, and this can be filled in later from the settings — but setting '
      + 'up does not come back to ask.',
    form: (taken) => <SabnzbdForm onSaved={taken} />,
  },
  {
    step: 'Indexers',
    slug: 'indexers',
    title: 'Indexers',
    description: 'Skippable, and one is enough when it is taken.',
    consequence:
      'Without an indexer nothing is searched and nothing is downloaded, because there is nowhere '
      + 'to look. One can be added later from the settings — but setting up does not come back to '
      + 'ask.',
    form: () => (
      <>
        <IndexerForm />
        <IndexerList />
      </>
    ),
  },
  {
    step: 'LibraryRoot',
    slug: 'library',
    title: 'The library root',
    description: 'Mandatory. It is where filing puts what arrives.',
    form: (taken) => <LibraryRootForm onSaved={taken} />,
  },
]

/** Where the page sends someone whose onboarding is not finished. */
export function routeFor(step: OnboardingStep | null | undefined): string {
  if (step === 'Complete') {
    // ADR 0010 always ended the path here: the wanted list, with the first sync
    // visibly running. What stood here until the catalogue existed was a page
    // saying so, and it is gone.
    return '/wanted'
  }

  const match = steps.find((candidate) => candidate.step === step)

  return match ? `/onboarding/${match.slug}` : '/skeleton'
}
