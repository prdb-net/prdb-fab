import type { VideoPreview } from '../api/client.ts'

/** Optional upstream facts: platform and studio Network are separate relationships. */
export function SiteMetadataView({ metadata }: { metadata?: VideoPreview['siteMetadata'] }) {
  if (!metadata) return null
  const links = [...(metadata.url ? [{ label: 'Site website', url: metadata.url }] : []), ...(metadata.links ?? [])]
  return (
    <div>
      {(metadata.network || metadata.platformTitle || metadata.accountHandle) && (
        <dl>
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
    </div>
  )
}

/** Descriptions are text, including when upstream supplies markup. */
export function VideoDescription({ description }: { description?: string | null }) {
  return description?.trim() ? <p style={{ whiteSpace: 'pre-wrap' }}>{description}</p> : null
}
