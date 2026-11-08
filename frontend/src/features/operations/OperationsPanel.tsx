import { auditActionLabel, displayValue } from '../../locale/vi';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import type { WorkspaceAccess } from '../auth/workspace';
import { api } from '../../api';
export type Selection = { kind: 'booking' | 'lease' | 'request' | 'grant'; id: string; accountId?: string };
type Kind = 'bookings' | 'leases' | 'stock-requests' | 'calendar' | 'audit' | 'conflicts';
type Row = { id: string; code?: string; organizationName?: string; state?: string; startDate?: string; endDate?: string; rentalAccountId?: string; action?: string; reason?: string; actorId?: string; correlationId?: string; at?: string;
  schedules?: { id: string; state: string; startDate: string; endDate: string; organizationName?: string }[]; custody?: { id: string; state: string; scheduledEnd: string; leaseUnitId: string }[] };
type Affected = { bookingId: string; unitId: string; organizationId: string; startDate: string; endDate: string };
const localDate = (days: number) => new Date(Date.now() + 7 * 3600000 + days * 86400000).toISOString().slice(0, 10);
export default function OperationsPanel({ access, onSelect }: { access: WorkspaceAccess | null; onSelect: (value: Selection) => void }) {
  const [search, setSearch] = useState(''); const [start, setStart] = useState(localDate(0)); const [end, setEnd] = useState(localDate(30));
  const [data, setData] = useState<{ signature: string; items: Row[]; total: number; page: number; kind: Kind; search: string; affected: Affected[]; affectedTotal: number; affectedPage: number } | null>(null);
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState(''); const lifecycle = useRef<AbortController | null>(null); const active = useRef(false);
  const signature = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId, access?.facilityRoles, access?.operatorAdmin]);
  const visible = data?.signature === signature ? data : null; const staff = !!access && (access.operatorAdmin || access.facilityRoles.length > 0);
  const draftBinding = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]); const [draftOwner, setDraftOwner] = useState(draftBinding);
  if (draftOwner !== draftBinding) { setDraftOwner(draftBinding); setSearch(''); setMessage(''); }
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  async function read(kind: Kind, page = 1, term = search, event?: FormEvent, affectedPage = 1) {
    event?.preventDefault(); const controller = lifecycle.current; if (!controller || active.current || !access || !access.facilityId || !staff && !access.organizationId) return;
    active.current = true; setBusy(true); setMessage('');
    const query = new URLSearchParams({ facilityId: access.facilityId, page: String(page), search: term });
    if (access.organizationId) query.set('organizationId', access.organizationId);
    if (kind === 'calendar') { query.set('startDate', start); query.set('endDate', end); }
    if (kind === 'conflicts') query.set('affectedPage', String(affectedPage));
    try { const result = await api<{ items: Row[]; total: number; affectedBookings?: Affected[]; affectedTotal?: number; affectedPage?: number }>(`/${kind === 'calendar' || kind === 'conflicts' ? `operations/${kind}` : kind}?${query}`, controller.signal); if (!Array.isArray(result.items)) throw Error('Chưa xác minh danh sách');
      if (!controller.signal.aborted) setData({ signature, items: result.items, total: result.total, page, kind, search: term, affected: result.affectedBookings ?? [], affectedTotal: result.affectedTotal ?? 0, affectedPage: result.affectedPage ?? 1 }); }
    catch { if (!controller.signal.aborted) { setData(null); setMessage('Chưa xác minh được danh sách làm việc. Kiểm tra cơ sở, ngày và phạm vi hiện tại.'); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  return <section className="workspace-panel" aria-labelledby="operations-title" aria-busy={busy}>
    <div className="panel-heading"><div><p className="eyebrow">LỊCH THUÊ VÀ THỰC TẾ</p><h2 id="operations-title">Điều phối thuê và lịch kho</h2></div></div>
    <div className="session-content"><p className="catalog-help">Chọn rõ cơ sở kho trong ngữ cảnh truy cập. Đặt chỗ theo lịch và lưu giữ hàng thực tế được hiển thị riêng. Dữ liệu trang hiện tại phản ánh thời điểm tra cứu, chưa giữ chỗ.</p>
      <label>Tìm khách hàng, trạng thái hoặc khoang<input maxLength={100} value={search} onChange={e => setSearch(e.target.value)} /></label>
      <div className="session-actions"><button disabled={!access || busy || !access.facilityId} onClick={() => void read('bookings')}>Tải danh sách đặt chỗ</button><button disabled={!access || busy || !access.facilityId} onClick={() => void read('leases')}>Tải danh sách hợp đồng</button><button disabled={!access || busy || !access.facilityId} onClick={() => void read('stock-requests')}>Tải yêu cầu nhập xuất</button></div>
      {staff && <><div className="manager-fields"><label>Bắt đầu lịch<input type="date" value={start} onChange={e => setStart(e.target.value)} /></label><label>Kết thúc lịch (không tính)<input type="date" value={end} onChange={e => setEnd(e.target.value)} /></label></div><div className="session-actions"><button disabled={busy || !access.facilityId} onClick={() => void read('calendar')}>Tải lịch kho</button><button disabled={busy || !access.facilityId} onClick={() => void read('conflicts')}>Tải xung đột quá hạn</button><button disabled={busy || !access.facilityId} onClick={() => void read('audit')}>Tải nhật ký cơ sở</button></div></>}
      {message && <p role="status" className="session-notice">{message}</p>}
      {visible && <div className="operations-results"><p>{visible.total} bản ghi phù hợp · Trang {visible.page}</p>{!visible.items.length && <p>Không có bản ghi phù hợp trên trang này.</p>}
        {visible.items.map(row => <article className="booking-detail" key={row.id}>{visible.kind === 'calendar' ? <><h3>{row.code}</h3><h4>Lịch theo hợp đồng</h4>{row.schedules?.length ? row.schedules.map(x => <p key={x.id}><strong>{displayValue(x.state)}</strong> · {x.startDate} đến {x.endDate} · {x.organizationName ?? 'Bảo trì'}</p>) : <p>Không có lịch cam kết trong khoảng này.</p>}<h4>Lưu giữ thực tế</h4>{row.custody?.length ? row.custody.map(x => <div key={x.id}><p><strong>{displayValue(x.state)}</strong> · Kết thúc dự kiến {x.scheduledEnd}</p><button disabled={busy} onClick={() => onSelect({ kind: 'grant', id: x.leaseUnitId })}>Mở tồn kho {x.leaseUnitId}</button></div>) : <p>Chưa ghi nhận lưu giữ trong khoảng này.</p>}</>
          : visible.kind === 'conflicts' ? <><strong>{row.code}</strong><p>Tình trạng chiếm dụng thực tế chưa được giải quyết. Các đặt chỗ tương lai đã xác nhận vẫn được giữ.</p></> : visible.kind === 'audit' ? <><strong>{auditActionLabel(row.action)}</strong><p>{row.reason}</p><p>{row.actorId} · {row.at}</p><small>Mã liên kết {row.correlationId}</small></>
          : <><p>{row.organizationName} · <strong>{displayValue(row.state)}</strong></p><p>{row.startDate} đến {row.endDate}</p><p>ID {row.id}</p><button disabled={busy} onClick={() => onSelect({ kind: visible.kind === 'bookings' ? 'booking' : visible.kind === 'leases' ? 'lease' : 'request', id: row.id, ...(row.rentalAccountId ? { accountId: row.rentalAccountId } : {}) })}>Mở {visible.kind === 'bookings' ? 'đặt chỗ' : visible.kind === 'leases' ? 'hợp đồng' : 'yêu cầu'} {row.id}</button></>}
        </article>)}
        {visible.kind === 'conflicts' && <section aria-label="Đặt chỗ bị ảnh hưởng"><h3>Đặt chỗ đã xác nhận bị ảnh hưởng</h3><p>{visible.affectedTotal} đặt chỗ của khoang bị ảnh hưởng · Trang {visible.affectedPage}</p>{visible.affected.map(x => <article className="booking-detail" key={`${x.bookingId}-${x.unitId}`}><p>Tổ chức {x.organizationId} · Khoang {x.unitId}</p><p>{x.startDate} đến {x.endDate}</p><button disabled={busy} onClick={() => onSelect({ kind: 'booking', id: x.bookingId })}>Mở đặt chỗ bị ảnh hưởng {x.bookingId}</button></article>)}<div className="session-actions"><button disabled={busy || visible.affectedPage <= 1} onClick={() => void read('conflicts', visible.page, visible.search, undefined, visible.affectedPage - 1)}>Trang đặt chỗ bị ảnh hưởng trước</button><button disabled={busy || visible.affectedPage * 50 >= visible.affectedTotal} onClick={() => void read('conflicts', visible.page, visible.search, undefined, visible.affectedPage + 1)}>Trang đặt chỗ bị ảnh hưởng sau</button></div></section>}
        <div className="session-actions"><button disabled={busy || visible.page <= 1} onClick={() => void read(visible.kind, visible.page - 1, visible.search)}>Trang kết quả trước</button><button disabled={busy || visible.page * 50 >= visible.total} onClick={() => void read(visible.kind, visible.page + 1, visible.search)}>Trang kết quả sau</button></div>
      </div>}
    </div></section>;
}
