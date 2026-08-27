import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'

import { SkeletonScreen } from './skeleton/SkeletonScreen.tsx'
import './index.css'

// ADR 0036: React Router in library mode only — the router is a library this
// application calls, not a framework it is built inside. Anything linkable
// lives in the address, which is why the page number below is a search
// parameter rather than component state.
//
// ADR 0040: a verdict is HTTP 200 with a typed body, so there is nothing here
// for TanStack Query to retry. Retries are off deliberately rather than by
// omission: a failed request is a failed request, and ADR 0041 already decided
// that nothing retries inside one.
const queries = new QueryClient({
  defaultOptions: {
    queries: { retry: false, refetchOnWindowFocus: false },
    mutations: { retry: false },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queries}>
      <BrowserRouter>
        <Routes>
          <Route path="/skeleton" element={<SkeletonScreen />} />
          <Route path="*" element={<Navigate to="/skeleton" replace />} />
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
)
