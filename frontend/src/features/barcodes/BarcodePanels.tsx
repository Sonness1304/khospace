import { displayValue } from '../../locale/vi';
import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react';
import { api, command, isDefiniteRejection } from '../../api';
import type { WorkspaceAccess } from '../auth/workspace';

export type BarcodeSelection = { leaseId: string; organizationId: string; facilityId: string; itemId: string; sku: string; baseUom: string; selectionOnly: boolean };
const context = (access: WorkspaceAccess | null) => JSON.stringify([access?.userId, access?.organizationId, access?.facilityId, access?.organizationRole, access?.facilityRoles, access?.operatorAdmin]);

export function BarcodeLookupPanel({ access, leaseId, onUse, disabled = false }: { access: WorkspaceAccess | null; leaseId?: string; onUse?: (item: BarcodeSelection) => void; disabled?: boolean }) {
  const [localLease, setLocalLease] = useState(''); const [value, setValue] = useState(''); const [kind, setKind] = useState('GTIN');
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState('');
  const [found, setFound] = useState<{ binding: string; item: BarcodeSelection } | null>(null);
  const targetLease = leaseId ?? localLease; const binding = JSON.stringify([context(access), targetLease, kind, value]);
  const current = useRef(binding); useLayoutEffect(() => { current.current = binding; }, [binding]);
  const lifecycle = useRef<AbortController | null>(null); const running = useRef(false);
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  const item = found?.binding === binding ? found.item : null;
  async function lookup(event: FormEvent) {
    event.preventDefault(); const controller = lifecycle.current; if (!controller || running.current || !access || disabled || !targetLease || !value) return;
    running.current = true; setBusy(true); setFound(null); setMessage(''); const original = binding;
    try {
      const result = await api<BarcodeSelection>(`/leases/${encodeURIComponent(targetLease)}/barcode-lookup?kind=${kind}&value=${encodeURIComponent(value)}`, controller.signal);
      if (result.leaseId !== targetLease || !result.itemId || !result.sku || !result.baseUom || result.selectionOnly !== true ||
        (access.organizationId ? result.organizationId !== access.organizationId : !result.organizationId) ||
        (!access.organizationId && !access.operatorAdmin && result.facilityId !== access.facilityId)) throw Error('Chưa xác minh ngữ cảnh mã vạch');
      if (!controller.signal.aborted && current.current === original) setFound({ binding: original, item: result });
    } catch { if (!controller.signal.aborted && current.current === original) setMessage('Không thể xác minh mã vạch. Kiểm tra hợp đồng thuê, loại mã và quyền truy cập hiện tại.'); }
    finally { running.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  return <div className="booking-detail" aria-busy={busy}><h3>Tìm mặt hàng bằng mã vạch</h3>
    <p className="catalog-help">Chọn rõ GTIN hoặc mã nội bộ. Phím Enter trên máy quét chỉ tra cứu mặt hàng. Số lượng, phê duyệt của khách hàng và ghi nhận xuất nhập thực tế là các bước riêng.</p>
    <form onSubmit={e => void lookup(e)}>
      {leaseId === undefined && <label>ID hợp đồng để tra mã vạch<input required value={localLease} onChange={e => setLocalLease(e.target.value)} /></label>}
      <label>Loại mã được quét<select value={kind} onChange={e => setKind(e.target.value)}><option>GTIN</option><option value="Internal">Nội bộ</option></select></label>
      <label>Mã vạch được quét<input required maxLength={64} autoComplete="off" value={value} onChange={e => { setValue(e.target.value); setMessage(''); }} /></label>
      <button disabled={busy || disabled || !access || !targetLease || !value}>Tra cứu mã vạch</button>
    </form>
    {message && <p role="status">{message}</p>}
    {item && <div><p>{item.sku} · {item.baseUom}</p>{onUse && access?.organizationId === item.organizationId && <button disabled={busy || disabled} onClick={() => { if (current.current === found?.binding) onUse(item); }}>Dùng mặt hàng trong yêu cầu</button>}</div>}
  </div>;
}

type Alias = { id: string; itemId: string; kind: string; canonicalValue: string; active: boolean; version: string };
type Pending = { key: string; binding: string; path: string; body: object; version?: string; resourceId?: string; uncertain?: boolean };
export default function BarcodeRegistryPanel({ access }: { access: WorkspaceAccess | null }) {
  const [itemId, setItemId] = useState(''); const [value, setValue] = useState(''); const [kind, setKind] = useState('GTIN'); const [reason, setReason] = useState('');
  const [rows, setRows] = useState<{ binding: string; itemId: string; page: number; total: number; items: Alias[] } | null>(null);
  const [pending, setPending] = useState<Pending | null>(null); const unresolved = useRef<Pending | null>(null);
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState('');
  const binding = context(access); const current = useRef(binding); useLayoutEffect(() => { current.current = binding; }, [binding]);
  const lifecycle = useRef<AbortController | null>(null); const running = useRef(false);
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  const admin = !!access?.organizationId && access.organizationRole === 'CustomerAdmin';
  const list = rows?.binding === binding && rows.itemId === itemId ? rows : null;
  async function read(page = 1) {
    const controller = lifecycle.current; if (!controller || running.current || !access?.organizationId || !itemId) return;
    running.current = true; setBusy(true); setMessage(''); const original = binding; const originalItem = itemId;
    try {
      const result = await api<{ items: Alias[]; total: number }>(`/items/${encodeURIComponent(itemId)}/barcodes?organizationId=${encodeURIComponent(access.organizationId)}&page=${page}`, controller.signal);
      if (!Array.isArray(result.items) || !Number.isFinite(result.total) || result.items.some(x => x.itemId !== originalItem || !x.id || !x.version)) throw Error('Chưa xác minh danh sách mã vạch');
      if (!controller.signal.aborted && current.current === original) setRows({ ...result, binding: original, itemId: originalItem, page });
    } catch { if (!controller.signal.aborted && current.current === original) { setRows(null); setMessage('Không tải được danh sách mã vạch. Kiểm tra quyền truy cập và ID mặt hàng.'); } }
    finally { running.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function attempt(original: Pending) {
    const controller = lifecycle.current; if (!controller || running.current || !admin || original.binding !== binding) return;
    running.current = true; unresolved.current = original; setPending(original); setBusy(true); setMessage('');
    try {
      const result = await command<{ result: { resourceId: string } }>(original.path, original.body, original.key, controller.signal, 'POST', original.version);
      if (!result.result?.resourceId || original.resourceId && original.resourceId !== result.result.resourceId) throw Error('Chưa xác minh kết quả đăng ký mã vạch');
      if (!controller.signal.aborted) { unresolved.current = null; setPending(null); setRows(null); setMessage('Đã hoàn tất thao tác mã vạch. Tải lại mã vạch của mặt hàng để xem phiên bản hiện tại.'); }
    } catch (error) { if (!controller.signal.aborted) {
      if (!original.uncertain && isDefiniteRejection(error)) { unresolved.current = null; setPending(null); setMessage('Thay đổi mã vạch bị từ chối. Tải phiên bản hiện tại và kiểm tra lại yêu cầu.'); }
      else { const unknown = { ...original, uncertain: true }; unresolved.current = unknown; setPending(unknown); setMessage('Chưa xác định kết quả thao tác mã vạch. Chỉ thử lại yêu cầu gốc trong đúng không gian làm việc ban đầu.'); }
    } } finally { running.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  function send(path: string, body: object, alias?: Alias) {
    if (unresolved.current || !reason.trim()) return;
    void attempt({ key: crypto.randomUUID(), binding, path, body, version: alias?.version, resourceId: alias?.id });
  }
  if (!access?.organizationId && !pending) return null;
  return <section className="workspace-panel" aria-labelledby="barcodes-title" aria-busy={busy}><div className="panel-heading"><div><p className="eyebrow">NHÃN HÀNG CỦA KHÁCH</p><h2 id="barcodes-title">Đăng ký mã vạch mặt hàng</h2></div></div>
    <div className="session-content"><p className="catalog-help">Nhãn luôn gắn với mặt hàng gốc của khách hàng, kể cả khi bị vô hiệu hóa. GTIN được kiểm tra số kiểm tra; mã nội bộ dùng ký tự ASCII viết hoa. Đăng ký mã không làm thay đổi tồn kho.</p>
      <label>ID mặt hàng đăng ký mã vạch<input value={itemId} onChange={e => setItemId(e.target.value)} /></label>
      <button disabled={busy || !access?.organizationId || !itemId} onClick={() => void read()}>Tải mã vạch mặt hàng</button>
      {message && <p role="status">{message}</p>}
      {pending && <div><p>Yêu cầu mã vạch gốc: {pending.key}</p><button disabled={busy || !admin || pending.binding !== binding} onClick={() => void attempt(pending)}>Thử lại yêu cầu mã vạch gốc</button></div>}
      {admin && <><label>Lý do thay đổi mã vạch<input maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label>
        <form onSubmit={e => { e.preventDefault(); send(`/items/${encodeURIComponent(itemId)}/barcodes`, { organizationId: access!.organizationId, kind, value, reason }); }}>
          <label>Loại mã vạch<select value={kind} onChange={e => setKind(e.target.value)}><option>GTIN</option><option value="Internal">Nội bộ</option></select></label>
          <label>Giá trị mã vạch<input required maxLength={64} value={value} onChange={e => setValue(e.target.value)} /></label>
          <button disabled={busy || !!pending || !itemId || !reason.trim()}>Đăng ký mã vạch</button></form></>}
      {list && <><p>Trang mã vạch {list.page} · {list.total} nhãn</p>{list.items.map(row => <div key={row.id}><p>{displayValue(row.kind)}: {row.canonicalValue} · {row.active ? 'Đang hoạt động' : 'Đã vô hiệu hóa'}</p>
        {admin && <button disabled={busy || !!pending || !reason.trim()} onClick={() => send(`/barcodes/${row.id}/status`, { organizationId: access!.organizationId, active: !row.active, reason }, row)}>{row.active ? 'Vô hiệu hóa' : 'Kích hoạt'} {row.canonicalValue}</button>}</div>)}
        <button disabled={busy || list.page <= 1} onClick={() => void read(list.page - 1)}>Trang mã vạch trước</button><button disabled={busy || list.page * 50 >= list.total} onClick={() => void read(list.page + 1)}>Trang mã vạch sau</button></>}
    </div></section>;
}
