import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'

import { readConnections, readStatus } from '../api/client.ts'
import { connectionsKey } from '../onboarding/state.ts'
import { Loaded } from '../ui/Loaded.tsx'
import styles from './Settings.module.css'

export function SettingsScreen() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })
  const status = useQuery({ queryKey: ['status'], queryFn: readStatus })
  const pointing = (status.data?.stages ?? [])
    .flatMap((stage) => [...stage.gaps, ...stage.brakes])
    .filter((condition) => !condition.cleared && condition.route?.startsWith('/settings'))
  const routes = [...new Set(pointing.map((condition) => condition.route!.split('?')[0]))]

  return <main className={styles.screen}>
    <header className={styles.maskHeader}>
      <h1>Settings</h1>
      <p className={styles.lede}>Make it yours. Changes take effect without a restart.</p>
    </header>
    <h2 className={styles.heading}>Your setup</h2>
    <Loaded query={connections} of="your connections">{(held) => <div className={styles.setupCards}>
      <Link to="/settings/connections/prdb"><strong>prdb</strong><span>{held.prdbConfigured ? 'Key stored' : 'Not configured'}</span></Link>
      <Link to="/settings/connections/sabnzbd"><strong>SABnzbd</strong><span>{held.sabnzbdConfigured ? `Configured · ${held.sabnzbdCategory}` : held.sabnzbdSkipped ? 'Skipped during setup' : 'Not configured'}</span></Link>
      <Link to="/settings/connections"><strong>Indexers</strong><span>{held.indexerCount} configured</span></Link>
      <Link to="/settings/library"><strong>Library</strong><span>{held.libraryRoot ?? 'Not configured'}</span></Link>
    </div>}</Loaded>
    {routes.length > 0 && <section className={styles.settingConditions}>
      <h2 className={styles.heading}>Limits and attention</h2>
      <p className={styles.detail}>Gaps need a repair. Brakes reflect your choices or scheduled waits.</p>
      {routes.map((route) => {
        const conditions = pointing.filter((condition) => condition.route!.split('?')[0] === route)
        const gaps = conditions.filter((condition) => condition.kind === 'Gap').length
        return <details key={route}>
          <summary>{conditions[0].title} <span>{gaps > 0 ? `${gaps} Gaps` : `${conditions.length} Brakes`}</span></summary>
          <ul className={styles.pointing}>{conditions.map((condition, index) => <li key={index}>
            <strong>{condition.kind}: {condition.title}</strong>
            <p className={styles.detail}>{condition.detail}</p>
          </li>)}</ul>
          <Link to={conditions[0].route!}>Review settings →</Link>
        </details>
      })}
    </section>}
    {status.isError && <p className={styles.detail}>Current Status could not be read. <Link to="/status">Open Status</Link></p>}
  </main>
}
