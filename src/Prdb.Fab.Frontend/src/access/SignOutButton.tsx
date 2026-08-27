import { useMutation, useQueryClient } from '@tanstack/react-query'

import { signOut } from '../api/client.ts'
import { accessStateKey } from './state.ts'
import styles from './Access.module.css'

/**
 * ADR 0010: signing out revokes the row, so the cookie it backed is worthless
 * from that moment rather than at its expiry.
 *
 * It lives on ADR 0020's Account route, beside the password it is the other
 * half of: what a session is, and how to end one.
 */
export function SignOutButton() {
  const queries = useQueryClient()

  const out = useMutation({
    mutationFn: signOut,
    onSettled: async () => {
      // Whatever happened, the answer comes from the state endpoint rather than
      // from what this call returned.
      queries.clear()
      await queries.invalidateQueries({ queryKey: accessStateKey })
    },
  })

  return (
    <button
      className={styles.signOut}
      type="button"
      onClick={() => out.mutate()}
      disabled={out.isPending}
    >
      Sign out
    </button>
  )
}
