import { ApiError } from '../../api/client'
import { Button } from './Button'
import { Empty } from './Empty'

function describe(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 404) return 'Not found. The data may not exist yet for this ticker.'
    if (error.status >= 500) return `The server had a problem (${error.status}). ${error.message}`
    return error.message
  }
  if (error instanceof TypeError) return 'Cannot reach the Sumire server. Is it running on localhost:5080?'
  if (error instanceof Error) return error.message
  return 'Something went wrong.'
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
    <Empty title={title} message={describe(error)}>
      {onRetry && (
        <Button variant="primary" onClick={onRetry}>
          Retry
        </Button>
      )}
    </Empty>
  )
}
