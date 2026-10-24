import { Fragment } from 'react'
import { FUNCTION_META } from '../types'
import { SHORTCUTS } from './shortcuts'
import s from './CheatSheet.module.css'

/** Bloomberg key roles: Enter is the green GO key, Esc the red cancel key. */
function Key({ k }: { k: string }) {
  if (k === 'Enter')
    return (
      <kbd className="key-go" title="Enter">
        GO
      </kbd>
    )
  if (k === 'Esc') return <kbd className="key-cancel">Esc</kbd>
  return <kbd>{k}</kbd>
}

/** Functions and keys. `onRun` runs a function's example command. */
export function CheatSheet({ onRun }: { onRun?: (command: string) => void }) {
  return (
    <div style={{ containerType: 'inline-size' }}>
      <div className={s.sheet}>
        <section>
          <h3 className={s.heading}>Functions</h3>
          <p className={s.syntax}>
            Type <code>[TICKER] [FUNCTION]</code> then <kbd className="key-go">GO</kbd>. A bare ticker opens <code>DES</code>; a ticker function
            typed alone uses the focused pane's ticker.
          </p>
          <ul className={s.fnList}>
            {Object.values(FUNCTION_META).map((f) => (
              <li key={f.code}>
                <button
                  type="button"
                  className={s.fn}
                  onClick={() => onRun?.(f.example)}
                  title={onRun ? `Run ${f.example}` : undefined}
                >
                  <kbd className={`key-fn ${s.code}`}>{f.code}</kbd>
                  <span className={s.desc}>{f.description}</span>
                  <span className={s.example}>{f.example}</span>
                </button>
              </li>
            ))}
          </ul>
        </section>
        <section>
          <h3 className={s.heading}>Keys</h3>
          {SHORTCUTS.map((g) => (
            <div key={g.group} className={s.group}>
              <h4 className={s.groupName}>{g.group}</h4>
              {g.items.map((it) => (
                <div key={it.action} className={s.key}>
                  <span>{it.action}</span>
                  <span className={s.combos}>
                    {it.keys.map((combo, i) => (
                      <Fragment key={i}>
                        {i > 0 && '/'}
                        <span>
                          {combo.map((k, j) => (
                            <Fragment key={k}>
                              {j > 0 && '+'}
                              <Key k={k} />
                            </Fragment>
                          ))}
                        </span>
                      </Fragment>
                    ))}
                  </span>
                </div>
              ))}
            </div>
          ))}
        </section>
      </div>
    </div>
  )
}
