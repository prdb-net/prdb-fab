import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router'

import styles from './ReviewQueueComparisonPrototype.module.css'

// PROTOTYPE — throw away after answering this question:
// Three Review Queue comparison variants, switchable via ?variant=, on the
// existing /review-queue route. Synthetic examples protect private library data.

type PrototypeEntry = {
  id: string
  fileName: string
  folderName: string
  releaseName: string
  title: string
  site: string
  releaseDate: string
  confidence: string
  matchedBy: string
  fileRuntime: string
  prdbRuntime: string
  quality: string
  size: string
  accent: string
}

const entries: PrototypeEntry[] = [
  {
    id: 'one',
    fileName: 'ExampleStudio.26.08.30.Alex.River.Afternoon.Session.2160p-GRP.mp4',
    folderName: 'ExampleStudio.26.08.30.Alex.River.Afternoon.Session.2160p-GRP',
    releaseName: 'ExampleStudio.26.08.30.Alex.River.Afternoon.Session.2160p-GRP',
    title: 'Afternoon Session',
    site: 'Example Studio',
    releaseDate: 'Aug 30, 2026',
    confidence: 'Probable',
    matchedBy: 'release name',
    fileRuntime: '27:33',
    prdbRuntime: '27:35 consensus',
    quality: '2160p · H.264',
    size: '5.4 GiB',
    accent: '#315f78',
  },
  {
    id: 'two',
    fileName: 'SampleNetwork.26.08.23.Morgan.Lee.New.Bedroom.Set.2160p-GRP.mp4',
    folderName: 'SampleNetwork.26.08.23.Morgan.Lee.New.Bedroom.Set.2160p-GRP',
    releaseName: 'SampleNetwork.26.08.23.Morgan.Lee.New.Bedroom.Set.2160p-GRP',
    title: 'The New Bedroom Set',
    site: 'Sample Network',
    releaseDate: 'Aug 23, 2026',
    confidence: 'Probable',
    matchedBy: 'release name',
    fileRuntime: '40:30',
    prdbRuntime: '40:28 consensus',
    quality: '2160p · H.264',
    size: '4.6 GiB',
    accent: '#68445e',
  },
  {
    id: 'three',
    fileName: 'DemoPictures.26.07.18.Jamie.Summer.Part.6.2160p-GRP.mp4',
    folderName: 'DemoPictures.26.07.18.Jamie.Summer.Part.6.2160p-GRP',
    releaseName: 'DemoPictures.26.07.18.Jamie.Summer.Part.6.2160p-GRP',
    title: 'Summer — Part 6',
    site: 'Demo Pictures',
    releaseDate: 'Jul 18, 2026',
    confidence: 'Probable',
    matchedBy: 'release name',
    fileRuntime: '20:38',
    prdbRuntime: '20:41 consensus',
    quality: '2160p · HEVC',
    size: '3.3 GiB',
    accent: '#665538',
  },
]

const variants = [
  { key: 'A', name: 'Side-by-side verifier' },
  { key: 'B', name: 'Focused review' },
  { key: 'C', name: 'Comparison ledger' },
] as const

export function ReviewQueueComparisonPrototype() {
  const [parameters] = useSearchParams()
  const requested = parameters.get('variant')?.toUpperCase()
  const variant = variants.some((candidate) => candidate.key === requested) ? requested as 'A' | 'B' | 'C' : 'A'

  return <PrototypeShell>
    {variant === 'A' && <VariantA />}
    {variant === 'B' && <VariantB />}
    {variant === 'C' && <VariantC />}
    <PrototypeSwitcher current={variant} />
  </PrototypeShell>
}

function PrototypeShell({ children }: { children: React.ReactNode }) {
  return <div className={styles.shell}>
    <aside className={styles.sidebar}>
      <strong className={styles.brand}><span>pf</span> prdb-fab</strong>
      <small>DISCOVER</small>
      <a>What’s new</a><a>Search</a><a>Sites</a><a>Actors</a><a>Wanted</a>
      <small>FETCH &amp; BUILD</small>
      <a>Downloads</a><a>Library</a><a className={styles.active}>Review queue <b>8</b></a><a>Operation log</a>
      <div className={styles.sidebarBottom}><a>Status</a><a>Settings</a></div>
    </aside>
    <main className={styles.main}>{children}</main>
  </div>
}

function PageHeading({ detail }: { detail: string }) {
  return <header className={styles.pageHeading}>
    <div><h1>Review Queue</h1><p>{detail}</p></div>
    <span>8 open</span>
  </header>
}

function VariantA() {
  return <>
    <PageHeading detail="Compare what arrived with the prdb Video before filing." />
    <div className={styles.queueTools}><button>☐ Select page</button><span>Showing Unidentified</span></div>
    <div className={styles.cardStack}>
      {entries.map((entry) => <article className={styles.comparisonCard} key={entry.id}>
        <div className={styles.cardTop}><span className={styles.reason}>Needs confirmation</span><span>{entry.confidence}, matched by {entry.matchedBy}</span></div>
        <div className={styles.directComparison}>
          <section>
            <Eyebrow>ARRIVING FILE</Eyebrow>
            <Filmstrip entry={entry} />
            <h2>{entry.fileName}</h2>
            <ComparisonFacts entry={entry} side="file" />
          </section>
          <div className={styles.matchBridge}><span>release name</span><b>→</b><span>{entry.confidence}</span></div>
          <section>
            <Eyebrow>PRDB VIDEO</Eyebrow>
            <ArtworkPanel entry={entry} />
            <h2>{entry.title}</h2>
            <ComparisonFacts entry={entry} side="prdb" />
          </section>
        </div>
        <div className={styles.nameComparison}>
          <div><small>FOLDER</small><code>{entry.folderName}</code></div>
          <div><small>PRDB TITLE</small><strong>{entry.title}</strong></div>
        </div>
        <DecisionBar entry={entry} />
      </article>)}
    </div>
  </>
}

function VariantB() {
  const [active, setActive] = useState(0)
  const entry = entries[active]
  return <>
    <PageHeading detail="Review one uncertain identification at a time." />
    <div className={styles.focusLayout}>
      <nav className={styles.caseRail} aria-label="Review cases">
        {entries.map((candidate, index) => <button className={index === active ? styles.caseActive : ''} key={candidate.id} onClick={() => setActive(index)}>
          <span>{index + 1}</span><div><strong>{candidate.title}</strong><small>{candidate.fileRuntime} · {candidate.quality.split(' · ')[0]}</small></div>
        </button>)}
      </nav>
      <article className={styles.focusCard}>
        <header><span className={styles.reason}>Unidentified</span><strong>{active + 1} of 8</strong></header>
        <section className={styles.focusVisuals}>
          <div><Eyebrow>LOCAL FILE — FIVE MOMENTS</Eyebrow><Filmstrip entry={entry} large /></div>
          <div><Eyebrow>PRDB ARTWORK</Eyebrow><ArtworkPanel entry={entry} /></div>
        </section>
        <section className={styles.identitySplit}>
          <div><Eyebrow>WHAT ARRIVED</Eyebrow><h2>{entry.fileName}</h2><code>{entry.folderName}</code><ComparisonFacts entry={entry} side="file" /></div>
          <div><Eyebrow>WHAT PRDB PROPOSES</Eyebrow><h2>{entry.title}</h2><p>{entry.site} · {entry.releaseDate}</p><ComparisonFacts entry={entry} side="prdb" /></div>
        </section>
        <div className={styles.confidenceCallout}><strong>{entry.confidence}</strong><span>The file matched this Video by release name, not by hash.</span></div>
        <DecisionBar entry={entry} onDone={() => setActive((active + 1) % entries.length)} />
      </article>
    </div>
  </>
}

function VariantC() {
  const [expanded, setExpanded] = useState(entries[0].id)
  return <>
    <PageHeading detail="Scan every proposed identification, then open only the uncertain ones." />
    <div className={styles.ledgerTools}><button>☐ Select all</button><span>File evidence</span><span>Identification</span><span>prdb Video</span></div>
    <div className={styles.ledger}>
      {entries.map((entry) => <article className={styles.ledgerRow} key={entry.id}>
        <button className={styles.expandButton} onClick={() => setExpanded(expanded === entry.id ? '' : entry.id)} aria-expanded={expanded === entry.id}>{expanded === entry.id ? '−' : '+'}</button>
        <div className={styles.ledgerFile}><strong>{entry.fileName}</strong><code>{entry.folderName}</code><small>{entry.fileRuntime} · {entry.quality} · {entry.size}</small></div>
        <div className={styles.ledgerEvidence}><strong>{entry.confidence}</strong><span>by {entry.matchedBy}</span><small>No hash match</small></div>
        <div className={styles.ledgerVideo}><ArtworkPanel entry={entry} compact /><div><strong>{entry.title}</strong><span>{entry.site}</span><small>{entry.releaseDate} · {entry.prdbRuntime}</small></div></div>
        <button className={styles.inlineConfirm}>File as this Video</button>
        {expanded === entry.id && <div className={styles.ledgerExpanded}>
          <div><Eyebrow>LOCAL FILE CONTACT SHEET</Eyebrow><Filmstrip entry={entry} large /></div>
          <div className={styles.expandedNames}><p><small>RELEASE NAME</small><code>{entry.releaseName}</code></p><p><small>PRDB VIDEO</small><strong>{entry.title}</strong></p></div>
        </div>}
      </article>)}
    </div>
  </>
}

function Filmstrip({ entry, large = false }: { entry: PrototypeEntry; large?: boolean }) {
  const times = ['02:45', '08:16', '13:47', '19:18', '24:49']
  return <div className={`${styles.filmstrip} ${large ? styles.filmstripLarge : ''}`}>
    {times.map((time, index) => <div key={time} style={{ '--frame': entry.accent, '--shift': `${index * 11}%` } as React.CSSProperties}>
      <span className={styles.figure} /><small>{time}</small>
    </div>)}
  </div>
}

function ArtworkPanel({ entry, compact = false }: { entry: PrototypeEntry; compact?: boolean }) {
  return <div className={`${styles.artworkPanel} ${compact ? styles.artworkCompact : ''}`} style={{ '--artwork': entry.accent } as React.CSSProperties}>
    <span className={styles.artworkFigure} /><span>{compact ? 'prdb' : entry.site}</span>
  </div>
}

function ComparisonFacts({ entry, side }: { entry: PrototypeEntry; side: 'file' | 'prdb' }) {
  return <dl className={styles.facts}>
    <div><dt>Runtime</dt><dd>{side === 'file' ? entry.fileRuntime : entry.prdbRuntime}</dd></div>
    <div><dt>{side === 'file' ? 'Encoding' : 'Released'}</dt><dd>{side === 'file' ? entry.quality : entry.releaseDate}</dd></div>
    <div><dt>{side === 'file' ? 'Size' : 'Site'}</dt><dd>{side === 'file' ? entry.size : entry.site}</dd></div>
  </dl>
}

function DecisionBar({ entry, onDone }: { entry: PrototypeEntry; onDone?: () => void }) {
  const [decision, setDecision] = useState('')
  return <div className={styles.decisionBar}>
    <button onClick={() => setDecision('Search would open here')}>Search another Video</button>
    <button onClick={() => setDecision('Entry would be dismissed')}>Dismiss</button>
    <button className={styles.primary} onClick={() => { setDecision(`Would file as “${entry.title}”`); onDone?.() }}>✓ File as this Video</button>
    <output>{decision}</output>
  </div>
}

function Eyebrow({ children }: { children: React.ReactNode }) {
  return <small className={styles.eyebrow}>{children}</small>
}

function PrototypeSwitcher({ current }: { current: 'A' | 'B' | 'C' }) {
  const [, setParameters] = useSearchParams()
  const index = variants.findIndex((variant) => variant.key === current)
  const select = (offset: number) => {
    const next = variants[(index + offset + variants.length) % variants.length]
    setParameters({ variant: next.key }, { replace: true })
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target
      if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || (target instanceof HTMLElement && target.isContentEditable)) return
      if (event.key === 'ArrowLeft') select(-1)
      if (event.key === 'ArrowRight') select(1)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  })

  if (!import.meta.env.DEV) return null
  return <nav className={styles.switcher} aria-label="Prototype variants">
    <button aria-label="Previous variant" onClick={() => select(-1)}>←</button>
    <strong>{current} — {variants[index].name}</strong>
    <button aria-label="Next variant" onClick={() => select(1)}>→</button>
  </nav>
}
