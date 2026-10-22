import type { ButtonHTMLAttributes } from 'react'
import s from './Button.module.css'

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'default' | 'primary' | 'ghost'
  active?: boolean
}

export function Button({ variant = 'default', active, className, type = 'button', ...rest }: ButtonProps) {
  const cls = [s.button, variant !== 'default' && s[variant], active && s.active, className].filter(Boolean).join(' ')
  return <button type={type} className={cls} {...rest} />
}
