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
  const [downloadDirectoryOverride, setDownloadDirectory] = useState<string | null>(stored?.downloadDirectory ?? null)
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
    onError: () => setFailure('Could not load SABnzbd categories. Check the connection details and try again.'),
  })

  const chosen = listing?.categories.find((candidate) => candidate.name === category)
  // Follow the selected category's path until a person supplies a mapping.
  // Stored mappings and edits, including clearing the field, stay untouched.
  const downloadDirectory = downloadDirectoryOverride ?? chosen?.completedRoot ?? ''

  const save = useMutation({
    mutationFn: () => saveSabnzbd({ url, apiKey, category, downloadDirectory }),
    onSuccess: async (answer) => {
      setVerdict(answer)

      if (answer.outcome === 'Saved') {
        await queries.invalidateQueries({ queryKey: connectionsKey })
        onSaved?.()
      }
    },
    onError: () => setFailure('Could not confirm the SABnzbd connection was saved. Check your connection to fab and try again.'),
  })

  const answered = listing?.outcome === 'Saved'
  const saved = verdict?.outcome === 'Saved'
  const busy = ask.isPending || save.isPending

  /**
   * Checking the categories is an act even before anything has been edited.
   * Once the connection is saved, the verdict marks this draft as clean;
   * changing any field clears it and offers Save again.
   */
  const somethingToDo = !saved

  const incomplete =
    url.trim().length === 0
    || (apiKey.trim().length === 0 && stored?.keyIsStored !== true)
    || (answered && (category.length === 0 || downloadDirectory.trim().length === 0))

  return (
    <form
      className={formStyles.form}
      onSubmit={(event) => {
        event.preventDefault()
        if (busy || incomplete || saved) return
        setFailure(null)

        if (answered) {
          save.mutate()
        } else {
          ask.mutate()
        }
      }}
    >
      <Field
        label="SABnzbd URL"
        hint="Use a URL reachable from this container, without /api at the end."
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="url"
            disabled={busy}
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
        label="SABnzbd API key"
        hint={stored?.keyIsStored ? 'A key is stored. Leave empty to keep it.' : 'Use the full API key from Config → General.'}
        help="Use the full API key, not the NZB key: fab needs to follow Downloads as well as submit them. A stored key is retained even when the address changes."
      >
        {(id) => (
          <input
            id={id}
            className={formStyles.field}
            type="text"
            disabled={busy}
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
            label="Download category"
            help="Choose a category returned by SABnzbd. An unknown category silently falls back to Default in SABnzbd, which can send files to the wrong folder."
          >
            {(id) => (
              <select
                id={id}
                className={formStyles.field}
                disabled={busy}
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
            label="Completed-download path in this container"
            hint="Change this only if this container sees the folder at a different path."
            help="This path can differ from the path SABnzbd sees. It is checked before saving to ensure completed Downloads can be found."
          >
            {(id) => (
              <input
                id={id}
                className={formStyles.field}
                type="text"
                disabled={busy}
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
        label={saved ? 'Saved' : answered ? submitLabel : 'Load categories'}
        dirty={somethingToDo}
        pending={busy}
        disabled={incomplete}
        unsaved={answered ? undefined : 'Check SABnzbd to choose a category and its folder.'}
        blocked={
          url.trim().length === 0
            ? 'Enter the SABnzbd URL.'
            : answered
              ? 'Choose a category and enter its completed-download path.'
              : 'Enter the full SABnzbd API key.'
        }
      >
        {verdict && (
          <Verdict tone={saved ? 'done' : 'refusal'}>
            {saved && <>SABnzbd connection saved. </>}
            {verdict.detail}
          </Verdict>
        )}
        {failure && <Verdict tone="refusal">{failure}</Verdict>}
      </SaveBar>
    </form>
  )
}
