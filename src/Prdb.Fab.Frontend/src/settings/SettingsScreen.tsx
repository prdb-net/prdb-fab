import { Link } from 'react-router'

import styles from './Settings.module.css'

/**
 * The installation's setting groups. Groups whose feature has not arrived yet remain
 * named rather than hidden, so the surface cannot quietly drift from what was
 * argued.
 */
export function SettingsScreen() {
  return (
    <main className={styles.screen}>
      <h1>Settings</h1>
      <p className={styles.lede}>
        Everything here takes effect from the next time it is used. Nothing needs
        a restart.
      </p>

      <ul className={styles.groups}>
        <li>
          <Link to="/settings/connections">Connections</Link>
          <br />
          <span className={styles.detail}>
            prdb, SABnzbd and the indexers &mdash; each checked against the
            service it names before anything is stored.
          </span>
        </li>
        <li>
          <Link to="/settings/account">Account</Link>
          <br />
          <span className={styles.detail}>
            The password, and signing out.
          </span>
        </li>
        <li>
          <Link to="/settings/identification">Identification</Link>
          <br />
          <span className={styles.detail}>
            Which named confidence lets an identified arrival proceed to filing.
          </span>
        </li>
        <li>
          <Link to="/settings/library">Library</Link>
          <br />
          <span className={styles.detail}>
            The library root and the fixed leftover types filing may remove.
          </span>
        </li>
        <li>
          <Link to="/settings/downloads">Downloads</Link>
          <br />
          <span className={styles.detail}>
            The preferred highest Quality used by Catalogue-card Download buttons.
          </span>
        </li>
        <li>
          <Link to="/settings/automation">Automation</Link>
          <br />
          <span className={styles.detail}>
            Permission rules and the cap on unfinished automatic Downloads.
          </span>
        </li>
        <li>
          <Link to="/settings/reporting">Reporting</Link>
          <br />
          <span className={styles.detail}>
            Two independent opt-in channels for what may be sent back to prdb.
          </span>
        </li>
        <li className={styles.pending}>
          Backup
          <br />
          <span className={styles.detail}>
            Exporting this installation. Arrives with the backup.
          </span>
        </li>
      </ul>
    </main>
  )
}
