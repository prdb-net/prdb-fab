import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { useQuery } from '@tanstack/react-query'
import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'

import { readAccessState } from './api/client.ts'
import { AccessGate } from './access/AccessGate.tsx'
import { SignOutButton } from './access/SignOutButton.tsx'
import { accessStateKey, createQueryClient } from './access/state.ts'
import { OnboardingScreen, routeFor } from './onboarding/OnboardingScreen.tsx'
import { SkeletonScreen } from './skeleton/SkeletonScreen.tsx'
import './index.css'

// ADR 0036: React Router in library mode only — the router is a library this
// application calls, not a framework it is built inside. Anything linkable
// lives in the address, which is why every onboarding step has one before the
// form behind it exists.
const queries = createQueryClient()

/**
 * Where someone lands who typed the address and nothing more. The state
 * endpoint says which step is next, and the answer is a redirect rather than a
 * screen of its own — so the address always names what is being looked at.
 */
function Landing() {
  const state = useQuery({ queryKey: accessStateKey, queryFn: readAccessState })

  if (state.isPending) {
    return null
  }

  return <Navigate to={routeFor(state.data?.nextStep)} replace />
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queries}>
      <BrowserRouter>
        <AccessGate>
          <SignOutButton />
          <Routes>
            <Route path="/" element={<Landing />} />
            <Route path="/onboarding/:step" element={<OnboardingScreen />} />
            <Route path="/skeleton" element={<SkeletonScreen />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </AccessGate>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
