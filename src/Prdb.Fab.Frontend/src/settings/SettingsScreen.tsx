import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import { readConnections, readStatus, type StatusCondition } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import styles from './Settings.module.css'

/**
 * `/settings` itself, which used to be a corridor: a `<ul>` of links with a
 * `<br>` between each one and a sentence about what the group was *for*.
 *
 * The links are the rail's now, and they are beside every mask rather than in
 * front of them. What is left is the thing the corridor never did — say what
 * this installation is like, and where the loop is currently bleeding.
 *
 * Below the breakpoint this page is not rendered at all: there the rail is the
 * page, because a summary above a list of links is a screen somebody scrolls
 * past.
 */
export function SettingsScreen() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })
  const status = useQuery({ queryKey: ['status'], queryFn: readStatus })

  const pointing = (status.data?.stages ?? [])
    .flatMap((stage) => [...stage.gaps, ...stage.brakes])
    .filter((condition) => !condition.cleared && condition.route?.startsWith('/settings'))

  const held = connections.data

  return (
    <main className={styles.screen}>
      <header className={styles.maskHeader}>
        <h1>Settings</h1>
        <p className={styles.lede}>
          Everything here takes effect from the next time it is used. Nothing needs
          a restart.
        </p>
      </header>

      {pointing.length > 0 && (
        <section>
          <h2 className={styles.heading}>Status is pointing here</h2>
          <ul className={styles.pointing}>
            {pointing.map((condition) => (
              <li key={`${condition.kind}-${condition.title}`}>
                <Link to={condition.route!}>{condition.title}</Link>
                <span className={styles.detail}>
                  {' '}
                  &mdash; {kindOf(condition)}. {condition.detail}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <h2 className={styles.heading}>What is set up</h2>
      <dl className={styles.summary}>
        <dt>prdb</dt>
        <dd>{held?.prdbConfigured ? 'A key is stored.' : 'Not configured, so nothing syncs.'}</dd>

        <dt>SABnzbd</dt>
        <dd>
          {held?.sabnzbdConfigured
            ? `${held.sabnzbdUrl}, category ${held.sabnzbdCategory}.`
            : held?.sabnzbdSkipped
              ? 'Skipped during setting up, so nothing is downloaded.'
              : 'Not configured, so nothing is downloaded.'}
        </dd>

        <dt>Indexers</dt>
        <dd>
          {Number(held?.indexerCount ?? 0) > 0
            ? `${held?.indexerCount} configured.`
            : held?.indexersSkipped
              ? 'None. This step was skipped, so nothing is searched for.'
              : 'None, so nothing is searched for.'}
        </dd>

        <dt>Library</dt>
        <dd>{held?.libraryRoot ?? 'No root answered yet.'}</dd>
      </dl>
    </main>
  )
}

function kindOf(condition: StatusCondition): string {
  return condition.kind === 'Gap'
    ? 'a Gap, which needs a repair'
    : 'a Brake, which is the tool doing what it was told'
}
