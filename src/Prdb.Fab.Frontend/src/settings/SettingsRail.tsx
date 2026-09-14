import { useQuery } from '@tanstack/react-query'
import { NavLink, useLocation } from 'react-router'

import {
  listIndexers,
  readAutomationSettings,
  readConnections,
  readDownloadSettings,
  readIdentificationSettings,
  readLibrarySettings,
  readReportingSettings,
  readStatus,
  type ReportingSettingsState,
  type StatusState,
} from '../api/client.ts'
import { connectionsKey, indexersKey } from '../onboarding/state.ts'
import { SettingsIcon, type SettingsIconName } from './SettingsIcon.tsx'
import styles from './Settings.module.css'

/**
 * ADR 0020's eight groups, always in reach, each saying what it currently holds.
 *
 * The surface used to be Sidebar → an index list → the mask: three clicks, and
 * the middle one a page whose whole content was links. Once inside a mask there
 * was no way to a sibling at all, and the deep routes ADR 0020 insists on — one
 * per Indexer, one per Automation Rule — appeared in no navigation whatever,
 * although Status links straight into them.
 *
 * It composes the reads the masks already make rather than gaining an overview
 * endpoint of its own. They are small, React Query caches them, and the mask
 * somebody then opens finds its data already there. One endpoint is the fallback
 * if this ever turns noisy; it is not worth a contract today.
 */
export function SettingsRail() {
  const connections = useQuery({ queryKey: connectionsKey, queryFn: readConnections })
  const indexers = useQuery({ queryKey: indexersKey, queryFn: listIndexers })
  const automation = useQuery({ queryKey: ['automation-settings'], queryFn: readAutomationSettings })
  const identification = useQuery({
    queryKey: ['identification-settings'],
    queryFn: readIdentificationSettings,
  })
  const downloads = useQuery({ queryKey: ['download-settings'], queryFn: readDownloadSettings })
  const library = useQuery({ queryKey: ['library-settings'], queryFn: readLibrarySettings })
  const reporting = useQuery({ queryKey: ['reporting-settings'], queryFn: readReportingSettings })
  const status = useQuery({ queryKey: ['status'], queryFn: readStatus })

  const pointedAt = conditionRoutes(status.data)
  const held = connections.data

  return (
    <nav className={styles.rail} aria-label="Settings">
      <Group to="/settings/connections" icon="connections" label="Connections" marks={pointedAt}>
        {held
          ? [
              held.prdbConfigured ? 'prdb key stored' : 'no prdb key',
              held.sabnzbdConfigured
                ? 'SABnzbd configured'
                : held.sabnzbdSkipped
                  ? 'SABnzbd skipped'
                  : 'no SABnzbd',
            ].join(' · ')
          : null}
      </Group>

      {/* ADR 0020 gives every Indexer its own address because Status points at
          the row rather than at the list. Nested here for the same reason. */}
      {(indexers.data ?? []).map((indexer) => (
        <Group
          key={indexer.id}
          to={`/settings/connections/indexers/${indexer.id}`}
          label={indexer.name}
          marks={pointedAt}
          nested
        >
          {indexer.enabled ? 'Enabled' : 'Disabled'}
        </Group>
      ))}

      <Group to="/settings/automation" icon="automation" label="Automation" marks={pointedAt}>
        {automation.data
          ? automation.data.rules.length === 0
            ? 'No rules, so nothing is automatic'
            : `${automation.data.rules.length} rule(s), ${automation.data.rules.filter((rule) => rule.enabled).length} enabled`
          : null}
      </Group>

      {(automation.data?.rules ?? []).map((rule) => (
        <Group
          key={rule.id}
          to={`/settings/automation/rules/${rule.id}`}
          label={rule.name}
          marks={pointedAt}
          nested
        >
          {rule.enabled ? 'Enabled' : 'Disabled'}
        </Group>
      ))}

      <Group to="/settings/identification" icon="identification" label="Identification" marks={pointedAt}>
        {identification.data
          ? `${gateLabel(identification.data.beforeDownload)} before, ${gateLabel(identification.data.afterDownload)} after`
          : null}
      </Group>

      <Group to="/settings/downloads" icon="download" label="Downloads" marks={pointedAt}>
        {downloads.data ? `Up to ${downloads.data.preferredQuality.slice(1)}p` : null}
      </Group>

      <Group to="/settings/library" icon="library" label="Library" marks={pointedAt}>
        {library.data
          ? library.data.deleteLeftovers
            ? 'Leftovers removed after filing'
            : 'Leftovers kept'
          : null}
      </Group>

      <Group to="/settings/reporting" icon="reporting" label="Reporting" marks={pointedAt}>
        {reporting.data ? channels(reporting.data) : null}
      </Group>

      <Group to="/settings/backup" icon="backup" label="Backup" marks={pointedAt}>
        One readable file, credentials included
      </Group>

      {/* Nothing to say, and it says nothing. */}
      <Group to="/settings/account" icon="account" label="Account" marks={pointedAt} />
    </nav>
  )
}

function Group({
  to,
  icon,
  label,
  marks,
  nested = false,
  children,
}: {
  to: string
  icon?: SettingsIconName
  label: string
  /** The settings routes Status is currently pointing a Gap or a Brake at. */
  marks: ReadonlySet<string>
  nested?: boolean
  children?: React.ReactNode
}) {
  const location = useLocation()
  const marked = marks.has(to)

  return (
    <NavLink
      to={{ pathname: to, search: from(location.pathname, location.search) }}
      className={({ isActive }) =>
        [styles.railRow, nested ? styles.railNested : '', isActive ? styles.railHere : '']
          .filter(Boolean)
          .join(' ')
      }
      end={to === '/settings/connections' || to === '/settings/automation'}
    >
      <span className={styles.railIcon}>{icon ? <SettingsIcon name={icon} /> : null}</span>
      <span className={styles.railLabel}>
        {label}
        {marked && (
          <span className={styles.railMark} title="Status is pointing at this">
            !
          </span>
        )}
      </span>
      {children && <span className={styles.railHolds}>{children}</span>}
    </NavLink>
  )
}

/**
 * A move within the surface keeps whatever `from` the visit arrived with, so
 * following a Brake into an Indexer and then stepping to a sibling still leaves
 * a way back to Status.
 */
function from(pathname: string, search: string): string {
  if (!pathname.startsWith('/settings')) return ''

  const carried = new URLSearchParams(search).get('from')

  return carried ? `?from=${encodeURIComponent(carried)}` : ''
}

/**
 * The settings routes Status has a condition on, without their own query — the
 * rail matches the path, and the `from` the route carries is not part of what
 * it identifies.
 */
function conditionRoutes(status: StatusState | undefined): ReadonlySet<string> {
  const routes = new Set<string>()

  for (const stage of status?.stages ?? []) {
    for (const condition of [...stage.gaps, ...stage.brakes]) {
      if (condition.cleared || !condition.route) continue

      const path = condition.route.split('?')[0]

      if (path.startsWith('/settings')) routes.add(path)
    }
  }

  return routes
}

function gateLabel(gate: string): string {
  return gate === 'ThroughProbable'
    ? 'Probable'
    : gate === 'ExactAndStrong'
      ? 'Strong'
      : 'Exact'
}

/**
 * Three switches in a line under a heading, which is one more than a sentence
 * naming each of them can carry. So it counts — except for the one state worth
 * a word of its own, which is ADR 0064's gate: publishing is on and has never
 * been answered, so it is not publishing.
 */
function channels(state: ReportingSettingsState): string {
  if (state.publishGeneratedPreviews && !state.previewPublicationExplained) {
    return 'Publishing waits to be answered'
  }

  const on = [
    state.reportFulfilments,
    state.reportConfirmedAssignments,
    state.publishGeneratedPreviews,
  ].filter(Boolean).length

  if (on === 3) return 'All three channels on'
  if (on === 0) return 'All three channels off'

  return `${on} of three channels on`
}
