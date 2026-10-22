import { lazy, type ComponentType, type LazyExoticComponent } from 'react'
import { FUNCTION_META, type FunctionCode, type FunctionMeta, type FunctionProps } from './types'

export type { FunctionCode, FunctionMeta, FunctionProps } from './types'

type Loader = () => Promise<{ default: ComponentType<FunctionProps> }>

// one lazily loaded file per function code: web/src/functions/<CODE>.tsx
const loaders: Record<FunctionCode, Loader> = {
  MKT: () => import('./MKT'),
  BRIEF: () => import('./BRIEF'),
  DES: () => import('./DES'),
  GP: () => import('./GP'),
  FA: () => import('./FA'),
  FCST: () => import('./FCST'),
  INS: () => import('./INS'),
  REL: () => import('./REL'),
  ASK: () => import('./ASK'),
  RQ: () => import('./RQ'),
  N: () => import('./N'),
  W: () => import('./W'),
  EQS: () => import('./EQS'),
  SECT: () => import('./SECT'),
  COMP: () => import('./COMP'),
  ALRT: () => import('./ALRT'),
  HELP: () => import('./HELP'),
}

export interface RegistryEntry extends FunctionMeta {
  component: LazyExoticComponent<ComponentType<FunctionProps>>
}

export const registry = Object.fromEntries(
  (Object.keys(loaders) as FunctionCode[]).map((code) => [
    code,
    { ...FUNCTION_META[code], component: lazy(loaders[code]) },
  ]),
) as Record<FunctionCode, RegistryEntry>

export const functionList: FunctionMeta[] = Object.values(FUNCTION_META)
