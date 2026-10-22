import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api/client'
import { load, save } from '../../lib/storage'

export interface Watchlist {
  id: number
  name: string
  sort: number
  items: { code: string; sort: number }[]
}

export const WATCHLISTS_KEY = ['watchlists'] as const
const ACTIVE_KEY = 'sumire.watchlist.active'

export function useWatchlists() {
  return useQuery({
    queryKey: WATCHLISTS_KEY,
    queryFn: ({ signal }) => api<Watchlist[]>('/watchlists', { signal }),
    staleTime: 60_000,
  })
}

export function loadActiveId(): number | null {
  return load<number | null>(ACTIVE_KEY, null)
}

export function saveActiveId(id: number) {
  save(ACTIVE_KEY, id)
  window.dispatchEvent(new CustomEvent('watchlist:active', { detail: { id } }))
}

/** The list the rail shows and `w` adds to: the remembered one, else the first. */
export function pickActive(lists: Watchlist[] | undefined, id: number | null): Watchlist | undefined {
  if (!lists?.length) return undefined
  return lists.find((l) => l.id === id) ?? lists[0]
}

/** All watchlist mutations; each one writes the returned list back into the cache. */
export function useWatchlistMutations() {
  const qc = useQueryClient()
  const put = (w: Watchlist) =>
    qc.setQueryData<Watchlist[]>(WATCHLISTS_KEY, (old) => (old ? old.map((l) => (l.id === w.id ? w : l)) : [w]))
  const refetch = () => qc.invalidateQueries({ queryKey: WATCHLISTS_KEY })

  return {
    create: useMutation({
      mutationFn: (name: string) => api<Watchlist>('/watchlists', { method: 'POST', body: { name } }),
      onSuccess: (w) => qc.setQueryData<Watchlist[]>(WATCHLISTS_KEY, (old) => [...(old ?? []), w]),
    }),
    rename: useMutation({
      mutationFn: ({ id, name }: { id: number; name: string }) =>
        api<Watchlist>(`/watchlists/${id}`, { method: 'PUT', body: { name } }),
      onSuccess: put,
    }),
    remove: useMutation({
      mutationFn: (id: number) => api<void>(`/watchlists/${id}`, { method: 'DELETE' }),
      onSuccess: refetch,
    }),
    addItem: useMutation({
      mutationFn: ({ id, code }: { id: number; code: string }) =>
        api<Watchlist>(`/watchlists/${id}/items`, { method: 'POST', body: { code: code.toUpperCase() } }),
      onSuccess: put,
    }),
    removeItem: useMutation({
      mutationFn: ({ id, code }: { id: number; code: string }) =>
        api<void>(`/watchlists/${id}/items/${encodeURIComponent(code)}`, { method: 'DELETE' }),
      onMutate: ({ id, code }) =>
        qc.setQueryData<Watchlist[]>(WATCHLISTS_KEY, (old) =>
          old?.map((l) => (l.id === id ? { ...l, items: l.items.filter((i) => i.code !== code) } : l)),
        ),
      onError: refetch,
    }),
    reorder: useMutation({
      mutationFn: ({ id, codes }: { id: number; codes: string[] }) =>
        api<Watchlist>(`/watchlists/${id}/items`, { method: 'PUT', body: { codes } }),
      onMutate: ({ id, codes }) =>
        qc.setQueryData<Watchlist[]>(WATCHLISTS_KEY, (old) =>
          old?.map((l) => (l.id === id ? { ...l, items: codes.map((code, sort) => ({ code, sort })) } : l)),
        ),
      onSuccess: put,
      onError: refetch,
    }),
  }
}
