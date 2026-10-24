import { forwardRef, useEffect, useImperativeHandle, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import { FUNCTION_META, isFunctionCode } from '../functions/types'
import { load, save } from '../lib/storage'
import { useInstrumentSearch } from './useInstrumentSearch'
import { useWorkspace } from './workspace'
import s from './CommandLine.module.css'

export interface CommandLineHandle {
  focus: (prefill?: string) => void
}

interface Suggestion {
  kind: 'fn' | 'ticker' | 'recent'
  label: string
  detail?: string
  meta?: string
  /** Replacement for the token being typed; for 'recent' the whole command. */
  insert: string
}

const RECENT_KEY = 'sumire.recent.v1'
const RECENT_LIMIT = 30
const ALL_FUNCTIONS = Object.values(FUNCTION_META)

type Context = 'first' | 'function' | 'ticker' | 'none'

function analyse(value: string): { tokens: string[]; current: string; context: Context } {
  const tokens = value.split(/\s+/)
  if (tokens[0] === '') tokens.shift()
  if (!tokens.length) return { tokens, current: '', context: 'first' }
  const current = tokens[tokens.length - 1]
  const i = tokens.length - 1
  if (i === 0) return { tokens, current, context: 'first' }
  const first = tokens[0].toUpperCase()
  const second = (tokens[1] ?? '').toUpperCase()
  if (first === 'ASK' || second === 'ASK') return { tokens, current, context: 'none' }
  if (first === 'COMP' || (i >= 2 && second === 'COMP')) return { tokens, current, context: 'ticker' }
  if (i === 1 && !isFunctionCode(first) && first !== '?' && first !== 'INS!') return { tokens, current, context: 'function' }
  return { tokens, current, context: 'none' }
}

/** The `[TICKER] [FUNCTION]` command line with autocomplete and recent history. */
export const CommandLine = forwardRef<CommandLineHandle>(function CommandLine(_props, ref) {
  const ws = useWorkspace()
  const inputRef = useRef<HTMLInputElement>(null)
  const [value, setValue] = useState('')
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const [navigated, setNavigated] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [recent, setRecent] = useState<string[]>(() => load<string[]>(RECENT_KEY, []))
  const [historyIdx, setHistoryIdx] = useState(-1)

  useImperativeHandle(ref, () => ({
    focus: (prefill) => {
      if (prefill !== undefined) {
        setValue(prefill)
        setOpen(true)
        setActive(0)
        setNavigated(false)
        setHistoryIdx(-1)
      }
      inputRef.current?.focus()
    },
  }))

  useEffect(() => {
    if (!error) return
    const id = setTimeout(() => setError(null), 4000)
    return () => clearTimeout(id)
  }, [error])

  const { current, context } = analyse(value)
  const wantTickers = (context === 'first' || context === 'ticker') && current.length > 0
  const hits = useInstrumentSearch(current, wantTickers)

  const suggestions = useMemo<Suggestion[]>(() => {
    const q = current.toUpperCase()
    if (!value.trim()) {
      return recent.slice(0, 8).map((r) => ({ kind: 'recent', label: r, insert: r }))
    }
    const out: Suggestion[] = []
    if (context === 'first' || context === 'function') {
      for (const f of ALL_FUNCTIONS) {
        if (context === 'function' && f.code === 'HELP') continue
        if (!q || f.code.startsWith(q)) out.push({ kind: 'fn', label: f.code, detail: f.description, meta: f.name, insert: f.code })
      }
      if (context === 'function' && 'INS!'.startsWith(q) && q.startsWith('INS')) {
        out.push({ kind: 'fn', label: 'INS!', detail: 'Queue a fresh AI insight', meta: 'Insight', insert: 'INS!' })
      }
    }
    if (context === 'first' || context === 'ticker') {
      for (const h of hits.slice(0, 10)) {
        out.push({ kind: 'ticker', label: h.code, detail: h.name, meta: h.sector, insert: h.code })
      }
    }
    // an exact match typed in full needs no dropdown row
    if (out.length === 1 && out[0].insert === q) return []
    return out
  }, [value, current, context, hits, recent])

  const showList = open && suggestions.length > 0
  const activeIndex = Math.min(active, suggestions.length - 1)

  const accept = (sug: Suggestion) => {
    if (sug.kind === 'recent') {
      setValue(sug.insert)
    } else {
      const head = value.replace(/\S*$/, '')
      setValue(`${head}${sug.insert} `)
    }
    setActive(0)
    setNavigated(false)
    inputRef.current?.focus()
  }

  const remember = (cmd: string) => {
    const next = [cmd, ...recent.filter((r) => r !== cmd)].slice(0, RECENT_LIMIT)
    setRecent(next)
    save(RECENT_KEY, next)
  }

  const execute = (text: string) => {
    const cmd = text.trim().toUpperCase().replace(/\s+/g, ' ')
    const raw = text.trim().replace(/\s+/g, ' ')
    // keep ASK questions in the user's own casing
    const toRun = /^(\S+\s+)?ASK\b/i.test(raw) ? raw.replace(/^(\S+\s+)?ask\b/i, (m) => m.toUpperCase()) : cmd
    const err = ws.run(toRun)
    if (err) {
      setError(err)
      return
    }
    remember(toRun)
    setValue('')
    setOpen(false)
    setError(null)
    setHistoryIdx(-1)
    inputRef.current?.blur()
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault()
        if (historyIdx >= 0) {
          const next = historyIdx - 1
          setHistoryIdx(next)
          setValue(next >= 0 ? recent[next] : '')
          return
        }
        setOpen(true)
        setNavigated(true)
        setActive((a) => (suggestions.length ? (a + 1) % suggestions.length : 0))
        return
      case 'ArrowUp':
        e.preventDefault()
        if ((!value.trim() || historyIdx >= 0) && recent.length) {
          const next = Math.min(recent.length - 1, historyIdx + 1)
          setHistoryIdx(next)
          setValue(recent[next])
          setOpen(false)
          return
        }
        setOpen(true)
        setNavigated(true)
        setActive((a) => (suggestions.length ? (a - 1 + suggestions.length) % suggestions.length : 0))
        return
      case 'Tab':
        if (showList) {
          e.preventDefault()
          accept(suggestions[activeIndex])
        } else if (!value.trim()) {
          // let Tab fall through to pane cycling
          e.preventDefault()
          inputRef.current?.blur()
        }
        return
      case 'Enter': {
        e.preventDefault()
        if (showList && navigated) {
          const sug = suggestions[activeIndex]
          if (sug.kind === 'recent') return execute(sug.insert)
          const head = value.replace(/\S*$/, '')
          return execute(`${head}${sug.insert}`)
        }
        if (value.trim()) execute(value)
        return
      }
      case 'Escape':
        e.preventDefault()
        if (showList) setOpen(false)
        else if (value) {
          setValue('')
          setHistoryIdx(-1)
        } else inputRef.current?.blur()
        return
    }
  }

  const placeholder = `Type a ticker or function, e.g. ${ws.focusedTicker ?? 'BBCA'} GP`

  return (
    <div className={s.wrap}>
      <div className={`${s.box} ${error ? s.error : ''}`}>
        <span className={s.prompt} aria-hidden="true">
          ▸
        </span>
        <input
          ref={inputRef}
          className={s.input}
          value={value}
          placeholder={placeholder}
          spellCheck={false}
          autoComplete="off"
          aria-label="Command line"
          aria-expanded={showList}
          aria-controls="cmd-suggestions"
          role="combobox"
          onChange={(e) => {
            setValue(e.target.value)
            setOpen(true)
            setActive(0)
            setNavigated(false)
            setHistoryIdx(-1)
            setError(null)
          }}
          onFocus={() => setOpen(true)}
          onBlur={() => setOpen(false)}
          onKeyDown={onKeyDown}
        />
        {error ? (
          <span className={s.errorText} role="alert">
            {error}
          </span>
        ) : (
          <span className={s.hint} aria-hidden="true">
            {showList && <kbd className="key-cancel">ESC</kbd>}
            <kbd className="key-go">GO</kbd>
          </span>
        )}
      </div>
      {showList && (
        <ul className={s.list} id="cmd-suggestions" role="listbox">
          {suggestions[0].kind === 'recent' && <li className={s.section}>Recent</li>}
          {suggestions.map((sug, i) => (
            <li
              key={`${sug.kind}:${sug.label}`}
              role="option"
              aria-selected={i === activeIndex}
              className={`${s.item} ${i === activeIndex ? s.itemActive : ''}`}
              onMouseDown={(e) => {
                e.preventDefault()
                if (sug.kind === 'recent') execute(sug.insert)
                else accept(sug)
              }}
              onMouseEnter={() => setActive(i)}
            >
              <span className={s.itemLabel}>{sug.label}</span>
              <span className={s.itemDetail}>{sug.detail}</span>
              <span className={s.itemMeta}>{sug.kind === 'ticker' ? (sug.meta ?? 'ticker') : sug.meta}</span>
            </li>
          ))}
          <li className={s.footer} aria-hidden="true">
            <span>
              <kbd>Tab</kbd> complete
            </span>
            <span>
              <kbd>↑</kbd>
              <kbd>↓</kbd> choose
            </span>
            <span>
              <kbd className="key-go">GO</kbd> run
            </span>
            <span>
              <kbd className="key-cancel">ESC</kbd> close
            </span>
          </li>
        </ul>
      )}
    </div>
  )
})
