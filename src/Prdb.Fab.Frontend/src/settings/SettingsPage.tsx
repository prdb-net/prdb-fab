import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, Navigate, Outlet, useLocation } from 'react-router'

import { readAccessState } from '../api/client.ts'
import { accessStateKey } from '../access/state.ts'
import { SettingsRail } from './SettingsRail.tsx'
import styles from './Settings.module.css'
import { PageLoading } from '../shell/LoadingScreen.tsx'

/**
 * Everything under `/settings` is for an installation that is set up. While
 * onboarding is unfinished the wizard is the whole page — it decides what is
 * asked and in which order, and a settings surface beside it would be a second
 * answer to the same question.
 *
 * It is also the layout route for the surface, which is what ADR 0058's rail
 * needs: the rail is beside every mask rather than a page in front of them, and
 * it stays mounted, so its reads happen once.
 */
export function SettingsGate() {
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })
  const location = useLocation()

  if (state.isPending) {
    return <PageLoading label="Loading Settings" />
  }

  if (state.data?.nextStep !== 'Complete') {
    return <Navigate to="/" replace />
  }

  // Below the breakpoint the rail *is* `/settings`, full width, and a mask
  // replaces it. Above it the two are panes side by side and `/settings` is the
  // surface's summary rather than a corridor of links.
  const atIndex = location.pathname === '/settings'

  return (
    <div className={styles.surface}>
      <div className={atIndex ? styles.railPane : styles.railPaneAside}>
        <SettingsRail />
      </div>
      <div className={atIndex ? styles.maskPaneOnlyWide : styles.maskPane}>
        <Outlet />
      </div>
    </div>
  )
}

/** One settings route: a header of its own, a way back, and its form. */
export function SettingsPage({
  title,
  lede,
  back,
  backLabel,
  children,
}: {
  title: string
  lede?: ReactNode
  /** Where the mask's own parent is, when it has one other than the surface. */
  back?: string
  backLabel?: string
  children: ReactNode
}) {
  const location = useLocation()
  const { to, label } = returnTo(location.search, back, backLabel)

  return (
    <main className={styles.screen}>
      <header className={styles.maskHeader}>
        <Link className={styles.back} to={to}>
          &larr; {label}
        </Link>
        <h1>{title}</h1>
        {lede && <p className={styles.lede}>{lede}</p>}
      </header>

      {children}
    </main>
  )
}

/**
 * Where back goes, which is where the visit began.
 *
 * ADR 0018 routes a Gap or a Brake to the setting behind it, and until ADR 0058
 * the return journey ended at the settings index whatever the visit started
 * from. A settings route now carries `?from=`, the way the Release route
 * carries its Catalogue context, and this reads it — refusing anything that is
 * not a single-slash relative path, exactly as `Chrome` already does, so the
 * parameter cannot become an open redirect.
 */
function returnTo(
  search: string,
  back: string | undefined,
  backLabel: string | undefined,
): { to: string; label: string } {
  const carried = new URLSearchParams(search).get('from')

  if (carried?.startsWith('/') && !carried.startsWith('//')) {
    return { to: carried, label: labelOf(carried) }
  }

  return { to: back ?? '/settings', label: backLabel ?? 'Settings' }
}

function labelOf(path: string): string {
  const known: Record<string, string> = {
    '/status': 'Status',
    '/downloads': 'Downloads',
    '/library': 'Library',
    '/review-queue': 'Review queue',
    '/settings': 'Settings',
  }

  return known[path.split('?')[0]] ?? 'Back'
}
