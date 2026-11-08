import { useEffect, useRef, useState } from 'react';
import { api, ApiError } from '../../api';
import type { WorkspaceAccess } from '../auth/workspace';

type Row = { statementId: string; leaseId: string; rentalAccountId: string; periodStart: string; dueDate: string; originalVnd: string; additionsVnd: string; creditsVnd: string;
 allocationsVnd: string; deductionsVnd: string; releasesVnd: string; effectiveChargeVnd: string; effectivePaidVnd: string; outstandingVnd: string; daysOverdue: number; bucket: string };
type Summary = { matchedStatements: number; openStatements: number; effectiveChargeVnd: string; effectivePaidVnd: string; outstandingVnd: string; currentVnd: string;
 overdue1To30Vnd: string; overdue31To60Vnd: string; overdue61To90Vnd: string; overdueOver90Vnd: string };
type Snapshot = { observedAt: string; asOfDate: string; timeZone: string; organizationName: string; summary: Summary; page: number; nextPage: number | null; items: Row[]; source: string };
const bucketLabels: Record<string, string> = { Settled: 'Đã tất toán', Current: 'Chưa quá hạn', Overdue1To30: 'Quá hạn 1–30 ngày', Overdue31To60: 'Quá hạn 31–60 ngày', Overdue61To90: 'Quá hạn 61–90 ngày', OverdueOver90: 'Quá hạn trên 90 ngày' };
export default function ReceivablesPanel({ access }: { access: WorkspaceAccess | null }) {
 const [start, setStart] = useState(() => new Date().toISOString().slice(0, 8) + '01');
 const [end, setEnd] = useState(() => { const now = new Date(); return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1)).toISOString().slice(0, 10); });
 const allowed = !!access && (access.operatorAdmin || access.facilityRoles.includes('Finance'));
 const span = (Date.parse(end) - Date.parse(start)) / 86400000;
 const valid = /^\d{4}-\d{2}-\d{2}$/.test(start) && /^\d{4}-\d{2}-\d{2}$/.test(end) && span > 0 && span <= 366;
 if (!allowed) return null;
 const binding = JSON.stringify([access.userId, access.organizationId, access.facilityId, access.facilityRoles, access.operatorAdmin, start, end]);
 return <section className="quote-panel" aria-labelledby="receivables-title">
  <h3 id="receivables-title">Đối soát công nợ</h3>
  <p>Số dư hiện tại của bảng kê đã phát hành theo hạn thanh toán. Không gồm tiền đặt cọc đang giữ, phiếu thu chưa phân bổ và thanh toán trực tuyến đang chờ. Đây không phải báo cáo số dư lịch sử hay báo cáo thuế.</p>
  <label>Hạn thanh toán từ<input type="date" value={start} onChange={e => setStart(e.target.value)} /></label>
  <label>Hạn thanh toán đến (không tính)<input type="date" value={end} onChange={e => setEnd(e.target.value)} /></label>
  <p>Chọn tối đa 366 ngày. Để xem nợ cũ hơn, chọn khoảng hạn thanh toán sớm hơn. Khoản đến hạn hôm nay chưa tính quá hạn.</p>
  {valid && access.organizationId && access.facilityId ? <ReceivableResults key={binding} access={access} start={start} end={end} /> : <button disabled>Tải công nợ</button>}
 </section>;
}
function ReceivableResults({ access, start, end }: { access: WorkspaceAccess; start: string; end: string }) {
 const [snapshot, setSnapshot] = useState<Snapshot | null>(null); const [error, setError] = useState('');
 const [busy, setBusy] = useState(false); const request = useRef<AbortController | null>(null);
 useEffect(() => () => { request.current?.abort(); }, []);
 const query = new URLSearchParams({ organizationId: access.organizationId, facilityId: access.facilityId, startDate: start, endDate: end });
 async function read(page = 0) {
  if (busy) return;
  const controller = new AbortController(); request.current = controller; setBusy(true); setError(''); setSnapshot(null);
  try {
   const value = await api<Snapshot>(`/reports/receivables?${query}&page=${page}`, controller.signal);
   if (!controller.signal.aborted) setSnapshot(value);
  } catch (failure) {
   if (!controller.signal.aborted) setError(failure instanceof ApiError && failure.status === 409
    ? 'Hồ sơ tài chính cần đối soát. Chỉ có thể xuất sau khi xử lý các bản ghi liên kết.'
    : 'Không tải được công nợ. Kiểm tra quyền tài chính hiện tại và khoảng hạn thanh toán có tối đa 5000 bảng kê.');
  } finally { if (!controller.signal.aborted) setBusy(false); }
 }
 return <div aria-busy={busy}>
  <button disabled={busy} onClick={() => void read()}>Tải công nợ</button>
  {error && <p role="status">{error}</p>}
  {snapshot && <div className="quote-result">
   <p>{snapshot.organizationName} · tổ chức {access?.organizationId} · cơ sở {access?.facilityId}</p>
   <p>Ghi nhận lúc {snapshot.observedAt}; ngày tính tuổi nợ {snapshot.asOfDate} ({snapshot.timeZone}). Dữ liệu số dư hiện tại đã đối soát.</p>
   <dl>{([['Bảng kê phù hợp', String(snapshot.summary.matchedStatements)], ['Bảng kê còn nợ', String(snapshot.summary.openStatements)], ['Khoản thu sau điều chỉnh · VND', snapshot.summary.effectiveChargeVnd], ['Đã trả sau điều chỉnh · VND', snapshot.summary.effectivePaidVnd], ['Còn nợ · VND', snapshot.summary.outstandingVnd], ['Chưa quá hạn · VND', snapshot.summary.currentVnd], ['Quá hạn 1–30 ngày · VND', snapshot.summary.overdue1To30Vnd], ['Quá hạn 31–60 ngày · VND', snapshot.summary.overdue31To60Vnd], ['Quá hạn 61–90 ngày · VND', snapshot.summary.overdue61To90Vnd], ['Quá hạn trên 90 ngày · VND', snapshot.summary.overdueOver90Vnd]]).map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
   <p>Tổng gồm mọi bảng kê phù hợp trong khoảng hạn thanh toán đã chọn. Trang {snapshot.page + 1} có tối đa 50 dòng. Mỗi lần tải hoặc chuyển trang đều đọc số dư hiện tại.</p>
   <div className="table-scroll" role="region" aria-label="Bảng chi tiết công nợ" tabIndex={0}><table><caption>Đối soát bảng kê đã phát hành · VND</caption>
    <thead><tr>{['Bảng kê', 'Hợp đồng / tài khoản', 'Đầu kỳ', 'Hạn thanh toán', 'Ban đầu', 'Bổ sung', 'Giảm trừ', 'Phân bổ', 'Khấu trừ tiền đặt cọc', 'Hoàn tác phân bổ', 'Khoản thu sau điều chỉnh', 'Đã trả sau điều chỉnh', 'Còn nợ', 'Số ngày quá hạn', 'Nhóm tuổi nợ'].map(label => <th scope="col" key={label}>{label}</th>)}</tr></thead>
    <tbody>{snapshot.items.map(row => <tr key={row.statementId}><th scope="row">{row.statementId}</th><td>{row.leaseId}<br />{row.rentalAccountId}</td><td>{row.periodStart}</td><td>{row.dueDate}</td>{[row.originalVnd, row.additionsVnd, row.creditsVnd, row.allocationsVnd, row.deductionsVnd, row.releasesVnd, row.effectiveChargeVnd, row.effectivePaidVnd, row.outstandingVnd].map((value, index) => <td key={index}>{value}</td>)}<td>{row.daysOverdue}</td><td>{bucketLabels[row.bucket] ?? 'Chưa xác định'}</td></tr>)}</tbody>
   </table></div>
   {snapshot.items.length === 0 && <p>Không có bảng kê đã phát hành phù hợp với trang và khoảng hạn thanh toán này.</p>}
   <div className="session-actions"><button disabled={busy || snapshot.page === 0} onClick={() => void read(snapshot.page - 1)}>Trang công nợ trước</button><button disabled={busy || snapshot.nextPage === null} onClick={() => void read(snapshot.nextPage!)}>Trang công nợ sau</button>
    <a href={`/api/v1/exports/receivables?${query}`}>Xuất CSV công nợ</a></div>
   <p>Tệp CSV tạo ảnh chụp dữ liệu mới cho toàn bộ khoảng đã chọn và ghi nhật ký xuất. Dữ liệu có thể khác lần xem này nếu hồ sơ tài chính thay đổi.</p>
  </div>}
 </div>;
}
