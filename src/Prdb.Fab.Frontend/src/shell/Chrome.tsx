import type { ReactNode } from 'react'
import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useLocation } from 'react-router'

import { readAccessState, readReviewQueueCount } from '../api/client.ts'
import { accessStateKey } from '../access/state.ts'
import styles from './Chrome.module.css'

type IconName =
  | 'actors'
  | 'download'
  | 'library'
  | 'log'
  | 'more'
  | 'new'
  | 'review'
  | 'search'
  | 'settings'
  | 'status'
  | 'sites'
  | 'wanted'

type Destination = {
  label: string
  mobileLabel?: string
  to: string
  icon: IconName
}

const discover: Destination[] = [
  { label: 'What’s new', mobileLabel: 'New', to: '/', icon: 'new' },
  { label: 'Search', to: '/search', icon: 'search' },
  { label: 'Sites', to: '/sites', icon: 'sites' },
  { label: 'Actors', to: '/actors', icon: 'actors' },
  { label: 'Wanted', to: '/wanted', icon: 'wanted' },
]

const work: Destination[] = [
  { label: 'Downloads', to: '/downloads', icon: 'download' },
  { label: 'Library', to: '/library', icon: 'library' },
  { label: 'Review queue', to: '/review-queue', icon: 'review' },
  { label: 'Operation log', to: '/operation-log', icon: 'log' },
]

const primaryMobile = [discover[0], discover[1], work[0], work[1]]

/**
 * The way between the application's two bodies of work: discovering what to
 * fetch, and following what becomes of it. Releases remain contextual to a
 * Video rather than becoming a tenth top-level destination.
 *
 * The shell appears only once setup is complete. During onboarding the wizard
 * remains the whole page, as settled by ADR 0010.
 */
export function Chrome({ children }: { children: ReactNode }) {
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })
  const review = useQuery({
    queryKey: ['review-queue-count'],
    queryFn: readReviewQueueCount,
    enabled: state.data?.nextStep === 'Complete',
  })
  const location = useLocation()
  const contextDestination = releaseContextDestination(location.pathname, location.search)
  const [moreOpen, setMoreOpen] = useState(false)
  const closeButton = useRef<HTMLButtonElement>(null)
  const moreButton = useRef<HTMLButtonElement>(null)
  const sheet = useRef<HTMLDivElement>(null)
  const reviewCount = Number(review.data?.open ?? 0)

  useEffect(() => setMoreOpen(false), [location.pathname])

  useEffect(() => {
    if (!moreOpen) return

    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    closeButton.current?.focus()

    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setMoreOpen(false)
        moreButton.current?.focus()
      }

      if (event.key !== 'Tab') return

      const focusable = sheet.current?.querySelectorAll<HTMLElement>('a[href], button:not([disabled])')
      const first = focusable?.item(0)
      const last = focusable?.item((focusable?.length ?? 1) - 1)

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last?.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first?.focus()
      }
    }

    window.addEventListener('keydown', closeOnEscape)
    return () => {
      document.body.style.overflow = previousOverflow
      window.removeEventListener('keydown', closeOnEscape)
    }
  }, [moreOpen])

  if (state.data?.nextStep !== 'Complete') {
    return children
  }

  return (
    <div className={styles.shell}>
      <aside className={styles.sidebar}>
        <Brand />
        <nav aria-label="Primary navigation">
          <NavigationGroup
            activeTo={contextDestination}
            label="Discover"
            destinations={discover}
            reviewCount={reviewCount}
          />
          <NavigationGroup
            activeTo={contextDestination}
            label="Fetch & build"
            destinations={work}
            reviewCount={reviewCount}
          />
        </nav>
        <nav className={styles.navBottom} aria-label="System navigation">
          <DestinationLink
            destination={{ label: 'Status', to: '/status', icon: 'status' }}
            activeTo={contextDestination}
            reviewCount={reviewCount}
          />
          <DestinationLink
            destination={{ label: 'Settings', to: '/settings', icon: 'settings' }}
            activeTo={contextDestination}
            reviewCount={reviewCount}
          />
        </nav>
      </aside>

      <header className={styles.mobileTop}>
        <Brand />
      </header>

      <div className={styles.content}>{children}</div>

      <nav className={styles.mobileBottom} aria-label="Primary navigation">
        {primaryMobile.map((destination) => (
          <DestinationLink
            destination={destination}
            activeTo={contextDestination}
            key={destination.to}
            mobile
            reviewCount={reviewCount}
          />
        ))}
        <button
          aria-expanded={moreOpen}
          aria-controls="mobile-navigation"
          className={`${styles.mobileLink} ${isMoreDestination(location.pathname) || isMoreContext(contextDestination) ? styles.active : ''}`}
          onClick={() => setMoreOpen(true)}
          ref={moreButton}
          type="button"
        >
          <span className={styles.mobileIcon}>
            <Icon name="more" />
            {reviewCount > 0 && <span className={styles.mobileBadge}>{reviewCount}</span>}
          </span>
          More
        </button>
      </nav>

      {moreOpen && (
        <div
          className={styles.sheetBackdrop}
          onMouseDown={(event) => {
            if (event.currentTarget === event.target) {
              setMoreOpen(false)
              moreButton.current?.focus()
            }
          }}
        >
          <div
            aria-label="Navigation"
            aria-modal="true"
            className={styles.sheet}
            id="mobile-navigation"
            ref={sheet}
            role="dialog"
          >
            <div aria-hidden="true" className={styles.sheetHandle} />
            <header className={styles.sheetHeader}>
              <strong>Go to</strong>
              <button
                aria-label="Close navigation"
                className={styles.closeButton}
                onClick={() => {
                  setMoreOpen(false)
                  moreButton.current?.focus()
                }}
                ref={closeButton}
                type="button"
              >
                &times;
              </button>
            </header>
            <nav className={styles.sheetGrid} aria-label="All destinations">
              <SheetGroup
                activeTo={contextDestination}
                label="Discover"
                destinations={discover}
                reviewCount={reviewCount}
              />
              <SheetGroup
                activeTo={contextDestination}
                label="Fetch & build"
                destinations={work}
                reviewCount={reviewCount}
              />
              <span className={styles.sheetLabel}>System</span>
              <DestinationLink
                destination={{ label: 'Status', to: '/status', icon: 'status' }}
                activeTo={contextDestination}
                reviewCount={reviewCount}
                sheet
              />
              <DestinationLink
                destination={{ label: 'Settings', to: '/settings', icon: 'settings' }}
                activeTo={contextDestination}
                reviewCount={reviewCount}
                sheet
              />
            </nav>
          </div>
        </div>
      )}
    </div>
  )
}

function Brand() {
  return (
    <Link className={styles.brand} to="/">
      <span className={styles.brandMark}>pf</span>
      <span>prdb-fab</span>
    </Link>
  )
}

function NavigationGroup({
  activeTo,
  label,
  destinations,
  reviewCount,
}: {
  activeTo: string | null
  label: string
  destinations: Destination[]
  reviewCount: number
}) {
  return (
    <div className={styles.navGroup}>
      <div className={styles.navLabel}>{label}</div>
      {destinations.map((destination) => (
        <DestinationLink
          destination={destination}
          activeTo={activeTo}
          key={destination.to}
          reviewCount={reviewCount}
        />
      ))}
    </div>
  )
}

function SheetGroup({
  activeTo,
  label,
  destinations,
  reviewCount,
}: {
  activeTo: string | null
  label: string
  destinations: Destination[]
  reviewCount: number
}) {
  return (
    <>
      <span className={styles.sheetLabel}>{label}</span>
      {destinations.map((destination) => (
        <DestinationLink
          destination={destination}
          activeTo={activeTo}
          key={destination.to}
          reviewCount={reviewCount}
          sheet
        />
      ))}
    </>
  )
}

function DestinationLink({
  destination,
  activeTo,
  mobile = false,
  reviewCount,
  sheet = false,
}: {
  destination: Destination
  activeTo: string | null
  mobile?: boolean
  reviewCount: number
  sheet?: boolean
}) {
  const location = useLocation()
  const className = mobile ? styles.mobileLink : sheet ? styles.sheetLink : styles.navLink
  const contextActive = activeTo === destination.to
  const routeActive = destination.to === '/'
    ? location.pathname === '/'
    : location.pathname === destination.to || location.pathname.startsWith(`${destination.to}/`)
  const active = routeActive || contextActive

  return (
    <Link
      aria-current={routeActive ? 'page' : contextActive ? 'location' : undefined}
      className={`${className} ${active ? styles.active : ''}`}
      to={destination.to}
    >
      {mobile ? (
        <span className={styles.mobileIcon}>
          <Icon name={destination.icon} />
        </span>
      ) : (
        <Icon name={destination.icon} />
      )}
      <span>{mobile ? destination.mobileLabel ?? destination.label : destination.label}</span>
      {destination.to === '/review-queue' && (
        <span className={`${styles.reviewBadge} ${reviewCount === 0 ? styles.emptyBadge : ''}`}>
          {reviewCount}
        </span>
      )}
    </Link>
  )
}

function isMoreDestination(pathname: string): boolean {
  return ['/wanted', '/sites', '/actors', '/review-queue', '/operation-log', '/status', '/settings'].some(
    (destination) => pathname === destination || pathname.startsWith(`${destination}/`),
  )
}

function isMoreContext(destination: string | null): boolean {
  return destination !== null && !primaryMobile.some((entry) => entry.to === destination)
}

function releaseContextDestination(pathname: string, search: string): string | null {
  if (pathname !== '/releases') return null

  const parameters = new URLSearchParams(search)
  const from = parameters.get('from')
  if (from?.startsWith('/') && !from.startsWith('//')) {
    const fromPath = from.split('?')[0]
    if (fromPath === '/') return '/'
    for (const destination of [...discover, ...work]) {
      if (fromPath === destination.to || fromPath.startsWith(`${destination.to}/`)) {
        return destination.to
      }
    }
  }

  if (parameters.has('site')) return '/sites'
  if (parameters.has('actor')) return '/actors'
  if (parameters.has('video')) return '/'
  return null
}

function Icon({ name }: { name: IconName }) {
  const common = {
    fill: 'none',
    stroke: 'currentColor',
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    strokeWidth: 1.7,
  }

  return (
    <svg aria-hidden="true" className={styles.icon} viewBox="0 0 24 24">
      {name === 'new' && <path d="m12 3 1.5 5.5L19 10l-5.5 1.5L12 17l-1.5-5.5L5 10l5.5-1.5L12 3Zm7 12 .6 2.4L22 18l-2.4.6L19 21l-.6-2.4L16 18l2.4-.6L19 15Z" fill="currentColor" />}
      {name === 'search' && <path d="m20 20-4.3-4.3m2.3-5.2a7.5 7.5 0 1 1-15 0 7.5 7.5 0 0 1 15 0Z" {...common} />}
      {name === 'sites' && <path d="M4 4h6v6H4V4Zm10 0h6v6h-6V4ZM4 14h6v6H4v-6Zm10 0h6v6h-6v-6Z" {...common} />}
      {name === 'actors' && <path d="M8.5 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7Zm7-1a3 3 0 1 0 0-6m-7 9C5.3 13 3 14.8 3 17.2V20h11v-2.8c0-2.4-2.3-4.2-5.5-4.2Zm7 0c2.9 0 5.5 1.7 5.5 4v3h-5" {...common} />}
      {name === 'wanted' && <path d="M12 20S4 15.6 4 9.5A4.5 4.5 0 0 1 12 6.7a4.5 4.5 0 0 1 8 2.8C20 15.6 12 20 12 20Z" {...common} />}
      {name === 'download' && <path d="M12 3v12m-4-4 4 4 4-4M4 20h16" {...common} />}
      {name === 'library' && <path d="M4 5h16v15H4V5Zm4 0v15m9-15v15M3 9h18" {...common} />}
      {name === 'review' && <path d="M12 3 3.5 19h17L12 3Zm0 5v5m0 3v.2" {...common} />}
      {name === 'log' && <path d="M6 3h12v18H6V3Zm3 5h6m-6 4h6m-6 4h4" {...common} />}
      {name === 'status' && <path d="M4 18V9m5 9V5m5 13v-7m5 7V3M3 21h18" {...common} />}
      {name === 'settings' && <path d="M12 15.2a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4Zm8-3.2-2.1-1 .2-2.3-2.2-2.2-2.3.3-1-2.2H9.4l-1 2.2-2.3-.3-2.2 2.2.2 2.3L2 12l1.1 3 2.3.4.7 2.2 3 1.2 1.8-1.5 1.8 1.5 3-1.2.7-2.2 2.3-.4 1.1-3Z" {...common} />}
      {name === 'more' && <path d="M5 12h.01M12 12h.01M19 12h.01" stroke="currentColor" strokeLinecap="round" strokeWidth="3" />}
    </svg>
  )
}
