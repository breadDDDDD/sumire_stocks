/** Sumire app icon (same drawing as public/favicon.svg): crimson hair with ponytail, round glasses, amber hair tie. */
export function SumireMark({ size = 16, label }: { size?: number; label?: string }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 32 32"
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      focusable="false"
      style={{ display: 'block', flex: 'none' }}
    >
      <rect width="32" height="32" fill="#000" />
      <path d="M24 7c6 3 8 11 6 20-2-4-4-6-7-7z" fill="#8E0C22" />
      <circle cx="15" cy="15" r="12.5" fill="#C8102E" />
      <path d="M6 25V17c3-1 5-3 6-6 2 3 7 5 12 5v9c0 4-4 7-9 7s-9-3-9-7z" fill="#F2D3BE" />
      <rect x="25" y="5" width="4.5" height="3" fill="#FFA028" transform="rotate(40 27 6.5)" />
      <circle cx="10.5" cy="21" r="3.6" fill="none" stroke="#000" strokeWidth="2" />
      <circle cx="19.5" cy="21" r="3.6" fill="none" stroke="#000" strokeWidth="2" />
      <path d="M14 20.5h2" stroke="#000" strokeWidth="1.6" />
    </svg>
  )
}

export default SumireMark
