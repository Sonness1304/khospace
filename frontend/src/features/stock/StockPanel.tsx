import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react';
import type { WorkspaceAccess } from '../auth/workspace';
import { api } from '../../api';
type Balance = { itemId: string; sku: string; baseUom: string; quantity: string };
type Entry = { id: string; sku: string; baseUom: string; delta: string; stockPostingId: string; postedAt: string };
type Page<T> = { items: T[]; total: number };
type View = { binding: string; focus: boolean; userId: string; contextOrg: string; contextFacility: string; org: string; grant: string; balancePage: number; ledgerPage: number; balances: Page<Balance>; ledger: Page<Entry> };
export default function StockPanel({ access, selectedId }: { access: WorkspaceAccess | null; selectedId?: string }) {
  const [externalSelection, setExternalSelection] = useState(selectedId);
  const [org, setOrg] = useState(''); const [grant, setGrant] = useState(selectedId ?? ''); const [data, setData] = useState<View | null>(null);
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState<{ binding: string; text: string } | null>(null); const active = useRef(false); const lifecycle = useRef<AbortController | null>(null);
  if (externalSelection !== selectedId) { setExternalSelection(selectedId); setGrant(selectedId ?? ''); }
  const draftBinding = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId, access?.facilityRoles, access?.operatorAdmin, access?.organizationRole]); const [draftOwner, setDraftOwner] = useState(draftBinding);
  if (draftOwner !== draftBinding) { setDraftOwner(draftBinding); setOrg(''); setGrant(''); setMessage(null); setData(null); }
  const binding = JSON.stringify([draftBinding, org, grant, selectedId]);
  const [owner, setOwner] = useState(binding);
  if (owner !== binding) { setOwner(binding); setData(null); setMessage(null); }
  const epoch = useRef(0); const currentBinding = useRef(binding);
  const errorRef = useRef<HTMLParagraphElement>(null); const resultsRef = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => { currentBinding.current = binding; epoch.current += 1; }, [binding]);
  function edit(setter: (value: string) => void, value: string) { epoch.current += 1; setData(null); setMessage(null); setter(value); }
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  const staff = !!access && (access.operatorAdmin || access.facilityRoles.some(x => ['Clerk', 'FacilityManager'].includes(x)));
  const feedback = message?.binding === binding ? message : null;
  const visible = data && data.binding === binding && access && data.userId === access.userId && data.contextOrg === access.organizationId && data.contextFacility === access.facilityId && (data.org === access.organizationId || staff) ? data : null;
  useEffect(() => { if (feedback) errorRef.current?.focus(); }, [feedback]);
  useEffect(() => { if (visible?.focus) resultsRef.current?.focus(); }, [visible]);
  async function read(event?: FormEvent, balancePage = 1, ledgerPage = 1, previous?: View) {
    event?.preventDefault(); const controller = lifecycle.current; if (!controller || active.current || !access) return;
    const organization = previous?.org ?? (access.organizationId || org.trim()); const unit = previous?.grant ?? grant.trim();
    if (!organization || !unit) return;
    const intent = ++epoch.current; const bound = binding;
    const current = () => !controller.signal.aborted && intent === epoch.current && bound === currentBinding.current;
    active.current = true; setBusy(true); setMessage(null);
    try {
      const query = `organizationId=${encodeURIComponent(organization)}&leaseUnitId=${encodeURIComponent(unit)}`;
      const [balances, ledger] = await Promise.all([api<Page<Balance>>(`/stock/balances?${query}&page=${balancePage}`, controller.signal), api<Page<Entry>>(`/stock/ledger?${query}&page=${ledgerPage}`, controller.signal)]);
      if (!Array.isArray(balances.items) || !Array.isArray(ledger.items)) throw Error('Chưa xác minh các trang tồn kho');
      if (current()) setData({ binding: bound, focus: !!event, userId: access.userId, contextOrg: access.organizationId, contextFacility: access.facilityId, org: organization, grant: unit, balancePage, ledgerPage, balances, ledger });
    } catch { if (current()) { setData(null); setMessage({ binding: bound, text: 'Chưa xác minh được tồn kho. Kiểm tra chủ sở hữu, quyền truy cập và kết nối, rồi bấm Tải tồn kho để thử lại.' }); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  return <section className="workspace-panel" aria-labelledby="inventory-title" aria-busy={busy}>
    <div className="panel-heading"><div><p className="eyebrow">HÀNG HÓA CÓ THỂ TRUY VẾT</p><h2 id="inventory-title">Tồn kho và lịch sử xuất nhập</h2></div></div>
    <div className="session-content"><p className="catalog-help">Số dư được tính từ các giao dịch đã ghi nhận, gồm thực hiện từng phần và điều chỉnh đã duyệt. Không tính bản nháp và quan sát chưa duyệt. Các trang được đọc riêng có thể phản ánh thời điểm khác nhau trong khi nghiệp vụ tiếp diễn.</p>
      <form aria-label="Tra cứu tồn kho" aria-describedby={feedback ? "inventory-error" : undefined} onSubmit={e => void read(e)}>{!access?.organizationId && staff && <label>ID tổ chức khách hàng xem tồn kho<input required value={org} onChange={e => edit(setOrg, e.target.value)} /></label>}
        <label>ID quyền sử dụng khoang xem tồn kho<input required value={grant} onChange={e => edit(setGrant, e.target.value)} /></label><button disabled={!access || busy || !grant.trim() || (!access.organizationId && (!staff || !org.trim()))}>Tải tồn kho</button></form>
      {feedback && <p ref={errorRef} id="inventory-error" role="alert" tabIndex={-1} className="session-notice">{feedback.text}</p>}
      {visible && <div ref={resultsRef} role="region" aria-label="Kết quả tồn kho" tabIndex={-1}><h3>Tồn kho · Trang {visible.balancePage}</h3>{visible.balances.items.length ? visible.balances.items.map(item => <div className="unit-row" key={item.itemId}><span>{item.sku}</span><strong>{item.quantity} {item.baseUom}</strong></div>) : <p>Trang này chưa có số dư tồn kho đã ghi nhận.</p>}
        <div className="session-actions"><button disabled={busy || visible.balancePage <= 1} onClick={() => void read(undefined, visible.balancePage - 1, visible.ledgerPage, visible)}>Trang tồn kho trước</button><button disabled={busy || visible.balancePage * 50 >= visible.balances.total} onClick={() => void read(undefined, visible.balancePage + 1, visible.ledgerPage, visible)}>Trang tồn kho sau</button></div>
        <h3>Lịch sử xuất nhập · Trang {visible.ledgerPage}</h3>{visible.ledger.items.length ? visible.ledger.items.map(item => <div className="unit-row" key={item.id}><span>{item.sku} · {item.delta} {item.baseUom}<small>Nguồn {item.stockPostingId} · {item.postedAt}</small></span></div>) : <p>Trang này chưa có giao dịch xuất nhập đã ghi nhận.</p>}
        <div className="session-actions"><button disabled={busy || visible.ledgerPage <= 1} onClick={() => void read(undefined, visible.balancePage, visible.ledgerPage - 1, visible)}>Trang nhật ký trước</button><button disabled={busy || visible.ledgerPage * 50 >= visible.ledger.total} onClick={() => void read(undefined, visible.balancePage, visible.ledgerPage + 1, visible)}>Trang nhật ký sau</button></div>
      </div>}
    </div></section>;
}
