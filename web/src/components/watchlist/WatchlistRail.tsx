import { Empty } from '../ui'

/** Left rail watchlist. Stub: the W task replaces this with the live watchlist. */
export default function WatchlistRail() {
  return (
    <Empty
      title="Watchlist"
      message={
        <>
          Live watchlist lands here. Press <kbd>w</kbd> on a ticker to add it.
        </>
      }
    />
  )
}
