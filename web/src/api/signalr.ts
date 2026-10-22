import { useEffect, useRef, useState } from 'react'
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'

/** Same shape as GET /api/quotes (docs/api/market.md). `changePct` is in percent. */
export interface QuoteTick {
  code: string
  name: string | null
  price: number | null
  change: number | null
  changePct: number | null
  prevClose: number | null
  open: number | null
  high: number | null
  low: number | null
  volume: number | null
  /** price x volume in Rupiah; null for indexes and macro */
  value: number | null
  /** ISO-8601 in WIB */
  time: string | null
  /** 'live' (delayed intraday) or 'close' (last daily close) */
  source: string
}

let connection: HubConnection | null = null
let starting: Promise<void> | null = null
const subscriptions = new Map<string, number>()
const statusListeners = new Set<(s: HubConnectionState) => void>()

function notifyStatus() {
  const s = connection?.state ?? HubConnectionState.Disconnected
  statusListeners.forEach((l) => l(s))
}

function getConnection(): HubConnection {
  if (connection) return connection
  connection = new HubConnectionBuilder()
    .withUrl('/hubs/quotes')
    .withAutomaticReconnect({
      // keep retrying forever, backing off to 30s
      nextRetryDelayInMilliseconds: (ctx) => Math.min(30_000, 1000 * 2 ** Math.min(ctx.previousRetryCount, 5)),
    })
    .configureLogging(LogLevel.None)
    .build()

  connection.onreconnecting(notifyStatus)
  connection.onreconnected(() => {
    notifyStatus()
    resubscribe()
  })
  connection.onclose(() => {
    notifyStatus()
    // automatic reconnect gave up or the initial start failed: try again later
    setTimeout(() => void ensureStarted(), 15_000)
  })
  return connection
}

function resubscribe() {
  const codes = [...subscriptions.keys()]
  if (codes.length && connection?.state === HubConnectionState.Connected) {
    connection.invoke('Subscribe', codes).catch(() => {})
  }
}

async function ensureStarted(): Promise<void> {
  const conn = getConnection()
  if (conn.state === HubConnectionState.Connected) return
  if (conn.state !== HubConnectionState.Disconnected) return starting ?? undefined
  if (!starting) {
    starting = conn
      .start()
      .then(() => {
        notifyStatus()
        resubscribe()
      })
      .catch(() => {
        notifyStatus()
        setTimeout(() => void ensureStarted(), 15_000)
      })
      .finally(() => {
        starting = null
      })
  }
  return starting
}

function addCodes(codes: string[]) {
  const fresh: string[] = []
  for (const c of codes) {
    const n = subscriptions.get(c) ?? 0
    subscriptions.set(c, n + 1)
    if (n === 0) fresh.push(c)
  }
  if (fresh.length && connection?.state === HubConnectionState.Connected) {
    connection.invoke('Subscribe', fresh).catch(() => {})
  }
}

function removeCodes(codes: string[]) {
  const gone: string[] = []
  for (const c of codes) {
    const n = subscriptions.get(c) ?? 0
    if (n <= 1) {
      subscriptions.delete(c)
      if (n === 1) gone.push(c)
    } else subscriptions.set(c, n - 1)
  }
  if (gone.length && connection?.state === HubConnectionState.Connected) {
    connection.invoke('Unsubscribe', gone).catch(() => {})
  }
}

/** Live quotes for the given codes, keyed by code. Empty until the hub pushes something. */
export function useQuotes(codes: string[]): Record<string, QuoteTick> {
  const [quotes, setQuotes] = useState<Record<string, QuoteTick>>({})
  const key = [...new Set(codes.map((c) => c.toUpperCase()))].sort().join(',')

  useEffect(() => {
    if (!key) return
    const list = key.split(',')
    const wanted = new Set(list)
    const conn = getConnection()
    const handler = (payload: QuoteTick[]) => {
      if (!Array.isArray(payload)) return
      const mine = payload.filter((q) => wanted.has(q.code))
      if (!mine.length) return
      setQuotes((prev) => {
        const next = { ...prev }
        for (const q of mine) next[q.code] = q
        return next
      })
    }
    conn.on('quotes', handler)
    addCodes(list)
    void ensureStarted()
    return () => {
      conn.off('quotes', handler)
      removeCodes(list)
    }
  }, [key])

  return quotes
}

/** Listen to any hub event, e.g. useHubEvent('insightReady', (e) => ...). */
export function useHubEvent<T = unknown>(name: string, handler: (payload: T) => void) {
  const ref = useRef(handler)
  useEffect(() => {
    ref.current = handler
  })

  useEffect(() => {
    const conn = getConnection()
    const h = (payload: T) => ref.current(payload)
    conn.on(name, h)
    void ensureStarted()
    return () => conn.off(name, h)
  }, [name])
}

/** 'Connected' | 'Connecting' | 'Reconnecting' | 'Disconnected' ... */
export function useHubStatus(): HubConnectionState {
  const [state, setState] = useState<HubConnectionState>(connection?.state ?? HubConnectionState.Disconnected)
  useEffect(() => {
    statusListeners.add(setState)
    void ensureStarted()
    return () => {
      statusListeners.delete(setState)
    }
  }, [])
  return state
}
