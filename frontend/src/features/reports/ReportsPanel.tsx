import { displayValue, noticeText } from '../../locale/vi';
import ReceivableReminders from './ReceivableReminders';
import ReceivablesPanel from './ReceivablesPanel';
import { useEffect, useRef, useState } from 'react';
import { api, command, isDefiniteRejection } from '../../api';
import type { WorkspaceAccess } from '../auth/workspace';
import { noticeTarget, type NoticeTarget } from './noticeTarget';
type Metrics = { scheduled: { bookedAreaDays: number; rentableAreaDays: number; maintenanceAreaDays: number; utilizationPercent: number | null; source: string };
 physical: { occupiedAreaM2: number; currentCatalogAreaM2: number; source: string }; collections: { grossVerifiedReceiptsVnd: number; recordedRefundsVnd: number; heldDepositNowVnd: number; source: string } };
type Pending = { context: string; key: string; path: string; body: { page: number } };
export default function ReportsPanel({ access, onOpenRecord }: { access: WorkspaceAccess | null; onOpenRecord?: (target: NoticeTarget) => void }) {
 const context = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]);
 const [start, setStart] = useState(() => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`; }); const [end, setEnd] = useState(() => { const d = new Date(); d.setMonth(d.getMonth() + 1, 1); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`; }); const [page, setPage] = useState(1);
 const [report, setReport] = useState<{ context: string; interval: string; value: Metrics } | null>(null);
 const [notices, setNotices] = useState<{ context: string; items: { id: string; message: string; aggregateId?: string; kind: string; createdAt: string }[]; total: number; page: number } | null>(null);
 const [pending, setPending] = useState<Pending | null>(null); const [busy, setBusy] = useState(false); const [message, setMessage] = useState('');
 const current = useRef(context); const lifecycle = useRef<AbortController | null>(null); const running = useRef(false);
 useEffect(() => { current.current = context; }, [context]); useEffect(() => { const c = new AbortController(); lifecycle.current = c; return () => c.abort(); }, []);
 const manager = access?.operatorAdmin || access?.facilityRoles.includes('FacilityManager'); const metricAllowed = manager || access?.facilityRoles.some(x => ['Finance', 'Sales'].includes(x));
 const scoped = !!access?.organizationId && !!access.facilityId; const interval = JSON.stringify([start, end]); const range = !!start && !!end && start < end;
 async function read(kind: 'metrics' | 'notices', number = 1) {
  const c = lifecycle.current; if (!c || running.current) return; const owner = context; running.current = true; setBusy(true);
  try {
   if (kind === 'metrics') { const value = await api<Metrics>(`/reports/facilities/${access?.facilityId}?startDate=${start}&endDate=${end}`, c.signal); if (!c.signal.aborted && current.current === owner) setReport({ context: owner, interval, value }); }
   else { const data = await api<{ items: NonNullable<typeof notices>['items']; total: number }>(`/notices?organizationId=${access?.organizationId}&facilityId=${access?.facilityId}&page=${number}`, c.signal); if (!c.signal.aborted && current.current === owner) setNotices({ context: owner, items: data.items, total: data.total, page: number }); }
  } catch { if (!c.signal.aborted) setMessage('Không tải được báo cáo hoặc thông báo. Kiểm tra khoảng đã chọn và phạm vi hiện tại.'); }
  finally { running.current = false; if (!c.signal.aborted) setBusy(false); }
 }
 async function attempt(original: Pending, retry = false) {
  const c = lifecycle.current; if (!c || running.current || original.context !== context || !manager) return; running.current = true; setBusy(true); setPending(original);
  try { const value = await command<{ result: { processed: number } }>(original.path, original.body, original.key, c.signal); if (!c.signal.aborted) { setPending(null); setMessage(`Đã xử lý ${value.result.processed} bản ghi thông báo. Xếp hàng và phát thông báo trong ứng dụng là các bước riêng.`); } }
  catch (error) { if (!c.signal.aborted) { if (isDefiniteRejection(error) && !retry) { setPending(null); setMessage('Tác vụ thông báo bị từ chối. Làm mới quyền truy cập hiện tại.'); } else setMessage('Chưa xác định kết quả tác vụ. Giữ yêu cầu gốc và thử lại sau khi khôi phục truy cập.'); } }
  finally { running.current = false; if (!c.signal.aborted) setBusy(false); }
 }
 const metrics = report?.context === context && report.interval === interval ? report.value : null; const visible = notices?.context === context ? notices : null;
 const query = new URLSearchParams({ organizationId: access?.organizationId ?? '', facilityId: access?.facilityId ?? '', startDate: start, endDate: end });
 const finance = access?.organizationRole === 'CustomerAdmin' || access?.operatorAdmin || access?.facilityRoles.includes('Finance'); const stock = !!access?.organizationId || access?.operatorAdmin || access?.facilityRoles.some(x => ['Clerk', 'FacilityManager'].includes(x));
 return <section className="workspace-panel" aria-labelledby="reports-title" aria-busy={busy}><div className="panel-heading"><h2 id="reports-title">Báo cáo và thông báo trong ứng dụng</h2></div><div className="session-content">
  <label>Ngày bắt đầu báo cáo<input type="date" value={start} onChange={e => setStart(e.target.value)} /></label><label>Ngày kết thúc báo cáo (không tính)<input type="date" value={end} onChange={e => setEnd(e.target.value)} /></label>
  <p>Chọn tối đa 366 ngày. Lưu giữ thực tế và tiền đặt cọc đang giữ được tách khỏi chỉ số theo kỳ. Tệp xuất có tối đa 5000 dòng; thu hẹp khoảng nếu cần.</p>
  {metricAllowed && <button disabled={busy || !access?.facilityId || !range} onClick={() => void read('metrics')}>Tải chỉ số cơ sở</button>}
  {metrics && <><h3>Diện tích theo lịch · khoảng đã chọn</h3><dl><dt>Diện tích × ngày đã đặt</dt><dd>{metrics.scheduled.bookedAreaDays}</dd><dt>Diện tích × ngày có thể thuê sau bảo trì</dt><dd>{metrics.scheduled.rentableAreaDays}</dd><dt>Diện tích × ngày loại trừ do bảo trì</dt><dd>{metrics.scheduled.maintenanceAreaDays}</dd><dt>Tỷ lệ sử dụng theo lịch</dt><dd>{metrics.scheduled.utilizationPercent === null ? 'Không có diện tích × ngày có thể thuê để tính' : `${metrics.scheduled.utilizationPercent}%`}</dd></dl><p>Chỉ số tính theo lịch thuê đã xác nhận và các khoảng bảo trì trong kỳ.</p>
   <h3>Diện tích thực tế · thời điểm hiện tại</h3><p>{metrics.physical.occupiedAreaM2} m² đang sử dụng / {metrics.physical.currentCatalogAreaM2} m² trong danh mục hiện tại</p><p>Diện tích hàng đang được lưu giữ tại thời điểm tra cứu.</p>
   <h3>Tiền · VND</h3><dl><dt>Tổng thu đã xác minh · trong kỳ</dt><dd>{metrics.collections.grossVerifiedReceiptsVnd}</dd><dt>Hoàn tiền đã ghi · trong kỳ</dt><dd>{metrics.collections.recordedRefundsVnd}</dd><dt>Tiền đặt cọc đang giữ · hiện tại</dt><dd>{metrics.collections.heldDepositNowVnd}</dd></dl><p>Phiếu thu đã xác minh và khoản hoàn tiền đã ghi trong kỳ; tiền đặt cọc đang giữ được theo dõi riêng.</p></>}
  {scoped && range && <div className="session-actions">{stock && <a href={`/api/v1/exports/stock?${query}`}>Xuất CSV tồn kho</a>}{finance && <a href={`/api/v1/exports/statements?${query}`}>Xuất CSV bảng kê</a>}</div>}
  <ReceivablesPanel access={access} />
  <ReceivableReminders access={access} />
  <h3>Thông báo trong ứng dụng</h3><p>Thông báo ghi lại một sự kiện. Mở yêu cầu hoặc hợp đồng liên quan để xác minh trạng thái hiện tại.</p><button disabled={busy || !scoped} onClick={() => void read('notices')}>Tải thông báo</button>
  {visible && <><ul className="notice-list">{visible.items.map(x => {
   const target = noticeTarget(x.kind, x.aggregateId);
   return <li key={x.id}><div>{noticeText(x.kind, x.message, x.aggregateId)}<small> · {displayValue(x.kind)} · {new Date(x.createdAt).toLocaleString('vi-VN')}</small></div>
    {target && onOpenRecord && <button type="button" className="secondary-button" onClick={() => onOpenRecord(target)}>{target.kind === 'lease' ? 'Mở hợp đồng liên quan' : 'Mở yêu cầu nhập / xuất liên quan'}</button>}</li>;
  })}</ul><div className="session-actions"><button disabled={busy || visible.page <= 1} onClick={() => void read('notices', visible.page - 1)}>Trang thông báo trước</button><button disabled={busy || visible.page * 50 >= visible.total} onClick={() => void read('notices', visible.page + 1)}>Trang thông báo sau</button></div></>}
  {manager && <><h3>Tác vụ thông báo</h3><p>Xếp hàng một trang hợp đồng và yêu cầu kho đủ điều kiện, sau đó phát các lô đang chờ. Các thao tác này chỉ gửi thông báo trong ứng dụng, không gửi email.</p><label>Trang nguồn thông báo<input type="number" min={1} max={10000} disabled={!!pending} value={page} onChange={e => setPage(Number(e.target.value))} /></label><div className="session-actions">{['enqueue', 'dispatch'].map(operation => <button key={operation} disabled={busy || !!pending || !access?.facilityId || !Number.isInteger(page) || page < 1 || page > 10000} onClick={() => void attempt({ context, key: crypto.randomUUID(), path: `/facilities/${access?.facilityId}/notice-jobs/${operation}`, body: { page } })}>{operation === 'enqueue' ? 'Xếp hàng lô thông báo' : 'Phát thông báo đang chờ'}</button>)}</div></>}
  {pending && <button disabled={busy || pending.context !== context || !manager} onClick={() => void attempt(pending, true)}>Thử lại tác vụ thông báo gốc</button>}{message && <p role="status">{message}</p>}
 </div></section>;
}
