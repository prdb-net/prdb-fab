import styles from './Settings.module.css'

/**
 * The rail's icons, in the shell's own set: one 24-unit box, `currentColor`,
 * stroked at 1.7, no fills except where the shell already fills one.
 *
 * Here rather than in `Chrome.tsx` because these name settings groups rather
 * than the application's destinations, and the shell's `IconName` is the union
 * of what its sidebar holds. Three of the eight — downloads, library, settings —
 * are the shell's own paths, repeated rather than exported, so that neither
 * file has to know when the other changes a glyph.
 */
export type SettingsIconName =
  | 'account'
  | 'automation'
  | 'backup'
  | 'connections'
  | 'download'
  | 'identification'
  | 'library'
  | 'reporting'

export function SettingsIcon({ name }: { name: SettingsIconName }) {
  const common = {
    fill: 'none',
    stroke: 'currentColor',
    strokeLinecap: 'round' as const,
    strokeLinejoin: 'round' as const,
    strokeWidth: 1.7,
  }

  return (
    <svg aria-hidden="true" className={styles.icon} viewBox="0 0 24 24">
      {name === 'connections' && <path d="M9.5 14.5 14.5 9.5m-4 8.5-1.8 1.8a3.5 3.5 0 0 1-5-5L5.5 13m8-7 1.8-1.8a3.5 3.5 0 0 1 5 5L18.5 11" {...common} />}
      {name === 'automation' && <path d="M12 3v3m0 12v3M5.6 5.6l2.1 2.1m8.6 8.6 2.1 2.1M3 12h3m12 0h3M5.6 18.4l2.1-2.1m8.6-8.6 2.1-2.1M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" {...common} />}
      {name === 'identification' && <path d="M3 8V5a2 2 0 0 1 2-2h3m8 0h3a2 2 0 0 1 2 2v3M3 16v3a2 2 0 0 0 2 2h3m8 0h3a2 2 0 0 0 2-2v-3M8.5 11a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm-2.5 6c0-1.7 1.1-2.8 2.5-2.8S11 15.3 11 17m2.5-6h4m-4 3.5h4" {...common} />}
      {name === 'download' && <path d="M12 3v12m-4-4 4 4 4-4M4 20h16" {...common} />}
      {name === 'library' && <path d="M4 5h16v15H4V5Zm4 0v15m9-15v15M3 9h18" {...common} />}
      {name === 'reporting' && <path d="M12 3.5a8.5 8.5 0 1 0 8.5 8.5M12 3.5V12h8.5M12 3.5a8.5 8.5 0 0 1 8.5 8.5" {...common} />}
      {name === 'backup' && <path d="M4 7c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3Zm0 0v10c0 1.7 3.6 3 8 3s8-1.3 8-3V7M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3" {...common} />}
      {name === 'account' && <path d="M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm0 0c-4 0-7 2.2-7 5v3h14v-3c0-2.8-3-5-7-5Z" {...common} />}
    </svg>
  )
}
