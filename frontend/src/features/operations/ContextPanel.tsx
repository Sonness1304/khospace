import { useCallback, useEffect, useRef, useState } from 'react';
import type { WorkspaceAccess } from '../auth/workspace';
import { api } from '../../api';

type Facility = { id: string; name: string; roles?: string[] };
type Page = { owner: string; items: Facility[]; page: number; total: number; after: string | null; previous: (string | null)[] };

export default function ContextPanel({ access, onChange }: { access: WorkspaceAccess | null; onChange: (value: WorkspaceAccess) => void }) {
  const [data, setData] = useState<Page | null>(null);
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState('');
  const lifecycle = useRef<AbortController | null>(null); const running = useRef(false);
  const owner = JSON.stringify([access?.userId, access?.organizationId, access?.operatorAdmin, access?.facilityRoles]);
  const current = data?.owner === owner ? data : null;
  const enabled = access !== null;
  const staff = !!access && (access.operatorAdmin || access.facilityRoles.length > 0 || !access.organizationId);

  const read = useCallback(async (page = 1, after: string | null = null, previous: (string | null)[] = []) => {
    const controller = lifecycle.current;
    if (!controller || controller.signal.aborted || running.current || !enabled) return;
    running.current = true; setBusy(true); setMessage('');
    try {
      const result = await api<{ items: Facility[]; total?: number; nextCursor?: string | null }>(staff ? `/facilities?page=${page}` : `/public/facilities${after ? `?after=${encodeURIComponent(after)}` : ''}`, controller.signal);
      if (!Array.isArray(result.items)) throw Error('Chưa xác minh cơ sở kho');
      if (!controller.signal.aborted) setData({ owner, items: result.items, page, total: result.total ?? page * 50 + (result.nextCursor ? 1 : 0), after: result.nextCursor ?? null, previous });
    } catch {
      if (!controller.signal.aborted) setMessage('Không tải được cơ sở có thể chọn. Làm mới quyền truy cập rồi thử lại.');
    } finally {
      if (lifecycle.current === controller) running.current = false;
      if (!controller.signal.aborted) setBusy(false);
    }
  }, [enabled, owner, staff]);

  useEffect(() => {
    const controller = new AbortController(); lifecycle.current = controller; running.current = false;
    // Load choices for the current identity; selection always requires the user's action.
    queueMicrotask(() => { if (!controller.signal.aborted && enabled) void read(); });
    return () => controller.abort();
  }, [enabled, read]);

  return <section className="workspace-panel context-panel" aria-label="Ngữ cảnh cơ sở kho" aria-busy={busy}><div className="session-content">
    <p>Chọn cơ sở kho để xem danh sách thuê và thao tác nghiệp vụ. Máy chủ kiểm tra quyền hiện tại cho từng thao tác.</p>
    <button disabled={!access || busy} onClick={() => void read()}>Tải danh sách cơ sở</button>
    {busy && <p role="status">Đang tải cơ sở kho…</p>}{message && <p role="status">{message}</p>}
    {current && <><label>Cơ sở đang làm việc<select value={access?.facilityId ?? ''} onChange={e => access && onChange({ ...access, facilityId: e.target.value, facilityRoles: current.items.find(x => x.id === e.target.value)?.roles ?? (e.target.value === access.facilityId ? access.facilityRoles : []) })}>
      <option value="">Chọn cơ sở kho</option>{current.items.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
      {current.items.length === 0 && <p>Chưa có cơ sở kho có thể chọn trong phạm vi hiện tại.</p>}
      <div className="session-actions"><button disabled={busy || current.page <= 1} onClick={() => void read(current.page - 1, current.previous[current.page - 3] ?? null, current.previous.slice(0, -1))}>Trang cơ sở trước</button>
        <button disabled={busy || current.page * 50 >= current.total} onClick={() => void read(current.page + 1, current.after, [...current.previous, current.after])}>Trang cơ sở sau</button></div></>}
  </div></section>;
}
