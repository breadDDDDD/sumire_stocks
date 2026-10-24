import { ApiError } from '../../api/client'
import { Button } from './Button'
import { Empty } from './Empty'
import s from './Empty.module.css'

function describe(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 404) return "There's no data for this ticker yet."
    if (error.status >= 500) return `Server error (${error.status}). ${error.message}`
    return error.message
  }
  if (error instanceof TypeError) return "The Sumire server at localhost:5080 isn't answering. Start it, then retry."
  if (error instanceof Error) return error.message
  return 'That request failed.'
}

/** "ERR 503", "ERR NET" or plain "ERR". */
function errCode(error: unknown): string {
  if (error instanceof ApiError) return `ERR ${error.status}`
  if (error instanceof TypeError) return 'ERR NET'
  return 'ERR'
}

export function ErrorState({
  error,
  onRetry,
  title = 'Could not load',
}: {
  error?: unknown
  onRetry?: () => void
  title?: string
}) {
  return (
    <Empty
      title={
        <>
          <span className={s.code}>{errCode(error)}</span>
          <span>{title}</span>
        </>
      }
      message={describe(error)}
      tone="error"
    >
      {onRetry && (
        <Button variant="primary" onClick={onRetry}>
          Retry
        </Button>
      )}
    </Empty>
  )
}
