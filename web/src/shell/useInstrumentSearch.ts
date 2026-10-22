import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'

export interface InstrumentHit {
  code: string
  name?: string
  sector?: string
}

function normalize(data: unknown): InstrumentHit[] {
  const list = Array.isArray(data)
    ? data
    : data && typeof data === 'object' && Array.isArray((data as { items?: unknown }).items)
      ? (data as { items: unknown[] }).items
      : []
  return list
    .filter((x): x is Record<string, unknown> => !!x && typeof x === 'object' && typeof (x as { code?: unknown }).code === 'string')
    .map((x) => ({
      code: String(x.code).toUpperCase(),
      name: typeof x.name === 'string' ? x.name : undefined,
      sector: typeof x.sector === 'string' ? x.sector : undefined,
    }))
}

/** Debounced GET /api/instruments?q=. Quietly returns [] if the endpoint is missing or down. */
export function useInstrumentSearch(q: string, enabled: boolean): InstrumentHit[] {
  const [debounced, setDebounced] = useState(q)
  useEffect(() => {
    const id = setTimeout(() => setDebounced(q), 120)
    return () => clearTimeout(id)
  }, [q])

  const term = debounced.trim()
  const { data } = useQuery({
    queryKey: ['instruments', 'search', term.toLowerCase()],
    queryFn: ({ signal }) => api<unknown>(`/instruments?q=${encodeURIComponent(term)}`, { signal }),
    enabled: enabled && term.length > 0,
    retry: false,
    staleTime: 5 * 60_000,
    placeholderData: (prev) => prev,
    select: normalize,
  })
  return enabled && term ? (data ?? []) : []
}
