import { afterEach, describe, expect, it, vi } from 'vitest';
import { readHealth } from './health';

afterEach(() => vi.unstubAllGlobals());

describe('Source validation', () => {
  it.each([null, [], {}, { status: 'live' }, { status: 'live', observedAt: 123 },
    { status: 'live', observedAt: 'invalid' }, { status: 'ready', observedAt: '2026-10-28T00:00:00Z' }])('does not promote malformed liveness to healthy: %j', async data => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => data }));
    expect(await readHealth('live', new AbortController().signal)).toEqual({ state: 'unavailable' });
  });
  it('handles a truncated JSON response safely', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => { throw new SyntaxError('truncated'); } }));
    expect(await readHealth('ready', new AbortController().signal)).toEqual({ state: 'unavailable' });
  });
});
