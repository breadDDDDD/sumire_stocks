export interface Shortcut {
  keys: string[][]
  action: string
}

/** Keyboard shortcuts per plan §7.3. Each inner array is one key combo; alternatives are separate arrays. */
export const SHORTCUTS: { group: string; items: Shortcut[] }[] = [
  {
    group: 'Command line',
    items: [
      { keys: [['/'], ['Ctrl', 'K']], action: 'Focus the command line (or just start typing)' },
      { keys: [['Tab']], action: 'Accept suggestion' },
      { keys: [['↑'], ['↓']], action: 'Move through suggestions / recent commands' },
      { keys: [['Esc']], action: 'Close suggestions, clear, leave' },
    ],
  },
  {
    group: 'Panes',
    items: [
      { keys: [['Alt', '1…4']], action: 'Focus a pane' },
      { keys: [['Tab']], action: 'Cycle panes' },
      { keys: [['Alt', 'L']], action: 'Layout: 1 / 2 / 4 panes' },
      { keys: [['Alt', '←'], ['Alt', '→']], action: 'Pane history back / forward' },
      { keys: [['Esc']], action: 'Close overlay, or go back' },
    ],
  },
  {
    group: 'Lists and tables',
    items: [
      { keys: [['↑'], ['↓'], ['j'], ['k']], action: 'Move through rows' },
      { keys: [['Enter']], action: 'Open the ticker' },
    ],
  },
  {
    group: 'Chart (GP)',
    items: [
      { keys: [['1–6']], action: 'Timeframes' },
      { keys: [['i']], action: 'Indicator menu' },
      { keys: [['f']], action: 'Forecast cone' },
      { keys: [['v']], action: 'Volume' },
    ],
  },
  {
    group: 'Current ticker',
    items: [
      { keys: [['n']], action: 'Open INS insight' },
      { keys: [['r']], action: 'Open REL relations map' },
      { keys: [['w']], action: 'Add to a watchlist' },
    ],
  },
  {
    group: 'General',
    items: [
      { keys: [['m']], action: 'Hide / show Sumire' },
      { keys: [['?']], action: 'This cheat sheet' },
    ],
  },
]
