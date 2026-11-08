import { reasonLabel } from '../../locale/vi';
import { useEffect, useRef, useState, type FormEvent } from 'react';
import type { WorkspaceAccess } from '../auth/workspace';
import { api, command } from '../../api';
import RenewalActions from './RenewalActions';
type Preview = { leaseId: string; quoteId: string; leaseVersion?: string; quoteVersion?: string; currentEnd: string; proposedEnd: string; addedRentalSubtotal: string; canApply: boolean; reasons: string[]; capacityReserved: boolean; applied: boolean; observedAt?: string; requiredDeposit?: string; additionalDeposit?: string };
export default function RenewalPreviewPanel({ access }: { access: WorkspaceAccess | null }) {
  const [leaseId, setLeaseId] = useState(''); const [quoteId, setQuoteId] = useState('');
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState('');
  const owner = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId, access?.organizationRole, access?.facilityRoles, access?.operatorAdmin]);
  const [draftOwner, setDraftOwner] = useState(owner); const [data, setData] = useState<{ binding: string; value: Preview } | null>(null);
  const binding = JSON.stringify([owner, leaseId, quoteId]); const running = useRef(false); const lifecycle = useRef<AbortController | null>(null);
  if (owner !== draftOwner) { setDraftOwner(owner); setLeaseId(''); setQuoteId(''); setMessage(''); setData(null); }
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  const permitted = !!access && (access.operatorAdmin || access.organizationRole === 'CustomerAdmin' || access.facilityRoles.includes('Sales'));
  async function preview(event: FormEvent) {
    event.preventDefault(); const controller = lifecycle.current; if (!controller || running.current || !permitted) return;
    running.current = true; setBusy(true); setData(null); setMessage('');
    try {
      const lease = await api<{ id: string; version: string }>(`/leases/${encodeURIComponent(leaseId.trim())}`, controller.signal);
      const quote = await api<{ id: string; version: string }>(`/quotes/${encodeURIComponent(quoteId.trim())}`, controller.signal);
      if (!lease.version || !quote.version) throw Error('Chưa xác minh phiên bản');
      const value = await command<Preview>(`/leases/${encodeURIComponent(lease.id)}/renewal-preview`, { quoteId: quote.id, expectedLeaseVersion: lease.version, expectedQuoteVersion: quote.version }, crypto.randomUUID(), controller.signal);
      if (typeof value.addedRentalSubtotal !== 'string' || !Array.isArray(value.reasons) || value.applied !== false || value.capacityReserved !== false) throw Error('Chưa xác minh bản xem trước');
      if (!controller.signal.aborted) setData({ binding, value });
    } catch { if (!controller.signal.aborted) setMessage('Chưa xác minh được bản xem trước gia hạn. Tải lại hợp đồng và báo giá rồi thử lại.'); }
    finally { running.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  const current = data?.binding === binding ? data.value : null;
  return <section className="workspace-panel" aria-labelledby="renewal-preview-title" aria-busy={busy}><div className="panel-heading"><h2 id="renewal-preview-title">Xem trước gia hạn hợp đồng</h2></div><div className="session-content">
    <p>Yêu cầu báo giá mới cho các khoang hiện tại, bắt đầu từ ngày kết thúc hợp đồng. Kiểm tra tiền thuê bổ sung và xung đột lịch. Bản xem trước không giữ chỗ hay thay đổi hợp đồng.</p>
    <form onSubmit={e => void preview(e)}><label>ID hợp đồng cần gia hạn<input required value={leaseId} onChange={e => setLeaseId(e.target.value)} /></label><label>ID báo giá kỳ bổ sung<input required value={quoteId} onChange={e => setQuoteId(e.target.value)} /></label><button disabled={busy || !permitted}>Xem trước gia hạn</button></form>
    {message && <p role="status">{message}</p>}{current && <div className="booking-detail"><p>Kết thúc hiện tại: {current.currentEnd} · Kết thúc đề xuất: {current.proposedEnd}</p><p>Tiền thuê bổ sung: <strong>{current.addedRentalSubtotal} VND</strong></p><p>Tiền đặt cọc hiện tại yêu cầu: {current.requiredDeposit ?? 'Chưa xác minh'} VND · Tiền đặt cọc bổ sung: {current.additionalDeposit ?? 'Chưa xác minh'} VND</p><p>{current.canApply ? 'Lịch đáp ứng điều kiện tại thời điểm kiểm tra này.' : 'Gia hạn cần được xem xét.'}</p>{current.observedAt && <p>Đã kiểm tra lúc <time dateTime={current.observedAt}>{new Date(current.observedAt).toLocaleString('vi-VN')}</time></p>}{current.reasons.map(x => <p key={x}>{reasonLabel(x)}</p>)}<p>Gia hạn giữ nguyên tiền đặt cọc hiện tại. Cần phê duyệt và điều chỉnh bảng kê đã phát hành, nếu có, trước khi áp dụng.</p></div>}
    <RenewalActions access={access} preview={current?.leaseVersion && current?.quoteVersion ? { ...current, leaseVersion: current.leaseVersion, quoteVersion: current.quoteVersion } : null} />
  </div></section>;
}
