import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'

import {
  askForLibraryPreviews,
  cancelLibraryPreviews,
  leavePublication,
  pauseLibraryPreviews,
  readPublications,
  resumeLibraryPreviews,
  sendPublicationAgain,
  type PublicationProgressState,
  type PublicationRequest,
  type PublicationTally,
  type UncertainPublication,
} from '../api/client.ts'
import { ConfirmationDialog } from '../ui/ConfirmationDialog.tsx'
import { formStyles } from '../ui/Form.tsx'
import { Loaded } from '../ui/Loaded.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { SettingsPage } from './SettingsPage.tsx'
import settingsStyles from './Settings.module.css'
import styles from './PublicationsScreen.module.css'

const key = ['publications']

/**
 * ADR 0064's publishing side, made operable without reading a log.
 *
 * It answers three questions and nothing else. Where does every generated
 * preview stand. Is the existing Library being published, and may it be
 * started, held or given up. And what is to be done about an upload whose
 * outcome nobody can establish — which is the one state in this tool that
 * waits for a person, because no amount of retrying settles it.
 */
export function PublicationsScreen() {
  const publications = useQuery({ queryKey: key, queryFn: readPublications })

  return (
    <SettingsPage
      title="Published previews"
      lede="What this installation has generated for prdb, what it has submitted, and what is still owed. Submitted is not published: a preview prdb accepted is waiting for a moderator until a read endpoint shows it."
      back="/settings/reporting"
      backLabel="Reporting settings"
    >
      <Loaded query={publications} of="the publication progress">
        {(held) => <Publications held={held} />}
      </Loaded>
    </SettingsPage>
  )
}

function Publications({ held }: { held: PublicationProgressState }) {
  const queries = useQueryClient()
  const settle = (answer: PublicationProgressState) => {
    queries.setQueryData(key, answer)
    void queries.invalidateQueries({ queryKey: ['status'] })
  }

  if (!held.connected) {
    return (
      <Verdict tone="warning">
        There is no prdb account configured, so nothing is generated and nothing
        is published. <Link to="/settings/connections/prdb">The prdb connection</Link> is
        where that is set.
      </Verdict>
    )
  }

  return (
    <>
      {!held.publishing && (
        <Verdict tone="warning">
          Publishing generated previews is switched off. Nothing new is generated
          or sent, and nothing already made is given up.{' '}
          <Link to="/settings/reporting">Reporting settings</Link> is where the
          switch is.
        </Verdict>
      )}

      {held.publishing && !held.explained && (
        <Verdict tone="warning">
          Publishing is switched on and what it sends has not been read yet.
          Nothing is generated or published until{' '}
          <Link to="/settings/reporting">the Reporting settings</Link> have been
          saved once.
        </Verdict>
      )}

      <Tally tally={held.tally} />

      <h2 className={settingsStyles.heading}>The existing Library</h2>

      <LibraryRequest held={held} onSettled={settle} />

      <h2 className={settingsStyles.heading}>Uploads nobody can account for</h2>

      <Uncertain held={held} onSettled={settle} />
    </>
  )
}

/**
 * ADR 0064's states, counted.
 *
 * Submitted and shown are two rows rather than one, which is the distinction
 * the ADR spends a section on: a `201` is a moderation process starting, and
 * only a read endpoint returning the row is evidence anybody can see it.
 */
function Tally({ tally }: { tally: PublicationTally }) {
  const uncertain = Number(tally.uncertain)
  const counts: [string, number, string | undefined, boolean][] = [
    ['Waiting to be made', Number(tally.waiting), 'A file is owed a sheet.', false],
    ['Waiting to be sent', Number(tally.ready), 'Made, validated, queued.', false],
    ['Submitted', Number(tally.submitted), 'Accepted by prdb; a moderator decides.', false],
    ['Publicly shown', Number(tally.shown), 'prdb is showing it to everyone.', false],
    ['Outcome unknown', uncertain, 'Sent, unanswered, never resent.', uncertain > 0],
    ['Declined', Number(tally.refused), 'prdb considered it and said no.', false],
    ['Given up', Number(tally.dropped), 'Nothing left this machine.', false],
  ]

  return (
    <dl className={styles.tally}>
      {counts.map(([label, count, hint, asking]) => (
        <div key={label} className={asking ? styles.asking : undefined}>
          <dt>{label}</dt>
          <dd>{count}</dd>
          {hint && <dd className={settingsStyles.detail}>{hint}</dd>}
        </div>
      ))}
    </dl>
  )
}

/**
 * The explicit request, which is the only way the existing Library is ever
 * published.
 *
 * The count comes before the button and the explanation comes with it, because
 * ADR 0064 requires both be in front of somebody before anything is taken up —
 * and nothing else in this tool starts one: not upgrading, not enabling the
 * switch, and not restoring a Backup.
 */
function LibraryRequest({
  held,
  onSettled,
}: {
  held: PublicationProgressState
  onSettled: (answer: PublicationProgressState) => void
}) {
  const queries = useQueryClient()
  const [confirming, setConfirming] = useState(false)
  const [cancelling, setCancelling] = useState(false)
  const [refusal, setRefusal] = useState<string | null>(null)

  const ask = useMutation({
    mutationFn: askForLibraryPreviews,
    onSuccess: (verdict) => {
      setConfirming(false)
      setRefusal(verdict.refusal ?? null)
      void queries.invalidateQueries({ queryKey: key })
      void queries.invalidateQueries({ queryKey: ['status'] })
    },
  })

  const pause = useMutation({ mutationFn: pauseLibraryPreviews, onSuccess: onSettled })
  const resume = useMutation({ mutationFn: resumeLibraryPreviews, onSuccess: onSettled })
  const cancel = useMutation({
    mutationFn: cancelLibraryPreviews,
    onSuccess: (answer) => {
      setCancelling(false)
      onSettled(answer)
    },
  })

  const request = held.request
  const open = request?.state === 'Running' || request?.state === 'Paused'
  const busy = pause.isPending || resume.isPending || cancel.isPending

  return (
    <>
      {request && <Request request={request} />}

      {open && request && (
        <div className={styles.controls}>
          {request.state === 'Running' ? (
            <button
              type="button"
              className={formStyles.button}
              disabled={busy}
              onClick={() => pause.mutate(request.id)}
            >
              Pause
            </button>
          ) : (
            <button
              type="button"
              className={formStyles.button}
              disabled={busy}
              onClick={() => resume.mutate(request.id)}
            >
              Resume
            </button>
          )}

          <button
            type="button"
            className={`${formStyles.button} ${formStyles.dangerButton}`}
            disabled={busy}
            onClick={() => setCancelling(true)}
          >
            Cancel the request
          </button>
        </div>
      )}

      {!open && (
        <>
          <p>
            {Number(held.offer.eligible) === 0 ? (
              <>
                No file the Library already holds is waiting for a preview.
                Everything eligible has been published, submitted or given up on.
              </>
            ) : (
              <>
                <strong>{Number(held.offer.eligible)}</strong> file(s) the Library already
                holds could have a preview generated and published. Files filed
                from now on are published on their own; these are not, and never
                will be unless this is asked for.
              </>
            )}
          </p>

          <button
            type="button"
            className={formStyles.button}
            disabled={!held.offer.askable || ask.isPending}
            onClick={() => { setRefusal(null); setConfirming(true) }}
          >
            Publish previews of the existing Library
          </button>
        </>
      )}

      {refusal && <Verdict tone="refusal">{refusal}</Verdict>}

      {confirming && (
        <ConfirmationDialog
          title="Publish previews of the existing Library?"
          confirmLabel={`Publish ${held.offer.eligible} file(s)`}
          busy={ask.isPending}
          onCancel={() => setConfirming(false)}
          onConfirm={() => ask.mutate()}
        >
          <p>
            One JPEG grid of small pictures per file, with a WebVTT saying which
            picture is which second, submitted to prdb under your account against
            the Video the file is filed as and the file&rsquo;s osHash.{' '}
            <strong>The video file itself never leaves</strong>, and neither does
            its name, its path or its sound.
          </p>
          <p>
            Each one is <strong>shown in public</strong> once a moderator has
            seen it, and <strong>prdb has no retraction</strong> for one it has
            accepted. This can be paused or cancelled at any time; cancelling
            stops what has not been sent and takes back nothing that has.
          </p>
          <p>
            It runs in the background at about one file a minute, well behind
            everything a person is waiting for.
          </p>
        </ConfirmationDialog>
      )}

      {cancelling && request && (
        <ConfirmationDialog
          title="Cancel the Library request?"
          confirmLabel="Cancel the request"
          busy={cancel.isPending}
          danger
          onCancel={() => setCancelling(false)}
          onConfirm={() => cancel.mutate(request.id)}
        >
          <p>
            Everything this request selected that has not been sent is given up:
            the files still waiting to be made, and the previews already made and
            queued. Their generated bytes go with them.
          </p>
          <p>
            <strong>What prdb has already accepted stays at prdb.</strong> There
            is no retraction for a submission, whether a moderator has shown it
            yet or not. An upload whose outcome is unknown is left as it is —
            cancelling a request is not an answer to that question.
          </p>
        </ConfirmationDialog>
      )}
    </>
  )
}

function Request({ request }: { request: PublicationRequest }) {
  // The bar is a picture of a number that can move: a file filed while the
  // request runs is taken up by Filing rather than by this, so the eligible
  // count can grow under it. Clamped, and the figures stand beside it.
  const selected = Number(request.selected)
  const takenUp = Number(request.takenUp)
  const remaining = Number(request.remaining)
  const total = Math.max(selected, takenUp)
  const done = total === 0 ? 100 : Math.min(100, Math.round((takenUp / total) * 100))

  return (
    <div className={styles.request}>
      <h3>{heading(request.state)}</h3>

      <div className={styles.bar}>
        <div className={styles.barFill} style={{ width: `${done}%` }} />
      </div>

      <p className={settingsStyles.detail}>
        {takenUp} of about {selected} file(s) taken up
        {remaining > 0 && <>, {remaining} still eligible</>}. Asked for at{' '}
        {request.requestedAt}.
      </p>

      {request.note && <p className={settingsStyles.detail}>{request.note}</p>}
    </div>
  )
}

function heading(state: PublicationRequest['state']): string {
  switch (state) {
    case 'Running':
      return 'Publishing the existing Library'
    case 'Paused':
      return 'Paused'
    case 'Cancelled':
      return 'The last request was cancelled'
    default:
      return 'The last request finished'
  }
}

/**
 * The two acts on an upload whose outcome nobody can establish, and the words
 * that say what each one costs.
 *
 * ADR 0064 refuses to let the tool decide this: prdb exposes no idempotency key
 * and a submission in moderation is invisible to every read endpoint, so
 * sending again risks a second picture in a public gallery that nobody can take
 * down, and leaving it risks a preview that was never published. Only a person
 * may weigh that, and this is where.
 */
function Uncertain({
  held,
  onSettled,
}: {
  held: PublicationProgressState
  onSettled: (answer: PublicationProgressState) => void
}) {
  const again = useMutation({ mutationFn: sendPublicationAgain, onSuccess: onSettled })
  const leave = useMutation({ mutationFn: leavePublication, onSuccess: onSettled })

  if (held.uncertain.length === 0) {
    return (
      <p className={settingsStyles.detail}>
        Every upload that left was answered. Nothing is waiting on a decision.
      </p>
    )
  }

  return (
    <>
      <p>
        Each of these left this machine and nothing came back saying what became
        of it. prdb cannot be asked: a submission in moderation is not publicly
        visible, so its absence from every endpoint proves nothing. They are
        never sent again on their own.
      </p>

      <ul className={styles.uncertain}>
        {held.uncertain.map((row) => (
          <Row
            key={row.id}
            row={row}
            busy={again.isPending || leave.isPending}
            onAgain={() => again.mutate(row.id)}
            onLeave={() => leave.mutate(row.id)}
          />
        ))}
      </ul>

      {Number(held.tally.uncertain) > held.uncertain.length && (
        <p className={settingsStyles.detail}>
          {Number(held.tally.uncertain) - held.uncertain.length} more are not listed here.
        </p>
      )}
    </>
  )
}

function Row({
  row,
  busy,
  onAgain,
  onLeave,
}: {
  row: UncertainPublication
  busy: boolean
  onAgain: () => void
  onLeave: () => void
}) {
  const [asking, setAsking] = useState(false)

  return (
    <li>
      <p>
        <span className={styles.hash}>{row.osHash}</span>{' '}
        <span className={styles.when}>
          {row.at ? `left at ${row.at}` : 'left at an unrecorded time'}
        </span>
      </p>

      {row.note && <p className={`${styles.why} ${settingsStyles.detail}`}>{row.note}</p>}

      <div className={styles.controls}>
        <button
          type="button"
          className={formStyles.button}
          disabled={busy}
          onClick={() => setAsking(true)}
        >
          Send it again
        </button>
        <button type="button" className={formStyles.button} disabled={busy} onClick={onLeave}>
          Leave it as it is
        </button>
      </div>

      {asking && (
        <ConfirmationDialog
          title="Send this preview again?"
          confirmLabel="Send it again"
          busy={busy}
          danger
          onCancel={() => setAsking(false)}
          onConfirm={() => { setAsking(false); onAgain() }}
        >
          <p>
            The first submission may have arrived. prdb offers nothing that
            recognises a second copy as the same submission, so if it did, this
            puts <strong>two pictures of the same file</strong> in your public
            gallery &mdash; and there is no retraction for either.
          </p>
          <p>
            If it did not arrive, this is the only thing that publishes it.
            Nothing else will.
          </p>
          {!row.holds && (
            <p>
              The generated pair is no longer on disk, so the same picture is
              decoded again from the same file before it goes.
            </p>
          )}
        </ConfirmationDialog>
      )}
    </li>
  )
}
