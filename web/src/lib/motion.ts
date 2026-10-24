/** True when the user asked the OS for reduced motion; JS-driven effects (tween, FLIP, shutter, boot) check this. */
export function reducedMotion(): boolean {
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true
}
