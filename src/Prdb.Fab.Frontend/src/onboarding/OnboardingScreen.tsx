import { Link, useParams } from 'react-router'

import type { OnboardingStep } from '../api/client.ts'
import styles from './Onboarding.module.css'

/**
 * ADR 0036: anything worth linking to lives in the address, and every
 * onboarding step is worth linking to — ADR 0018 has every Gap carrying a route
 * to the form that fills it.
 *
 * So the addresses exist from the start and the forms behind them do not: they
 * are tickets 05 to 08, and ticket 09 is what turns these into a path with
 * *skip* and *continue* around it. What stands here until then says which step
 * it is and that it is not built, rather than pretending to be one.
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
          <p className={styles.pending}>
            This step is not built yet. The password is, and so is everything
            underneath it — what is missing is the form and the check behind it.
          </p>
        </>
      ) : (
        <p className={styles.pending}>There is no setup step by that name.</p>
      )}

      <p className={styles.note}>
        The walking skeleton is still <Link to="/skeleton">where it was</Link>.
      </p>
    </main>
  )
}

export const steps: ReadonlyArray<{
  step: OnboardingStep
  slug: string
  title: string
  description: string
}> = [
  {
    step: 'PrdbKey',
    slug: 'prdb',
    title: 'The prdb API key',
    description:
      'Mandatory. Without it there is no identification, no wanted list, no artwork and no duplicate detection.',
  },
  {
    step: 'Sabnzbd',
    slug: 'sabnzbd',
    title: 'SABnzbd',
    description:
      'Skippable. A tool that cannot download is still a tool that holds a library.',
  },
  {
    step: 'Indexers',
    slug: 'indexers',
    title: 'Indexers',
    description: 'Skippable, and one is enough when it is taken.',
  },
  {
    step: 'LibraryRoot',
    slug: 'library',
    title: 'The library root',
    description: 'Mandatory. It is where filing puts what arrives.',
  },
]

/** Where the page sends someone whose onboarding is not finished. */
export function routeFor(step: OnboardingStep | null | undefined): string {
  const match = steps.find((candidate) => candidate.step === step)

  return match ? `/onboarding/${match.slug}` : '/skeleton'
}
