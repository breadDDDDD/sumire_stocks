import { Fragment } from 'react'
import { FUNCTION_META } from '../types'
import { SHORTCUTS } from './shortcuts'
import s from './CheatSheet.module.css'

/** Functions and keys. `onRun` runs a function's example command. */
export function CheatSheet({ onRun }: { onRun?: (command: string) => void }) {
  return (
    <div style={{ containerType: 'inline-size' }}>
      <div className={s.sheet}>
        <section>
          <h3 className={s.heading}>Functions</h3>
          <p className={s.syntax}>
            Type <code>[TICKER] [FUNCTION]</code> then Enter. A bare ticker opens <code>DES</code>; a ticker function
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
                  <span className={s.code}>{f.code}</span>
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
                              <kbd>{k}</kbd>
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
