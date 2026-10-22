/** Sumire mascot slot (bottom-right). Stub: the mascot task adds portraits, expressions and the speech bubble. */
export default function Mascot({ hidden }: { hidden: boolean }) {
  if (hidden) return null
  return (
    <div
      title="Sumire (press m to hide)"
      style={{
        display: 'grid',
        placeItems: 'center',
        width: 44,
        height: 44,
        margin: '0 8px',
        borderRadius: '50%',
        background: 'var(--crimson)',
        color: '#fff',
        fontFamily: 'var(--font-display)',
        fontSize: 20,
        boxShadow: '0 0 0 2px var(--bg), 0 0 0 3px var(--crimson)',
      }}
    >
      S
    </div>
  )
}
