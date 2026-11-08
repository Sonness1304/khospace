import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { WorkspaceAccess } from '../auth/workspace';
import { api, ApiError, command, isDefiniteRejection } from '../../api';
type Facility = { id: string; code: string; name: string; address: string; permittedGoods: string; published: boolean; version: string };
type Member = { id: string; userId: string; role: string; active: boolean; version: string };
type Pending = { key: string; userId: string; scopeId: string; facilityId: string; organizationId: string; kind: 'global' | 'facility' | 'member'; path: string; body: object; method: string; version?: string; uncertain?: boolean };
const emptyScope = '00000000-0000-0000-0000-000000000000';
export default function SettingsPanel({ access }: { access: WorkspaceAccess | null }) {
  const [facility, setFacility] = useState<Facility | null>(null); const [code, setCode] = useState(''); const [name, setName] = useState(''); const [address, setAddress] = useState(''); const [goods, setGoods] = useState(''); const [published, setPublished] = useState(false); const [zone, setZone] = useState('');
  const [members, setMembers] = useState<{ owner: string; values: Member[]; page: number; total: number } | null>(null); const [member, setMember] = useState<Member | null>(null); const [username, setUsername] = useState(''); const [role, setRole] = useState('Member'); const [activeMember, setActiveMember] = useState(true);
  const [reason, setReason] = useState(''); const [message, setMessage] = useState(''); const [busy, setBusy] = useState(false); const [pending, setPending] = useState<Pending | null>(null); const [retry, setRetry] = useState(false);
  const running = useRef(false); const unresolved = useRef<Pending | null>(null); const lifecycle = useRef<AbortController | null>(null);
  const context = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]); const [draftContext, setDraftContext] = useState(context);
  if (draftContext !== context) { setDraftContext(context); setFacility(null); setCode(''); setName(''); setAddress(''); setGoods(''); setZone(''); setMember(null); setUsername(''); setReason(''); setMessage(''); }
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  const currentContext = useRef(context); useLayoutEffect(() => { currentContext.current = context; }, [context]);
  const manager = !!access && (access.operatorAdmin || access.facilityRoles.includes('FacilityManager')); const admin = access?.organizationRole === 'CustomerAdmin';
  const permitted = (value: Pending) => value.userId === access?.userId && (value.kind === 'global' ? access.operatorAdmin : value.kind === 'member' ? admin && value.organizationId === access.organizationId : manager && value.facilityId === access.facilityId);
  const canRead = (value: Pending) => permitted(value) || value.kind === 'member' && value.userId === access?.userId && value.organizationId === access.organizationId && !!access.organizationRole;
  async function read(memberList = false, page = 1) {
    const controller = lifecycle.current; if (!controller || running.current || !access) return; running.current = true; setBusy(true); setMessage('');
    try {
      if (memberList) { const result = await api<{ items: Member[]; total?: number }>(`/memberships?organizationId=${access.organizationId}&page=${page}`, controller.signal); if (!controller.signal.aborted && currentContext.current === context) setMembers({ owner: context, values: result.items, page, total: result.total ?? result.items.length }); }
      else { const result = await api<{ facility: Facility }>(`/facilities/${access.facilityId}/catalog`, controller.signal); if (!controller.signal.aborted && currentContext.current === context) { setFacility(result.facility); setCode(result.facility.code); setName(result.facility.name); setAddress(result.facility.address); setGoods(result.facility.permittedGoods); setPublished(result.facility.published); } }
    } catch { if (!controller.signal.aborted) setMessage('Chưa xác minh được thiết lập. Làm mới quyền truy cập hiện tại rồi thử lại.'); }
    finally { running.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  function saved(result: { result?: { resourceId?: string }; resourceId?: string }) {
    const id = result.result?.resourceId ?? result.resourceId; if (!id) throw Error('Chưa xác minh mã định danh thiết lập'); unresolved.current = null; setPending(null); setRetry(false); setMessage(`Đã lưu dữ liệu: ${id}. Tải thiết lập hiện tại trước khi thay đổi tiếp.`);
  }
  async function attempt(value: Pending) {
    const controller = lifecycle.current; if (!controller || running.current || !permitted(value)) return; running.current = true; unresolved.current = value; setPending(value); setBusy(true); setRetry(false);
    try { const result = await command(value.path, value.body, value.key, controller.signal, value.method, value.version); if (!controller.signal.aborted) saved(result as Parameters<typeof saved>[0]); }
    catch (error) { if (!controller.signal.aborted) { if (!value.uncertain && isDefiniteRejection(error)) { unresolved.current = null; setPending(null); setMessage('Thiết lập bị từ chối. Giữ bản nháp, tải lại phiên bản và bảo đảm tổ chức còn quản trị viên hoạt động.'); } else { const uncertain = { ...value, uncertain: true }; unresolved.current = uncertain; setPending(uncertain); setMessage('Chưa xác định kết quả yêu cầu thiết lập. Đọc kết quả đã lưu trước khi thay đổi tiếp.'); } } }
    finally { running.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  function send(kind: Pending['kind'], path: string, body: object, method = 'POST', version?: string) {
    if (!access || unresolved.current || !reason.trim()) return;
    void attempt({ key: crypto.randomUUID(), userId: access.userId, scopeId: kind === 'global' ? emptyScope : kind === 'member' ? access.organizationId : access.facilityId, facilityId: access.facilityId, organizationId: access.organizationId, kind, path, body, method, version });
  }
  async function readback() {
    const controller = lifecycle.current; if (!controller || running.current || !pending || !canRead(pending)) return; running.current = true; setBusy(true);
    try { const result = await api<Parameters<typeof saved>[0]>(`/commands/${pending.key}?scopeId=${pending.scopeId}`, controller.signal); if (!controller.signal.aborted) saved(result); }
    catch (error) { if (!controller.signal.aborted) { setRetry(error instanceof ApiError && error.status === 404); setMessage('Chưa xác minh được yêu cầu thiết lập. Giữ nguyên mã định danh gốc.'); } }
    finally { running.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  const blocked = busy || !!pending; const fields = { code, name, address, permittedGoods: goods, published, reason };
  return <section className="workspace-panel" aria-labelledby="settings-title" aria-busy={busy}><div className="panel-heading"><div><p className="eyebrow">THIẾT LẬP CÓ TRÁCH NHIỆM</p><h2 id="settings-title">Thiết lập cơ sở và tổ chức</h2></div></div><div className="session-content">
    <label>Lý do thay đổi thiết lập<input maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label>{message && <p role="status">{message}</p>}{pending && <div className="session-actions"><button disabled={busy || !canRead(pending)} onClick={() => void readback()}>Đọc kết quả yêu cầu thiết lập</button>{retry && <button disabled={busy || !permitted(pending)} onClick={() => void attempt(pending)}>Thử lại yêu cầu thiết lập gốc</button>}</div>}
    {manager && <><button disabled={busy || !access?.facilityId} onClick={() => void read()}>Tải thiết lập cơ sở</button><div className="manager-fields"><label>Mã cơ sở kho<input maxLength={40} value={code} onChange={e => setCode(e.target.value)} /></label><label>Tên cơ sở kho<input maxLength={200} value={name} onChange={e => setName(e.target.value)} /></label><label>Địa chỉ cơ sở kho<input maxLength={500} value={address} onChange={e => setAddress(e.target.value)} /></label><label>Quy định hàng hóa được phép<input maxLength={500} value={goods} onChange={e => setGoods(e.target.value)} /></label><label className="check-label"><input type="checkbox" checked={published} onChange={e => setPublished(e.target.checked)} />Công khai cơ sở kho</label></div>
      <div className="session-actions">{access?.operatorAdmin && <button disabled={blocked || !reason || !code || !name || !address || !goods} onClick={() => send('global', '/facilities', fields)}>Tạo cơ sở kho</button>}<button disabled={blocked || !facility || !reason || !code || !name || !address || !goods} onClick={() => facility && send('facility', `/facilities/${facility.id}`, fields, 'PATCH', facility.version)}>Cập nhật cơ sở kho</button></div>
      <label>Mã khu vực mới<input maxLength={40} value={zone} onChange={e => setZone(e.target.value)} /></label><button disabled={blocked || !access?.facilityId || !zone.trim() || !reason.trim()} onClick={() => send('facility', `/facilities/${access?.facilityId}/zones`, { code: zone, reason })}>Tạo khu vực</button></>}
    {admin && <><h3>Thành viên tổ chức đã có tài khoản</h3><p>Dùng mục Lời mời và quyền nhân viên để tạo lời mời cho tài khoản mới. Máy chủ bảo vệ quản trị viên cuối cùng.</p><button disabled={busy} onClick={() => void read(true)}>Tải thành viên tổ chức</button>
      {members?.owner === context && <><div className="session-actions">{members.values.map(x => <button key={x.id} disabled={busy} onClick={() => { setMember(x); setRole(x.role); setActiveMember(x.active); setUsername(x.userId); }}>Sửa thành viên {x.userId}</button>)}</div><div className="session-actions"><button disabled={busy || members.page <= 1} onClick={() => void read(true, members.page - 1)}>Trang thành viên trước</button><button disabled={busy || members.page * 50 >= members.total} onClick={() => void read(true, members.page + 1)}>Trang thành viên sau</button></div></>}
      <label>Tên đăng nhập đã có<input maxLength={256} value={username} onChange={e => { setUsername(e.target.value); setMember(null); }} /></label><label>Vai trò thành viên khách hàng<select value={role} onChange={e => setRole(e.target.value)}><option value="Member">Thành viên</option><option value="CustomerAdmin">Quản lý khách hàng</option></select></label><label className="check-label"><input type="checkbox" checked={activeMember} onChange={e => setActiveMember(e.target.checked)} />Thành viên đang hoạt động</label><div className="session-actions"><button disabled={blocked || !reason || !username || !!member} onClick={() => send('member', '/memberships', { organizationId: access?.organizationId, userName: username, role, reason })}>Thêm người dùng đã có</button><button disabled={blocked || !reason || !member} onClick={() => member && send('member', `/memberships/${member.id}`, { organizationId: access?.organizationId, role, active: activeMember, reason }, 'PATCH', member.version)}>Cập nhật thành viên đã chọn</button></div></>}
    {!manager && !admin && <p>Ngữ cảnh hiện tại không có quyền thiết lập.</p>}
  </div></section>;
}
