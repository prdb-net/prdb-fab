import styles from './LoadingScreen.module.css'

export function AppLoading() {
  return (
    <main className="initialLoading" aria-busy="true">
      <div className="initialLoadingBrand" aria-hidden="true">
        <span className="initialLoadingMark">pf</span>
        <strong>prdb-fab</strong>
      </div>
      <p className="initialLoadingStatus" role="status">
        Opening the workspace&hellip;
      </p>
    </main>
  )
}

export function PageLoading({ label }: { label: string }) {
  return (
    <main className={styles.page} aria-busy="true">
      <div className={styles.pageHeading} aria-hidden="true" />
      <div className={styles.pageLine} aria-hidden="true" />
      <div className={styles.pagePanel} aria-hidden="true" />
      <p className={styles.srOnly} role="status">
        {label}
      </p>
    </main>
  )
}
