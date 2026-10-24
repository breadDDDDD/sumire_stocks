import { useEffect, useState } from 'react'
import { HubConnectionState } from '@microsoft/signalr'
import { useHubStatus } from '../../api/signalr'
import { reducedMotion } from '../../lib/motion'
import { sessionState, type SessionState } from '../../lib/session'
import { formatTimeWib } from '../../lib/time'
import { useMarketStatus } from '../../shell/useMarketStatus'
import { SumireMark } from '../brand/SumireMark'
import s from './Boot.module.css'

const BOOT_KEY = 'sumire.boot'
/** Fade starts here (ms after mount); the overlay is gone 160 ms later. */
const FADE_AT = 700
const FADE_MS = 160

const SESSION_TEXT: Record<SessionState, string> = {
  'PRE-OPEN': 'Pre-open',
  LIVE: 'Session live',
  BREAK: 'Session break',
  'PRE-CLOSE': 'Pre-close',
  POST: 'Post-trading',
  CLOSED: 'Market closed',
}

function shouldBoot(): boolean {
  if (reducedMotion()) return false
  try {
    return sessionStorage.getItem(BOOT_KEY) !== '1'
  } catch {
    return false
  }
}

function markBooted() {
  try {
    sessionStorage.setItem(BOOT_KEY, '1')
  } catch {
    // storage blocked: the boot simply runs again next load
  }
}

type Result = { text: string; tone: 'ok' | 'warn' | 'info' | 'wait' }

function Line({ k, v, r }: { k: string; v: string; r: Result }) {
  return (
    <div className={s.line}>
      <span className={s.key}>{k}</span>
      <span className={s.val}>{v}</span>
      <span className={s.leader} aria-hidden="true" />
      <span className={s.res} data-tone={r.tone}>
        {r.text}
      </span>
    </div>
  )
}

/**
 * Once-per-session status readout drawn over the already-running shell. It never delays the app:
 * the shell mounts and fetches underneath, and the command line keeps focus. Any key, click or wheel skips it.
 */
export function Boot() {
  const [phase, setPhase] = useState<'run' | 'out' | 'done'>(() => (shouldBoot() ? 'run' : 'done'))

  useEffect(() => {
    if (phase === 'done') return
    if (phase === 'out') {
      window.dispatchEvent(new Event('sumire:boot-out'))
      const id = setTimeout(() => {
        markBooted()
        setPhase('done')
      }, FADE_MS)
      return () => clearTimeout(id)
    }
    const id = setTimeout(() => setPhase('out'), FADE_AT)
    const skip = () => setPhase('out')
    // capture, passive, and never prevented: the first keystroke still lands in the command line
    const opts = { capture: true, passive: true } as const
    window.addEventListener('keydown', skip, opts)
    window.addEventListener('pointerdown', skip, opts)
    window.addEventListener('wheel', skip, opts)
    return () => {
      clearTimeout(id)
      window.removeEventListener('keydown', skip, opts)
      window.removeEventListener('pointerdown', skip, opts)
      window.removeEventListener('wheel', skip, opts)
    }
  }, [phase])

  if (phase === 'done') return null
  return <BootScreen out={phase === 'out'} />
}

function BootScreen({ out }: { out: boolean }) {
  const hub = useHubStatus()
  const status = useMarketStatus()
  const d = status.data
  const now = new Date()

  const link: Result =
    hub === HubConnectionState.Connected
      ? { text: 'OK', tone: 'ok' }
      : hub === HubConnectionState.Disconnected && status.isError
        ? { text: 'Retry', tone: 'warn' }
        : { text: '…', tone: 'wait' }
  const feed: Result = d?.lastQuoteTime
    ? { text: 'OK', tone: 'ok' }
    : d
      ? { text: 'No quotes', tone: 'warn' }
      : status.isError
        ? { text: 'Retry', tone: 'warn' }
        : { text: '…', tone: 'wait' }
  const db: Result = d
    ? d.db === 'ok'
      ? { text: 'OK', tone: 'ok' }
      : { text: 'Offline', tone: 'warn' }
    : status.isError
      ? { text: 'Offline', tone: 'warn' }
      : { text: '…', tone: 'wait' }
  const session = sessionState(now)

  return (
    <div className={`${s.overlay} ${out ? s.out : ''}`} aria-hidden="true">
      <div className={s.block}>
        <div className={s.brand}>
          <SumireMark size={32} />
          <span className={s.word}>Sumire</span>
        </div>
        <div className={s.lines}>
          <Line k="Link" v="/hubs/quotes" r={link} />
          <Line k="Feed" v={d?.lastQuoteTime ? `Yahoo · ${formatTimeWib(d.lastQuoteTime)} WIB` : 'Yahoo'} r={feed} />
          <Line k="IDX" v={SESSION_TEXT[session]} r={{ text: `${formatTimeWib(now).slice(0, 5)} WIB`, tone: 'info' }} />
          <Line k="DB" v="Postgres" r={db} />
        </div>
      </div>
    </div>
  )
}
