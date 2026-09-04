import { useEffect, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'

import type { LibraryPage } from '../api/client.ts'
import { Artwork } from '../catalogue/Grid.tsx'
import styles from './LibraryPrototype.module.css'

// PROTOTYPE — throwaway. Three variants of the existing Library page,
// switchable via ?variant=A|B|C on /library. Only the rendering changes; the
// route, live data, filters and paging remain the real ones.

export type LibraryPrototypeVariant = 'A' | 'B' | 'C'

type LibraryCard = LibraryPage['entries'][number]
type FilterName = 'site' | 'actor' | 'quality'

type PrototypeProps = {
  variant: LibraryPrototypeVariant
  data: LibraryPage | undefined
  isError: boolean
  searchInput: string
  site: string
  actor: string
  quality: string
  page: number
  onSearchChange: (value: string) => void
  onFilterChange: (name: FilterName, value: string) => void
  onClearFilters: () => void
  onPageChange: (page: number) => void
}

const variants: readonly { key: LibraryPrototypeVariant; name: string }[] = [
  { key: 'A', name: 'Catalogue sibling' },
  { key: 'B', name: 'Filter rail' },
  { key: 'C', name: 'Compact collection' },
]

export function LibraryPrototype(props: PrototypeProps) {
  return (
    <>
      {props.variant === 'A' && <VariantA {...props} />}
      {props.variant === 'B' && <VariantB {...props} />}
      {props.variant === 'C' && <VariantC {...props} />}
      <PrototypeSwitcher current={props.variant} />
    </>
  )
}

function VariantA(props: PrototypeProps) {
  return (
    <main className={`${styles.screen} ${styles.catalogueScreen}`}>
      <PageHeading data={props.data} />
      <p className={styles.lede}>
        Browse the Videos held by this installation. Open an entry to see its files,
        qualities and where they are filed.
      </p>
      <HorizontalFilters {...props} />
      <ResultState {...props}>
        <ul className={styles.catalogueGrid}>
          {props.data?.entries.map((entry) => <CatalogueCard entry={entry} key={entry.id} />)}
        </ul>
      </ResultState>
      <Pager {...props} newer="Previous" older="Next" />
    </main>
  )
}

function VariantB(props: PrototypeProps) {
  return (
    <main className={styles.screen}>
      <PageHeading data={props.data} />
      <p className={styles.lede}>
        Everything currently held, with the filters kept beside the collection they shape.
      </p>
      <div className={styles.railLayout}>
        <aside aria-label="Library filters" className={styles.filterRail}>
          <label className={styles.searchLabel}>
            Search by title
            <input
              autoComplete="off"
              className={styles.field}
              name="search"
              onChange={(event) => props.onSearchChange(event.target.value)}
              type="search"
              value={props.searchInput}
            />
          </label>
          <RailSelect
            label="Site"
            value={props.site}
            all="All Sites"
            items={props.data?.filters.sites ?? []}
            onChange={(value) => props.onFilterChange('site', value)}
          />
          <RailSelect
            label="Actor"
            value={props.actor}
            all="All Actors"
            items={props.data?.filters.actors ?? []}
            onChange={(value) => props.onFilterChange('actor', value)}
          />
          <RailSelect
            label="Quality"
            value={props.quality}
            all="All Qualities"
            items={(props.data?.filters.qualities ?? []).map((item) => ({ id: item, name: item }))}
            onChange={(value) => props.onFilterChange('quality', value)}
          />
          {hasFilters(props) && (
            <button
              className={styles.clearButton}
              onClick={props.onClearFilters}
              type="button"
            >
              Clear filters
            </button>
          )}
        </aside>
        <section aria-label="Library entries" className={styles.railResults}>
          <ResultState {...props}>
            <ul className={styles.railGrid}>
              {props.data?.entries.map((entry) => <CatalogueCard entry={entry} key={entry.id} />)}
            </ul>
          </ResultState>
          <Pager {...props} newer="Previous" older="Next" />
        </section>
      </div>
    </main>
  )
}

function VariantC(props: PrototypeProps) {
  return (
    <main className={`${styles.screen} ${styles.compactScreen}`}>
      <div className={styles.compactHead}>
        <PageHeading data={props.data} />
        <HorizontalFilters {...props} />
      </div>
      <ResultState {...props}>
        <ul className={styles.compactGrid}>
          {props.data?.entries.map((entry) => <CompactCard entry={entry} key={entry.id} />)}
        </ul>
      </ResultState>
      <Pager {...props} newer="Previous" older="Next" />
    </main>
  )
}

function PageHeading({ data }: { data: LibraryPage | undefined }) {
  return (
    <div className={styles.heading}>
      <h1>Library</h1>
      {Number(data?.total ?? 0) > 0 && (
        <span className={styles.count}>{Number(data?.total ?? 0)} videos</span>
      )}
    </div>
  )
}

function HorizontalFilters(props: PrototypeProps) {
  return (
    <div aria-label="Library filters" className={styles.filters} role="group">
      <label className={styles.searchLabel}>
        Title
        <input
          autoComplete="off"
          className={`${styles.field} ${styles.searchField}`}
          name="search"
          onChange={(event) => props.onSearchChange(event.target.value)}
          type="search"
          value={props.searchInput}
        />
      </label>
      <FilterSelect
        label="Site"
        value={props.site}
        all="All Sites"
        items={props.data?.filters.sites ?? []}
        onChange={(value) => props.onFilterChange('site', value)}
      />
      <FilterSelect
        label="Actor"
        value={props.actor}
        all="All Actors"
        items={props.data?.filters.actors ?? []}
        onChange={(value) => props.onFilterChange('actor', value)}
      />
      <FilterSelect
        label="Quality"
        value={props.quality}
        all="All Qualities"
        items={(props.data?.filters.qualities ?? []).map((item) => ({ id: item, name: item }))}
        onChange={(value) => props.onFilterChange('quality', value)}
      />
    </div>
  )
}

function FilterSelect({
  label,
  value,
  all,
  items,
  onChange,
}: {
  label: string
  value: string
  all: string
  items: readonly { id: string; name: string }[]
  onChange: (value: string) => void
}) {
  return (
    <label>
      {label}
      <select className={styles.field} onChange={(event) => onChange(event.target.value)} value={value}>
        <option value="">{all}</option>
        {items.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
      </select>
    </label>
  )
}

function RailSelect(props: Parameters<typeof FilterSelect>[0]) {
  return (
    <div className={styles.railFilter}>
      <FilterSelect {...props} />
    </div>
  )
}

function CatalogueCard({ entry }: { entry: LibraryCard }) {
  const qualities = compactQualities(entry.qualities)
  return (
    <li className={styles.catalogueCard}>
      <Link className={styles.artworkLink} to={`/library/${entry.id}`}>
        <Artwork
          videoId={entry.artworkId}
          title={entry.title}
          frameClassName={styles.artwork}
          imageClassName={styles.artworkImage}
          absentClassName={styles.artworkAbsent}
        />
        {qualities.length > 0 && (
          <span className={styles.held}>In Library · {qualities.join(', ')}</span>
        )}
      </Link>
      <h2 className={styles.cardTitle}><Link to={`/library/${entry.id}`}>{entry.title}</Link></h2>
      <span className={styles.detail}>{[entry.site, entry.releaseDate].filter(Boolean).join(' · ')}</span>
      <span className={styles.status}>{describeFiles(entry)}</span>
      <Link className={styles.entryLink} to={`/library/${entry.id}`}>View Library entry</Link>
    </li>
  )
}

function CompactCard({ entry }: { entry: LibraryCard }) {
  return (
    <li className={styles.compactCard}>
      <Link className={styles.compactArtworkLink} to={`/library/${entry.id}`}>
        <Artwork
          videoId={entry.artworkId}
          title={entry.title}
          frameClassName={styles.compactArtwork}
          imageClassName={styles.artworkImage}
          absentClassName={styles.artworkAbsent}
        />
      </Link>
      <div className={styles.compactBody}>
        <h2 className={styles.cardTitle}><Link to={`/library/${entry.id}`}>{entry.title}</Link></h2>
        <span className={styles.detail}>{[entry.site, entry.releaseDate].filter(Boolean).join(' · ')}</span>
        <div className={styles.qualityList}>
          {entry.qualities.map((item) => <span className={styles.quality} key={item}>{item}</span>)}
        </div>
        <span className={styles.status}>{runtime(entry.runtimeSeconds)}</span>
      </div>
      <Link aria-label={`View ${entry.title}`} className={styles.compactArrow} to={`/library/${entry.id}`}>&rarr;</Link>
    </li>
  )
}

function ResultState(props: PrototypeProps & { children: ReactNode }) {
  if (props.isError) return <p className={styles.error}>The Library could not be read.</p>
  if (props.data?.entries.length === 0) {
    return <p className={styles.empty}>No held Library Entries match these filters.</p>
  }
  return props.children
}

function Pager(props: PrototypeProps & { newer: string; older: string }) {
  const total = Number(props.data?.total ?? 0)
  const pageSize = Number(props.data?.pageSize ?? 24)
  const pages = Math.max(1, Math.ceil(total / pageSize))
  if (pages <= 1) return null
  return (
    <nav aria-label="Library pages" className={styles.pager}>
      <button disabled={props.page <= 1} onClick={() => props.onPageChange(props.page - 1)} type="button">{props.newer}</button>
      <span>Page {props.page} of {pages}</span>
      <button disabled={props.page >= pages} onClick={() => props.onPageChange(props.page + 1)} type="button">{props.older}</button>
    </nav>
  )
}

function PrototypeSwitcher({ current }: { current: LibraryPrototypeVariant }) {
  const [parameters, setParameters] = useSearchParams()
  const index = variants.findIndex((item) => item.key === current)
  const go = (offset: number) => {
    const nextVariant = variants[(index + offset + variants.length) % variants.length]
    const next = new URLSearchParams(parameters)
    next.set('variant', nextVariant.key)
    setParameters(next, { replace: true })
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return
      const target = event.target
      if (target instanceof HTMLElement && (target.matches('input, textarea, select, [contenteditable]') || target.isContentEditable)) return
      event.preventDefault()
      go(event.key === 'ArrowLeft' ? -1 : 1)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  })

  return (
    <div aria-label="Library prototype variants" className={styles.prototypeSwitcher}>
      <button aria-label="Previous variant" onClick={() => go(-1)} type="button">&larr;</button>
      <span>{current} &mdash; {variants[index].name}</span>
      <button aria-label="Next variant" onClick={() => go(1)} type="button">&rarr;</button>
    </div>
  )
}

function compactQualities(qualities: readonly string[]): string[] {
  return qualities.length <= 2 ? [...qualities] : [...qualities.slice(0, 2), `+${qualities.length - 2}`]
}

function describeFiles(entry: LibraryCard): string {
  const qualities = compactQualities(entry.qualities)
  return [qualities.length === 1 ? `${qualities[0]} copy` : `${entry.qualities.length} quality copies`, runtime(entry.runtimeSeconds)]
    .filter(Boolean)
    .join(' · ')
}

function runtime(seconds: LibraryCard['runtimeSeconds']): string {
  if (seconds == null) return ''
  const minutes = Math.round(Number(seconds) / 60)
  return minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes} min`
}

function hasFilters(props: PrototypeProps): boolean {
  return Boolean(props.searchInput || props.site || props.actor || props.quality)
}
