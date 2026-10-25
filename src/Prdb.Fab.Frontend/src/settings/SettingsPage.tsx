import { type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, Navigate, Outlet, useLocation } from 'react-router'

import { readAccessState } from '../api/client.ts'
import { accessStateKey } from '../access/state.ts'
import styles from './Settings.module.css'
import { InfoHelp } from '../ui/InfoHelp.tsx'
import { PageLoading } from '../shell/LoadingScreen.tsx'

/** Settings routes share the shell's navigation once setup is complete. */
export function SettingsGate() {
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })

  if (state.isPending) {
    return <PageLoading label="Loading Settings" />
  }

  if (state.data?.nextStep !== 'Complete') {
    return <Navigate to="/" replace />
  }

  return <div className={styles.surface}><Outlet /></div>
}

/** One settings route: a header of its own, a way back, and its form. */
export function SettingsPage({
  title,
  lede,
  help,
  back,
  backLabel,
  children,
}: {
  title: string
  lede?: ReactNode
  help?: ReactNode
  /** Where the mask's own parent is, when it has one other than the surface. */
  back?: string
  backLabel?: string
  children: ReactNode
}) {
  const location = useLocation()
  const search = new URLSearchParams(location.search).get('from')
  const parent = back ? { pathname: back, search: search ? `?from=${encodeURIComponent(search)}` : '' } : undefined

  return (
    <main className={styles.screen}>
      <header className={styles.maskHeader}>
        {parent && <Link className={styles.back} to={parent}>
          &larr; {backLabel ?? 'Settings'}
        </Link>}
        {help ? <InfoHelp key={title} label={title} heading={<h1>{title}</h1>}>{help}</InfoHelp> : <h1>{title}</h1>}
        {lede && <p className={styles.lede}>{lede}</p>}
      </header>

      {children}
    </main>
  )
}
