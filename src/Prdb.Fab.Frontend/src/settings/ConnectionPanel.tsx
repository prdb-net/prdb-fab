import { useEffect, useState, type ReactNode } from 'react'
import { revealConnectionKey } from '../api/client.ts'
import { formStyles } from '../ui/Form.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import styles from './Settings.module.css'

export function ConnectionSection({ title, description, action, children }: {
  title: string; description?: string; action?: ReactNode; children: ReactNode
}) {
  return <section className={styles.connectionSection}>
    <header className={styles.sectionHeader}>
      <div><h2>{title}</h2>{description && <p>{description}</p>}</div>{action}
    </header>
    {children}
  </section>
}

const outcomes: Record<string, string> = {
  Saved: 'Connection verified', WrongKey: 'API key rejected', NoApiAccess: 'API access unavailable',
  QuotaSpent: 'Request limit reached', LimitReached: 'Request limit reached',
  NotRightNow: 'Service unreachable', AccessDenied: 'Access denied',
  NotSabnzbd: 'Unexpected API response', NotAnIndexer: 'Unexpected API response',
  Refused: 'Request refused', NoCategories: 'Categories unavailable',
}

export function ConnectionStatus({ configured, outcome, checkedAt, successAt, checking, onCheck }: {
  configured: boolean; outcome?: string | null; checkedAt?: string | null; successAt?: string | null;
  checking?: boolean; onCheck?: () => void
}) {
  const tone = !configured || !outcome ? 'unknown' : outcome === 'Saved' ? 'success' : 'attention'
  return <section className={styles.connectionStatus} data-tone={tone} aria-label="Connection status">
    <div className={styles.statusHeading}>
      <div><span className={styles.eyebrow}>Connection status</span>
        <h2>{checking ? 'Checking connection…' : !configured ? 'Not connected' : !outcome ? 'Ready to check' : outcomes[outcome] ?? 'Needs attention'}</h2>
      </div>
      {configured && onCheck && <button className={`${formStyles.button} ${formStyles.primaryButton}`} disabled={checking} onClick={onCheck} type="button">{checking ? 'Checking…' : 'Test connection'}</button>}
    </div>
    <p className={styles.statusDescription}>{!configured ? 'Add your credentials below to connect this service.' : !outcome ? 'An API key is stored. Test the connection to confirm it works.' : outcome === 'Saved' ? 'The stored connection worked at the time shown below.' : 'The latest check did not succeed. Your saved credentials are still in place.'}</p>
    {configured && <dl className={styles.checkTimes}>
      <div><dt>Last checked</dt><dd><CheckTime value={checkedAt} /></dd></div>
      <div><dt>Last successful check</dt><dd><CheckTime value={successAt} /></dd></div>
    </dl>}
  </section>
}

function CheckTime({ value }: { value?: string | null }) {
  return value ? <time dateTime={value}>{new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' })}</time> : <>Not recorded yet</>
}

export function StoredKey({ connection, hint = '•••• ••••' }: { connection: string; hint?: string | null }) {
  const [key, setKey] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const [failed, setFailed] = useState(false)
  useEffect(() => {
    if (!key) return
    const hide = () => setKey(null)
    const timer = window.setTimeout(hide, 30_000)
    window.addEventListener('blur', hide)
    return () => { window.clearTimeout(timer); window.removeEventListener('blur', hide) }
  }, [key])
  return <div>
    <div className={styles.keyLabel}><span>API key</span><span className={styles.storedBadge}>Stored</span></div>
    <div className={styles.keyField}>
      <code>{key ?? hint ?? '•••• ••••'}</code>
      <button type="button" className={formStyles.button} disabled={pending} aria-pressed={key !== null} onClick={async () => {
        if (key) { setKey(null); return }
        setPending(true); setFailed(false)
        try { setKey((await revealConnectionKey(connection)).apiKey) }
        catch { setFailed(true) }
        finally { setPending(false) }
      }}>{pending ? 'Loading…' : key ? 'Hide' : 'Show'}</button>
    </div>
    <p className={styles.detail}>{key ? 'Hidden automatically after 30 seconds or when you leave this window.' : 'The saved key is used for this connection. Show it to verify the value.'}</p>
    {failed && <Verdict tone="refusal">Could not display the saved key. Try again.</Verdict>}
  </div>
}
