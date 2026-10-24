import { ApiError } from '../../api/client'
import { Button } from './Button'
import { Empty } from './Empty'

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
    <Empty title={title} message={describe(error)} tone="error">
      {onRetry && (
        <Button variant="primary" onClick={onRetry}>
          Retry
        </Button>
      )}
    </Empty>
  )
}
