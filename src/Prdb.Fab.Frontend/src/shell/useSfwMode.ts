import { useEffect, useState } from 'react'

const storageKey = 'prdb-fab.sfw'

function readSfwMode(): boolean {
  try {
    return localStorage.getItem(storageKey) === 'true'
  } catch {
    return false
  }
}

/** A browser preference, restored before the first artwork can render. */
export function useSfwMode() {
  const [enabled, setEnabled] = useState(readSfwMode)

  useEffect(() => {
    const sync = (event: StorageEvent) => {
      if (event.key === storageKey || event.key === null) setEnabled(readSfwMode())
    }
    window.addEventListener('storage', sync)
    return () => window.removeEventListener('storage', sync)
  }, [])

  function toggle() {
    const next = !enabled
    setEnabled(next)
    try {
      localStorage.setItem(storageKey, String(next))
    } catch {
      // The switch still works for this session when storage is unavailable.
    }
  }

  return { enabled, toggle }
}
