import type { FunctionCode } from '../functions/types'

/** Related functions shown in a ticker pane's footer, in the order they are listed. */
export const RELATED: Partial<Record<FunctionCode, FunctionCode[]>> = {
  DES: ['GP', 'FA', 'FCST', 'INS', 'N'],
  GP: ['DES', 'FA', 'FCST', 'INS'],
  FA: ['DES', 'GP', 'FCST', 'REL'],
  FCST: ['GP', 'INS', 'FA', 'DES'],
  INS: ['DES', 'GP', 'REL', 'N'],
  REL: ['INS', 'DES', 'GP', 'N'],
  N: ['DES', 'GP', 'INS'],
}
