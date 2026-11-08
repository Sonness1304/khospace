import DiscoveryHome from './features/discovery/DiscoveryHome';
import { type SearchPreset, type SearchCriteria } from './features/discovery/sizeAdvisor';
import ReportsPanel from './features/reports/ReportsPanel';
import DocumentsPanel from './features/documents/DocumentsPanel';
import IdentityOperationsPanel, { CredentialPanel } from './features/identityOperations/IdentityOperationsPanel';
import { useEffect, useRef, useState } from 'react';
import { readHealth, type Observation } from './health';
import SessionPanel from './features/auth/SessionPanel';
import FacilitiesPanel from './features/facilities/FacilitiesPanel';
import type { WorkspaceAccess } from './features/auth/workspace';
import FinancePanel from './features/finance/FinancePanel';
import SettlementPanel from './features/settlements/SettlementPanel';
import StockPanel from './features/stock/StockPanel';
import StockCorrectionsPanel from './features/stock-corrections/StockCorrectionsPanel';
import StockRequestPanel from './features/stock-requests/StockRequestPanel';
import BarcodeRegistryPanel from './features/barcodes/BarcodePanels';
import OnlinePaymentPanel from './features/online-payments/OnlinePaymentPanel';
import GatePanel from './features/gates/GatePanel';
import SettingsPanel from './features/operations/SettingsPanel';
import ContextPanel from './features/operations/ContextPanel';
import OperationsPanel, { type Selection } from './features/operations/OperationsPanel';
import BookingPanel from './features/bookings/BookingPanel';
import LeasePanel from './features/leases/LeasePanel';
import RenewalPreviewPanel from './features/lease-amendments/RenewalPreviewPanel';
import type { NoticeTarget } from './features/reports/noticeTarget';

const checking: Observation = { state: 'checking' };
type WorkspaceSelection = Selection & { noticeKey?: string };
const routes = [['overview', 'Tổng quan'], ['catalog', 'Tìm kho & báo giá'], ['rentals', 'Đặt thuê'], ['leases', 'Hợp đồng'], ['requests', 'Yêu cầu nhập / xuất'], ['stock', 'Hàng trong kho'], ['gates', 'Lịch ra vào'], ['finance', 'Thanh toán'], ['settings', 'Quản lý'], ['account', 'Tài khoản']];
const descriptions: Record<string, string> = {
  catalog: 'Chọn thời gian, tìm diện tích phù hợp và xem báo giá trước khi đặt.', rentals: 'Theo dõi đặt thuê và các bước xác nhận của bạn.',
  leases: 'Quản lý hợp đồng, bàn giao, gia hạn và tài liệu thuê kho.', requests: 'Gửi yêu cầu nhập, xuất hàng và theo dõi tiến độ xử lý.',
  stock: 'Tra cứu số lượng, vị trí lưu trữ và lịch sử hàng hóa.', gates: 'Sắp xếp lịch xe ra vào và theo dõi giao nhận tại kho.',
  finance: 'Theo dõi tiền thuê, tiền cọc và các khoản thanh toán.', settings: 'Thiết lập kho, quản lý nhân sự và quyền truy cập.',
  account: 'Đăng nhập, tạo tài khoản và quản lý thông tin truy cập của bạn.',
};
const homeAnchors = ['#planner', '#faq'];
function focusDestination(id: string) { const target = document.getElementById(id) ?? document.getElementById('main'); target?.focus(); if (id !== 'main') target?.scrollIntoView({ block: 'start', behavior: 'instant' }); }
const routeFromHash = () => homeAnchors.includes(window.location.hash) ? 'overview' : routes.find(([id]) => `#${id}` === window.location.hash)?.[0];
const labels = { api: { checking: 'Đang kiểm tra kết nối', available: 'Ứng dụng đã kết nối', unavailable: 'Chưa kết nối ứng dụng' },
  sql: { checking: 'Đang kiểm tra dữ liệu', available: 'Dữ liệu sẵn sàng', unavailable: 'Dữ liệu chưa sẵn sàng' } };
const iconPaths: Record<string, string> = { overview: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
  catalog: 'M3 10 12 4l9 6v11H3z M7 21v-8h10v8 M7 10h10', rentals: 'M5 4h14v17H5z M8 9h8 M8 13h8 M8 17h5',
  leases: 'M6 3h9l4 4v14H6z M14 3v5h5 M9 12h7 M9 16h7', requests: 'M4 7h15 M15 3l4 4-4 4 M20 17H5 M9 13l-4 4 4 4',
  stock: 'm3 7 9-4 9 4v10l-9 4-9-4z M3 7l9 4 9-4 M12 11v10', gates: 'M4 21V4h16v17 M8 21V8h8v13 M2 21h20',
  finance: 'M3 6h18v14H3z M3 10h18 M15 15h3 M5 6V3h12', settings: 'M4 6h16 M4 12h16 M4 18h16 M8 3v6 M16 9v6 M10 15v6',
  account: 'M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0 M4 21v-2a8 8 0 0 1 16 0v2' };
function Icon({ name }: { name: string }) { return <svg className="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={iconPaths[name] ?? iconPaths.catalog} /></svg>; }
function HealthCard({ kind, observation }: { kind: 'api' | 'sql'; observation: Observation }) {
  return <section className="health-card" aria-label={kind === 'api' ? 'Kết nối ứng dụng' : 'Kết nối dữ liệu'}>
    <div className="card-eyebrow">{kind === 'api' ? 'Ứng dụng' : 'Dữ liệu'}</div>
    <strong className={`health-value ${observation.state}`}><span aria-hidden="true">{observation.state === 'available' ? '✓' : observation.state === 'checking' ? '◷' : '!'}</span>{labels[kind][observation.state]}</strong>
    <p>{observation.observedAt ? <>Cập nhật lúc <time dateTime={observation.observedAt}>{new Date(observation.observedAt).toLocaleTimeString('vi-VN')}</time></>
      : observation.state === 'checking' ? 'Đang kiểm tra kết nối…' : 'Chưa xác nhận được kết nối.'}</p>
  </section>;
}

export default function App() {
  const frontendOnly = import.meta.env.VITE_KHOSPACE_MODE === 'frontend-only';
  const [menuOpen, setMenuOpen] = useState(false);
  const menuToggle = useRef<HTMLButtonElement>(null);
  const [currentHash, setCurrentHash] = useState(() => window.location.hash);
  const [searchPreset, setSearchPreset] = useState<SearchPreset | undefined>();
  const [view, setView] = useState(() => routeFromHash() ?? 'overview'); const [selection, setSelection] = useState<{ context: string; value: WorkspaceSelection } | null>(null);
  const [access, setAccess] = useState<WorkspaceAccess | null>(null);
  const [api, setApi] = useState<Observation>(checking);
  const [sql, setSql] = useState<Observation>(checking);
  const [readback, setReadback] = useState(0);

  const focusNavigation = useRef(false);
  const focusTarget = useRef('main');
  function searchFromHome(criteria: SearchCriteria) { setSearchPreset({ ...criteria, id: crypto.randomUUID() }); window.history.pushState(null, '', '#catalog'); navigate('catalog'); }
  const main = useRef<HTMLElement>(null);
  useEffect(() => {
    if (focusNavigation.current) { focusDestination(focusTarget.current); focusNavigation.current = false; }
  }, [view]);
  function navigate(id: string, target = 'main') { focusTarget.current = target; focusNavigation.current = true; setCurrentHash(window.location.hash); setView(id); setMenuOpen(false); if (view === id) { focusDestination(focusTarget.current); focusNavigation.current = false; } }

  useEffect(() => {
    let frame = 0;
    const syncView = () => {
      const destination = homeAnchors.includes(window.location.hash) ? window.location.hash.slice(1) : 'main';
      setCurrentHash(window.location.hash);
      focusTarget.current = destination;
      const route = routeFromHash();
      if (route) { focusNavigation.current = true; setView(route); }
      else if (!window.location.hash) { focusNavigation.current = true; setView('overview'); }
      setMenuOpen(false);
      // History between two home sections does not change the mounted view.
      if (route || !window.location.hash) {
        cancelAnimationFrame(frame);
        frame = requestAnimationFrame(() => { focusDestination(destination); focusNavigation.current = false; });
      }
    };
    window.addEventListener('hashchange', syncView);
    return () => { window.removeEventListener('hashchange', syncView); cancelAnimationFrame(frame); };
  }, []);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    const pending = new Set(['live', 'ready']);
    const timeout = setTimeout(() => {
      controller.abort();
      if (active) {
        if (pending.has('live')) setApi({ state: 'unavailable' });
        if (pending.has('ready')) setSql({ state: 'unavailable' });
      }
    }, 7000);
    function observe(kind: 'live' | 'ready', update: (observation: Observation) => void) {
      void readHealth(kind, controller.signal).then(observation => {
        if (!active || controller.signal.aborted) return;
        pending.delete(kind);
        if (!pending.size) clearTimeout(timeout);
        update(observation);
      });
    }
    observe('live', setApi);
    observe('ready', setSql);
    return () => { active = false; clearTimeout(timeout); controller.abort(); };
  }, [readback]);

  const context = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]);
  const selected = selection?.context === context ? selection.value : null;
  function select(value: WorkspaceSelection) { const target = value.kind === 'booking' ? 'rentals' : value.kind === 'lease' ? 'leases' : value.kind === 'request' ? 'requests' : 'stock'; setSelection({ context, value }); if (window.location.hash !== `#${target}`) window.history.pushState(null, '', `#${target}`); navigate(target); }
  function openNotice(target: NoticeTarget) { select({ ...target, noticeKey: crypto.randomUUID() }); }
  const isChecking = api.state === 'checking' || sql.state === 'checking';
  function refresh() { if (isChecking) return; setApi(checking); setSql(checking); setReadback(value => value + 1); }

  const activeTitle = view === 'overview' ? 'Không gian của bạn' : routes.find(([id]) => id === view)?.[1];
  return <div className="workspace khospace" onClick={event => {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || !(event.target instanceof Element)) return;
    const destination = event.target.closest('a')?.getAttribute('href');
    const route = homeAnchors.includes(destination ?? '') ? 'overview' : routes.find(([id]) => destination === `#${id}`)?.[0];
    if (!route) return;
    // Avoid native fragment focus racing the destination content focus.
    event.preventDefault();
    if (window.location.hash !== destination) window.history.pushState(null, '', destination);
    navigate(route, homeAnchors.includes(destination ?? '') ? destination!.slice(1) : 'main');
  }}>
    <a className="skip-link" href="#main">Đến nội dung chính</a>
    <header className="ks-top"><div className="ks-wrap ks-nav" onKeyDown={event => {
      if (event.key !== 'Escape' || !menuOpen || event.defaultPrevented) return;
      event.preventDefault(); setMenuOpen(false); menuToggle.current?.focus();
    }}><a className="ks-logo" href="#overview" aria-label="KhoSpace · Trang chủ"><span className="ks-logo-mark"><Icon name="catalog" /></span><span>Kho<strong>Space</strong><b>.</b></span></a>
      <button ref={menuToggle} type="button" className="mobile-menu secondary-button" aria-expanded={menuOpen} aria-controls="workspace-navigation" onClick={() => setMenuOpen(!menuOpen)}>{menuOpen ? 'Đóng menu' : 'Mở menu'}</button>
      <nav id="workspace-navigation" className={`ks-nav-links ${menuOpen ? 'menu-open' : ''}`} aria-label="Điều hướng chính">{[['overview','Tổng quan'],['catalog','Tìm kho & báo giá'],['planner','Chọn diện tích'],['faq','Hướng dẫn'],['account','Tài khoản']].map(([id,label]) => <a key={id} href={`#${id}`} aria-current={(view === id && !homeAnchors.includes(currentHash)) || currentHash === `#${id}` ? 'page' : undefined}>{label}</a>)}</nav>
      <a href="#account" className="ks-header-account"><Icon name="account" />{access ? 'Tài khoản của tôi' : 'Đăng nhập / Đăng ký'}</a></div>
      {access && <nav className="ks-workspace-nav ks-wrap" aria-label="Điều hướng công việc">{routes.filter(([id]) => !['overview','catalog','account'].includes(id)).map(([id,label]) => <a key={id} href={`#${id}`} aria-current={view === id ? 'page' : undefined}><Icon name={id} />{label}</a>)}</nav>}
    </header>
    {frontendOnly && <section className="deployment-notice ks-wrap" aria-label="Trạng thái triển khai"><strong>Bản giao diện — chưa kết nối máy chủ</strong><p>Bạn có thể khám phá giao diện và ước tính diện tích. Đăng nhập Google, đặt thuê, hàng hóa và thanh toán chưa hoạt động trên bản web này; không có dữ liệu khách hàng được đưa lên.</p></section>}
    <div className="workspace-body">
      <header className="context-header" hidden={view === 'overview' || view === 'account'}><div><span className="context-label">Không gian làm việc</span><strong>{activeTitle}</strong></div><span>KhoSpace · Quản lý theo quyền của bạn</span></header>
      <main id="main" ref={main} tabIndex={-1}>
        {view !== 'overview' && <div className="page-heading"><div><h1>{activeTitle}</h1><p>{descriptions[view]}</p></div></div>}
        <div className={`welcome-grid ${access ? 'is-member' : ''}`} hidden={view !== 'account'}>
          <section className="account-intro" hidden={view !== 'account' || !!access} aria-labelledby="account-intro-title">
            <a className="back-to-discovery" href="#catalog"><span aria-hidden="true">← </span>Xem kho trước khi đăng ký</a>
            <span className="welcome-tag">KHÔNG GIAN CỦA BẠN</span>
            <h2 id="account-intro-title">Chỉ cần một tài khoản.<br /><span>Việc kho đã có chỗ.</span></h2>
            <p>Từ tìm diện tích phù hợp đến theo dõi từng lần nhập xuất, quản lý công việc của bạn trong cùng một nơi.</p>
            <ul className="account-benefits"><li><Icon name="catalog" /><div><strong>Chọn kho theo nhu cầu</strong><span>Xem diện tích, ngày thuê và báo giá trước khi đặt.</span></div></li><li><Icon name="stock" /><div><strong>Nắm rõ hàng trong kho</strong><span>Theo dõi yêu cầu nhập xuất và số lượng đang lưu trữ.</span></div></li><li><Icon name="finance" /><div><strong>Rõ từng khoản thanh toán</strong><span>Tra cứu tiền thuê, tiền cọc và lịch sử giao dịch.</span></div></li></ul>
            <p className="account-footnote">Tài khoản khách hàng có không gian riêng. Quyền nhân viên do người quản lý cấp.</p>
          </section>
          <div className="account-slot" hidden={view !== 'account'} id="account">{frontendOnly ? <section className="workspace-panel"><h2>Tài khoản chưa khả dụng trên bản giao diện</h2><p>Máy chủ và cơ sở dữ liệu công khai chưa được kết nối. Bản này không thu thập email, mật khẩu hoặc thông tin thanh toán.</p><a href="#planner">Ước tính diện tích không cần tài khoản</a></section> : <SessionPanel onAccessChange={setAccess} />}</div>
        </div>
        <div hidden={view !== 'overview'} id="overview">
          <DiscoveryHome onSearch={searchFromHome} />
          {access && <div className="quick-actions"><a href="#rentals" onClick={() => setView('rentals')}><Icon name="rentals" />Đặt thuê của tôi <span aria-hidden="true">→</span></a><a href="#stock" onClick={() => setView('stock')}><Icon name="stock" />Xem hàng trong kho <span aria-hidden="true">→</span></a><a href="#finance" onClick={() => setView('finance')}><Icon name="finance" />Theo dõi thanh toán <span aria-hidden="true">→</span></a></div>}
          <div hidden={!access}><ReportsPanel access={access} onOpenRecord={openNotice} /></div>
        </div>
        <div className="ks-workspace-view" hidden={!access || view === 'overview' || view === 'account'}><ContextPanel access={access} onChange={setAccess} /></div>
        <div className="ks-workspace-view" hidden={view !== 'catalog'} id="catalog"><FacilitiesPanel access={access} preset={searchPreset} active={view === 'catalog'} /></div>
        <div className="ks-workspace-view" hidden={view !== 'rentals'} id="rentals"><OperationsPanel access={access} onSelect={select} /><BookingPanel access={access} quote={null} selectedId={selected?.kind === 'booking' ? selected.id : undefined} /></div>
        <div className="ks-workspace-view" hidden={view !== 'leases'} id="leases"><LeasePanel access={access} selectedId={selected?.kind === 'lease' ? selected.id : undefined} noticeKey={selected?.kind === 'lease' ? selected.noticeKey : undefined} /><RenewalPreviewPanel access={access} /><DocumentsPanel access={access} selectedLeaseId={selected?.kind === 'lease' ? selected.id : undefined} /></div>
        <div className="ks-workspace-view" hidden={view !== 'requests'} id="requests"><StockRequestPanel access={access} selectedId={selected?.kind === 'request' ? selected.id : undefined} noticeKey={selected?.kind === 'request' ? selected.noticeKey : undefined} /></div>
        <div className="ks-workspace-view" hidden={view !== 'stock'} id="stock"><StockPanel access={access} selectedId={selected?.kind === 'grant' ? selected.id : undefined} /><StockCorrectionsPanel access={access} /></div>
        <div className="ks-workspace-view" hidden={view !== 'gates'} id="gates"><GatePanel access={access} /></div>
        <div className="ks-workspace-view" hidden={view !== 'finance'} id="finance"><FinancePanel access={access} selectedId={selected?.accountId} /><OnlinePaymentPanel access={access} /><SettlementPanel access={access} /></div>
        <div className="ks-workspace-view" hidden={view !== 'settings'} id="settings"><SettingsPanel access={access} /><IdentityOperationsPanel access={access} /><BarcodeRegistryPanel access={access} /></div>
        {!frontendOnly && <div className="advanced-account" hidden={view !== 'account'}><CredentialPanel /></div>}
        <details className="system-status"><summary><span className={`status-dot ${api.state === 'available' && sql.state === 'available' ? 'is-online' : ''}`} aria-hidden="true" />Trạng thái hệ thống</summary><div className="system-status-content"><button type="button" onClick={refresh} aria-disabled={isChecking}>Kiểm tra lại kết nối</button><div className="health-grid" role="status" aria-live="polite" aria-busy={isChecking}><HealthCard kind="api" observation={api} /><HealthCard kind="sql" observation={sql} /></div>
          {!isChecking && (api.state === 'unavailable' || sql.state === 'unavailable') && <div className="notice" role="alert"><strong>Kết nối cần được kiểm tra</strong><p>{api.state === 'available' ? 'Ứng dụng đang chạy nhưng dữ liệu chưa sẵn sàng. Hãy liên hệ người quản lý để kiểm tra kết nối dữ liệu.' : 'Chưa kết nối được ứng dụng. Kiểm tra mạng hoặc liên hệ người quản lý rồi thử lại.'}</p></div>}</div></details>
        <footer className="workspace-footer"><span>KhoSpace · Không gian cho nhu cầu của bạn.</span><span>Thuê kho · Hàng hóa · Thanh toán</span></footer>
      </main>
    </div>
  </div>;
}
