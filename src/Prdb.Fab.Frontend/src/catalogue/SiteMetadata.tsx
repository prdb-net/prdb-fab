import { InfoHelp } from '../ui/InfoHelp.tsx'
import styles from './SiteMetadata.module.css'
import type { VideoPreview } from '../api/client.ts'

/** Optional upstream facts: platform and studio Network are separate relationships. */
export function SiteMetadataView({ metadata }: { metadata?: VideoPreview['siteMetadata'] }) {
  if (!metadata) return null
  const links = [...(metadata.url ? [{ label: 'Site website', url: metadata.url }] : []), ...(metadata.links ?? [])]
  if (!metadata.network && !metadata.platformTitle && !metadata.accountHandle && links.length === 0) return null
  return (
    <InfoHelp label="Site details">
      {(metadata.network || metadata.platformTitle || metadata.accountHandle) && (
        <dl className={styles.facts}>
          {metadata.network && <><dt>Network</dt><dd>{metadata.network}</dd></>}
          {metadata.platformTitle && <><dt>Platform</dt><dd>{metadata.platformTitle}</dd></>}
          {metadata.accountHandle && <><dt>Account</dt><dd>{metadata.accountHandle}</dd></>}
        </dl>
      )}
      {links.length > 0 && (
        <nav aria-label="Site links">
          {links.map((link, index) => link.url && (
            <p key={`${link.url}-${index}`}>
              <a href={link.url} rel="noopener noreferrer" target="_blank">{link.label || 'Website'}</a>
            </p>
          ))}
        </nav>
      )}
    </InfoHelp>
  )
}

/** Descriptions are text, including when upstream supplies markup. */
export function VideoDescription({ description }: { description?: string | null }) {
  return description?.trim() ? <InfoHelp label="Video description"><p className={styles.description}>{description}</p></InfoHelp> : null
}
