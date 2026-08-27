import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'

import {
  readSabnzbdCategories,
  saveSabnzbd,
  type SabnzbdCategoriesVerdict,
  type SabnzbdConnectionVerdict,
} from '../api/client.ts'
import { connectionsKey } from './state.ts'
import styles from './Onboarding.module.css'

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
  onSaved,
}: {
  submitLabel?: string
  onSaved?: () => void
}) {
  const [url, setUrl] = useState('')
  const [apiKey, setApiKey] = useState('')
  const [category, setCategory] = useState('')
  const [downloadDirectory, setDownloadDirectory] = useState('')
  const [listing, setListing] = useState<SabnzbdCategoriesVerdict | null>(null)
  const [verdict, setVerdict] = useState<SabnzbdConnectionVerdict | null>(null)
  const [failure, setFailure] = useState<string | null>(null)
  const queries = useQueryClient()

  const forget = () => {
    setListing(null)
    setVerdict(null)
    setCategory('')
  }

  const ask = useMutation({
    mutationFn: () => readSabnzbdCategories(url, apiKey),
    onSuccess: (answer) => {
      setListing(answer)
      setCategory(answer.categories[0]?.name ?? '')
    },
    onError: (error) => setFailure(String(error)),
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
    onError: (error) => setFailure(String(error)),
  })

  const chosen = listing?.categories.find((candidate) => candidate.name === category)
  const answered = listing?.outcome === 'Saved'
  const busy = ask.isPending || save.isPending

  return (
    <form
      className={styles.form}
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
      <label className={styles.label} htmlFor="sabnzbd-url">
        Where SABnzbd is
      </label>
      <input
        id="sabnzbd-url"
        className={styles.field}
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
      <p className={styles.hint}>The address you open SABnzbd at, without /api on the end.</p>

      <label className={styles.label} htmlFor="sabnzbd-key">
        Its API key
      </label>
      <input
        id="sabnzbd-key"
        className={styles.field}
        type="text"
        autoComplete="off"
        spellCheck={false}
        value={apiKey}
        onChange={(event) => {
          setApiKey(event.target.value)
          forget()
        }}
      />
      <p className={styles.hint}>
        The full API key from Config &rarr; General, not the NZB key. The NZB key
        can submit a download and cannot follow one.
      </p>

      {listing && listing.outcome !== 'Saved' && <p className={styles.refusal}>{listing.detail}</p>}

      {answered && (
        <>
          <label className={styles.label} htmlFor="sabnzbd-category">
            The category it downloads into
          </label>
          <select
            id="sabnzbd-category"
            className={styles.field}
            value={category}
            onChange={(event) => setCategory(event.target.value)}
          >
            {listing.categories.map((candidate) => (
              <option key={candidate.name} value={candidate.name}>
                {candidate.name}
              </option>
            ))}
          </select>
          <p className={styles.hint}>
            SABnzbd's own list. A category it does not know is not an error there
            &mdash; it quietly becomes Default, and the downloads land somewhere
            nothing is looking.
          </p>

          <label className={styles.label} htmlFor="sabnzbd-mapping">
            Where that folder is in this container
          </label>
          <p className={styles.hint}>SABnzbd finishes downloads for that category in:</p>
          <code className={styles.path}>{chosen?.completedRoot}</code>
          <input
            id="sabnzbd-mapping"
            className={styles.field}
            type="text"
            placeholder="/downloads/complete"
            autoComplete="off"
            spellCheck={false}
            value={downloadDirectory}
            onChange={(event) => setDownloadDirectory(event.target.value)}
          />
          <p className={styles.hint}>
            The same folder, as this container sees it. They are often different,
            and this one is checked before it is stored &mdash; a wrong answer is
            otherwise found at the first finished download, where it looks like a
            download that hangs.
          </p>
        </>
      )}

      {verdict && (
        <p className={verdict.outcome === 'Saved' ? styles.done : styles.refusal}>{verdict.detail}</p>
      )}
      {failure && <p className={styles.refusal}>{failure}</p>}

      <button
        className={styles.button}
        type="submit"
        disabled={
          busy ||
          url.trim().length === 0 ||
          apiKey.trim().length === 0 ||
          (answered && (category.length === 0 || downloadDirectory.trim().length === 0))
        }
      >
        {answered ? submitLabel : 'Ask SABnzbd for its categories'}
      </button>
    </form>
  )
}
