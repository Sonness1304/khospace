export class ApiError extends Error { constructor(public status: number) { super('Request rejected'); } }
export const isDefiniteRejection = (error: unknown) => error instanceof ApiError && [400, 401, 403, 404, 405, 409, 412, 413, 415, 422, 428, 429].includes(error.status);
export async function api<T>(path: string, signal: AbortSignal, init: RequestInit = {}): Promise<T> {
  const response = await fetch(`/api/v1${path}`, { ...init, credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.any([signal, AbortSignal.timeout(7000)]) });
  if (!response.ok) throw new ApiError(response.status);
  return response.json() as Promise<T>;
}
export async function command<T>(path: string, body: unknown, key: string, signal: AbortSignal, method = 'POST', version?: string): Promise<T> {
  const csrf = await api<{ token: string }>('/auth/csrf', signal);
  if (typeof csrf.token !== 'string' || !csrf.token) throw Error('Security token unavailable');
  return api<T>(path, signal, { method, headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf.token,
    'Idempotency-Key': key, ...(version ? { 'If-Match': `"${version}"` } : {}) }, body: JSON.stringify(body) });
}
