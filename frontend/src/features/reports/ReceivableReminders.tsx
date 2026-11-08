import { useEffect, useRef, useState } from 'react';
import { command, isDefiniteRejection } from '../../api';
import type { WorkspaceAccess } from '../auth/workspace';

type Input = { startDate: string; endDate: string; page: number };
type Pending = { context: string; path: string; key: string; body: Input };
type Result = { observedAt: string; asOfDate: string; timeZone: string; scanned: number; eligible: number; created: number; duplicates: number; page: number; nextPage: number | null };
export default function ReceivableReminders({ access }: { access: WorkspaceAccess | null }) {
 const context = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]);
 const allowed = !!access && (access.operatorAdmin || access.facilityRoles.includes('Finance'));
 const [start, setStart] = useState(() => new Date().toISOString().slice(0, 8) + '01');
 const [end, setEnd] = useState(() => { const date = new Date(); return new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 1)).toISOString().slice(0, 10); });
 const [page, setPage] = useState(0); const [pending, setPending] = useState<Pending | null>(null);
 const [result, setResult] = useState<{ context: string; value: Result } | null>(null);
 const [feedback, setFeedback] = useState<{ context: string; text: string } | null>(null);
 const [busy, setBusy] = useState(false); const running = useRef(false); const controller = useRef<AbortController | null>(null);
 useEffect(() => () => { controller.current?.abort(); }, []);
 const span = (Date.parse(end) - Date.parse(start)) / 86400000;
 const valid = /^\d{4}-\d{2}-\d{2}$/.test(start) && /^\d{4}-\d{2}-\d{2}$/.test(end) && span > 0 && span <= 366 && Number.isInteger(page) && page >= 0 && page <= 99;
 async function attempt(item: Pending, retry = false) {
  if (running.current || !allowed || item.context !== context || !access?.organizationId || !access.facilityId) return;
  running.current = true; setBusy(true); setPending(item); setFeedback(null); setResult(null);
  const abort = new AbortController(); controller.current = abort;
  try {
   const response = await command<{ result: Result }>(item.path, item.body, item.key, abort.signal);
   if (!abort.signal.aborted) { setPending(null); setResult({ context: item.context, value: response.result }); }
  } catch (error) {
   if (!abort.signal.aborted) {
    if (!retry && isDefiniteRejection(error)) { setPending(null); setFeedback({ context: item.context, text: 'Yêu cầu nhắc nợ bị từ chối. Kiểm tra quyền tài chính hiện tại và thu hẹp khoảng xuống tối đa 5000 bảng kê.' }); }
    else setFeedback({ context: item.context, text: 'Chưa xác định kết quả nhắc nợ. Giữ trang này mở và thử lại yêu cầu gốc sau khi khôi phục đúng tổ chức, cơ sở và quyền tài chính.' });
   }
  } finally { running.current = false; if (!abort.signal.aborted) setBusy(false); }
 }
 if (!allowed && !pending) return null;
 const visible = allowed && result?.context === context ? result.value : null;
 return <section className="quote-panel" aria-labelledby="reminders-title" aria-busy={busy}>
  <h3 id="reminders-title">Nhắc thanh toán quá hạn</h3>
  <p>Ghi nhận lịch sử số dư quá hạn trong ứng dụng. Thao tác không gửi email, thu tiền hay thay đổi bảng kê. Khách hàng cần kiểm tra bảng kê hiện tại trước khi trả tiền.</p>
  <label>Nhắc nợ từ hạn thanh toán<input type="date" disabled={!!pending || !allowed} value={start} onChange={e => { setStart(e.target.value); setResult(null); }} /></label>
  <label>Nhắc nợ đến hạn thanh toán (không tính)<input type="date" disabled={!!pending || !allowed} value={end} onChange={e => { setEnd(e.target.value); setResult(null); }} /></label>
  <label>Trang dữ liệu nguồn nhắc nợ<input type="number" min={0} max={99} disabled={!!pending || !allowed} value={page} onChange={e => { setPage(Number(e.target.value)); setResult(null); }} /></label>
  <p>Chọn tối đa 366 ngày. Mỗi trang nguồn gồm 50 bảng kê, kể cả khoản chưa quá hạn và đã tất toán; mỗi bảng kê nhận tối đa một nhắc nợ trong một ngày tại cơ sở. Yêu cầu mới sẽ đọc lại số dư hiện tại.</p>
  <button disabled={busy || !!pending || !allowed || !access?.organizationId || !access.facilityId || !valid}
   onClick={() => void attempt({ context, path: `/organizations/${access?.organizationId}/facilities/${access?.facilityId}/receivable-reminders`, key: crypto.randomUUID(), body: { startDate: start, endDate: end, page } })}>Tạo nhắc thanh toán quá hạn</button>
  {pending && <><p>Yêu cầu gốc chỉ được giữ trên trang này. Khôi phục phạm vi gốc và quyền tài chính trước khi thử lại. Tải lại hoặc rời mục Báo cáo sẽ mất trạng thái khôi phục này.</p>
   <button disabled={busy || !allowed || pending.context !== context} onClick={() => void attempt(pending, true)}>Thử lại yêu cầu nhắc nợ gốc</button></>}
  {allowed && feedback?.context === context && <p role="status">{feedback.text}</p>}
  {visible && <div role="status"><p>Đã tạo {visible.created} nhắc nợ trong ứng dụng; {visible.duplicates} đã ghi hôm nay. Đã quét {visible.scanned}; đủ điều kiện {visible.eligible}; trang nguồn {visible.page}; trang nguồn tiếp theo: {visible.nextPage ?? 'không có'}.</p>
   <p>Đã ghi nhận {visible.observedAt}; ngày tại cơ sở {visible.asOfDate} ({visible.timeZone}). Tải thông báo trong ứng dụng theo phạm vi để xem lịch sử sự kiện.</p></div>}
 </section>;
}
