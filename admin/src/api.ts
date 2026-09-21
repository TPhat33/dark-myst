import type {
  AdminContentCurrent,
  AdminPublishResult,
  AdminRollbackResult,
  AdminValidateResult,
  AdminVersionEntry,
  ApiProblem,
  CharacterData,
  ContentDiff
} from './types'

// Overridable at build/dev time (`VITE_API_BASE_URL`) so the same build can be pointed at a local
// dev API, a CI-spawned one, or a Playwright-managed instance without a rebuild — see
// docs/11-admin-spec.md "รันจริง". Falls back to the conventional local dev port.
const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:5099'

export const ADMIN_TOKEN_HEADER = 'X-Admin-Token'

/** Thrown for any non-2xx response. Carries the parsed body (docs/10-backend-spec.md's error
 * shape: `{ error, message? }` or, for a failed content validation, `{ error, problems: [] }`) so
 * callers can show a designer the actual reason rather than a generic failure. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly problem: ApiProblem
  ) {
    super(problem.message ?? problem.error ?? `Request failed with status ${status}`)
  }
}

async function request<T>(
  path: string,
  options: { method?: string; token?: string; idempotencyKey?: string; body?: unknown } = {}
): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (options.token) {
    headers[ADMIN_TOKEN_HEADER] = options.token
  }
  if (options.idempotencyKey) {
    headers['Idempotency-Key'] = options.idempotencyKey
  }

  const response = await fetch(`${API_BASE}${path}`, {
    method: options.method ?? 'GET',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body)
  })

  const text = await response.text()
  const json = text.length > 0 ? JSON.parse(text) : {}

  if (!response.ok) {
    throw new ApiError(response.status, json as ApiProblem)
  }

  return json as T
}

/** Generates a fresh client-chosen key per mutating click, per the idempotency contract every
 * mutating endpoint requires (docs/10-backend-spec.md, docs/11-admin-spec.md). */
export function newIdempotencyKey(): string {
  return crypto.randomUUID()
}

export function bootstrapAdmin(name: string): Promise<{ adminId: string; name: string; accessToken: string }> {
  return request('/admin/bootstrap', { method: 'POST', body: { name } })
}

export function getCurrent(token: string): Promise<AdminContentCurrent> {
  return request('/admin/content/current', { token })
}

export function validateCharacters(token: string, characters: CharacterData[]): Promise<AdminValidateResult> {
  return request('/admin/content/validate', { method: 'POST', token, body: { characters, notes: null } })
}

export function publishCharacters(
  token: string,
  characters: CharacterData[],
  notes: string,
  idempotencyKey: string
): Promise<AdminPublishResult> {
  return request('/admin/content/publish', { method: 'POST', token, idempotencyKey, body: { characters, notes } })
}

export function rollbackTo(
  token: string,
  targetVersion: string,
  notes: string,
  idempotencyKey: string
): Promise<AdminRollbackResult> {
  return request('/admin/content/rollback', {
    method: 'POST',
    token,
    idempotencyKey,
    body: { targetVersion, notes }
  })
}

export function listVersions(token: string): Promise<AdminVersionEntry[]> {
  return request('/admin/content/versions', { token })
}

export function diffVersions(token: string, from: string, to: string): Promise<ContentDiff> {
  return request(`/admin/content/diff?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`, { token })
}
