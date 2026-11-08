import { displayValue } from '../../locale/vi';
import { useEffect, useRef, useState } from 'react';
import { api, command, isDefiniteRejection } from '../../api';
import type { WorkspaceAccess } from '../auth/workspace';

type Invitation = { id: string; email: string; role: string; expiresAt: string; consumedAt: string | null; revokedAt: string | null };
type Directory = { version: string; total: number; items: { id: string; userName: string; active: boolean; operatorAdmin: boolean }[];
  grants: { userId: string; facilityId: string; staffRole: string }[] };
type Pending = { key: string; context: string; path: string; body: unknown; version?: string };

export default function IdentityOperationsPanel({ access }: { access: WorkspaceAccess | null }) {
  const context = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]);
  const [draft, setDraft] = useState({ context, email: '', role: 'Member', reason: '' });
  const fields = draft.context === context ? draft : { context, email: '', role: 'Member', reason: '' };
  const [invites, setInvites] = useState<{ context: string; items: Invitation[]; page: number; total: number } | null>(null);
  const [directory, setDirectory] = useState<{ context: string; data: Directory; page: number } | null>(null);
  const [selection, setSelection] = useState({ context, userId: '', staffRole: 'Clerk' });
  const selected = selection.context === context ? selection : { context, userId: '', staffRole: 'Clerk' };
  const [pending, setPending] = useState<Pending | null>(null); const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState(''); const running = useRef(false); const lifecycle = useRef<AbortController | null>(null);
  const currentContext = useRef(context); useEffect(() => { currentContext.current = context; }, [context]);
  useEffect(() => { const c = new AbortController(); lifecycle.current = c; return () => c.abort(); }, []);
  const admin = access?.organizationRole === 'CustomerAdmin' && !!access.organizationId;
  const operator = !!access?.operatorAdmin;
  const currentDirectory = directory?.context === context ? directory : null;
  const currentInvites = invites?.context === context ? invites : null;
  const selectedUser = currentDirectory?.data.items.find(x => x.id === selected.userId);
  const staffAllowed = operator && selectedUser?.active && !selectedUser.operatorAdmin && selectedUser.id !== access?.userId;
  function field(key: 'email' | 'role' | 'reason', value: string) { setDraft({ ...fields, [key]: value }); }
  async function read(kind: 'invitations' | 'staff', page = 1) {
    const c = lifecycle.current; if (!c || running.current) return; const owner = context; running.current = true; setBusy(true);
    try {
      if (kind === 'invitations') {
        const result = await api<{ items: Invitation[]; total: number }>(`/invitations?organizationId=${access?.organizationId}&page=${page}`, c.signal);
        if (!c.signal.aborted && currentContext.current === owner) setInvites({ context: owner, items: result.items, page, total: result.total });
      } else {
        const result = await api<Directory>(`/staff-directory?page=${page}`, c.signal);
        if (!c.signal.aborted && currentContext.current === owner) setDirectory({ context: owner, data: result, page });
      }
    } catch { if (!c.signal.aborted) setMessage('Không tải được danh sách quyền. Làm mới quyền truy cập rồi thử lại.'); }
    finally { running.current = false; if (!c.signal.aborted) setBusy(false); }
  }
  async function attempt(original: Pending, retry = false) {
    const c = lifecycle.current; if (!c || running.current || original.context !== context) return;
    running.current = true; setBusy(true); setPending(original);
    try {
      await command(original.path, original.body, original.key, c.signal, 'POST', original.version);
      if (!c.signal.aborted) { setPending(null); setMessage('Đã hoàn tất yêu cầu quyền truy cập. Tải lại danh sách và phiên để xem quyền hiện tại.'); setDirectory(null); setInvites(null); }
    } catch (error) {
      if (!c.signal.aborted) {
        if (isDefiniteRejection(error) && !retry) {
          setPending(null); setMessage('Thay đổi quyền bị từ chối. Làm mới quyền và phiên bản hiện tại trước khi gửi yêu cầu mới.');
        } else setMessage('Chưa xác định kết quả. Giữ yêu cầu quyền truy cập gốc và thử lại sau khi khôi phục truy cập.');
      }
    } finally { running.current = false; if (!c.signal.aborted) setBusy(false); }
  }
  function send(path: string, body: unknown, version?: string) { if (!busy && !pending) void attempt({ key: crypto.randomUUID(), context, path, body, version }); }
  const blocked = busy || !!pending;
  return <section className="workspace-panel" aria-labelledby="identity-ops-title" aria-busy={busy}><div className="panel-heading"><h2 id="identity-ops-title">Lời mời và quyền nhân viên</h2></div><div className="session-content">
    <p>Hướng dẫn mời và khôi phục được xếp hàng riêng tư. Bản cài đặt cục bộ này chưa cấu hình gửi email.</p>
    {(admin || operator) && <label>Lý do thay đổi quyền<input maxLength={500} value={fields.reason} disabled={!!pending} onChange={e => field('reason', e.target.value)} /></label>}
    {message && <p role="status">{message}</p>}{pending && <button disabled={busy || pending.context !== context || !(pending.path.startsWith('/staff') ? operator : admin)} onClick={() => void attempt(pending, true)}>Thử lại yêu cầu quyền truy cập gốc</button>}
    {admin && <><h3>Mời đồng nghiệp của khách hàng</h3><label>Email người được mời<input type="email" maxLength={256} disabled={!!pending} value={fields.email} onChange={e => field('email', e.target.value)} /></label><label>Vai trò được mời<select disabled={!!pending} value={fields.role} onChange={e => field('role', e.target.value)}><option value="Member">Thành viên</option><option value="CustomerAdmin">Quản lý khách hàng</option></select></label>
      <div className="session-actions"><button disabled={blocked || !fields.email.trim() || !fields.reason.trim()} onClick={() => send('/invitations', { organizationId: access?.organizationId, email: fields.email, role: fields.role, reason: fields.reason })}>Tạo lời mời</button><button disabled={busy} onClick={() => void read('invitations')}>Tải lời mời</button></div>
      {currentInvites && <><ul>{currentInvites.items.map(x => <li key={x.id}>{x.email} · {displayValue(x.role)} · hết hạn {new Date(x.expiresAt).toLocaleString('vi-VN')} · {x.consumedAt ? 'Đã chấp nhận' : x.revokedAt ? 'Đã thu hồi' : 'Đang chờ'} <button disabled={blocked || !!x.consumedAt || !!x.revokedAt || !fields.reason.trim()} onClick={() => send(`/invitations/${x.id}/revoke`, { reason: fields.reason })}>Thu hồi {x.email}</button></li>)}</ul><div className="session-actions"><button disabled={busy || currentInvites.page <= 1} onClick={() => void read('invitations', currentInvites.page - 1)}>Trang lời mời trước</button><button disabled={busy || currentInvites.page * 50 >= currentInvites.total} onClick={() => void read('invitations', currentInvites.page + 1)}>Trang lời mời sau</button></div></>}
    </>}
    {operator && <><h3>Danh sách nhân viên cơ sở</h3><button disabled={busy} onClick={() => void read('staff')}>Tải danh sách nhân viên</button>
      {currentDirectory && <><div className="session-actions">{currentDirectory.data.items.map(x => <button key={x.id} disabled={blocked || !x.active || x.operatorAdmin || x.id === access?.userId} onClick={() => setSelection({ ...selected, userId: x.id })}>Chọn {x.userName}</button>)}</div><ul>{currentDirectory.data.grants.filter(x => x.userId === selected.userId).map(x => <li key={`${x.facilityId}:${x.staffRole}`}>{x.facilityId} · {displayValue(x.staffRole)}</li>)}</ul>
        <div className="session-actions"><button disabled={busy || currentDirectory.page <= 1} onClick={() => void read('staff', currentDirectory.page - 1)}>Trang nhân viên trước</button><button disabled={busy || currentDirectory.page * 50 >= currentDirectory.data.total} onClick={() => void read('staff', currentDirectory.page + 1)}>Trang nhân viên sau</button></div>
        <p>Đã chọn: {selectedUser?.userName ?? 'Chưa có'}. Cơ sở kho: {access?.facilityId || 'Chọn cơ sở trong ngữ cảnh làm việc'}.</p><label>Vai trò nhân viên<select disabled={blocked} value={selected.staffRole} onChange={e => setSelection({ ...selected, staffRole: e.target.value })}>{['Clerk', 'Sales', 'Finance', 'FacilityManager'].map(r => <option key={r} value={r}>{displayValue(r)}</option>)}</select></label>
        <div className="session-actions">{[true, false].map(granted => <button key={String(granted)} disabled={blocked || !staffAllowed || !access?.facilityId || !fields.reason.trim()} onClick={() => send('/staff-directory/grants', { userId: selected.userId, facilityId: access?.facilityId, role: selected.staffRole, granted, reason: fields.reason }, currentDirectory.data.version)}>{granted ? 'Cấp vai trò tại cơ sở' : 'Thu hồi vai trò tại cơ sở'}</button>)}
        <button disabled={blocked || !staffAllowed || !fields.reason.trim()} onClick={() => send('/staff-directory/disable', { userId: selected.userId, reason: fields.reason }, currentDirectory.data.version)}>Vô hiệu hóa tài khoản đã chọn</button></div></>}
    </>}
    {!admin && !operator && <p>Ngữ cảnh hiện tại không có quyền mời người dùng hoặc quản trị nhân viên.</p>}
  </div></section>;
}

export function CredentialPanel() {
  const [purpose, setPurpose] = useState('Reset'); const [email, setEmail] = useState('');
  const [token, setToken] = useState(''); const [password, setPassword] = useState(''); const [message, setMessage] = useState(''); const [busy, setBusy] = useState(false);
  const running = useRef(false); const lifecycle = useRef<AbortController | null>(null);
  useEffect(() => { const c = new AbortController(); lifecycle.current = c; return () => c.abort(); }, []);
  async function submit(recovery: boolean) {
    const c = lifecycle.current; if (!c || running.current) return; running.current = true; setBusy(true);
    try {
      await command(recovery ? '/auth/request-reset' : purpose === 'Reset' ? '/auth/reset-password' : '/auth/accept-invitation', recovery ? { email } : { token, password }, crypto.randomUUID(), c.signal);
      if (!c.signal.aborted) { setMessage(recovery ? 'Nếu tài khoản đủ điều kiện, hướng dẫn khôi phục sẽ được xếp hàng. Chưa cấu hình gửi email.' : 'Đã đặt mật khẩu. Đăng nhập bằng tài khoản và mật khẩu mới.'); if (!recovery) setToken(''); }
    } catch { if (!c.signal.aborted) setMessage(recovery ? 'Chưa xác nhận được yêu cầu khôi phục. Vui lòng thử lại sau.' : 'Chưa xác nhận được thay đổi mật khẩu. Thử đăng nhập bằng mật khẩu mới; nếu không được, dùng lại mã cũ hoặc yêu cầu hướng dẫn mới.'); }
    finally { running.current = false; if (!c.signal.aborted) { setBusy(false); setPassword(''); } }
  }
  return <details className="session-content"><summary>Lời mời và khôi phục tài khoản</summary><p>Dùng mã một lần được cung cấp qua quy trình riêng tư. Không gửi mã hoặc mật khẩu trong tin nhắn hỗ trợ.</p>
    <form onSubmit={e => { e.preventDefault(); void submit(true); }}><label>Email khôi phục<input type="email" maxLength={256} required value={email} disabled={busy} onChange={e => setEmail(e.target.value)} /></label><button disabled={busy || !email.trim()}>Yêu cầu khôi phục</button></form>
    <form onSubmit={e => { e.preventDefault(); void submit(false); }}><label>Mục đích cập nhật thông tin đăng nhập<select value={purpose} disabled={busy} onChange={e => setPurpose(e.target.value)}><option value="Reset">Đặt lại mật khẩu</option><option value="Invitation">Chấp nhận lời mời</option></select></label><label>Mã dùng một lần<input autoComplete="off" type="password" minLength={64} maxLength={64} required value={token} disabled={busy} onChange={e => setToken(e.target.value)} /></label><label>Mật khẩu mới<input type="password" autoComplete="new-password" minLength={12} maxLength={1024} required value={password} disabled={busy} onChange={e => setPassword(e.target.value)} /></label><p>Ít nhất 12 ký tự, gồm chữ hoa, chữ thường, chữ số và ký hiệu.</p><button disabled={busy || token.length !== 64 || password.length < 12}>Đặt mật khẩu</button></form>
    {message && <p role="status">{message}</p>}
  </details>;
}
