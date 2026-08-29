import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { useQuery } from '@tanstack/react-query'
import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'

import { readAccessState } from './api/client.ts'
import { AccessGate } from './access/AccessGate.tsx'
import { WantedScreen } from './catalogue/WantedScreen.tsx'
import { WhatsNewScreen } from './catalogue/WhatsNewScreen.tsx'
import { ActorsScreen } from './catalogue/ActorsScreen.tsx'
import { SitesScreen } from './catalogue/SitesScreen.tsx'
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
import { Chrome } from './shell/Chrome.tsx'
import { ReleaseScreen } from './release/ReleaseScreen.tsx'
import { DownloadsScreen } from './download/DownloadsScreen.tsx'
import { LibraryScreen } from './filing/LibraryScreen.tsx'
import { LibraryEntryScreen } from './filing/LibraryEntryScreen.tsx'
import { ReviewQueueScreen } from './filing/ReviewQueueScreen.tsx'
import { OperationLogScreen } from './filing/OperationLogScreen.tsx'
import { LibrarySettingsScreen } from './settings/LibraryScreen.tsx'
import { SkeletonScreen } from './skeleton/SkeletonScreen.tsx'
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
    return null
  }

  if (state.data?.nextStep === 'Complete') {
    return <WhatsNewScreen />
  }

  return <Navigate to={routeFor(state.data?.nextStep)} replace />
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queries}>
      <BrowserRouter>
        <AccessGate>
          <Chrome>
            <Routes>
              <Route path="/" element={<Landing />} />
              <Route path="/onboarding/:step" element={<OnboardingScreen />} />
              {/* ADR 0010's last step lands here, which is what makes onboarding
                  lead to a first download rather than to a page saying it could. */}
              <Route path="/wanted" element={<WantedScreen />} />
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
                <Route path="library" element={<LibrarySettingsScreen />} />
              </Route>
              <Route path="/skeleton" element={<SkeletonScreen />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </Chrome>
        </AccessGate>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
