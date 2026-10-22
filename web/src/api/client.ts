import { QueryClient } from '@tanstack/react-query'

export class ApiError extends Error {
  readonly status: number
  readonly body: unknown

  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

type ApiInit = Omit<RequestInit, 'body'> & { body?: unknown }

/** Fetch a JSON endpoint under /api. `path` may start with or without "/api". */
export async function api<T>(path: string, init: ApiInit = {}): Promise<T> {
  const url = path.startsWith('/api') ? path : `/api${path.startsWith('/') ? '' : '/'}${path}`
  const { body, headers, ...rest } = init
  const res = await fetch(url, {
    ...rest,
    headers: {
      Accept: 'application/json',
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...headers,
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  const text = await res.text()
  let data: unknown = undefined
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = text
    }
  }

  if (!res.ok) {
    const detail =
      data && typeof data === 'object' && 'title' in data
        ? String((data as { title: unknown }).title)
        : typeof data === 'string' && data.length < 200
          ? data
          : res.statusText
    throw new ApiError(res.status, detail || `HTTP ${res.status}`, data)
  }
  return data as T
}

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

/*
 * Pattern for screen tasks (keep hooks next to the screen that uses them):
 *
 *   export function useProfile(code: string) {
 *     return useQuery({
 *       queryKey: ['profile', code],
 *       queryFn: ({ signal }) => api<Profile>(`/stocks/${code}/profile`, { signal }),
 *     })
 *   }
 */
