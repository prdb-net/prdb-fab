import { StrictMode, useEffect } from 'react'
import { createRoot } from 'react-dom/client'
import { useQuery } from '@tanstack/react-query'
import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Link, Navigate, Route, Routes, useLocation } from 'react-router'

import { readAccessState } from './api/client.ts'
import { AccessGate } from './access/AccessGate.tsx'
import { WantedScreen } from './catalogue/WantedScreen.tsx'
import { WhatsNewScreen } from './catalogue/WhatsNewScreen.tsx'
import { ActorsScreen } from './catalogue/ActorsScreen.tsx'
import { SitesScreen } from './catalogue/SitesScreen.tsx'
import { SearchScreen } from './catalogue/SearchScreen.tsx'
import { accessStateKey, createQueryClient } from './access/state.ts'
import { OnboardingScreen, routeFor } from './onboarding/OnboardingScreen.tsx'
import { AccountScreen } from './settings/AccountScreen.tsx'
import { ConnectionsScreen } from './settings/ConnectionsScreen.tsx'
import { IndexerSettings } from './settings/IndexerSettings.tsx'
import { IdentificationScreen } from './settings/IdentificationScreen.tsx'
import { PrdbSettings } from './settings/PrdbSettings.tsx'
import { SabnzbdSettings } from './settings/SabnzbdSettings.tsx'
import { SettingsGate } from './settings/SettingsPage.tsx'
import { SettingsScreen } from './settings/SettingsScreen.tsx'
import { ReportingScreen } from './settings/ReportingScreen.tsx'
import { AutomationScreen } from './settings/AutomationScreen.tsx'
import { DownloadSettingsScreen } from './settings/DownloadSettingsScreen.tsx'
import { Chrome } from './shell/Chrome.tsx'
import { ReleaseScreen } from './release/ReleaseScreen.tsx'
import { DownloadsScreen } from './download/DownloadsScreen.tsx'
import { LibraryScreen } from './filing/LibraryScreen.tsx'
import { LibraryEntryScreen } from './filing/LibraryEntryScreen.tsx'
import { ReviewQueueScreen } from './filing/ReviewQueueScreen.tsx'
import { ReviewQueueComparisonPrototype } from './filing/ReviewQueueComparisonPrototype.tsx'
import { OperationLogScreen } from './filing/OperationLogScreen.tsx'
import { LibrarySettingsScreen } from './settings/LibraryScreen.tsx'
import { PageLoading } from './shell/LoadingScreen.tsx'
import { StatusScreen } from './status/StatusScreen.tsx'
import './index.css'

// ADR 0036: React Router in library mode only — the router is a library this
// application calls, not a framework it is built inside. Anything linkable
// lives in the address, which is why every onboarding step has one before the
// form behind it exists.
const queries = createQueryClient()

/**
 * Where someone lands who typed the address and nothing more.
 *
 * While setting up is unfinished the state endpoint says which step is next and
 * the answer is a redirect, so the address always names what is being looked
 * at. Once it is finished this stops redirecting and *is* the landing page:
 * What's New, which ADR 0013 calls it and which is what the catalogue exists
 * for.
 */
function Landing() {
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })

  if (state.isPending) {
    return <PageLoading label="Loading What’s new" />
  }

  if (state.data?.nextStep === 'Complete') {
    return <WhatsNewScreen />
  }

  return <Navigate to={routeFor(state.data?.nextStep)} replace />
}

function DocumentTitle() {
  const { pathname } = useLocation()

  useEffect(() => {
    document.title = `${pageTitle(pathname)} · prdb-fab`
  }, [pathname])

  return null
}

function pageTitle(pathname: string): string {
  if (pathname === '/') return 'What’s new'
  if (pathname === '/wanted') return 'Wanted'
  if (pathname === '/search') return 'Search'
  if (pathname === '/sites') return 'Sites'
  if (pathname.startsWith('/sites/')) return 'Site'
  if (pathname === '/actors') return 'Actors'
  if (pathname.startsWith('/actors/')) return 'Actor'
  if (pathname === '/releases') return 'Releases'
  if (pathname === '/downloads') return 'Downloads'
  if (pathname === '/library') return 'Library'
  if (pathname.startsWith('/library/')) return 'Library entry'
  if (pathname === '/review-queue') return 'Review queue'
  if (pathname === '/operation-log') return 'Operation log'
  if (pathname === '/status') return 'Status'
  if (pathname === '/settings') return 'Settings'
  if (pathname === '/settings/connections') return 'Connections'
  if (pathname.startsWith('/settings/connections/indexers/')) return 'Indexer'
  if (pathname === '/settings/connections/prdb') return 'prdb connection'
  if (pathname === '/settings/connections/sabnzbd') return 'SABnzbd connection'
  if (pathname === '/settings/account') return 'Account'
  if (pathname === '/settings/identification') return 'Identification'
  if (pathname === '/settings/downloads') return 'Download settings'
  if (pathname === '/settings/automation') return 'Automation'
  if (pathname.startsWith('/settings/automation/rules/')) return 'Automation rule'
  if (pathname === '/settings/library') return 'Library settings'
  if (pathname === '/settings/reporting') return 'Reporting settings'
  if (pathname.startsWith('/onboarding/')) return 'Setup'
  return 'Page not found'
}

function NotFoundScreen() {
  return (
    <main className="routeMessage">
      <h1>Page not found</h1>
      <p>The address does not name a page in prdb-fab.</p>
      <Link to="/">Go to What&rsquo;s new</Link>
    </main>
  )
}

const reviewPrototype = import.meta.env.DEV
  && window.location.pathname === '/review-queue'
  && new URLSearchParams(window.location.search).has('variant')

createRoot(document.getElementById('root')!).render(reviewPrototype
  ? <StrictMode>
      <BrowserRouter>
        <ReviewQueueComparisonPrototype />
      </BrowserRouter>
    </StrictMode>
  : <StrictMode>
      <QueryClientProvider client={queries}>
        <BrowserRouter>
          <DocumentTitle />
          <AccessGate>
            <Chrome>
              <Routes>
              <Route path="/" element={<Landing />} />
              <Route path="/onboarding/:step" element={<OnboardingScreen />} />
              {/* ADR 0010's last step lands here, which is what makes onboarding
                  lead to a first download rather than to a page saying it could. */}
              <Route path="/wanted" element={<WantedScreen />} />
              <Route path="/search" element={<SearchScreen />} />
              <Route path="/sites" element={<SitesScreen />} />
              <Route path="/sites/:id" element={<SitesScreen />} />
              <Route path="/actors" element={<ActorsScreen />} />
              <Route path="/actors/:id" element={<ActorsScreen />} />
              <Route path="/releases" element={<ReleaseScreen />} />
              <Route path="/downloads" element={<DownloadsScreen />} />
              <Route path="/library" element={<LibraryScreen />} />
              <Route path="/library/:id" element={<LibraryEntryScreen />} />
              <Route path="/review-queue" element={<ReviewQueueScreen />} />
              <Route path="/operation-log" element={<OperationLogScreen />} />
              <Route path="/status" element={<StatusScreen />} />
              {/* ADR 0020: routes rather than one page with anchors, one level
                  down as well — every indexer has its own. */}
              <Route path="/settings" element={<SettingsGate />}>
                <Route index element={<SettingsScreen />} />
                <Route path="connections" element={<ConnectionsScreen />} />
                <Route path="connections/prdb" element={<PrdbSettings />} />
                <Route path="connections/sabnzbd" element={<SabnzbdSettings />} />
                <Route path="connections/indexers/new" element={<IndexerSettings />} />
                <Route path="connections/indexers/:id" element={<IndexerSettings />} />
                <Route path="account" element={<AccountScreen />} />
                <Route path="identification" element={<IdentificationScreen />} />
                <Route path="downloads" element={<DownloadSettingsScreen />} />
                <Route path="automation" element={<AutomationScreen />} />
                <Route path="automation/rules/:id" element={<AutomationScreen />} />
                <Route path="library" element={<LibrarySettingsScreen />} />
                <Route path="reporting" element={<ReportingScreen />} />
              </Route>
              <Route path="*" element={<NotFoundScreen />} />
              </Routes>
            </Chrome>
          </AccessGate>
        </BrowserRouter>
      </QueryClientProvider>
    </StrictMode>)
