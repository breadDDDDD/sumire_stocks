import { useCallback, useEffect, useRef, useState } from 'react'
import {
  ColorType,
  createChart,
  CrosshairMode,
  LineStyle,
  type DeepPartial,
  type IChartApi,
  type ChartOptions,
} from 'lightweight-charts'
import { revealChart } from './reveal'

/** Hex copies of theme/tokens.css (the canvas can't read CSS variables). */
export const CHART_COLORS = {
  bgTop: '#10151c',
  bgBottom: '#07090c',
  bg: '#07090c', // shutter overlay and fallback fill
  grid: '#1c232c',
  border: '#222222',
  text: '#8c8c8c',
  amber: '#ffa028',
  line: '#f2f2f2', // single-series line
  cyan: '#4fa3e0',
  crimson: '#c8102e',
  label: '#242424', // crosshair label background
  up: '#22c55e',
  down: '#f43f5e',
  upAlpha: 'rgba(34, 197, 94, 0.35)',
  downAlpha: 'rgba(244, 63, 94, 0.35)',
  cone: 'rgba(255, 160, 40, 0.10)',
}

const BASE: DeepPartial<ChartOptions> = {
  autoSize: true, // ResizeObserver on the container
  layout: {
    background: { type: ColorType.VerticalGradient, topColor: CHART_COLORS.bgTop, bottomColor: CHART_COLORS.bgBottom },
    textColor: CHART_COLORS.text,
    fontFamily: "'JetBrains Mono', 'IBM Plex Mono', ui-monospace, Consolas, monospace",
    fontSize: 11,
    attributionLogo: false,
    panes: { separatorColor: CHART_COLORS.border, separatorHoverColor: CHART_COLORS.crimson },
  },
  grid: {
    vertLines: { color: CHART_COLORS.grid, style: LineStyle.Dotted },
    horzLines: { color: CHART_COLORS.grid, style: LineStyle.Dotted },
  },
  crosshair: {
    mode: CrosshairMode.Normal,
    vertLine: { color: CHART_COLORS.text, labelBackgroundColor: CHART_COLORS.label, width: 1, style: LineStyle.Dashed },
    horzLine: { color: CHART_COLORS.text, labelBackgroundColor: CHART_COLORS.label, width: 1, style: LineStyle.Dashed },
  },
  rightPriceScale: { borderColor: CHART_COLORS.border },
  timeScale: { borderColor: CHART_COLORS.border, rightOffset: 4 },
  localization: { locale: 'en-US' },
}

/**
 * Creates a lightweight-charts instance in the returned container ref with the Sumire theme,
 * sized to its container, and disposes it on unmount. `chart` is null until mounted.
 * `reveal()` plays the one-time draw-in shutter; call it after the first setData, not on live updates.
 */
export function useChart(options?: DeepPartial<ChartOptions>) {
  const ref = useRef<HTMLDivElement>(null)
  const [chart, setChart] = useState<IChartApi | null>(null)
  const initial = useRef(options)

  useEffect(() => {
    if (!ref.current) return
    const c = createChart(ref.current, BASE)
    if (initial.current) c.applyOptions(initial.current)
    setChart(c)
    return () => {
      setChart(null)
      c.remove()
    }
  }, [])

  const reveal = useCallback(() => {
    if (!ref.current || !chart) return
    let inset = 0
    try {
      inset = chart.priceScale('right').width()
    } catch {
      inset = 0
    }
    revealChart(ref.current, inset)
  }, [chart])

  return { ref, chart, reveal }
}
