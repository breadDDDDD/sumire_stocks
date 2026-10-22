import { useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react'
import {
  CandlestickSeries,
  HistogramSeries,
  LineSeries,
  LineStyle,
  type IChartApi,
  type ISeriesApi,
  type MouseEventParams,
  type SeriesType,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts'
import { useQuotes } from '../api/signalr'
import { CHART_COLORS, useChart } from '../components/chart/useChart'
import { Badge, Button, Empty, ErrorState, FreshnessBadge, formatNum, Num, Panel } from '../components/ui'
import { load, save } from '../lib/storage'
import { quotePrice, useTechnicals, type LiveQuote, type Technicals } from './des/api'
import { modelStyle, useForecast, type Forecast } from './fcst/api'
import {
  addTradingDays,
  DEFAULT_PREFS,
  dayTime,
  INDICATORS,
  PREFS_KEY,
  RANGES,
  SERIES_PARAM,
  useChartBars,
  type Bar,
  type GpPrefs,
  type IndicatorId,
} from './gp/api'
import s from './gp/GP.module.css'
import type { FunctionProps } from './types'

const WIB_OFFSET = 7 * 3600
const t = (n: number) => n as UTCTimestamp

interface Built {
  candle: ISeriesApi<'Candlestick'>
  volume: ISeriesApi<'Histogram'> | null
  all: ISeriesApi<SeriesType>[]
}

export default function GP({ ticker }: FunctionProps) {
  const code = (ticker ?? '').toUpperCase()
  const [prefs, setPrefs] = useState<GpPrefs>(() => ({ ...DEFAULT_PREFS, ...load<Partial<GpPrefs>>(PREFS_KEY, {}) }))
  const [menuOpen, setMenuOpen] = useState(false)
  const range = RANGES.find((r) => r.range === prefs.range) ?? RANGES[3]
  const daily = !range.intraday
  const wantsSeries = daily && prefs.indicators.length > 0

  const bars = useChartBars(code, range.range, range.intraday)
  const tech = useTechnicals(code, SERIES_PARAM, wantsSeries)
  const fc = useForecast(code, daily && prefs.forecast)
  const live = useQuotes(code ? [code] : [])[code] as LiveQuote | undefined

  const { ref: chartRef, chart } = useChart()
  const wrapRef = useRef<HTMLDivElement>(null)
  const built = useRef<Built | null>(null)
  const lastBar = useRef<Bar | null>(null)
  const byTime = useRef(new Map<number, { bar: Bar; prevClose: number | null }>())
  const fitted = useRef('')
  const [, setLiveTick] = useState(0) // re-render the legend after ref updates
  const [hover, setHover] = useState<{ bar: Bar; prevClose: number | null } | null>(null)

  useEffect(() => save(PREFS_KEY, prefs), [prefs])

  const update = (p: Partial<GpPrefs>) => setPrefs((prev) => ({ ...prev, ...p }))
  const toggleIndicator = (id: IndicatorId) =>
    setPrefs((prev) => ({
      ...prev,
      indicators: prev.indicators.includes(id) ? prev.indicators.filter((x) => x !== id) : [...prev.indicators, id],
    }))

  const data = bars.data?.bars
  const seriesData = wantsSeries ? tech.data?.series : undefined
  const forecast = daily && prefs.forecast ? fc.data : undefined

  // (re)build every series when data or toggles change
  useEffect(() => {
    if (!chart || !data?.length) return
    // refit when the range or the x-extent (cone) changes; plain refreshes keep the user's zoom
    const key = `${code}|${range.range}|${!!forecast}|${!!seriesData}`
    const keepRange = fitted.current === key ? chart.timeScale().getVisibleLogicalRange() : null

    if (built.current) for (const ser of built.current.all) chart.removeSeries(ser)
    while (chart.panes().length > 1) chart.removePane(chart.panes().length - 1)

    built.current = buildSeries(chart, data, {
      volume: prefs.volume,
      indicators: daily ? prefs.indicators : [],
      series: seriesData,
      forecast,
    })
    chart.applyOptions({ timeScale: { timeVisible: !daily, secondsVisible: false } })

    const map = new Map<number, { bar: Bar; prevClose: number | null }>()
    data.forEach((b, i) => map.set(b.time, { bar: b, prevClose: i ? data[i - 1].close : (bars.data?.prevClose ?? null) }))
    byTime.current = map
    lastBar.current = data[data.length - 1]

    if (keepRange) chart.timeScale().setVisibleLogicalRange(keepRange)
    else chart.timeScale().fitContent()
    fitted.current = key
    setLiveTick((n) => n + 1)
  }, [chart, data, seriesData, forecast, prefs.volume, prefs.indicators, daily, code, range.range, bars.data?.prevClose])

  // crosshair legend
  useEffect(() => {
    if (!chart) return
    const onMove = (p: MouseEventParams<Time>) => {
      setHover(p.time == null ? null : (byTime.current.get(p.time as number) ?? null))
    }
    chart.subscribeCrosshairMove(onMove)
    return () => chart.unsubscribeCrosshairMove(onMove)
  }, [chart])

  // live last bar from the quote stream
  useEffect(() => {
    const price = quotePrice(live)
    const b = built.current
    const last = lastBar.current
    if (!live?.time || price == null || !b || !last) return
    const qTime = live.time
    let next: Bar | null = null
    if (daily) {
      const time = dayTime(qTime.slice(0, 10))
      if (time === last.time) {
        next = { ...last, high: Math.max(last.high, live.high ?? price), low: Math.min(last.low, live.low ?? price), close: price, volume: live.volume || last.volume }
      } else if (time > last.time) {
        next = { time, date: qTime.slice(0, 10), open: live.open ?? price, high: live.high ?? price, low: live.low ?? price, close: price, volume: live.volume ?? 0 }
      }
    } else {
      const qt = Math.floor(Date.parse(qTime) / 1000) + WIB_OFFSET
      const prev = data && data.length > 1 ? data[data.length - 2] : null
      const step = prev ? last.time - prev.time : 300
      if (qt >= last.time && qt < last.time + step) {
        next = { ...last, high: Math.max(last.high, price), low: Math.min(last.low, price), close: price }
      } else if (qt >= last.time + step && qt - last.time < 6 * 3600) {
        const time = last.time + step * Math.floor((qt - last.time) / step)
        next = { time, date: null, open: last.close, high: Math.max(last.close, price), low: Math.min(last.close, price), close: price, volume: 0 }
      }
    }
    if (!next) return
    b.candle.update({ time: t(next.time), open: next.open, high: next.high, low: next.low, close: next.close })
    if (b.volume && daily) b.volume.update(volumePoint(next))
    const prevClose = next.time === last.time ? (byTime.current.get(last.time)?.prevClose ?? null) : last.close
    byTime.current.set(next.time, { bar: next, prevClose })
    lastBar.current = next
    setLiveTick((n) => n + 1)
  }, [live, daily, data])

  const legend = hover ?? (lastBar.current ? byTime.current.get(lastBar.current.time) ?? null : null)

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.altKey || e.ctrlKey || e.metaKey || e.target !== e.currentTarget) return
    const r = RANGES.find((x) => x.key === e.key)
    if (r) update({ range: r.range })
    else if (e.key === 'i') setMenuOpen(true)
    else if (e.key === 'f') update({ forecast: !prefs.forecast })
    else if (e.key === 'v') update({ volume: !prefs.volume })
    else return
    e.preventDefault()
  }

  const closeMenu = () => {
    setMenuOpen(false)
    wrapRef.current?.focus()
  }

  const overlayNote = range.intraday && (prefs.indicators.length > 0 || prefs.forecast)

  return (
    <Panel
      title={`${code} GP`}
      subtitle={`${range.label} · ${bars.data?.interval ?? ''}`}
      actions={live?.time ? <FreshnessBadge asOf={live.time} /> : bars.data ? <Badge tone="muted">{bars.data.source}</Badge> : null}
      bodyClassName={s.panelBody}
    >
      <div className={s.toolbar}>
        <div className={s.group} role="group" aria-label="Range">
          {RANGES.map((r) => (
            <Button key={r.key} variant="ghost" active={r.range === prefs.range} onClick={() => update({ range: r.range })} title={`${r.label} (${r.key})`}>
              {r.label}
            </Button>
          ))}
        </div>
        <div className={s.group}>
          <Button variant="ghost" active={menuOpen} onClick={() => setMenuOpen((v) => !v)} title="Indicators (i)" aria-expanded={menuOpen}>
            Ind {prefs.indicators.length ? `· ${prefs.indicators.length}` : ''}
          </Button>
          <Button variant="ghost" active={prefs.forecast} onClick={() => update({ forecast: !prefs.forecast })} title="Forecast cone (f)">
            Fcst
          </Button>
          <Button variant="ghost" active={prefs.volume} onClick={() => update({ volume: !prefs.volume })} title="Volume (v)">
            Vol
          </Button>
        </div>
        {menuOpen && <IndicatorMenu active={prefs.indicators} onToggle={toggleIndicator} onClose={closeMenu} />}
      </div>

      <div
        ref={wrapRef}
        className={s.chartWrap}
        tabIndex={0}
        onKeyDown={onKeyDown}
        aria-label={`${code} price chart. Keys: 1 to 6 range, i indicators, f forecast, v volume`}
      >
        <div ref={chartRef} className={s.chart} />
        <div className={s.legends}>
          {legend && <Legend bar={legend.bar} prevClose={legend.prevClose} daily={daily} />}
          <SeriesLegend prefs={prefs} daily={daily} forecast={forecast} />
        </div>
        {overlayNote && <span className={s.note}>Indicators and forecast show on 1M and longer</span>}
        {daily && prefs.forecast && fc.isError && <span className={s.note}>Forecast unavailable</span>}
        {bars.isPending && code && <div className={s.overlay}><span className={s.loading}>Loading {range.label}…</span></div>}
        {bars.isError && (
          <div className={s.overlay}>
            <ErrorState error={bars.error} onRetry={() => bars.refetch()} />
          </div>
        )}
        {(!code || (bars.isSuccess && !data?.length)) && (
          <div className={s.overlay}>
            <Empty title={code ? 'No price data' : 'No ticker'} message={code ? `Nothing for ${range.label}. Try another range (1-6).` : 'Try BBRI GP.'} />
          </div>
        )}
      </div>
    </Panel>
  )
}

function volumePoint(b: Bar) {
  return { time: t(b.time), value: b.volume, color: b.close >= b.open ? CHART_COLORS.upAlpha : CHART_COLORS.downAlpha }
}

function buildSeries(
  chart: IChartApi,
  bars: Bar[],
  o: { volume: boolean; indicators: IndicatorId[]; series?: Technicals['series']; forecast?: Forecast },
): Built {
  const all: ISeriesApi<SeriesType>[] = []
  const line = (color: string, pane = 0, width: 1 | 2 = 1, style: LineStyle = LineStyle.Solid) => {
    const ser = chart.addSeries(LineSeries, { color, lineWidth: width, lineStyle: style, priceLineVisible: false, lastValueVisible: false, crosshairMarkerVisible: false }, pane)
    all.push(ser)
    return ser
  }

  const candle = chart.addSeries(CandlestickSeries, {
    upColor: CHART_COLORS.up,
    downColor: CHART_COLORS.down,
    borderUpColor: CHART_COLORS.up,
    borderDownColor: CHART_COLORS.down,
    wickUpColor: CHART_COLORS.up,
    wickDownColor: CHART_COLORS.down,
    priceLineColor: CHART_COLORS.amber,
  })
  all.push(candle)
  candle.setData(bars.map((b) => ({ time: t(b.time), open: b.open, high: b.high, low: b.low, close: b.close })))
  candle.priceScale().applyOptions({ scaleMargins: { top: 0.08, bottom: o.volume ? 0.22 : 0.06 } })

  let volume: ISeriesApi<'Histogram'> | null = null
  if (o.volume) {
    volume = chart.addSeries(HistogramSeries, { priceScaleId: 'vol', priceFormat: { type: 'volume' }, priceLineVisible: false, lastValueVisible: false })
    volume.priceScale().applyOptions({ scaleMargins: { top: 0.82, bottom: 0 } })
    volume.setData(bars.map(volumePoint))
    all.push(volume)
  }

  const sd = o.series
  if (sd?.dates) {
    const idx = new Map(sd.dates.map((d, i) => [d, i]))
    const points = (key: string) => {
      const arr = sd[key] as (number | null)[] | undefined
      if (!arr) return []
      return bars.flatMap((b) => {
        const i = b.date ? idx.get(b.date) : undefined
        const v = i == null ? null : arr[i]
        return v == null ? [] : [{ time: t(b.time), value: v }]
      })
    }
    const color = (id: IndicatorId) => INDICATORS.find((x) => x.id === id)!.color
    for (const id of ['sma20', 'sma50', 'sma200'] as const) {
      if (o.indicators.includes(id)) line(color(id)).setData(points(id))
    }
    if (o.indicators.includes('bb')) {
      line(color('bb'), 0, 1, LineStyle.Dashed).setData(points('bbUpper'))
      line(color('bb'), 0, 1, LineStyle.Dashed).setData(points('bbLower'))
    }
    let pane = 1
    if (o.indicators.includes('rsi')) {
      const rsi = line(color('rsi'), pane)
      rsi.applyOptions({ lastValueVisible: true })
      rsi.setData(points('rsi'))
      for (const level of [70, 30]) {
        rsi.createPriceLine({ price: level, color: level > 50 ? CHART_COLORS.down : CHART_COLORS.up, lineStyle: LineStyle.Dashed, lineWidth: 1, axisLabelVisible: false, title: '' })
      }
      pane++
    }
    if (o.indicators.includes('macd')) {
      const h = chart.addSeries(HistogramSeries, { priceLineVisible: false, lastValueVisible: true }, pane)
      h.setData(points('macdHist').map((p) => ({ ...p, color: p.value >= 0 ? CHART_COLORS.upAlpha : CHART_COLORS.downAlpha })))
      all.push(h)
      pane++
    }
    const panes = chart.panes()
    panes.forEach((p, i) => p.setStretchFactor(i === 0 ? 4 : 1))
  }

  const f = o.forecast
  if (f) {
    for (const m of f.models) {
      const { color } = modelStyle(m.model)
      const hs = [...m.horizons].sort((a, b) => a.horizon - b.horizon)
      // one point per trading day (log-linear between horizons) so the fan spans real time on the index-based axis
      const fan = (k: 'p10' | 'p50' | 'p90') => {
        const anchors = [{ h: 0, v: f.last }, ...hs.map((h) => ({ h: h.horizon, v: h[k] }))]
        const pts = []
        for (let d = 0; d <= anchors[anchors.length - 1].h; d++) {
          const j = anchors.findIndex((a) => a.h >= d)
          const a = anchors[Math.max(0, j - 1)]
          const b = anchors[j]
          const w = b.h === a.h ? 1 : (d - a.h) / (b.h - a.h)
          pts.push({ time: t(dayTime(addTradingDays(m.asOf, d))), value: Math.exp(Math.log(a.v) * (1 - w) + Math.log(b.v) * w) })
        }
        return pts
      }
      line(color, 0, 2).setData(fan('p50'))
      line(color, 0, 1, LineStyle.Dashed).setData(fan('p90'))
      line(color, 0, 1, LineStyle.Dashed).setData(fan('p10'))
    }
  }

  return { candle, volume, all }
}

function Legend({ bar, prevClose, daily }: { bar: Bar; prevClose: number | null; daily: boolean }) {
  const chg = prevClose ? bar.close - prevClose : null
  const chgPct = prevClose ? (chg! / prevClose) * 100 : null
  const when = new Date(bar.time * 1000).toISOString()
  return (
    <div className={s.legend}>
      <span className="muted">{daily ? when.slice(0, 10) : `${when.slice(0, 10)} ${when.slice(11, 16)}`}</span>
      <span>
        <span className={s.k}>O</span> <Num value={bar.open} format="price" />
      </span>
      <span>
        <span className={s.k}>H</span> <Num value={bar.high} format="price" />
      </span>
      <span>
        <span className={s.k}>L</span> <Num value={bar.low} format="price" />
      </span>
      <span>
        <span className={s.k}>C</span> <Num value={bar.close} format="price" colorBy={chg ?? 0} />
      </span>
      {chgPct != null && <Num value={chgPct} format="pct" signed colored />}
      <span>
        <span className={s.k}>V</span> {formatNum(bar.volume, 'compact')}
      </span>
    </div>
  )
}

function SeriesLegend({ prefs, daily, forecast }: { prefs: GpPrefs; daily: boolean; forecast?: Forecast }) {
  const items = useMemo(() => {
    if (!daily) return []
    const out = INDICATORS.filter((i) => prefs.indicators.includes(i.id) && i.id !== 'rsi' && i.id !== 'macd').map((i): { label: string; color: string; dashed: boolean } => ({
      label: i.label,
      color: i.color,
      dashed: i.id === 'bb',
    }))
    for (const m of forecast?.models ?? []) {
      const st = modelStyle(m.model)
      out.push({ label: `${st.name} P50 · P10–P90`, color: st.color, dashed: false })
    }
    return out
  }, [prefs.indicators, daily, forecast])
  if (!items.length) return null
  return (
    <div className={s.seriesLegend}>
      {items.map((i) => (
        <span key={i.label}>
          <span className={s.swatch} style={{ borderTopColor: i.color, borderTopStyle: i.dashed ? 'dashed' : 'solid' }} />
          {i.label}
        </span>
      ))}
    </div>
  )
}

function IndicatorMenu({
  active,
  onToggle,
  onClose,
}: {
  active: IndicatorId[]
  onToggle: (id: IndicatorId) => void
  onClose: () => void
}) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    ref.current?.querySelector<HTMLButtonElement>('button')?.focus()
  }, [])

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const buttons = [...(ref.current?.querySelectorAll<HTMLButtonElement>('button') ?? [])]
    const i = buttons.indexOf(document.activeElement as HTMLButtonElement)
    if (e.key === 'Escape' || e.key === 'i') {
      e.preventDefault()
      e.stopPropagation()
      onClose()
    } else if (e.key === 'ArrowDown' || e.key === 'j') {
      e.preventDefault()
      buttons[(i + 1) % buttons.length]?.focus()
    } else if (e.key === 'ArrowUp' || e.key === 'k') {
      e.preventDefault()
      buttons[(i - 1 + buttons.length) % buttons.length]?.focus()
    }
  }

  return (
    <div ref={ref} className={s.menu} role="menu" aria-label="Indicators" onKeyDown={onKeyDown}>
      {INDICATORS.map((ind) => {
        const on = active.includes(ind.id)
        return (
          <button key={ind.id} type="button" role="menuitemcheckbox" aria-checked={on} className={s.menuItem} onClick={() => onToggle(ind.id)}>
            <span className={s.check} style={{ background: on ? ind.color : 'transparent', borderColor: ind.color }} />
            {ind.label}
          </button>
        )
      })}
      <span className={s.menuHint}>Enter toggles · Esc closes</span>
    </div>
  )
}
