import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { api } from '../../api/client'
import { useHubEvent, useQuotes } from '../../api/signalr'
import { sessionState } from '../../lib/session'
import { load, save } from '../../lib/storage'
import { useResearchStatus } from '../research/api'
import type { Insight } from '../insight/types'
import { Portrait, type Mood, type Outfit } from './Portrait'
import s from './Mascot.module.css'

type OutfitSetting = 'auto' | Outfit
const OUTFIT_KEY = 'sumire.mascot.outfit'
const EVENT_MS = 12_000

interface Bubble {
  key: string
  text: string
  mood?: Mood
}

interface AlertEvent {
  id: number
  message: string
  time: string
}

interface InsightReadyEvent {
  kind: string
  subject: string
}

function firstSentence(text: string, max = 140) {
  const m = /^(.+?[.!?])(\s|$)/.exec(text.trim())
  const t = m ? m[1] : text.trim()
  return t.length > max ? `${t.slice(0, max - 1)}…` : t
}

function moodFromIhsg(chg: number | null | undefined): Mood {
  if (chg == null) return 'neutral'
  if (chg >= 1) return 'smiling'
  if (chg <= -1) return 'worried'
  return 'neutral'
}

/** Sumire bust-up, bottom right. Mood follows IHSG, alerts, new insights and research activity. `m` hides it. */
export default function Mascot({ hidden }: { hidden: boolean }) {
  const [setting, setSetting] = useState<OutfitSetting>(() => load<OutfitSetting>(OUTFIT_KEY, 'auto'))
  const [event, setEvent] = useState<Bubble | null>(null)
  const [dismissed, setDismissed] = useState<string | null>(null)
  const [, tick] = useState(0)

  const ihsg = useQuotes(['IHSG']).IHSG
  const research = useResearchStatus()
  const brief = useQuery({
    queryKey: ['briefs', 'latest', 'any'],
    queryFn: ({ signal }) => api<Insight>('/briefs/latest', { signal }),
    staleTime: 10 * 60_000,
    refetchInterval: 15 * 60_000,
    retry: false,
  })

  useHubEvent<AlertEvent>('alert', (a) => setEvent({ key: `alert-${a.id}-${a.time}`, text: a.message, mood: 'surprised' }))
  useHubEvent<InsightReadyEvent>('insightReady', (e) =>
    setEvent({
      key: `ins-${e.kind}-${e.subject}-${Date.now()}`,
      text: e.kind.startsWith('brief') ? 'A new market brief is ready.' : `My notes on ${e.subject} are ready.`,
      mood: 'smiling',
    }),
  )

  useEffect(() => save(OUTFIT_KEY, setting), [setting])
  useEffect(() => {
    if (!event) return
    const id = setTimeout(() => setEvent(null), EVENT_MS)
    return () => clearTimeout(id)
  }, [event])
  // re-evaluate the auto outfit as sessions open and close
  useEffect(() => {
    const id = setInterval(() => tick((n) => n + 1), 60_000)
    return () => clearInterval(id)
  }, [])

  if (hidden) return null

  const session = sessionState()
  const outfit: Outfit = setting === 'auto' ? (session === 'LIVE' || session === 'BREAK' ? 'gym' : 'uniform') : setting
  const researching = research.data?.jobs.some((j) => j.status === 'running')
  const mood: Mood = event?.mood ?? (researching ? 'determined' : moodFromIhsg(ihsg?.changePct))

  const briefText = brief.data?.summary ? firstSentence(brief.data.summary) : null
  const bubble: Bubble | null = event ?? (briefText ? { key: `brief-${brief.data!.id}`, text: briefText } : null)
  const showBubble = bubble && bubble.key !== dismissed

  const nextSetting: Record<OutfitSetting, OutfitSetting> = { auto: 'uniform', uniform: 'gym', gym: 'auto' }
  const ihsgText = ihsg?.changePct != null ? ` · IHSG ${ihsg.changePct >= 0 ? '+' : ''}${ihsg.changePct.toFixed(2)}%` : ''

  return (
    <div className={s.wrap}>
      {showBubble && (
        <div className={s.plate}>
          <button
            type="button"
            className={`${s.bubble} ${bubble.key.startsWith('alert-') ? s.alert : ''}`}
            onClick={() => {
              setDismissed(bubble.key)
              if (event) setEvent(null)
            }}
            title="Dismiss"
          >
            {bubble.key.startsWith('alert-') && <span className={s.tag}>Violet</span>}
            {bubble.text}
          </button>
        </div>
      )}
      <div className={s.figure} title={`Sumire (${mood}${ihsgText}) · press m to hide`}>
        <Portrait outfit={outfit} mood={mood} size={120} />
        <button
          type="button"
          className={s.outfit}
          onClick={() => setSetting((v) => nextSetting[v])}
          title="Outfit: auto switches to gym clothes during the market session"
        >
          {setting === 'auto' ? `auto · ${outfit}` : setting}
        </button>
      </div>
    </div>
  )
}
