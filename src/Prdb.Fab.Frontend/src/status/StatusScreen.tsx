import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'

import { readStatus, runRoutineNow, type StatusState } from '../api/client.ts'
import { PageLoading } from '../shell/LoadingScreen.tsx'
import styles from './StatusScreen.module.css'

export function StatusScreen() {
  const queryClient = useQueryClient()
  const status = useQuery({
    queryKey: ['status'],
    queryFn: readStatus,
    refetchInterval: 5000,
  })
  const now = useNow()
  const runNow = useMutation({
    mutationFn: ({ name, target }: { name: string; target: string | null }) => runRoutineNow(name, target),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['status'] }),
  })

  if (status.isPending) return <PageLoading label="Reading Status" />
  if (status.isError) return <main className={styles.screen}><h1>Status</h1><p>Status could not be read.</p></main>

  const answer = status.data
  return (
    <main className={styles.screen}>
      <header className={styles.heading}>
        <div>
          <p className={styles.eyebrow}>The unattended loop</p>
          <h1>Status</h1>
          <p>Local facts only. This page refreshes every five seconds and makes no remote request.</p>
        </div>
        <div className={`${styles.gapCount} ${Number(answer.gapCount) === 0 ? styles.healthy : ''}`}>
          <strong>{answer.gapCount}</strong>
          <span>{Number(answer.gapCount) === 1 ? 'Gap' : 'Gaps'}</span>
        </div>
      </header>

      <section className={styles.useful}>
        <span>Last useful act</span>
        {answer.lastUsefulAct?.at
          ? <strong>{answer.lastUsefulAct.act} {ago(answer.lastUsefulAct.at, now)}</strong>
          : <strong>No useful act has been recorded yet.</strong>}
      </section>

      <nav className={styles.related} aria-label="Related work surfaces">
        {answer.related.map((link) => <Link key={link.route} to={link.route}>{link.label}</Link>)}
      </nav>

      <div className={styles.loop}>
        {answer.stages.map((stage, index) => (
          <Stage
            key={stage.id}
            stage={stage}
            number={index + 1}
            now={now}
            running={runNow.isPending ? runNow.variables : null}
            onRun={(name, target) => runNow.mutate({ name, target })}
          />
        ))}
      </div>
    </main>
  )
}

function Stage({
  stage,
  number,
  now,
  running,
  onRun,
}: {
  stage: StatusState['stages'][number]
  number: number
  now: number
  running: { name: string; target: string | null } | null
  onRun: (name: string, target: string | null) => void
}) {
  return (
    <section className={styles.stage}>
      <header className={styles.stageHeading}>
        <span>{number}</span><h2>{stage.title}</h2>
      </header>

      {stage.gaps.map((condition, index) => (
        <Condition condition={condition} key={`${condition.title}-${index}`} />
      ))}
      {stage.brakes.map((condition, index) => (
        <Condition condition={condition} key={`${condition.title}-${index}`} />
      ))}

      {stage.facts.length > 0 && <dl className={styles.facts}>
        {stage.facts.map((fact) => <div key={`${fact.label}-${fact.value}`}>
          <dt>{fact.route ? <Link to={fact.route}>{fact.label}</Link> : fact.label}</dt>
          <dd>{fact.value}</dd>
        </div>)}
      </dl>}

      <div className={styles.routines}>
        {stage.routines.map((routine) => {
          const isRunning = running?.name === routine.name && running.target === routine.target
          return <article className={styles.routine} key={`${routine.name}-${routine.target ?? ''}`}>
            <div className={styles.routineTop}>
              <strong>{routine.label}</strong>
              <button
                type="button"
                disabled={isRunning || routine.runNowPending}
                onClick={() => onRun(routine.name, routine.target)}
              >
                {isRunning ? 'Requesting…' : routine.runNowPending ? 'Scheduled' : 'Run now'}
              </button>
            </div>
            <dl className={styles.routineFacts}>
              {routine.workSetSize !== null && <Fact label="Work set" value={String(routine.workSetSize)} />}
              <Fact label="Last completed item" value={when(routine.lastCompletedAt, now)} />
              <Fact label="Last success" value={when(routine.lastSuccessAt, now)} />
              {routine.resultsSeen !== null && <Fact label="Results seen" value={String(routine.resultsSeen)} />}
              {routine.rowsAdded !== null && <Fact label="Rows added" value={String(routine.rowsAdded)} />}
            </dl>
            {routine.backingOff && <p className={styles.backoff}>Backing off after {routine.consecutiveFailures} failed turn(s). This is not a Gap yet.</p>}
            {routine.lastRunNowOutcome && <p className={styles.runVerdict}>
              <strong>{routine.lastRunNowOutcome}:</strong> {routine.lastRunNowDetail}
            </p>}
          </article>
        })}
      </div>
    </section>
  )
}

function Condition({ condition }: { condition: StatusState['stages'][number]['gaps'][number] }) {
  return <article className={`${styles.condition} ${condition.kind === 'Gap' ? styles.gap : styles.brake} ${condition.cleared ? styles.cleared : ''}`}>
    <span>{condition.cleared ? 'Cleared Gap' : condition.kind}</span>
    <div>
      <strong>{condition.title}</strong>
      <p>{condition.detail}</p>
      {condition.route && <Link to={condition.route}>{condition.kind === 'Gap' ? 'Repair' : 'Review this choice'}</Link>}
    </div>
  </article>
}

function Fact({ label, value }: { label: string; value: string }) {
  return <div><dt>{label}</dt><dd>{value}</dd></div>
}

function useNow(): number {
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [])
  return now
}

function when(value: string | null, now: number): string {
  return value ? ago(value, now) : 'Never'
}

function ago(value: string, now: number): string {
  const seconds = Math.max(0, Math.floor((now - new Date(value).getTime()) / 1000))
  if (seconds < 60) return `${seconds}s ago`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 48) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}
