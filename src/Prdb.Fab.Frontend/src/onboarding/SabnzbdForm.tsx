import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import {
  readSabnzbdCategories,
  saveSabnzbd,
  type SabnzbdCategoriesVerdict,
  type SabnzbdConnectionVerdict,
} from '../api/client.ts'
import { Field, formStyles } from '../ui/Form.tsx'
import { SaveBar } from '../ui/SaveBar.tsx'
import { Verdict } from '../ui/Verdict.tsx'
import { connectionsKey } from './state.ts'

/**
 * ADR 0010's downloader step, in the order that ADR insists on: the address and
 * the key first, then a category taken from SABnzbd's own list, and only then
 * the mapping — because the category decides which of SABnzbd's folders is
 * being mapped.
 *
 * There is no field for the download directory. It is the second half of the
 * mapping, and asking twice for one fact is how two answers end up disagreeing.
 */
export function SabnzbdForm({
  submitLabel = 'Check and continue',
  stored,
  onSaved,
}: {
  submitLabel?: string
  /**
   * What is configured, when something is. ADR 0020: the key is not part of it
   * — keys are write-only, and an empty field means the one that is there.
   */
  stored?: {
    url: string | null
    category: string | null
    downloadDirectory: string | null
    keyIsStored: boolean
  }
  onSaved?: () => void
}) {
  const [url, setUrl] = useState(stored?.url ?? '')
  const [apiKey, setApiKey] = useState('')
  const [category, setCategory] = useState('')
  const [downloadDirectory, setDownloadDirectory] = useState(stored?.downloadDirectory ?? '')
  const [listing, setListing] = useState<SabnzbdCategoriesVerdict | null>(null)
  const [verdict, setVerdict] = useState<SabnzbdConnectionVerdict | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const queries = useQueryClient()

  /**
   * A verdict is about what was submitted, so editing any field makes it stale.
   * Every field drops it, because a green sentence left standing under a form
   * that has been changed since reads as saved when nothing was.
   */
  const forgetVerdict = () => setVerdict(null)

  /**
   * The list is what one address and one key answered, so those two forget the
   * list as well — and with it the category, which is chosen from that list.
   */
  const forget = () => {
    forgetVerdict()
    setListing(null)
    setCategory('')
  }

  const ask = useMutation({
    mutationFn: () => readSabnzbdCategories(url, apiKey),
    onSuccess: (answer) => {
      setListing(answer)

      // The category that is configured, when SABnzbd still has it. A category
      // it has stopped having is not silently kept: ADR 0020 has this chosen
      // from SABnzbd's own list, and the list is what just came back.
      const held = answer.categories.find((candidate) => candidate.name === stored?.category)

      setCategory(held?.name ?? answer.categories[0]?.name ?? '')
    },
    onError: () => setFailure('SABnzbd could not be reached from here. The log says what happened.'),
  })

  const save = useMutation({
    mutationFn: () => saveSabnzbd({ url, apiKey, category, downloadDirectory }),
    onSuccess: async (answer) => {
      setVerdict(answer)

      if (answer.outcome === 'Saved') {
        await queries.invalidateQueries({ queryKey: connectionsKey })
        onSaved?.()
      }
    },
    onError: () => setFailure('The connection could not be sent. The tool may have stopped; the log says.'),
  })

  const chosen = listing?.categories.find((candidate) => candidate.name === category)
  const answered = listing?.outcome === 'Saved'
  const busy = ask.isPending || save.isPending

  /**
   * This form's submit is never idempotent: before the list is in it asks
   * SABnzbd for its categories, and afterwards it stores a mapping that is
   * verified against the filesystem. Neither is a write that can be skipped
   * because the fields look unchanged, so the dirty gate is not what guards it
   * here — the completeness of the fields is.
   */
  const alwaysSomethingToDo = true

  const incomplete =
    url.trim().length === 0
    || (apiKey.trim().length === 0 && stored?.keyIsStored !== true)
    || (answered && (category.length === 0 || downloadDirectory.trim().length === 0))

  return (
    <form
      className={formStyles.form}
      onSubmit={(event) => {
        event.preventDefault()
        setFailure(null)

        if (answered) {
          save.mutate()
        } else {
          ask.mutate()
        }
      }}
    >
      <Field
        label="Where SABnzbd is"
        hint="The address you open SABnzbd at, without /api on the end."
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="url"
            placeholder="http://sabnzbd:8080"
            autoComplete="off"
            spellCheck={false}
            value={url}
            onChange={(event) => {
              setUrl(event.target.value)
              forget()
            }}
          />
        )}
      </Field>

      <Field
        label="Its API key"
        hint={
          <>
            {stored?.keyIsStored ? (
              <>
                A key is stored. Leave this empty to keep it &mdash; it is kept even
                when the address changes, since SABnzbd moving to another port is
                not a new key.{' '}
              </>
            ) : null}
            The full API key from Config &rarr; General, not the NZB key. The NZB key
            can submit a download and cannot follow one.
          </>
        }
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="text"
            autoComplete="off"
            spellCheck={false}
            value={apiKey}
            onChange={(event) => {
              setApiKey(event.target.value)
              forget()
            }}
          />
        )}
      </Field>

      {listing && listing.outcome !== 'Saved' && (
        <Verdict tone="refusal">{listing.detail}</Verdict>
      )}

      {answered && (
        <>
          <Field
            label="The category it downloads into"
            hint={
              <>
                SABnzbd's own list. A category it does not know is not an error
                there &mdash; it quietly becomes Default, and the downloads land
                somewhere nothing is looking.
              </>
            }
          >
            {(id) => (
              <select
                id={id}
                className={formStyles.field}
                value={category}
                onChange={(event) => {
                  setCategory(event.target.value)
                  forgetVerdict()
                }}
              >
                {listing.categories.map((candidate) => (
                  <option key={candidate.name} value={candidate.name}>
                    {candidate.name}
                  </option>
                ))}
              </select>
            )}
          </Field>

          <p className={formStyles.hint}>SABnzbd finishes downloads for that category in:</p>
          <code className={formStyles.path}>{chosen?.completedRoot}</code>

          <Field
            label="Where that folder is in this container"
            hint={
              <>
                The same folder, as this container sees it. They are often
                different, and this one is checked before it is stored &mdash; a
                wrong answer is otherwise found at the first finished download,
                where it looks like a download that hangs.
              </>
            }
          >
            {(id) => (
              <input
                id={id}
                className={formStyles.field}
                type="text"
                placeholder="/downloads/complete"
                autoComplete="off"
                spellCheck={false}
                value={downloadDirectory}
                onChange={(event) => {
                  setDownloadDirectory(event.target.value)
                  forgetVerdict()
                }}
              />
            )}
          </Field>
        </>
      )}

      <SaveBar
        label={answered ? submitLabel : 'Ask SABnzbd for its categories'}
        dirty={alwaysSomethingToDo}
        pending={busy}
        disabled={incomplete}
        blocked={
          url.trim().length === 0
            ? 'The address is needed before SABnzbd can be asked anything.'
            : answered
              ? 'The category and the folder it lands in are both needed.'
              : 'A key is needed: the address alone answers to anybody.'
        }
      >
        {verdict && (
          <Verdict tone={verdict.outcome === 'Saved' ? 'done' : 'refusal'}>{verdict.detail}</Verdict>
        )}
        {failure && <Verdict tone="refusal">{failure}</Verdict>}
      </SaveBar>
    </form>
  )
}
