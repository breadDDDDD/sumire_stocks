import { useEffect, useRef, useState } from 'react'
import { ColorType, createChart, CrosshairMode, type DeepPartial, type IChartApi, type ChartOptions } from 'lightweight-charts'

/** Hex copies of theme/tokens.css (the canvas can't read CSS variables). */
export const CHART_COLORS = {
  bg: '#100e0b',
  grid: '#2a2218',
  border: '#4a3a22',
  text: '#b37c00',
  amber: '#ffb000',
  crimson: '#c8102e',
  up: '#22c55e',
  down: '#f43f5e',
  upAlpha: 'rgba(34, 197, 94, 0.35)',
  downAlpha: 'rgba(244, 63, 94, 0.35)',
}

const BASE: DeepPartial<ChartOptions> = {
  autoSize: true, // ResizeObserver on the container
  layout: {
    background: { type: ColorType.Solid, color: CHART_COLORS.bg },
    textColor: CHART_COLORS.text,
    fontFamily: "'JetBrains Mono', 'IBM Plex Mono', ui-monospace, Consolas, monospace",
    fontSize: 11,
    panes: { separatorColor: CHART_COLORS.border, separatorHoverColor: CHART_COLORS.crimson },
  },
  grid: {
    vertLines: { color: CHART_COLORS.grid },
    horzLines: { color: CHART_COLORS.grid },
  },
  crosshair: {
    mode: CrosshairMode.Normal,
    vertLine: { color: CHART_COLORS.amber, labelBackgroundColor: CHART_COLORS.crimson, width: 1, style: 3 },
    horzLine: { color: CHART_COLORS.amber, labelBackgroundColor: CHART_COLORS.crimson, width: 1, style: 3 },
  },
  rightPriceScale: { borderColor: CHART_COLORS.border },
  timeScale: { borderColor: CHART_COLORS.border, rightOffset: 4 },
  localization: { locale: 'en-US' },
}

/**
 * Creates a lightweight-charts instance in the returned container ref with the Sumire theme,
 * sized to its container, and disposes it on unmount. `chart` is null until mounted.
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

  return { ref, chart }
}
