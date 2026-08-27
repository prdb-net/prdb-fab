import { Link } from 'react-router'

import styles from './Settings.module.css'

/**
 * ADR 0020's seven groups, of which two hold anything so far. The other five
 * are named rather than hidden: each of them configures a feature that is not
 * built, and a page quietly five links shorter than the decision is how a
 * surface drifts from what was argued.
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
        <li className={styles.pending}>
          Identification
          <br />
          <span className={styles.detail}>
            The two confidence gates. Arrives with identification.
          </span>
        </li>
        <li className={styles.pending}>
          Library
          <br />
          <span className={styles.detail}>
            The library root and what filing leaves behind. Arrives with filing.
          </span>
        </li>
        <li className={styles.pending}>
          Automation
          <br />
          <span className={styles.detail}>
            The rules, the cap on unfinished downloads, the retry budget.
            Arrives with automation.
          </span>
        </li>
        <li className={styles.pending}>
          Reporting
          <br />
          <span className={styles.detail}>
            What is sent back to prdb. Arrives with reporting.
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
