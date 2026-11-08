import type { WorkspaceAccess } from '../auth/workspace';
import { useEffect, useRef, useState } from 'react';
import { api, ApiError, command, isDefiniteRejection } from '../../api';
import PriceBasisNote from '../pricing/PriceBasis';
import BookingPanel from '../bookings/BookingPanel';
export type QuoteSelection = { facilityId: string; unitIds: string[]; startDate: string; endDate: string; verified: boolean };
type QuoteBody = { organizationId: string; facilityId: string; unitIds: string[]; startDate: string; endDate: string };
type Pending = { key: string; userId: string; signature: string; body: QuoteBody; uncertain?: boolean };
type Receipt = { result: { resourceId: string } };
type Quote = { id: string; organizationId: string; facilityId: string; startDate: string; endDate: string; expiresAt: string; version: string;
  rentalSubtotal: string; requiredDeposit: string; currency: string; reservation: boolean; lines: {
    id: string; unitCode: string; startDate: string; endDate: string; days: number; areaM2: string; dailyRate: string; billingBasis?: string; monthlyRate?: string | null; calendarDays?: number | null; amount: string }[] };

export default function QuotePanel({ access, selection }: { access: WorkspaceAccess | null; selection: QuoteSelection }) {
  const [onBehalf, setOnBehalf] = useState('');
  const sales = !!access && (access.operatorAdmin || access.facilityId === selection.facilityId && access.facilityRoles.includes('Sales'));
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState('');
  const [pending, setPending] = useState<Pending | null>(null); const [canRetry, setCanRetry] = useState(false);
  const [saved, setSaved] = useState<{ id: string; signature: string } | null>(null);
  const [snapshot, setSnapshot] = useState<{ data: Quote; signature: string } | null>(null);
  const lifecycle = useRef<AbortController | null>(null); const active = useRef(false); const unresolved = useRef<Pending | null>(null);
  const body: QuoteBody = { organizationId: access?.organizationId || (sales ? onBehalf.trim() : ''), facilityId: selection.facilityId, unitIds: [...selection.unitIds].sort(), startDate: selection.startDate, endDate: selection.endDate };
  const signature = JSON.stringify([access?.userId ?? '', body]);
  const canQuote = !!access && !!body.organizationId && (access.organizationRole === 'CustomerAdmin' || sales);
  const sameIssuer = !!pending && pending.userId === access?.userId && (pending.body.organizationId === access?.organizationId || sales && pending.body.facilityId === selection.facilityId);
  const quote = snapshot?.signature === signature ? snapshot.data : null;
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);

  async function details(id: string, binding: string, signal: AbortSignal) {
    const result = await api<Quote>(`/quotes/${id}`, signal);
    if (result.reservation !== false || typeof result.rentalSubtotal !== 'string' || typeof result.requiredDeposit !== 'string' || !Array.isArray(result.lines)) throw Error('Phản hồi báo giá không hợp lệ');
    if (!signal.aborted) setSnapshot({ data: result, signature: binding });
  }
  async function loadSaved() {
    const controller = lifecycle.current;
    if (!controller || !saved || busy || saved.signature !== signature) return;
    setBusy(true);
    try { await details(saved.id, saved.signature, controller.signal); if (!controller.signal.aborted) setMessage('Đã xác minh chi tiết báo giá.'); }
    catch { if (!controller.signal.aborted) setMessage('Chưa xác minh được chi tiết báo giá. Tải lại báo giá đã lưu.'); }
    finally { if (!controller.signal.aborted) setBusy(false); }
  }
  async function attempt(request: Pending) {
    const controller = lifecycle.current;
    if (!controller || controller.signal.aborted || active.current || !canQuote || request.userId !== access?.userId || !(request.body.organizationId === access?.organizationId || sales && request.body.facilityId === selection.facilityId)) return;
    active.current = true; unresolved.current = request; setPending(request); setBusy(true); setCanRetry(false); setMessage('');
    try {
      const receipt = await command<Receipt>('/quotes', request.body, request.key, controller.signal);
      if (typeof receipt.result?.resourceId !== 'string') throw Error('Chưa xác nhận mã định danh báo giá');
      if (controller.signal.aborted) return;
      unresolved.current = null; setPending(null); setSaved({ id: receipt.result.resourceId, signature: request.signature }); setMessage('Đã lưu báo giá.');
      try { await details(receipt.result.resourceId, request.signature, controller.signal); }
      catch { if (!controller.signal.aborted) setMessage('Đã lưu báo giá nhưng chưa tải lại được chi tiết. Tải báo giá đã lưu trước khi tiếp tục.'); }
    } catch (error) {
      if (controller.signal.aborted) return;
      if (!request.uncertain && isDefiniteRejection(error)) { unresolved.current = null; setPending(null); setMessage('Yêu cầu báo giá bị từ chối. Làm mới quyền truy cập, tình trạng kho và giá; điều kiện tìm kiếm của bạn được giữ lại.'); }
      else { const unknown = { ...request, uncertain: true }; unresolved.current = unknown; setPending(unknown); setMessage('Chưa xác định kết quả báo giá. Đọc yêu cầu gốc trước khi tạo báo giá khác.'); }
    } finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function create() {
    if (!canQuote || !access || unresolved.current || !selection.verified || body.unitIds.length < 1 || body.unitIds.length > 20) return;
    await attempt({ key: crypto.randomUUID(), userId: access.userId, signature, body: structuredClone(body) });
  }
  async function readback() {
    const controller = lifecycle.current;
    if (!controller || !pending || !sameIssuer || busy || active.current) return;
    setBusy(true); active.current = true;
    try {
      const receipt = await api<Receipt>(`/commands/${pending.key}?scopeId=${pending.body.organizationId}`, controller.signal);
      if (typeof receipt.result?.resourceId !== 'string') throw Error('Mã định danh báo giá đã lưu không hợp lệ');
      if (controller.signal.aborted) return;
      unresolved.current = null; setPending(null); setCanRetry(false); setSaved({ id: receipt.result.resourceId, signature: pending.signature }); setMessage('Đã xác minh yêu cầu báo giá đã lưu.');
      try { await details(receipt.result.resourceId, pending.signature, controller.signal); }
      catch { if (!controller.signal.aborted) setMessage('Đã lưu báo giá nhưng chưa tải lại được chi tiết. Tải báo giá đã lưu trước khi tiếp tục.'); }
    } catch (error) { if (!controller.signal.aborted) { setCanRetry(error instanceof ApiError && error.status === 404); setMessage('Chưa xác minh được báo giá đã lưu. Giữ nguyên mã định danh yêu cầu gốc.'); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  return <section className="quote-panel" aria-labelledby="quote-title" aria-busy={busy}>
    <h3 id="quote-title">Báo giá chính xác của bạn</h3>
    <p className="catalog-help">Báo giá chưa giữ chỗ. Các dòng tiền thuê tháng và tiền đặt cọc hoàn lại riêng được máy chủ tính toán.</p>
    {sales && !access?.organizationId && <label>ID tổ chức khách hàng cần báo giá<input value={onBehalf} onChange={e => setOnBehalf(e.target.value)} /></label>}
    {!canQuote && <p>Đăng nhập với vai trò quản trị khách hàng và chọn tổ chức để yêu cầu báo giá.</p>}
    <button type="button" disabled={!canQuote || !selection.verified || body.unitIds.length === 0 || body.unitIds.length > 20 || busy || !!pending} onClick={() => void create()}>Nhận báo giá chính xác</button>
    {message && <p role="status" className="session-notice">{message}</p>}
    {pending && <><p className="catalog-help">Yêu cầu gốc: {pending.body.unitIds.length} khoang · {pending.body.startDate} đến {pending.body.endDate} (không tính ngày kết thúc). Thử lại vẫn dùng lựa chọn gốc.</p>
      <div className="session-actions"><button type="button" disabled={busy || !sameIssuer} onClick={() => void readback()}>Đọc kết quả yêu cầu báo giá</button>
      {canRetry && <button type="button" disabled={busy || !sameIssuer || !canQuote} onClick={() => void attempt(pending)}>Thử lại báo giá gốc</button>}</div></>}
    {saved?.signature === signature && <button className="secondary-button" type="button" disabled={busy} onClick={() => void loadSaved()}>Tải báo giá</button>}
    {quote && <div className="quote-result"><dl><div><dt>Tổng tiền thuê trước cọc</dt><dd>{quote.rentalSubtotal} {quote.currency}</dd></div><div><dt>Tiền đặt cọc hoàn lại ·20%</dt><dd>{quote.requiredDeposit} {quote.currency}</dd></div></dl>
      <p>Có hiệu lực đến <time dateTime={quote.expiresAt}>{new Date(quote.expiresAt).toLocaleString('vi-VN')}</time>. Không tính ngày kết thúc.</p>
      <div className="table-scroll"><table><caption>Đơn giá tháng đã chốt</caption><thead><tr><th>Khoang</th><th>Từ</th><th>Kết thúc (không tính)</th><th>Số ngày</th><th>Diện tích (m²)</th><th>Đơn giá và phân bổ theo ngày</th><th>Số tiền (VND)</th></tr></thead>
        <tbody>{quote.lines.map(line => <tr key={line.id}><th scope="row">{line.unitCode}</th><td>{line.startDate}</td><td>{line.endDate}</td><td>{line.days}</td><td>{line.areaM2}</td><td><PriceBasisNote value={line} /></td><td>{line.amount}</td></tr>)}</tbody></table></div>
    </div>}
    <BookingPanel access={access} quote={quote} />
  </section>;
}
