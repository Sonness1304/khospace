import { useEffect, useRef, useState } from 'react';
import { api, command, isDefiniteRejection } from '../../api';
import type { WorkspaceAccess } from '../auth/workspace';
type Document = { id: string; fileName: string; contentType: string; bytes: number; sha256: string; revokedAt: string | null };
type Pending = { context: string; key: string; path: string; body: unknown };
export default function DocumentsPanel({ access, selectedLeaseId }: { access: WorkspaceAccess | null; selectedLeaseId?: string }) {
  const owner = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]);
  const [choice, setChoice] = useState({ owner, lease: selectedLeaseId ?? '' }); const lease = choice.owner === owner ? choice.lease : '';
  const leaseId = selectedLeaseId || lease; const context = JSON.stringify([owner, leaseId]);
  const [reasonDraft, setReason] = useState({ context, value: '' }); const reason = reasonDraft.context === context ? reasonDraft.value : '';
  const [file, setFile] = useState<{ context: string; value: File } | null>(null);
  const [listing, setListing] = useState<{ context: string; items: Document[]; page: number; total: number } | null>(null);
  const [pending, setPending] = useState<Pending | null>(null); const [busy, setBusy] = useState(false); const [message, setMessage] = useState('');
  const lifecycle = useRef<AbortController | null>(null); const running = useRef(false); const current = useRef(context);
  useEffect(() => { current.current = context; }, [context]);
  useEffect(() => { const c = new AbortController(); lifecycle.current = c; return () => c.abort(); }, []);
  const canUse = !!access && (!!access.organizationId || !!access.facilityId || access.operatorAdmin);
  const manage = access?.organizationRole === 'CustomerAdmin' || access?.operatorAdmin || access?.facilityRoles.includes('FacilityManager');
  async function read(page = 1) {
    const c = lifecycle.current; if (!c || running.current || !canUse || !leaseId) return; const scope = context; running.current = true; setBusy(true);
    try { const data = await api<{ items: Document[]; total: number }>(`/leases/${encodeURIComponent(leaseId)}/documents?page=${page}`, c.signal); if (!c.signal.aborted && current.current === scope) setListing({ ...data, page, context: scope }); }
    catch { if (!c.signal.aborted) setMessage('Không tải được danh sách tài liệu riêng tư. Kiểm tra quyền truy cập hiện tại.'); }
    finally { running.current = false; if (!c.signal.aborted) setBusy(false); }
  }
  async function attempt(original: Pending, retry = false) {
    const c = lifecycle.current; if (!c || running.current || original.context !== context || !canUse) return;
    running.current = true; setBusy(true); setPending(original);
    try { await command(original.path, original.body, original.key, c.signal); if (!c.signal.aborted) { setPending(null); setListing(null); setFile(null); setMessage('Đã hoàn tất yêu cầu tài liệu. Tải lại danh sách tài liệu riêng tư.'); } }
    catch (error) { if (!c.signal.aborted) { if (isDefiniteRejection(error) && !retry) { setPending(null); setMessage('Yêu cầu tài liệu bị từ chối. Kiểm tra nội dung, quyền truy cập và lý do.'); } else setMessage('Chưa xác định kết quả tài liệu. Giữ và thử lại yêu cầu gốc.'); } }
    finally { running.current = false; if (!c.signal.aborted) setBusy(false); }
  }
  async function upload() {
    const value = file?.context === context ? file.value : null; if (!value || pending || running.current) return;
    const original = context; setBusy(true); running.current = true;
    try {
      const base64 = await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onerror = () => reject(Error('Không đọc được tệp')); reader.onload = () => resolve(String(reader.result).split(',')[1]); reader.readAsDataURL(value); });
      if (current.current !== original || lifecycle.current?.signal.aborted) return;
      running.current = false;
      await attempt({ context: original, key: crypto.randomUUID(), path: `/leases/${encodeURIComponent(leaseId)}/documents`, body: { fileName: value.name, contentType: value.type, contentBase64: base64, reason } });
    } catch { setMessage('Không thể đọc tệp. Vui lòng chọn lại.'); }
    finally { running.current = false; if (!lifecycle.current?.signal.aborted) setBusy(false); }
  }
  const visible = listing?.context === context ? listing : null; const blocked = busy || !!pending;
  return <section className="workspace-panel" aria-labelledby="documents-title" aria-busy={busy}><div className="panel-heading"><h2 id="documents-title">Tài liệu hợp đồng riêng tư</h2></div><div className="session-content">
    <p>Chấp nhận JPEG, PNG hoặc PDF, tối đa 10 MiB. Tệp luôn riêng tư và quyền truy cập được kiểm tra mỗi lần tải xuống. Cần triển khai quét mã độc trước khi đưa vào vận hành chính thức.</p>
    <label>ID hợp đồng của tài liệu<input maxLength={36} disabled={blocked || !!selectedLeaseId} value={leaseId} onChange={e => setChoice({ owner, lease: e.target.value })} /></label>
    <label>Lý do thao tác tài liệu<input maxLength={500} value={reason} disabled={!!pending} onChange={e => setReason({ context, value: e.target.value })} /></label>
    <label>Tệp tài liệu riêng tư<input key={context + (file ? '' : '-empty')} type="file" accept="image/jpeg,image/png,application/pdf" disabled={blocked || !canUse} onChange={e => { const value = e.target.files?.[0]; if (value && value.size > 10 * 1024 * 1024) { setFile(null); setMessage('Chọn tệp đúng định dạng, tối đa 10 MiB.'); } else setFile(value ? { context, value } : null); }} /></label>
    <div className="session-actions"><button disabled={blocked || !canUse || !leaseId || file?.context !== context || !reason.trim()} onClick={() => void upload()}>Tải lên tài liệu riêng tư</button><button disabled={busy || !canUse || !leaseId} onClick={() => void read()}>Tải danh sách tài liệu</button></div>
    {message && <p role="status">{message}</p>}{pending && <button disabled={busy || pending.context !== context || !canUse} onClick={() => void attempt(pending, true)}>Thử lại yêu cầu tài liệu gốc</button>}
    {visible && <><ul>{visible.items.map(x => <li key={x.id}>{x.fileName} · {x.bytes} byte · {x.revokedAt ? 'Đã thu hồi' : <a href={`/api/v1/documents/${encodeURIComponent(x.id)}/download`}>Tải xuống {x.fileName}</a>}{manage && <button disabled={blocked || !!x.revokedAt || !reason.trim()} onClick={() => void attempt({ context, key: crypto.randomUUID(), path: `/documents/${encodeURIComponent(x.id)}/revoke`, body: { reason } })}>Thu hồi {x.fileName}</button>}</li>)}</ul><div className="session-actions"><button disabled={busy || visible.page <= 1} onClick={() => void read(visible.page - 1)}>Trang tài liệu trước</button><button disabled={busy || visible.page * 50 >= visible.total} onClick={() => void read(visible.page + 1)}>Trang tài liệu sau</button></div></>}
  </div></section>;
}
