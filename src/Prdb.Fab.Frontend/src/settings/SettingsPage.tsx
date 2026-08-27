import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, Navigate, Outlet } from 'react-router'

import { readAccessState } from '../api/client.ts'
import { accessStateKey } from '../access/state.ts'
import styles from './Settings.module.css'

/**
 * Everything under `/settings` is for an installation that is set up. While
 * onboarding is unfinished the wizard is the whole page — it decides what is
 * asked and in which order, and a settings surface beside it would be a second
 * answer to the same question.
 */
export function SettingsGate() {
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })

  if (state.isPending) {
    return null
  }

  if (state.data?.nextStep !== 'Complete') {
    return <Navigate to="/" replace />
  }

  return <Outlet />
}

/** One settings route: where it goes back to, what it is, and its form. */
export function SettingsPage({
  title,
  lede,
  back = '/settings',
  backLabel = 'Settings',
  children,
}: {
  title: string
  lede?: ReactNode
  back?: string
  backLabel?: string
  children: ReactNode
}) {
  return (
    <main className={styles.screen}>
      <Link className={styles.back} to={back}>
        &larr; {backLabel}
      </Link>

      <h1>{title}</h1>
      {lede && <p className={styles.lede}>{lede}</p>}

      {children}
    </main>
  )
}
