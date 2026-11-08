import { useCallback, useEffect, useRef, useState, useId, type FormEvent } from 'react';
import type { WorkspaceAccess } from './workspace';

type Organization = { id: string; name: string; role: string };
type Session = { userId: string; userName: string; operatorAdmin: boolean; organizations: Organization[];
  facilities: { id: string; role: string; name?: string }[] };
type Access = { state: 'checking' | 'signed-out' | 'unavailable' } | { state: 'signed-in'; session: Session };
const roles: Record<string, string> = { CustomerAdmin: 'Chủ không gian', Member: 'Thành viên', FacilityManager: 'Quản lý kho',
  Finance: 'Kế toán', Clerk: 'Nhân viên kho', Sales: 'Kinh doanh' };
const googleErrors: Record<string, string> = {
  'google-https-required': 'Hãy mở địa chỉ HTTPS của ứng dụng để tiếp tục với Google. Bạn vẫn có thể dùng email tại đây.',
  'google-link-required': 'Email này đã có tài khoản. Hãy đăng nhập bằng mật khẩu để tiếp tục.',
  'google-unavailable': 'Google chưa được cấu hình. Bạn vẫn có thể đăng nhập hoặc đăng ký bằng email.',
  'google-cancelled': 'Bạn đã dừng đăng nhập Google. Hãy thử lại khi sẵn sàng.',
  'google-unverified': 'Tài khoản Google cần có email đã xác minh để tiếp tục.',
  'account-disabled': 'Tài khoản hiện không thể đăng nhập. Vui lòng liên hệ người quản lý.',
  'google-failed': 'Chưa đăng nhập được bằng Google. Hãy thử lại hoặc dùng email và mật khẩu.',
};
async function request(path: string, signal: AbortSignal, init: RequestInit = {}) {
  return fetch(`/api/v1/auth/${path}`, { ...init, credentials: 'same-origin', cache: 'no-store',
    signal: AbortSignal.any([signal, AbortSignal.timeout(7000)]) });
}
async function readSession(signal: AbortSignal): Promise<Access> {
  const result = await request('me', signal);
  if (result.status === 401) return { state: 'signed-out' };
  if (!result.ok) throw Error('SESSION_UNAVAILABLE');
  const data: Session = await result.json();
  if (typeof data.userId !== 'string' || typeof data.userName !== 'string' || !Array.isArray(data.organizations)
    || !Array.isArray(data.facilities) || data.organizations.some(o => typeof o.id !== 'string' || typeof o.name !== 'string' || typeof o.role !== 'string')
    || data.facilities.some(f => typeof f.id !== 'string' || typeof f.role !== 'string')) throw Error('SESSION_INVALID');
  return { state: 'signed-in', session: data };
}
export default function SessionPanel({ onAccessChange }: { onAccessChange?: (access: WorkspaceAccess | null) => void }) {
  const [access, setAccess] = useState<Access>({ state: 'checking' });
  const [organizationId, setOrganizationId] = useState(''); const [facilityId, setFacilityId] = useState('');
  const [notice, setNotice] = useState(() => googleErrors[new URLSearchParams(window.location.search).get('authError') ?? ''] ?? '');
  const [busy, setBusy] = useState(false); const [mode, setMode] = useState<'login' | 'register'>('login');
  const [username, setUsername] = useState(''); const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState(''); const [name, setName] = useState(''); const [showPassword, setShowPassword] = useState(false);
  const noticeId = useId();
  const noticeTarget = useRef<HTMLParagraphElement>(null); const confirmationTarget = useRef<HTMLInputElement>(null);
  const focusPending = useRef(false); const [focusRequest, setFocusRequest] = useState(0);
  const [noticeError, setNoticeError] = useState(false); const [confirmationError, setConfirmationError] = useState(false);
  useEffect(() => {
    if (!focusPending.current || busy) return;
    const target = confirmationError ? confirmationTarget.current : noticeTarget.current;
    if (target) { target.focus(); focusPending.current = false; }
  }, [focusRequest, busy, confirmationError]);
  function showError(message: string, confirmation = false) {
    setNotice(message); setNoticeError(true); setConfirmationError(confirmation);
    focusPending.current = true; setFocusRequest(value => value + 1);
  }
  function clearFeedback() {
    if (noticeError) setNotice('');
    setNoticeError(false); setConfirmationError(false); focusPending.current = false;
  }
  const [google, setGoogle] = useState<'loading' | 'ready' | 'disabled' | 'unavailable'>('loading');
  const lifecycle = useRef<AbortController | null>(null); const pending = useRef(false);
  const accept = useCallback((value: Access) => {
    setAccess(value); setOrganizationId(value.state === 'signed-in' && value.session.organizations.length === 1 ? value.session.organizations[0].id : '');
    setFacilityId('');
  }, []);
  useEffect(() => {
    if (!onAccessChange) return;
    if (busy || access.state !== 'signed-in') { onAccessChange(null); return; }
    const session = access.session;
    onAccessChange({ userId: session.userId, organizationId, organizationRole: session.organizations.find(o => o.id === organizationId)?.role,
      facilityId, facilityRoles: session.facilities.filter(f => f.id === facilityId).map(f => f.role), operatorAdmin: session.operatorAdmin });
  }, [access, busy, organizationId, facilityId, onAccessChange]);
  useEffect(() => {
    const c = new AbortController(); lifecycle.current = c;
    void readSession(c.signal).then(value => { if (!c.signal.aborted) accept(value); })
      .catch(() => { if (!c.signal.aborted) setAccess({ state: 'unavailable' }); });
    void request('providers', c.signal).then(async r => {
      if (!r.ok) throw Error('PROVIDERS_UNAVAILABLE'); const data: { google?: boolean } = await r.json();
      if (!c.signal.aborted) setGoogle(data.google === true ? 'ready' : 'disabled');
    }).catch(() => { if (!c.signal.aborted) setGoogle('unavailable'); });
    return () => c.abort();
  }, [accept]);
  async function run(action: 'login' | 'register' | 'logout' | 'refresh') {
    const c = lifecycle.current; if (!c || c.signal.aborted || pending.current) return;
    if (action === 'register' && password !== confirmation) { showError('Mật khẩu nhập lại chưa khớp.', true); return; }
    pending.current = true; setBusy(true); clearFeedback(); setNotice(''); setOrganizationId(''); setFacilityId('');
    if (action === 'refresh') setAccess({ state: 'checking' });
    try {
      if (action !== 'refresh') {
        const csrf = await request('csrf', c.signal); if (!csrf.ok) throw Error('CSRF_UNAVAILABLE');
        const data: { token?: string } = await csrf.json(); if (typeof data.token !== 'string' || !data.token) throw Error('CSRF_UNAVAILABLE');
        const result = await request(action, c.signal, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': data.token },
          ...(action === 'login' ? { body: JSON.stringify({ userName: username.trim(), password }) }
            : action === 'register' ? { body: JSON.stringify({ name: name.trim(), email: username.trim(), password }) } : {}) });
        if ([400, 401, 403, 409, 422, 429].includes(result.status)) {
          if (action === 'logout') {
            try {
              const value = await readSession(c.signal);
              if (!c.signal.aborted) {
                accept(value);
                setNotice(value.state === 'signed-out' ? 'Phiên đăng nhập đã kết thúc.' : 'Chưa đăng xuất được. Trạng thái tài khoản đã được kiểm tra lại.');
              }
            } catch { if (!c.signal.aborted) accept({ state: 'unavailable' }); }
            return;
          }
          if (!c.signal.aborted) {
            accept({ state: 'signed-out' });
            showError(result.status === 429 ? 'Bạn thao tác quá nhanh. Vui lòng đợi một chút rồi thử lại.'
              : action === 'login' ? 'Email hoặc mật khẩu chưa đúng. Vui lòng kiểm tra lại.'
              : action === 'register' && result.status === 409 ? 'Email này đã có tài khoản. Hãy đăng nhập bằng mật khẩu để tiếp tục.'
              : action === 'register' ? 'Chưa tạo được tài khoản. Kiểm tra email và yêu cầu mật khẩu bên dưới.' : 'Chưa đăng xuất được. Hãy làm mới tài khoản rồi thử lại.');
          }
          return;
        }
        if (!result.ok) throw Error('ACTION_UNCONFIRMED');
      }
      const value = await readSession(c.signal); if (!c.signal.aborted) accept(value);
    } catch {
      // Lost replies are read back once; never automatically replay credentials or registration.
      try {
        const value = await readSession(c.signal);
        if (!c.signal.aborted) { accept(value); setNotice('Chưa xác nhận được yêu cầu. Trạng thái đăng nhập đã được kiểm tra lại.'); }
      } catch { if (!c.signal.aborted) setAccess({ state: 'unavailable' }); }
    } finally {
      pending.current = false; if (!c.signal.aborted) { setBusy(false); setPassword(''); setConfirmation(''); }
    }
  }
  function submit(event: FormEvent) { event.preventDefault(); void run(mode); }
  function chooseMode(next: 'login' | 'register') { if (busy || next === mode) return; setMode(next); setPassword(''); setConfirmation(''); clearFeedback(); setNotice(''); }
  function switchMode() { chooseMode(mode === 'login' ? 'register' : 'login'); }
  const session = access.state === 'signed-in' ? access.session : null;
  const organization = session?.organizations.find(item => item.id === organizationId);
  return <section className="session-panel workspace-panel" aria-labelledby="access-title" aria-busy={busy}>
    <div className="panel-heading"><div><p className="eyebrow">TÀI KHOẢN WAREHOUSERENT</p>
      <h2 id="access-title">{session ? 'Tài khoản của bạn' : mode === 'register' ? 'Tạo tài khoản mới' : 'Chào mừng trở lại'}</h2></div>
      {session && <span className="outline-badge">Đã đăng nhập</span>}</div>
    <div className="session-content">
      {access.state === 'checking' && <p role="status">Đang kiểm tra tài khoản…</p>}
      {access.state === 'unavailable' && <div className="account-unavailable"><span className="connection-symbol" aria-hidden="true">↻</span><h3>Kết nối đang gián đoạn</h3><p role="alert">Chưa kết nối được tài khoản. Kiểm tra mạng rồi thử lại.</p><p>Trạng thái tài khoản chưa được xác nhận. Kết nối lại để tiếp tục an toàn.</p>
        <button type="button" disabled={busy} onClick={() => void run('refresh')}>Thử lại kết nối</button><a href="#catalog">Quay lại tìm kho</a></div>}
      {access.state === 'signed-out' && <>
        <div className="auth-tabs" role="tablist" aria-label="Chọn đăng nhập hoặc đăng ký">{(['login', 'register'] as const).map(value => <button key={value} type="button" role="tab" id={`auth-${value}-tab`} aria-controls="auth-form" aria-selected={mode === value} tabIndex={mode === value ? 0 : -1} onKeyDown={event => {
          if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key) || busy) return;
          event.preventDefault(); const next = event.key === 'Home' ? 'login' : event.key === 'End' ? 'register' : value === 'login' ? 'register' : 'login';
          chooseMode(next); document.getElementById(`auth-${next}-tab`)?.focus();
        }} disabled={busy} onClick={() => chooseMode(value)}>{value === 'login' ? 'Đăng nhập' : 'Đăng ký'}</button>)}</div>
        <p className="auth-lead">{mode === 'login' ? 'Đăng nhập để đặt kho, quản lý hàng hóa và theo dõi chi phí.' : 'Một tài khoản để quản lý kho thuê và hàng hóa của bạn.'}</p>
        {google === 'ready' ? <a className="google-button" href="/api/v1/auth/google" aria-disabled={busy} onClick={event => { if (busy) event.preventDefault(); }}><svg aria-hidden="true" className="google-mark" viewBox="0 0 24 24"><path fill="#4285F4" d="M21.6 12.2c0-.7-.1-1.4-.2-2.1H12v4h5.4a4.6 4.6 0 0 1-2 3v2.5h3.2c1.9-1.8 3-4.3 3-7.4Z"/><path fill="#34A853" d="M12 22c2.7 0 5-.9 6.6-2.4L15.4 17a6 6 0 0 1-8.9-3.1H3.2v2.6A10 10 0 0 0 12 22Z"/><path fill="#FBBC05" d="M6.5 13.9a6 6 0 0 1 0-3.8V7.5H3.2a10 10 0 0 0 0 9l3.3-2.6Z"/><path fill="#EA4335" d="M12 6a5.4 5.4 0 0 1 3.8 1.5l2.9-2.8A9.6 9.6 0 0 0 12 2a10 10 0 0 0-8.8 5.5l3.3 2.6A6 6 0 0 1 12 6Z"/></svg>Tiếp tục với Google</a>
          : <button className="google-button" type="button" disabled><svg aria-hidden="true" className="google-mark" viewBox="0 0 24 24"><path fill="#4285F4" d="M21.6 12.2c0-.7-.1-1.4-.2-2.1H12v4h5.4a4.6 4.6 0 0 1-2 3v2.5h3.2c1.9-1.8 3-4.3 3-7.4Z"/><path fill="#34A853" d="M12 22c2.7 0 5-.9 6.6-2.4L15.4 17a6 6 0 0 1-8.9-3.1H3.2v2.6A10 10 0 0 0 12 22Z"/><path fill="#FBBC05" d="M6.5 13.9a6 6 0 0 1 0-3.8V7.5H3.2a10 10 0 0 0 0 9l3.3-2.6Z"/><path fill="#EA4335" d="M12 6a5.4 5.4 0 0 1 3.8 1.5l2.9-2.8A9.6 9.6 0 0 0 12 2a10 10 0 0 0-8.8 5.5l3.3 2.6A6 6 0 0 1 12 6Z"/></svg>Tiếp tục với Google</button>}
        <p className="auth-provider-note">{google === 'loading' ? 'Đang kiểm tra đăng nhập Google…' : google === 'disabled' ? 'Google chưa được cấu hình. Bạn có thể dùng email và mật khẩu ngay bên dưới.'
          : google === 'unavailable' ? 'Tạm chưa kết nối được Google. Hãy dùng email và mật khẩu.' : 'Dùng tài khoản Google của bạn để tiếp tục.'}</p>
        <div className="auth-divider"><span>hoặc dùng email</span></div>
        <form className="login-form" id="auth-form" role="tabpanel" aria-labelledby={`auth-${mode}-tab`} aria-describedby={noticeError ? noticeId : undefined} onSubmit={submit}>
          {mode === 'register' && <label>Tên cá nhân hoặc doanh nghiệp<input name="name" autoComplete="organization" maxLength={160} required value={name} onChange={e => { setName(e.target.value); clearFeedback(); }} /></label>}
          <label>Email<input type="email" name="username" autoComplete="username" maxLength={256} placeholder="ban@example.com" required value={username} onChange={e => { setUsername(e.target.value); clearFeedback(); }} /></label>
          <label>Mật khẩu<span className="password-field"><input aria-label="Mật khẩu" name="password" type={showPassword ? 'text' : 'password'} autoComplete={mode === 'register' ? 'new-password' : 'current-password'}
            minLength={mode === 'register' ? 12 : undefined} maxLength={1024} required value={password} onChange={e => { setPassword(e.target.value); clearFeedback(); }} aria-describedby={mode === 'register' ? 'password-help' : undefined} />
            <button type="button" className="password-toggle" onClick={() => setShowPassword(!showPassword)} aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}>{showPassword ? 'Ẩn' : 'Hiện'}</button></span></label>
          {mode === 'register' && <><small id="password-help">Tối thiểu 12 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.</small>
            <label>Nhập lại mật khẩu<input ref={confirmationTarget} aria-invalid={confirmationError || undefined} aria-describedby={confirmationError ? noticeId : undefined} name="confirmPassword" type={showPassword ? 'text' : 'password'} autoComplete="new-password" maxLength={1024} required value={confirmation} onChange={e => { setConfirmation(e.target.value); clearFeedback(); }} /></label></>}
          <button type="submit" disabled={busy}>{busy ? 'Đang xử lý…' : mode === 'register' ? 'Tạo tài khoản' : 'Đăng nhập'}<span aria-hidden="true"> →</span></button>
        </form>
        <p className="auth-switch">{mode === 'login' ? 'Chưa có tài khoản?' : 'Đã có tài khoản?'} <button type="button" className="text-button" disabled={busy} onClick={switchMode}>{mode === 'login' ? 'Đăng ký' : 'Đăng nhập'}</button></p>
        {mode === 'register' && <p className="auth-provider-note">Tài khoản mới có không gian khách hàng riêng. Quyền nhân viên do người quản lý cấp.</p>}
      </>}
      {session && <div className="access-context"><p className="signed-in-user">Bạn đang đăng nhập bằng <strong>{session.userName}</strong></p>
        {session.operatorAdmin && <p>Quản trị vận hành</p>}
        {session.organizations.length === 1 ? <div role="group" aria-label="Không gian của bạn"><p>Không gian của bạn: <strong>{session.organizations[0].name}</strong></p></div>
          : session.organizations.length > 1 ? <label>Không gian của bạn<select aria-label="Không gian của bạn" disabled={busy} value={organizationId} onChange={e => setOrganizationId(e.target.value)}>
          <option value="">Chọn không gian</option>{session.organizations.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
          : <p>Bạn chưa được thêm vào không gian khách hàng.</p>}
        {organization && <p role="status">Vai trò hiện tại: {roles[organization.role] ?? 'Thành viên'}</p>}
        {!!session.facilities.length && <label>Kho được phân công<select value={facilityId} disabled={busy} onChange={e => setFacilityId(e.target.value)}><option value="">Chọn kho</option>
          {Array.from(new Set(session.facilities.map(f => f.id))).map(id => <option key={id} value={id}>{session.facilities.find(f => f.id === id)?.name ?? 'Kho được phân công'}</option>)}</select></label>}
        <div className="session-actions"><button type="button" disabled={busy} onClick={() => void run('refresh')}>Làm mới tài khoản</button>
          <button className="secondary-button" type="button" disabled={busy} onClick={() => void run('logout')}>Đăng xuất</button></div>
      </div>}
      {notice && <p ref={noticeTarget} id={noticeId} tabIndex={-1} className="session-notice notice" role={noticeError ? 'alert' : 'status'}>{notice}</p>}
    </div>
  </section>;
}
