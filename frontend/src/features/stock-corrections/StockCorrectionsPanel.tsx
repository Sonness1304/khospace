import { displayValue } from '../../locale/vi';
import { useEffect, useRef, useState } from 'react';
import type { WorkspaceAccess } from '../auth/workspace';
import { api, ApiError, command, isDefiniteRejection } from '../../api';

type Scope = { organizationId: string; facilityId: string };
type Posting = Scope & { id: string; type: string; reversedBy: string | null; entries: { id: string; leaseUnitId: string; itemId: string; delta: string }[] };
type Count = Scope & { id: string; state: string; createdBy: string; version: string; expectedQuantity: string; countedQuantity: string };
type CountPage = { items: { id: string; state: string }[]; total: number; page: number };
type Receipt = { result: { resourceId: string } };
type Role = 'Clerk' | 'FacilityManager';
type Pending = Scope & { key: string; userId: string; context: string; role: Role; path: string; body: object; version?: string; expectedId?: string; uncertain?: boolean };
type Bound<T> = { value: T; context: string };
const quantity = (value: string, zero = false) => /^[0-9]+([.][0-9]{1,3})?$/.test(value) && (zero ? Number(value) >= 0 : Number(value) > 0);

export default function StockCorrectionsPanel({ access }: { access: WorkspaceAccess | null }) {
  const context = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId, access?.facilityRoles, access?.operatorAdmin]);
  const [owner, setOwner] = useState(context); const [org, setOrg] = useState(''); const [source, setSource] = useState(''); const [destination, setDestination] = useState(''); const [item, setItem] = useState('');
  const [reason, setReason] = useState(''); const [amount, setAmount] = useState(''); const [expected, setExpected] = useState(''); const [counted, setCounted] = useState('');
  const [checked, setChecked] = useState(false); const [reverseChecked, setReverseChecked] = useState(false);
  const [postingId, setPostingId] = useState(''); const [countId, setCountId] = useState('');
  const [postingData, setPostingData] = useState<Bound<Posting> | null>(null); const [countData, setCountData] = useState<Bound<Count> | null>(null); const [pageData, setPageData] = useState<Bound<CountPage> | null>(null);
  const [message, setMessage] = useState<Bound<string> | null>(null); const [busy, setBusy] = useState(false); const [pending, setPending] = useState<Pending | null>(null); const [retry, setRetry] = useState(false);
  const lifecycle = useRef<AbortController | null>(null); const active = useRef(false); const unresolved = useRef<Pending | null>(null);
  if (owner !== context) { setOwner(context); setOrg(access?.organizationId ?? ''); setSource(''); setDestination(''); setItem(''); setReason(''); setAmount(''); setExpected(''); setCounted(''); setPostingId(''); setCountId(''); setChecked(false); setReverseChecked(false); }
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  const has = (role: Role, facility = access?.facilityId) => !!access && (access.operatorAdmin || facility === access.facilityId && access.facilityRoles.includes(role));
  const staff = has('Clerk') || has('FacilityManager');
  const scoped = (value: Scope) => !!access && (has('Clerk', value.facilityId) || has('FacilityManager', value.facilityId));
  const allowed = (value: Pending) => value.userId === access?.userId && has(value.role, value.facilityId);
  const posting = postingData?.context === context && scoped(postingData.value) ? postingData.value : null;
  const count = countData?.context === context && scoped(countData.value) ? countData.value : null;
  const page = pageData?.context === context ? pageData.value : null;
  const blocked = busy || !!pending;
  function saved(result: Receipt, request: Pending) {
    if (typeof result.result?.resourceId !== 'string' || !result.result.resourceId || request.expectedId && request.expectedId !== result.result.resourceId) throw Error('Chưa xác minh mã định danh điều chỉnh');
    unresolved.current = null; setPending(null); setRetry(false); setPostingData(null); setCountData(null); setPageData(null); setChecked(false); setReverseChecked(false);
    setMessage({ value: `Đã lưu điều chỉnh: ${result.result.resourceId}. Tải chứng từ trước khi thay đổi tiếp.`, context: request.context });
  }
  async function attempt(request: Pending) {
    const controller = lifecycle.current; if (!controller || active.current || !allowed(request)) return;
    active.current = true; unresolved.current = request; setPending(request); setBusy(true); setRetry(false); setMessage(null);
    const bound = context;
    try { const result = await command<Receipt>(request.path, request.body, request.key, controller.signal, 'POST', request.version); if (!controller.signal.aborted) saved(result, request); }
    catch (error) { if (!controller.signal.aborted) {
      if (!request.uncertain && isDefiniteRejection(error)) { unresolved.current = null; setPending(null); setMessage({ value: 'Điều chỉnh bị từ chối. Tải tồn kho và phiên bản chứng từ hiện tại; giữ kết quả quan sát để xem xét.', context: bound }); }
      else { const unknown = { ...request, uncertain: true }; unresolved.current = unknown; setPending(unknown); setMessage({ value: 'Chưa xác định kết quả điều chỉnh. Đọc yêu cầu đã lưu trước khi điều chỉnh khác.', context: bound }); }
    } } finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  function send(path: string, body: object, role: Role, scope: Scope, expectedId?: string, version?: string) {
    if (!access || unresolved.current || !reason.trim()) return;
    void attempt({ ...scope, key: crypto.randomUUID(), userId: access.userId, context, role, path, body: structuredClone(body), expectedId, version });
  }
  async function readback() {
    const controller = lifecycle.current; if (!controller || active.current || !pending || !allowed(pending)) return;
    active.current = true; setBusy(true); const bound = context;
    try { const result = await api<Receipt>(`/commands/${pending.key}?scopeId=${pending.organizationId}`, controller.signal); if (!controller.signal.aborted) saved(result, pending); }
    catch (error) { if (!controller.signal.aborted) { setRetry(error instanceof ApiError && error.status === 404); setMessage({ value: 'Chưa xác minh được kết quả điều chỉnh đã lưu. Giữ nguyên mã định danh gốc.', context: bound }); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function read(kind: 'posting' | 'count' | 'list', number = 1) {
    const controller = lifecycle.current; if (!controller || active.current || !staff) return;
    active.current = true; setBusy(true); setMessage(null); const bound = context; setReverseChecked(false);
    try {
      if (kind === 'posting') { const value = await api<Posting>(`/stock/postings/${encodeURIComponent(postingId.trim())}`, controller.signal); if (value.id !== postingId.trim() || !Array.isArray(value.entries)) throw Error('Chưa xác minh chứng từ ghi kho'); if (!controller.signal.aborted) setPostingData({ value, context: bound }); }
      else if (kind === 'count') { const value = await api<Count>(`/stock/counts/${encodeURIComponent(countId.trim())}`, controller.signal); if (value.id !== countId.trim() || typeof value.version !== 'string') throw Error('Chưa xác minh kiểm đếm'); if (!controller.signal.aborted) setCountData({ value, context: bound }); }
      else { const value = await api<CountPage>(`/stock/counts?organizationId=${encodeURIComponent(org)}&facilityId=${encodeURIComponent(access!.facilityId)}&page=${number}`, controller.signal); if (!Array.isArray(value.items)) throw Error('Chưa xác minh trang kiểm đếm'); if (!controller.signal.aborted) setPageData({ value, context: bound }); }
    } catch { if (!controller.signal.aborted) { setPostingData(null); setCountData(null); setPageData(null); setMessage({ value: 'Chưa xác minh được dữ liệu điều chỉnh. Kiểm tra các ID, quyền truy cập và kết nối.', context: bound }); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  const draftScope = { organizationId: org, facilityId: access?.facilityId ?? '' };
  const ready = !!org.trim() && !!source.trim() && !!item.trim() && !!reason.trim();
  return <section className="workspace-panel" aria-labelledby="stock-corrections-title" aria-busy={busy}>
    <div className="panel-heading"><div><p className="eyebrow">THAY ĐỔI TỒN KHO CÓ KIỂM SOÁT</p><h2 id="stock-corrections-title">Chuyển kho, đảo giao dịch và kiểm đếm</h2></div></div>
    <div className="session-content">
      <p>Chuyển kho luôn ghi đồng thời cả hai đầu. Đảo giao dịch thêm bút toán bù trừ. Mỗi điều chỉnh kiểm đếm phải được quản lý khác duyệt theo dữ liệu hiện tại.</p>
      {message?.context === context && <p role="status" className="session-notice">{message.value}</p>}
      {pending && <div className="session-actions"><button disabled={busy || !allowed(pending)} onClick={() => void readback()}>Đọc kết quả yêu cầu điều chỉnh</button>{retry && <button disabled={busy || !allowed(pending)} onClick={() => void attempt(pending)}>Thử lại điều chỉnh gốc</button>}</div>}
      {!staff && <p>Chọn cơ sở có quyền thủ kho hoặc quản lý để ghi điều chỉnh.</p>}
      {staff && <>
        <label>ID tổ chức cần điều chỉnh<input value={org} onChange={e => { setOrg(e.target.value); setPageData(null); }} /></label>
        <label>ID quyền sử dụng khoang nguồn<input value={source} onChange={e => { setSource(e.target.value); setChecked(false); }} /></label>
        <label>ID mặt hàng cần điều chỉnh<input value={item} onChange={e => { setItem(e.target.value); setChecked(false); }} /></label>
        <label>Lý do điều chỉnh<input maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label>
        {has('Clerk') && <fieldset><legend>Chuyển hàng hiện có</legend>
          <label>ID quyền sử dụng khoang đích<input value={destination} onChange={e => { setDestination(e.target.value); setChecked(false); }} /></label>
          <label>Số lượng chuyển<input inputMode="decimal" value={amount} onChange={e => { setAmount(e.target.value); setChecked(false); }} /></label>
          <label className="check-label"><input type="checkbox" checked={checked} onChange={e => setChecked(e.target.checked)} />Đã kiểm tra thực tế cả hai đầu chuyển</label>
          <button disabled={blocked || !ready || !destination.trim() || destination === source || !quantity(amount) || !checked} onClick={() => { const transferId = crypto.randomUUID(); send('/stock/transfers', { transferId, organizationId: org, sourceLeaseUnitId: source, destinationLeaseUnitId: destination, itemId: item, quantity: amount, reason }, 'Clerk', draftScope, transferId); }}>Ghi nhận chuyển kho</button>
          <p>Cả hai quyền sử dụng phải đã bàn giao, còn hạn và thuộc cùng khách hàng tại cơ sở này.</p>
        </fieldset>}
        <fieldset><legend>Ghi nhận kiểm đếm thực tế</legend>
          <label>Số lượng tồn dự kiến<input inputMode="decimal" value={expected} onChange={e => setExpected(e.target.value)} /></label>
          <label>Số lượng kiểm đếm<input inputMode="decimal" value={counted} onChange={e => setCounted(e.target.value)} /></label>
          <button disabled={blocked || !ready || !quantity(expected, true) || !quantity(counted, true)} onClick={() => send('/stock/counts', { organizationId: org, leaseUnitId: source, itemId: item, expectedQuantity: expected, countedQuantity: counted, reason }, has('Clerk') ? 'Clerk' : 'FacilityManager', draftScope)}>Lưu kết quả kiểm đếm</button>
          <p>Thao tác lưu kết quả quan sát. Khi phê duyệt, hệ thống kiểm tra lại tồn kho; hủy kết quả cũ và kiểm đếm lại nếu dữ liệu đã thay đổi.</p>
        </fieldset>
        <h3>Lịch sử ghi kho</h3><label>ID chứng từ ghi kho<input value={postingId} onChange={e => { setPostingId(e.target.value); setReverseChecked(false); }} /></label>
        <button disabled={busy || !postingId.trim()} onClick={() => void read('posting')}>Tải chứng từ ghi kho</button>
        {posting && <div><p>Ghi kho {posting.id} · {displayValue(posting.type)}{posting.reversedBy && ` · Đảo bởi ${posting.reversedBy}`}</p>
          {posting.entries.map(entry => <p key={entry.id}>{entry.leaseUnitId} · {entry.itemId} · {entry.delta}</p>)}
          {has('FacilityManager', posting.facilityId) && ['Receipt', 'Issue', 'Transfer'].includes(posting.type) && !posting.reversedBy && <>
            <label className="check-label"><input type="checkbox" checked={reverseChecked} onChange={e => setReverseChecked(e.target.checked)} />Tôi xác nhận đảo toàn bộ bằng bút toán bù trừ</label>
            <button disabled={blocked || !reason.trim() || !reverseChecked} onClick={() => send(`/stock/postings/${posting.id}/reverse`, { reason, reversalConfirmed: true }, 'FacilityManager', posting)}>Đảo chứng từ ghi kho</button>
          </>}
        </div>}
        <h3>Xét duyệt kiểm đếm</h3><button disabled={busy || !org.trim() || !access?.facilityId} onClick={() => void read('list')}>Tải danh sách kiểm đếm</button>
        {page && <div>{page.items.map(value => <p key={value.id}>{value.id} · {displayValue(value.state)}</p>)}<button disabled={busy || page.page <= 1} onClick={() => void read('list', page.page - 1)}>Trang kiểm đếm trước</button><button disabled={busy || page.page * 50 >= page.total} onClick={() => void read('list', page.page + 1)}>Trang kiểm đếm sau</button></div>}
        <label>ID kết quả kiểm đếm<input value={countId} onChange={e => setCountId(e.target.value)} /></label><button disabled={busy || !countId.trim()} onClick={() => void read('count')}>Tải kết quả kiểm đếm</button>
        {count && <div><p>Kiểm đếm {count.id} · {displayValue(count.state)} · Dự kiến {count.expectedQuantity} / Đã đếm {count.countedQuantity} · Người ghi {count.createdBy}</p>
          {count.state === 'Pending' && <>
            {has('FacilityManager', count.facilityId) && <button disabled={blocked || !reason.trim() || count.createdBy === access?.userId} onClick={() => send(`/stock/counts/${count.id}/approve`, { reason }, 'FacilityManager', count, count.id, count.version)}>Phê duyệt điều chỉnh kiểm đếm</button>}
            {(has('FacilityManager', count.facilityId) || count.createdBy === access?.userId) && <button disabled={blocked || !reason.trim()} onClick={() => send(`/stock/counts/${count.id}/cancel`, { reason }, has('FacilityManager') ? 'FacilityManager' : 'Clerk', count, count.id, count.version)}>Hủy kết quả kiểm đếm</button>}
          </>}
        </div>}
      </>}
    </div>
  </section>;
}
