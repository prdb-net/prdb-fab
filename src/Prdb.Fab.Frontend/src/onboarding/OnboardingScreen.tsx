import type { ReactNode } from 'react'
import { Link, useParams } from 'react-router'

import type { OnboardingStep } from '../api/client.ts'
import { IndexerForm } from './IndexerForm.tsx'
import { LibraryRootForm } from './LibraryRootForm.tsx'
import { PrdbForm } from './PrdbForm.tsx'
import { SabnzbdForm } from './SabnzbdForm.tsx'
import styles from './Onboarding.module.css'

/**
 * ADR 0036: anything worth linking to lives in the address, and every
 * onboarding step is worth linking to — ADR 0018 has every Gap carrying a route
 * to the form that fills it.
 *
 * The addresses came first and the forms are behind them now. What is still
 * missing is the path *between* them: *skip*, *continue*, and the marker moving
 * to the next step. Each form commits on its own, which is what ADR 0010 asks
 * of a step, and the order they are taken in is its own piece of work.
 */
export function OnboardingScreen() {
  const { step } = useParams<{ step: string }>()
  const known = steps.find((candidate) => candidate.slug === step)

  return (
    <main className={styles.screen}>
      <h1>Setting up</h1>

      {known ? (
        <>
          <h2 className={styles.heading}>{known.title}</h2>
          <p className={styles.lede}>{known.description}</p>
          {known.form}
        </>
      ) : (
        <p className={styles.pending}>There is no setup step by that name.</p>
      )}

      <p className={styles.note}>
        The steps do not lead into one another yet — each is its own address, and{' '}
        <Link to="/skeleton">the walking skeleton</Link> is still where it was.
      </p>
    </main>
  )
}

export const steps: ReadonlyArray<{
  step: OnboardingStep
  slug: string
  title: string
  description: string
  form: ReactNode
}> = [
  {
    step: 'PrdbKey',
    slug: 'prdb',
    title: 'The prdb API key',
    description:
      'Mandatory. Without it there is no identification, no wanted list, no artwork and no duplicate detection.',
    form: <PrdbForm />,
  },
  {
    step: 'Sabnzbd',
    slug: 'sabnzbd',
    title: 'SABnzbd',
    description:
      'Skippable. A tool that cannot download is still a tool that holds a library.',
    form: <SabnzbdForm />,
  },
  {
    step: 'Indexers',
    slug: 'indexers',
    title: 'Indexers',
    description: 'Skippable, and one is enough when it is taken.',
    form: <IndexerForm />,
  },
  {
    step: 'LibraryRoot',
    slug: 'library',
    title: 'The library root',
    description: 'Mandatory. It is where filing puts what arrives.',
    form: <LibraryRootForm />,
  },
]

/** Where the page sends someone whose onboarding is not finished. */
export function routeFor(step: OnboardingStep | null | undefined): string {
  const match = steps.find((candidate) => candidate.step === step)

  return match ? `/onboarding/${match.slug}` : '/skeleton'
}
