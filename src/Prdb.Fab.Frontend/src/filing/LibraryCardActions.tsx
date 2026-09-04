import { useEffect, useId, useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'

import {
  deleteLibraryEntry,
  previewLibraryEntryDelete,
  type LibraryEntryDeletePreview,
  type LibraryPage,
} from '../api/client.ts'
import { Icon } from '../catalogue/CardActions.tsx'
import actions from '../catalogue/CardActions.module.css'
import { prdbVideoUrl } from '../catalogue/prdb.ts'
import { videoReleasePath } from '../release/routes.ts'
import { ConfirmationDialog } from './ConfirmationDialog.tsx'
import filing from './Filing.module.css'

type LibraryCard = LibraryPage['entries'][number]

export function LibraryCardActions({ entry, returnTo }: { entry: LibraryCard; returnTo: string }) {
  const queries = useQueryClient()
  const [menuOpen, setMenuOpen] = useState(false)
  const [confirmation, setConfirmation] = useState<LibraryEntryDeletePreview | null>(null)
  const [problem, setProblem] = useState<string | null>(null)
  const menuId = useId()
  const menu = useRef<HTMLSpanElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)
  const preview = useMutation({
    mutationFn: () => previewLibraryEntryDelete(entry.id),
    onSuccess: (answer) => {
      if (answer.outcome === 'Ready') {
        setConfirmation(answer)
        setProblem(null)
      } else {
        setProblem(answer.detail)
      }
    },
    onError: (error) => setProblem(messageOf(error)),
  })
  const remove = useMutation({
    mutationFn: (answer: LibraryEntryDeletePreview) =>
      deleteLibraryEntry(entry.id, answer.files.map((file) => file.id)),
    onSuccess: async (answer) => {
      setConfirmation(null)
      if (answer.outcome !== 'Deleted') {
        setProblem(answer.detail)
        return
      }

      await Promise.all([
        queries.invalidateQueries({ queryKey: ['library'] }),
        queries.invalidateQueries({ queryKey: ['library-entry', entry.id] }),
        queries.invalidateQueries({ queryKey: ['catalogue'] }),
        queries.invalidateQueries({ queryKey: ['catalogue-videos'] }),
        queries.invalidateQueries({ queryKey: ['releases'] }),
        queries.invalidateQueries({ queryKey: ['operation-log'] }),
        queries.invalidateQueries({ queryKey: ['reporting-settings'] }),
      ])
    },
    onError: (error) => setProblem(messageOf(error)),
  })

  useEffect(() => {
    if (!menuOpen) return

    const closeOutside = (event: PointerEvent) => {
      if (event.target instanceof Node && !menu.current?.contains(event.target)) {
        setMenuOpen(false)
      }
    }
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setMenuOpen(false)
        trigger.current?.focus()
      }
    }

    document.addEventListener('pointerdown', closeOutside)
    window.addEventListener('keydown', closeOnEscape)
    return () => {
      document.removeEventListener('pointerdown', closeOutside)
      window.removeEventListener('keydown', closeOnEscape)
    }
  }, [menuOpen])

  const totalBytes = confirmation?.files.reduce((total, file) => total + Number(file.sizeBytes), 0) ?? 0

  return <span className={actions.control}>
    <span className={actions.actions}>
      <Link
        aria-label={`Find another Release for ${entry.title}`}
        className={actions.primary}
        title={`Find another Release for ${entry.title}`}
        to={videoReleasePath(entry.id, returnTo)}
      >
        <Icon name="search" />
        <span>Find another Release</span>
      </Link>

      <span
        className={actions.more}
        onBlur={(event) => {
          if (event.relatedTarget instanceof Node && !event.currentTarget.contains(event.relatedTarget)) {
            setMenuOpen(false)
          }
        }}
        ref={menu}
      >
        <button
          aria-controls={menuOpen ? menuId : undefined}
          aria-expanded={menuOpen}
          aria-haspopup="true"
          aria-label={`More actions for ${entry.title}`}
          className={actions.iconButton}
          onClick={() => setMenuOpen((current) => !current)}
          ref={trigger}
          title="More actions"
          type="button"
        >
          <Icon name="more" />
        </button>

        {menuOpen && <span className={actions.menu} id={menuId}>
          <a
            href={prdbVideoUrl(entry.id)}
            onClick={() => setMenuOpen(false)}
            rel="noreferrer"
            target="_blank"
          >
            <Icon name="external" />
            <span>Open in prdb</span>
          </a>
          <button
            className={actions.dangerAction}
            disabled={preview.isPending}
            onClick={() => {
              setMenuOpen(false)
              preview.mutate()
            }}
            type="button"
          >
            <Icon name="delete" />
            <span>{preview.isPending ? 'Checking files…' : 'Delete Library Entry…'}</span>
          </button>
        </span>}
      </span>
    </span>

    {problem && <span className={actions.error} role="alert">{problem}</span>}

    {confirmation && <ConfirmationDialog
      title={`Delete “${entry.title}” permanently?`}
      confirmLabel={remove.isPending ? 'Deleting…' : `Delete ${confirmation.files.length} file${confirmation.files.length === 1 ? '' : 's'}`}
      busy={remove.isPending}
      danger
      onCancel={() => setConfirmation(null)}
      onConfirm={() => remove.mutate(confirmation)}
    >
      <p>This cannot be undone. Every Video File will be removed from disk and recorded separately in the Operation Log. The generated sidecar and Entry Image will also be removed.</p>
      <p className={filing.dialogSummary}><strong>{formatBytes(totalBytes)}</strong> across {confirmation.files.length} Video File{confirmation.files.length === 1 ? '' : 's'}.</p>
      <ul className={filing.confirmationList}>
        {confirmation.files.map((file) => <li key={file.id}>
          <strong>{file.fileName}</strong>
          <span>{file.quality} · {formatBytes(file.sizeBytes)}</span>
          <code>{file.path}</code>
        </li>)}
      </ul>
    </ConfirmationDialog>}
  </span>
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'The Library Entry could not be deleted.'
}

function formatBytes(value: number | string): string {
  const bytes = Number(value)
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(bytes >= 10 * 1024 ** 3 ? 1 : 2)} GiB`
  return `${(bytes / 1024 ** 2).toFixed(1)} MiB`
}
