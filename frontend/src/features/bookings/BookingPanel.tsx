import { displayValue } from '../../locale/vi';
import type { WorkspaceAccess } from '../auth/workspace';
import { useEffect, useRef, useState, useId } from 'react';
import { api, ApiError, command, isDefiniteRejection } from '../../api';
export type AcceptedQuote = { id: string; version: string; organizationId: string; facilityId: string; expiresAt: string };
type Booking = { id: string; quoteId: string; organizationId: string; facilityId: string; state: string; version: string;
  startDate: string; endDate: string; expiresAt: string; rentalAccountId: string; requiredDeposit: string; depositVerification?: string; depositHeld?: string; refundNeeded: boolean };
type Pending = { facilityId?: string; userId: string; organizationId: string; key: string; path: string; body: unknown; version?: string; uncertain?: boolean };
type Receipt = { result: { resourceId: string } };
export default function BookingPanel({ access, quote, selectedId }: { access: WorkspaceAccess | null; quote: AcceptedQuote | null; selectedId?: string }) {
  const titleId = useId();
  const [lookup, setLookup] = useState(selectedId ?? ''); const [selection, setSelection] = useState(selectedId);
  if (selection !== selectedId) { setSelection(selectedId); setLookup(selectedId ?? ''); }
  const [booking, setBooking] = useState<{ data: Booking; userId: string } | null>(null);
  const [saved, setSaved] = useState<{ id: string; userId: string; organizationId: string; facilityId?: string } | null>(null);
  const [pending, setPending] = useState<Pending | null>(null); const [retry, setRetry] = useState(false);
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState(''); const [reason, setReason] = useState('');
  const [leaseId, setLeaseId] = useState<{ id: string; bookingId: string } | null>(null);
  const lifecycle = useRef<AbortController | null>(null); const active = useRef(false); const unresolved = useRef<Pending | null>(null);
  const sameIssuer = (value: { userId: string; organizationId: string; facilityId?: string }) => value.userId === access?.userId && (value.organizationId === access?.organizationId || !!access && (access.operatorAdmin || value.facilityId === access.facilityId && access.facilityRoles.includes('Sales')));
  const current = booking && sameIssuer({ userId: booking.userId, organizationId: booking.data.organizationId, facilityId: booking.data.facilityId }) ? booking.data : null;
  const canHold = !!access && !!quote && (quote.organizationId === access.organizationId && access.organizationRole === 'CustomerAdmin' || access.operatorAdmin || quote.facilityId === access.facilityId && access.facilityRoles.includes('Sales'));
  const sales = !!current && !!access && (access.operatorAdmin || access.facilityId === current.facilityId && access.facilityRoles.includes('Sales'));
  const draftBinding = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]); const [draftOwner, setDraftOwner] = useState(draftBinding);
  if (draftOwner !== draftBinding) { setDraftOwner(draftBinding); setLookup(''); setReason(''); setMessage(''); }
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  async function load(id: string, userId: string, signal: AbortSignal) {
    const data = await api<Booking>(`/bookings/${id}`, signal);
    if (typeof data.id !== 'string' || typeof data.organizationId !== 'string' || typeof data.requiredDeposit !== 'string') throw Error('Chưa xác minh phản hồi đặt chỗ');
    if (!signal.aborted) setBooking({ data, userId });
  }
  async function reload() {
    const controller = lifecycle.current; if (!controller || !saved || !sameIssuer(saved) || active.current) return;
    active.current = true; setBusy(true);
    try { await load(saved.id, saved.userId, controller.signal); if (!controller.signal.aborted) setMessage('Đã xác minh trạng thái thuê.'); }
    catch { if (!controller.signal.aborted) setMessage('Không tải được trạng thái thuê. Vui lòng tải lại trạng thái từ máy chủ.'); }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function attempt(request: Pending) {
    const controller = lifecycle.current;
    if (!controller || controller.signal.aborted || active.current || !sameIssuer(request) || !(access?.organizationRole === 'CustomerAdmin' || access?.operatorAdmin || request.facilityId === access?.facilityId && access?.facilityRoles.includes('Sales'))) return;
    active.current = true; unresolved.current = request; setPending(request); setBusy(true); setRetry(false); setMessage('');
    try {
      const result = await command<Receipt>(request.path, request.body, request.key, controller.signal, 'POST', request.version);
      if (typeof result.result?.resourceId !== 'string') throw Error('Chưa xác minh kết quả yêu cầu');
      if (controller.signal.aborted) return;
      unresolved.current = null; setPending(null); setSaved({ id: result.result.resourceId, userId: request.userId, organizationId: request.organizationId, facilityId: request.facilityId }); setMessage('Đã lưu yêu cầu thuê.');
      if (request.path.endsWith('/lease')) {
        setLeaseId({ id: result.result.resourceId, bookingId: request.path.split('/')[2] });
        setSaved(null); setMessage('Đã tạo hợp đồng thuê. Dùng ID này tại mục Hợp đồng và bàn giao.'); return;
      }
      try { await load(result.result.resourceId, request.userId, controller.signal); }
      catch { if (!controller.signal.aborted) setMessage('Đã lưu yêu cầu thuê nhưng chưa tải lại được trạng thái. Tải trạng thái thuê trước khi thao tác tiếp.'); }
    } catch (error) {
      if (controller.signal.aborted) return;
      if (!request.uncertain && isDefiniteRejection(error)) { unresolved.current = null; setPending(null); setMessage('Yêu cầu thuê bị từ chối. Tải lại báo giá hoặc đặt chỗ hiện tại; ngày thuê, giá hoặc sức chứa có thể đã thay đổi.'); }
      else { const unknown = { ...request, uncertain: true }; unresolved.current = unknown; setPending(unknown); setMessage('Chưa xác định kết quả yêu cầu thuê. Đọc kết quả đã lưu trước khi gửi yêu cầu thuê khác.'); }
    } finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  function send(path: string, body: unknown, version?: string) {
    if (!access || unresolved.current) return;
    void attempt({ userId: access.userId, organizationId: path.startsWith('/quotes/') ? quote?.organizationId ?? access.organizationId : current?.organizationId ?? access.organizationId, facilityId: path.startsWith('/quotes/') ? quote?.facilityId ?? access.facilityId : current?.facilityId ?? access.facilityId, key: crypto.randomUUID(), path, body: structuredClone(body), version });
  }
  async function readback() {
    const controller = lifecycle.current; if (!controller || !pending || !sameIssuer(pending) || active.current) return;
    active.current = true; setBusy(true);
    try {
      const result = await api<Receipt>(`/commands/${pending.key}?scopeId=${pending.organizationId}`, controller.signal);
      if (typeof result.result?.resourceId !== 'string') throw Error('Chưa xác minh kết quả yêu cầu');
      if (controller.signal.aborted) return;
      unresolved.current = null; setPending(null); setRetry(false); setSaved({ id: result.result.resourceId, userId: pending.userId, organizationId: pending.organizationId, facilityId: pending.facilityId }); setMessage('Đã xác minh yêu cầu thuê đã lưu.');
      if (pending.path.endsWith('/lease')) {
        setLeaseId({ id: result.result.resourceId, bookingId: pending.path.split('/')[2] });
        setSaved(null); setMessage('Đã tạo hợp đồng thuê. Dùng ID này tại mục Hợp đồng và bàn giao.'); return;
      }
      try { await load(result.result.resourceId, pending.userId, controller.signal); }
      catch { if (!controller.signal.aborted) setMessage('Đã lưu yêu cầu thuê nhưng chưa tải lại được trạng thái. Tải trạng thái thuê trước khi thao tác tiếp.'); }
    } catch (error) { if (!controller.signal.aborted) { setRetry(error instanceof ApiError && error.status === 404); setMessage('Chưa xác minh được kết quả thuê đã lưu. Giữ nguyên mã định danh yêu cầu gốc.'); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  return <section className="booking-panel" aria-labelledby={titleId} aria-busy={busy}>
    <h3 id={titleId}>Đặt chỗ và trạng thái thuê</h3>
    <form onSubmit={event => { event.preventDefault(); const controller = lifecycle.current; if (!access || !controller || active.current) return; active.current = true; setBusy(true); void load(lookup.trim(), access.userId, controller.signal).catch(() => { if (!controller.signal.aborted) { setBooking(null); setMessage('Không thể xác minh đặt chỗ. Kiểm tra quyền truy cập hiện tại.'); } }).finally(() => { active.current = false; if (!controller.signal.aborted) setBusy(false); }); }}><label>ID đặt chỗ<input required value={lookup} onChange={e => setLookup(e.target.value)} /></label><button disabled={!access || busy || !lookup.trim()}>Tải đặt chỗ</button></form>
    <p className="catalog-help">Các khoang đã chọn được giữ trong 15 phút kể từ khi máy chủ chấp nhận. Chứng từ thanh toán không gia hạn thời hạn này.</p>
    <button type="button" disabled={!canHold || busy || !!pending} onClick={() => quote && send(`/quotes/${quote.id}/holds`, { expectedQuoteVersion: quote.version })}>Giữ các khoang đã chọn trong 15 phút</button>
    {message && <p role="status" className="session-notice">{message}</p>}
    {pending && <div className="session-actions"><button type="button" disabled={busy || !sameIssuer(pending)} onClick={() => void readback()}>Đọc kết quả yêu cầu thuê</button>
      {retry && <button type="button" disabled={busy || !sameIssuer(pending) || !(access?.organizationRole === 'CustomerAdmin' || access?.operatorAdmin || pending.facilityId === access?.facilityId && access?.facilityRoles.includes('Sales'))} onClick={() => void attempt(pending)}>Thử lại yêu cầu thuê gốc</button>}</div>}
    {saved && sameIssuer(saved) && <button className="secondary-button" type="button" disabled={busy} onClick={() => void reload()}>Tải trạng thái thuê</button>}
    {current && <div className="booking-detail"><p>Trạng thái trên máy chủ: <strong>{displayValue(current.state)}</strong></p><p>{current.startDate} đến {current.endDate} · Không tính ngày kết thúc</p>
      <p>ID đặt chỗ: <strong>{current.id}</strong></p><p>ID tài khoản thuê: <strong>{current.rentalAccountId}</strong></p>
      <p>Hạn giữ chỗ: <time dateTime={current.expiresAt}>{new Date(current.expiresAt).toLocaleString('vi-VN')}</time>. Tải trạng thái từ máy chủ trước khi thao tác.</p>
      <p>Tiền đặt cọc hoàn lại: <strong>{current.requiredDeposit} VND</strong></p><p>Trạng thái đặt cọc: {displayValue(current.depositVerification ?? 'Pending')}. Tiền bảo đảm đang giữ: {current.depositHeld ?? '0'} VND. Bộ phận tài chính phải xác minh tiền và kinh doanh phải xác nhận trước khi hết hạn giữ chỗ.</p>
      {current.refundNeeded && <p role="status">Tiền cần bộ phận tài chính kiểm tra; hệ thống chưa xác nhận chuyển tiền hoàn lại.</p>}
      {['Held', 'Confirmed', 'Expired'].includes(current.state) && <><label>Lý do hủy<input maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label>
        <button type="button" disabled={busy || !!pending || !reason || !(access?.organizationRole === 'CustomerAdmin' || sales)} onClick={() => send(`/bookings/${current.id}/cancel`, { reason }, current.version)}>Hủy thuê</button></>}
      {sales && <button type="button" disabled={busy || !!pending || current.state !== 'Held' || !reason} onClick={() => send(`/bookings/${current.id}/confirm`, { reason }, current.version)}>Xác nhận đặt chỗ</button>}
      {sales && current.state === 'Confirmed' && <button type="button" disabled={busy || !!pending || !reason} onClick={() => send(`/bookings/${current.id}/lease`, { reason }, current.version)}>Tạo hợp đồng thuê</button>}
      {leaseId?.bookingId === current.id && <p>ID hợp đồng thuê: <strong>{leaseId.id}</strong></p>}
    </div>}
  </section>;
}
