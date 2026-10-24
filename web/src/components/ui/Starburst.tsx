const POINTS =
  '30.0,1.0 33.4,17.4 43.5,6.6 40.6,19.4 56.0,15.0 41.6,26.9 56.0,30.0 43.5,33.6 54.2,44.0 37.8,37.8 45.0,56.0 33.9,44.5 30.0,57.0 26.6,42.6 15.5,55.1 21.5,38.5 7.5,43.0 16.5,33.6 2.0,30.0 15.5,26.1 4.0,15.0 20.8,20.8 16.5,6.6 26.9,18.4'

/** Irregular 24-point impact burst. Filled with currentColor unless `outline` is set. */
export function Starburst({ size = 64, className, outline }: { size?: number; className?: string; outline?: boolean }) {
  return (
    <svg className={className} width={size} height={size} viewBox="0 0 60 60" aria-hidden="true" focusable="false">
      <polygon
        points={POINTS}
        fill={outline ? 'none' : 'currentColor'}
        stroke={outline ? 'currentColor' : 'none'}
        strokeWidth={outline ? 1.5 : undefined}
        strokeLinejoin="miter"
      />
    </svg>
  )
}
