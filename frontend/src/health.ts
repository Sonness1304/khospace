export type Observation = { state: 'checking' | 'available' | 'unavailable'; observedAt?: string };

export async function readHealth(kind: 'live' | 'ready', signal: AbortSignal): Promise<Observation> {
  try {
    const response = await fetch(`/health/${kind}`, { method: 'GET', cache: 'no-store', signal });
    const data: unknown = await response.json();
    if (!response.ok || !data || typeof data !== 'object' || !('status' in data) || !('observedAt' in data)
      || data.status !== (kind === 'live' ? 'live' : 'ready') || typeof data.observedAt !== 'string'
      || !Number.isFinite(Date.parse(data.observedAt))) return { state: 'unavailable' };
    return { state: 'available', observedAt: data.observedAt };
  } catch {
    return { state: 'unavailable' };
  }
}
