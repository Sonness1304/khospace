import { displayValue } from '../../locale/vi';
import PriceBasisNote from '../pricing/PriceBasis';
import type { PriceBasis } from '../pricing/priceLabel';
import type { WorkspaceAccess } from '../auth/workspace';
import { useEffect, useId, useLayoutEffect, useRef, useState, type FormEvent } from 'react';
import { api, ApiError, command, isDefiniteRejection } from '../../api';
type Lease = { id: string; bookingId: string; rentalAccountId: string; organizationId: string; facilityId: string; state: string; version: string;
  terminatedAt?: string | null; terminationReason?: string | null;
  startDate: string; endDate: string; units: { id: string; unitId: string }[]; lines: ({ id: string; startDate: string; endDate: string; amount: string } & PriceBasis)[] };
type Pending = { key: string; viewBinding: string; userId: string; facilityId: string; organizationId: string; leaseId: string; path: string; body: { reason: string; inspectionConfirmed?: boolean; terminationConfirmed?: boolean }; version: string; uncertain?: boolean };
type Receipt = { result: { resourceId: string } };
const noticePrompt = 'Thông báo là lịch sử. Mã hồ sơ đã được điền; bấm Tải hợp đồng thuê để kiểm tra quyền và trạng thái hiện tại.';
export default function LeasePanel({ access, selectedId, noticeKey }: { access: WorkspaceAccess | null; selectedId?: string; noticeKey?: string }) {
  const [externalSelection, setExternalSelection] = useState(selectedId);
  const [id, setId] = useState(selectedId ?? ''); const [data, setData] = useState<{ lease: Lease; userId: string } | null>(null);
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState(noticeKey ? noticePrompt : ''); const [reason, setReason] = useState(''); const [inspected, setInspected] = useState(false);
  const [terminationReason, setTerminationReason] = useState(''); const [terminateConfirmed, setTerminateConfirmed] = useState(false);
  const [pending, setPending] = useState<Pending | null>(null); const [retry, setRetry] = useState(false);
  const lookupNoticeId = useId(); const lookupTarget = useRef<HTMLInputElement>(null); const detailTarget = useRef<HTMLDivElement>(null);
  const lookupIntent = useRef(0);
  const [lookupFeedback, setLookupFeedback] = useState<{ kind: 'error' | 'success'; view: string; intent: number } | null>(null);
  function clearLookupFeedback() {
    lookupIntent.current += 1;
    if (lookupFeedback) setMessage('');
    setLookupFeedback(null);
  }
  const lifecycle = useRef<AbortController | null>(null); const active = useRef(false); const unresolved = useRef<Pending | null>(null);
  const allowed = (value: { userId: string; facilityId: string; organizationId: string }) => value.userId === access?.userId
    && (access.operatorAdmin || value.organizationId === access.organizationId || value.facilityId === access.facilityId && access.facilityRoles.length > 0);
  const manager = (facility: string) => !!access && (access.operatorAdmin || access.facilityId === facility && access.facilityRoles.includes('FacilityManager'));
  const lease = data && data.lease.id.toLowerCase() === id.trim().toLowerCase() && allowed({ ...data.lease, userId: data.userId }) ? data.lease : null;
  if (externalSelection !== selectedId) { setExternalSelection(selectedId); setId(selectedId ?? ''); setLookupFeedback(null); if (lookupFeedback) setMessage(''); }
  const [noticeIntent, setNoticeIntent] = useState(noticeKey);
  if (noticeIntent !== noticeKey) { setNoticeIntent(noticeKey); setLookupFeedback(null); if (noticeKey) { setId(selectedId ?? ''); setData(null); setInspected(false); setTerminateConfirmed(false); setMessage(noticePrompt); } else if (lookupFeedback) setMessage(''); }
  const draftBinding = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]); const [draftOwner, setDraftOwner] = useState(draftBinding);
  if (draftOwner !== draftBinding) { setDraftOwner(draftBinding); setId(''); setReason(''); setInspected(false); setTerminationReason(''); setTerminateConfirmed(false); setMessage(''); setLookupFeedback(null); }
  const viewBinding = JSON.stringify([draftBinding, id.trim().toLowerCase(), noticeKey]);
  const visibleView = useRef(viewBinding);
  useLayoutEffect(() => { visibleView.current = viewBinding; lookupIntent.current += 1; }, [viewBinding]);
  const lookupError = lookupFeedback?.view === viewBinding && lookupFeedback.kind === 'error';
  useEffect(() => {
    if (busy || !lookupFeedback || lookupFeedback.view !== visibleView.current || lookupFeedback.intent !== lookupIntent.current) return;
    (lookupFeedback.kind === 'error' ? lookupTarget.current : detailTarget.current)?.focus();
  }, [busy, lookupFeedback]);
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  async function load(leaseId: string, userId: string, signal: AbortSignal, expectedView: string) {
    const value = await api<Lease>(`/leases/${encodeURIComponent(leaseId)}`, signal);
    if (typeof value.id !== 'string' || value.id.toLowerCase() !== leaseId.toLowerCase() || typeof value.organizationId !== 'string' || !Array.isArray(value.units) || !Array.isArray(value.lines)) throw Error('Chưa xác minh hợp đồng thuê');
    if (!signal.aborted && visibleView.current === expectedView) { setData({ lease: value, userId }); setInspected(false); setTerminateConfirmed(false); return true; }
    return false;
  }
  async function read(event: FormEvent) {
    event.preventDefault(); const controller = lifecycle.current; if (!controller || active.current || !access || !id.trim()) return;
    const expectedView = visibleView.current; const intent = ++lookupIntent.current;
    active.current = true; setBusy(true); setMessage(''); setLookupFeedback(null);
    try { if (await load(id.trim(), access.userId, controller.signal, expectedView) && lookupIntent.current === intent) { setMessage('Đã xác minh trạng thái hợp đồng.'); setLookupFeedback({ kind: 'success', view: expectedView, intent }); } }
    catch { if (!controller.signal.aborted && visibleView.current === expectedView && lookupIntent.current === intent) { setData(null); setMessage('Chưa xác minh được hợp đồng thuê. Kiểm tra ID, quyền truy cập và kết nối hiện tại.'); setLookupFeedback({ kind: 'error', view: expectedView, intent }); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function attempt(request: Pending) {
    const controller = lifecycle.current;
    if (!controller || active.current || !allowed(request) || !manager(request.facilityId)) return;
    const expectedView = visibleView.current;
    clearLookupFeedback(); active.current = true; unresolved.current = request; setPending(request); setBusy(true); setRetry(false); setMessage('');
    try {
      const result = await command<Receipt>(request.path, request.body, request.key, controller.signal, 'POST', request.version);
      if (result.result?.resourceId !== request.leaseId) throw Error('Chưa xác minh mã định danh yêu cầu hợp đồng');
      if (controller.signal.aborted) return;
      unresolved.current = null; setPending(null); setMessage('Đã lưu yêu cầu bàn giao thực tế.');
      try { if (expectedView === request.viewBinding && visibleView.current === expectedView) await load(request.leaseId, request.userId, controller.signal, expectedView); }
      catch { if (!controller.signal.aborted) setMessage('Đã lưu yêu cầu bàn giao thực tế nhưng chưa đọc lại được hợp đồng. Vui lòng tải lại hợp đồng.'); }
    } catch (error) {
      if (controller.signal.aborted) return;
      if (!request.uncertain && isDefiniteRejection(error)) { unresolved.current = null; setPending(null); setMessage('Yêu cầu bàn giao bị từ chối. Tải lại hợp đồng và xử lý tình trạng chiếm dụng, tồn kho hoặc yêu cầu đang chờ trước khi thử lại.'); }
      else { const unknown = { ...request, uncertain: true }; unresolved.current = unknown; setPending(unknown); setMessage('Chưa xác định kết quả bàn giao thực tế. Đọc kết quả đã lưu trước khi gửi yêu cầu bàn giao khác.'); }
    } finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  function send(clear: boolean) {
    if (!lease || !access || unresolved.current || !inspected || !reason) return;
    void attempt({ key: crypto.randomUUID(), viewBinding, userId: access.userId, facilityId: lease.facilityId, organizationId: lease.organizationId, leaseId: lease.id,
      path: `/leases/${lease.id}/${clear ? 'physical-clearance' : 'handover'}`, body: { reason, inspectionConfirmed: inspected }, version: lease.version });
  }
  function terminate() {
    if (!lease || !access || unresolved.current || !terminateConfirmed || !terminationReason.trim() || lease.terminatedAt) return;
    void attempt({ key: crypto.randomUUID(), viewBinding, userId: access.userId, facilityId: lease.facilityId, organizationId: lease.organizationId, leaseId: lease.id,
      path: `/leases/${lease.id}/terminate`, body: { reason: terminationReason, terminationConfirmed: true }, version: lease.version });
  }
  async function readback() {
    const controller = lifecycle.current; if (!controller || active.current || !pending || !allowed(pending)) return;
    const expectedView = visibleView.current;
    clearLookupFeedback(); active.current = true; setBusy(true);
    try {
      const result = await api<Receipt>(`/commands/${pending.key}?scopeId=${pending.organizationId}`, controller.signal);
      if (result.result?.resourceId !== pending.leaseId) throw Error('Chưa xác minh mã định danh yêu cầu hợp đồng');
      if (controller.signal.aborted) return;
      unresolved.current = null; setPending(null); setRetry(false); setMessage('Đã xác minh yêu cầu bàn giao đã lưu.');
      try { if (expectedView === pending.viewBinding && visibleView.current === expectedView) await load(pending.leaseId, pending.userId, controller.signal, expectedView); }
      catch { if (!controller.signal.aborted) setMessage('Đã lưu yêu cầu bàn giao thực tế nhưng chưa đọc lại được hợp đồng. Vui lòng tải lại hợp đồng.'); }
    } catch (error) { if (!controller.signal.aborted) { setRetry(error instanceof ApiError && error.status === 404); setMessage('Chưa xác minh được kết quả bàn giao đã lưu. Giữ nguyên mã định danh gốc.'); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  return <section className="workspace-panel lease-panel" aria-labelledby="leases-title" aria-busy={busy}>
    <div className="panel-heading"><div><p className="eyebrow">BÀN GIAO VÀ LƯU GIỮ</p><h2 id="leases-title">Hợp đồng và bàn giao</h2></div></div>
    <div className="session-content"><form className="lease-lookup" onSubmit={event => void read(event)}><label>ID hợp đồng thuê<input ref={lookupTarget} required maxLength={64} value={id} aria-invalid={lookupError || undefined} aria-describedby={lookupError ? lookupNoticeId : undefined} onChange={e => { clearLookupFeedback(); setId(e.target.value); }} /></label>
      <button type="submit" disabled={!access || busy || !id.trim()}>Tải hợp đồng thuê</button></form>
      <p className="catalog-help">Lịch hợp đồng và tình trạng sử dụng thực tế được theo dõi riêng. Hết hạn thuê hoặc tồn kho bằng 0 không tự động giải phóng khoang.</p>
      {message && <p id={lookupNoticeId} className="session-notice" role={lookupError ? 'alert' : 'status'}>{message}</p>}
      {pending && <div className="session-actions"><button type="button" disabled={busy || !allowed(pending)} onClick={() => void readback()}>Đọc kết quả yêu cầu bàn giao</button>
        {retry && <button type="button" disabled={busy || !allowed(pending) || !manager(pending.facilityId)} onClick={() => void attempt(pending)}>Thử lại yêu cầu bàn giao gốc</button>}</div>}
      {lease && <div ref={detailTarget} className="booking-detail" role="region" aria-label="Chi tiết hợp đồng thuê" tabIndex={-1}><p>Trạng thái trên máy chủ: <strong>{displayValue(lease.state)}</strong></p><p>{lease.startDate} đến {lease.endDate} · Không tính ngày kết thúc</p>
        {lease.terminatedAt && <p>Đã ghi nhận chấm dứt: {lease.terminatedAt} · {lease.terminationReason}. Hàng vẫn được lưu giữ đến khi giải phóng khoang; quyết toán tài chính là bước riêng.</p>}
        <p>{lease.units.length} quyền sử dụng nguyên khoang · Tài khoản thuê {lease.rentalAccountId}</p>{lease.units.map(unit => <p key={unit.id}>Quyền sử dụng khoang: <strong>{unit.id}</strong> · Khoang thực tế {unit.unitId}</p>)}
        {!!lease.lines.length && <div className="unit-list">{lease.lines.map(line => <div className="unit-row" key={line.id}><span>{line.startDate} đến {line.endDate}</span><PriceBasisNote value={line} /><strong>{line.amount} VND</strong></div>)}</div>}
        {manager(lease.facilityId) && <><label>Lý do kiểm tra<input maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label>
          <label className="check-label"><input type="checkbox" checked={inspected} onChange={e => setInspected(e.target.checked)} />Đã hoàn tất kiểm tra thực tế</label>
          {lease.state === 'Scheduled' && <button type="button" disabled={busy || !!pending || !reason || !inspected} onClick={() => send(false)}>Ghi nhận bàn giao</button>}
          {['Active', 'ClearancePending'].includes(lease.state) && <button type="button" disabled={busy || !!pending || !reason || !inspected} onClick={() => send(true)}>Ghi nhận giải phóng khoang thực tế</button>}
          <p className="catalog-help">Giải phóng khoang còn yêu cầu máy chủ xác minh tồn kho bằng 0 và không còn yêu cầu kho đang chờ. Quyết toán tài chính là bước riêng.</p>
          {!lease.terminatedAt && ['Active', 'ClearancePending'].includes(lease.state) && <fieldset><legend>Chấm dứt sử dụng kho</legend>
            <label>Lý do chấm dứt<input maxLength={500} value={terminationReason} onChange={e => setTerminationReason(e.target.value)} /></label>
            <label className="check-label"><input type="checkbox" checked={terminateConfirmed} onChange={e => setTerminateConfirmed(e.target.checked)} />Tôi xác nhận chấm dứt ngay; hàng hóa và quyết toán vẫn cần xử lý</label>
            <p>Dừng nhận hàng mới ngay. Quản lý phải cho phép xuất hàng. Các khoản thu và tiền đặt cọc hiện tại vẫn được giữ để xem xét quyết toán.</p>
            <button type="button" disabled={busy || !!pending || !terminationReason.trim() || !terminateConfirmed} onClick={terminate}>Chấm dứt hợp đồng thuê</button>
          </fieldset>}
        </>}
      </div>}
    </div>
  </section>;
}
