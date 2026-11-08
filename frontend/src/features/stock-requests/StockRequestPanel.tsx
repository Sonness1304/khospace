import { displayValue } from '../../locale/vi';
import { useEffect, useId, useLayoutEffect, useRef, useState, type FormEvent } from 'react';
import type { WorkspaceAccess } from '../auth/workspace';
import { api, ApiError, command, isDefiniteRejection } from '../../api';
import { BarcodeLookupPanel, type BarcodeSelection } from '../barcodes/BarcodePanels';
type Item = { id: string; sku: string; baseUom: string };
type Request = { id: string; organizationId: string; facilityId: string; leaseId: string; leaseUnitId: string; type: string; state: string; version: string; replacesRequestId?: string; clearanceAuthorizedAt?: string;
  lines: { id: string; itemId: string; sku: string; baseUom: string; quantity: string; fulfilledQuantity?: string; remainingQuantity?: string }[] };
type Pending = { key: string; viewBinding: string; userId: string; organizationId: string; facilityId: string; path: string; body: object; version?: string; resourceId?: string; kind: 'customer' | 'staff' | 'posting'; uncertain?: boolean };
type Receipt = { result: { resourceId: string } };
const noticePrompt = 'Thông báo là lịch sử. Mã hồ sơ đã được điền; bấm Tải yêu cầu để kiểm tra quyền và trạng thái hiện tại.';
export default function StockRequestPanel({ access, selectedId, noticeKey }: { access: WorkspaceAccess | null; selectedId?: string; noticeKey?: string }) {
  const [externalSelection, setExternalSelection] = useState(selectedId);
  const [lookup, setLookup] = useState(selectedId ?? ''); const [data, setData] = useState<{ value: Request; userId: string } | null>(null);
  const [items, setItems] = useState<{ values: Item[]; organizationId: string; userId: string; page: number; total: number } | null>(null);
  const [createdItem, setCreatedItem] = useState<{ id: string; organizationId: string; userId: string } | null>(null);
  const [savedRequest, setSavedRequest] = useState<{ id: string; organizationId: string; facilityId: string; userId: string } | null>(null);
  const [leaseId, setLeaseId] = useState(''); const [grantId, setGrantId] = useState(''); const [type, setType] = useState('Receipt'); const [predecessor, setPredecessor] = useState('');
  const [lines, setLines] = useState([{ key: 1, itemId: '', quantity: '' }]); const sequence = useRef(1);
  const [sku, setSku] = useState(''); const [description, setDescription] = useState(''); const [uom, setUom] = useState('');
  const [observed, setObserved] = useState<{ requestLineId: string; quantity: string }[]>([]); const [checked, setChecked] = useState(false);
  const [reason, setReason] = useState(''); const [busy, setBusy] = useState(false); const [message, setMessage] = useState(noticeKey ? noticePrompt : '');
  const [pending, setPending] = useState<Pending | null>(null); const [retry, setRetry] = useState(false);
  const lifecycle = useRef<AbortController | null>(null); const active = useRef(false); const unresolved = useRef<Pending | null>(null);
  const feedbackId = useId(); const feedbackTarget = useRef<HTMLParagraphElement>(null); const feedbackIntent = useRef(0);
  const [error, setError] = useState<{ viewBinding: string; intent: number; kind: 'lookup' | 'items' | 'draft' | 'command'; text: string } | null>(null);
  function clearError() { feedbackIntent.current += 1; setError(null); }
  if (externalSelection !== selectedId) { setExternalSelection(selectedId); setLookup(selectedId ?? ''); setError(null); }
  const [noticeIntent, setNoticeIntent] = useState(noticeKey);
  if (noticeIntent !== noticeKey) { setNoticeIntent(noticeKey); setError(null); if (noticeKey) { setLookup(selectedId ?? ''); setData(null); setChecked(false); setMessage(noticePrompt); } }
  const draftBinding = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]); const [draftOwner, setDraftOwner] = useState(draftBinding);
  if (draftOwner !== draftBinding) { setDraftOwner(draftBinding); setError(null); setLookup(''); setLeaseId(''); setGrantId(''); setPredecessor(''); setLines([{ key: 0, itemId: '', quantity: '' }]); setSku(''); setDescription(''); setUom(''); setReason(''); setMessage(''); }
  const viewBinding = JSON.stringify([draftBinding, lookup.trim().toLowerCase(), noticeKey]);
  const visibleView = useRef(viewBinding);
  useLayoutEffect(() => { visibleView.current = viewBinding; feedbackIntent.current += 1; }, [viewBinding, selectedId]);
  const currentError = error?.viewBinding === viewBinding ? error : null;
  useEffect(() => { if (currentError && currentError.intent === feedbackIntent.current && !busy) feedbackTarget.current?.focus(); }, [currentError, busy]);
  function showError(text: string, kind: NonNullable<typeof error>['kind'], expectedView = visibleView.current, intent = feedbackIntent.current) {
    if (visibleView.current !== expectedView || feedbackIntent.current !== intent) return;
    setMessage(''); setError({ text, kind, viewBinding: expectedView, intent });
  }
  function invalidDraft(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    showError('Điền đầy đủ các trường bắt buộc. Số lượng phải lớn hơn 0, dùng dấu chấm và tối đa 3 chữ số thập phân (ví dụ 1.5).', 'draft');
  }
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  const staff = (facilityId: string) => !!access && (access.operatorAdmin || access.facilityId === facilityId && access.facilityRoles.some(x => ['Clerk', 'FacilityManager'].includes(x)));
  const canPost = (facilityId: string) => !!access && (access.operatorAdmin || access.facilityId === facilityId && access.facilityRoles.includes('Clerk'));
  const customer = !!access?.organizationId;
  const allowed = (value: { userId: string; organizationId: string; facilityId: string }) => value.userId === access?.userId && (value.organizationId === access.organizationId || staff(value.facilityId));
  const canWrite = (value: Pending) => allowed(value) && (value.kind === 'customer' ? value.organizationId === access?.organizationId : value.kind === 'posting' ? canPost(value.facilityId) : staff(value.facilityId));
  const request = data && data.value.id.toLowerCase() === lookup.trim().toLowerCase() && allowed({ ...data.value, userId: data.userId }) ? data.value : null;
  const customerItems = items?.organizationId === access?.organizationId && items?.userId === access?.userId ? items?.values ?? [] : [];
  async function load(id: string, userId: string, signal: AbortSignal, expectedView: string, intent?: number) {
    const value = await api<Request>(`/stock-requests/${encodeURIComponent(id)}`, signal);
    if (typeof value.id !== 'string' || value.id.toLowerCase() !== id.toLowerCase() || !value.organizationId || !value.facilityId || !Array.isArray(value.lines)) throw Error('Chưa xác minh yêu cầu');
    if (!signal.aborted && visibleView.current === expectedView && (intent === undefined || feedbackIntent.current === intent)) { setData({ value, userId }); setLookup(id); setObserved(value.lines.map(line => ({ requestLineId: line.id, quantity: '' }))); setChecked(false); }
  }
  async function loadItems(signal: AbortSignal, organizationId: string, userId: string, page = 1) {
    const value = await api<{ items: Item[]; total: number }>(`/items?organizationId=${organizationId}&page=${page}`, signal);
    if (!Array.isArray(value.items)) throw Error('Chưa xác minh mặt hàng');
    if (!signal.aborted) { setItems(previous => ({ values: [...(previous?.userId === userId && previous.organizationId === organizationId ? previous.values.filter(x => lines.some(line => line.itemId === x.id) && !value.items.some(item => item.id === x.id)) : []), ...value.items], organizationId, userId, page, total: value.total })); }
  }
  async function itemPage(page: number) {
    const controller = lifecycle.current; if (!controller || !access || active.current) return; active.current = true; setBusy(true);
    const expectedView = visibleView.current; clearError(); const intent = feedbackIntent.current;
    try { await loadItems(controller.signal, access.organizationId, access.userId, page); } catch { if (!controller.signal.aborted) showError('Không tải được trang mặt hàng khách hàng.', 'items', expectedView, intent); } finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function read(event?: FormEvent, itemList = false) {
    event?.preventDefault(); const controller = lifecycle.current; if (!controller || active.current || !access) return;
    const expectedView = visibleView.current;
    active.current = true; setBusy(true); setMessage(''); clearError(); const intent = feedbackIntent.current;
    try { if (itemList) await loadItems(controller.signal, access.organizationId, access.userId); else await load(lookup.trim(), access.userId, controller.signal, expectedView, intent); }
    catch { if (!controller.signal.aborted && visibleView.current === expectedView && feedbackIntent.current === intent) { if (!itemList) setData(null); showError('Chưa xác minh được dữ liệu kho. Kiểm tra quyền truy cập và kết nối hiện tại.', itemList ? 'items' : 'lookup', expectedView, intent); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function saved(result: Receipt, value: Pending, signal: AbortSignal, expectedView: string, intent: number) {
    if (!result.result?.resourceId || value.resourceId && result.result.resourceId !== value.resourceId) throw Error('Chưa xác minh mã định danh yêu cầu');
    if (signal.aborted) return;
    if (value.path === '/items') setCreatedItem({ id: result.result.resourceId, organizationId: value.organizationId, userId: value.userId });
    else setSavedRequest({ id: result.result.resourceId, organizationId: value.organizationId, facilityId: value.facilityId, userId: value.userId });
    unresolved.current = null; setPending(null); setRetry(false);
    if (visibleView.current === expectedView) { setError(null); setMessage(value.path === '/items' ? 'Đã lưu mặt hàng khách hàng. Giữ ID hiển thị để đăng ký mã vạch.' : value.kind === 'posting' ? value.path.endsWith('/fulfillments') ? 'Đã lưu giao dịch kho thực hiện từng phần.' : 'Đã lưu giao dịch kho thực hiện toàn bộ.' : 'Đã lưu yêu cầu kho. Tạo mặt hàng hoặc phê duyệt yêu cầu không tự ghi nhận xuất nhập.'); }
    // Resolve the original receipt without letting it replace a subsequently selected notice.
    try { if (value.path === '/items') await loadItems(signal, value.organizationId, value.userId); else if (expectedView === value.viewBinding && visibleView.current === expectedView) await load(result.result.resourceId, value.userId, signal, expectedView); }
    catch { if (!signal.aborted) showError(value.path === '/items' ? 'Đã lưu mặt hàng nhưng chưa đọc lại được danh sách. Giữ ID hiển thị để đăng ký mã vạch.' : 'Đã lưu yêu cầu kho nhưng chưa đọc lại được dữ liệu hiện tại. Vui lòng tải lại.', 'command', expectedView, intent); }
  }
  async function attempt(value: Pending) {
    const controller = lifecycle.current; if (!controller || active.current || !canWrite(value)) return;
    const expectedView = visibleView.current;
    active.current = true; unresolved.current = value; setPending(value); setBusy(true); setRetry(false); setMessage(''); clearError(); const intent = feedbackIntent.current;
    try { const result = await command<Receipt>(value.path, value.body, value.key, controller.signal, 'POST', value.version); await saved(result, value, controller.signal, expectedView, intent); }
    catch (error) { if (!controller.signal.aborted) {
      if (!value.uncertain && isDefiniteRejection(error)) { unresolved.current = null; setPending(null); showError(value.kind === 'posting' ? 'Ghi kho bị từ chối. Tải số lượng còn lại và tồn kho hiện tại. Thay đổi chỉ dẫn cần khách hàng cho phép.' : 'Yêu cầu kho bị từ chối. Tải dữ liệu mới và kiểm tra lại chỉ dẫn.', 'command', expectedView, intent); }
      else { const unknown = { ...value, uncertain: true }; unresolved.current = unknown; setPending(unknown); showError('Chưa xác định kết quả yêu cầu kho. Đọc kết quả đã lưu trước khi gửi yêu cầu khác.', 'command', expectedView, intent); }
    } } finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  function send(path: string, body: object, kind: Pending['kind'], resourceId?: string, version?: string) {
    if (!access || unresolved.current || !reason.trim()) return;
    void attempt({ key: crypto.randomUUID(), viewBinding, userId: access.userId, organizationId: resourceId && request ? request.organizationId : access.organizationId,
      facilityId: resourceId && request ? request.facilityId : access.facilityId, path, body, kind, resourceId, version });
  }
  async function readback() {
    const controller = lifecycle.current; if (!controller || active.current || !pending || !allowed(pending)) return;
    const expectedView = visibleView.current;
    active.current = true; setBusy(true); clearError(); const intent = feedbackIntent.current;
    try { const result = await api<Receipt>(`/commands/${pending.key}?scopeId=${pending.organizationId}`, controller.signal); await saved(result, pending, controller.signal, expectedView, intent); }
    catch (error) { if (!controller.signal.aborted) { setRetry(error instanceof ApiError && error.status === 404); showError('Chưa xác minh được kết quả kho đã lưu. Giữ nguyên mã định danh gốc.', 'command', expectedView, intent); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  const blocked = busy || !!pending;
  function useBarcode(item: BarcodeSelection) {
    if (!access || active.current || unresolved.current || item.organizationId !== access.organizationId || item.leaseId !== leaseId) return;
    if (!lines.some(x => x.itemId === item.itemId) && lines.every(x => x.itemId) && lines.length >= 50) { setMessage('Yêu cầu đã có 50 dòng. Hãy chọn thủ công một dòng hiện có.'); return; }
    const selected = { id: item.itemId, sku: item.sku, baseUom: item.baseUom };
    setItems(previous => ({ values: [...customerItems.filter(x => x.id !== item.itemId), selected], organizationId: access.organizationId, userId: access.userId, page: previous?.organizationId === access.organizationId && previous.userId === access.userId ? previous.page : 1, total: Math.max(customerItems.length + (customerItems.some(x => x.id === item.itemId) ? 0 : 1), previous?.total ?? 0) }));
    setLines(previous => { if (previous.some(x => x.itemId === item.itemId)) return previous; const empty = previous.find(x => !x.itemId); return empty ? previous.map(x => x.key === empty.key ? { ...x, itemId: item.itemId } : x) : [...previous, { key: ++sequence.current, itemId: item.itemId, quantity: '' }]; });
  }
  return <section className="workspace-panel" aria-labelledby="stock-requests-title" aria-busy={busy}>
    <div className="panel-heading"><div><p className="eyebrow">HÀNG LƯU GIỮ CỦA KHÁCH</p><h2 id="stock-requests-title">Mặt hàng và yêu cầu nhập xuất</h2></div></div>
    <div className="session-content"><p className="catalog-help">Tạo và phê duyệt yêu cầu không làm thay đổi tồn kho. Ghi nhận toàn bộ hoặc một phần số lượng thực tế trong giới hạn đã duyệt. Hủy phần chưa thực hiện; vượt chỉ dẫn phải có yêu cầu mới được khách hàng phê duyệt.</p>
      <label>Lý do thao tác kho<input maxLength={500} value={reason} onChange={e => { setReason(e.target.value); clearError(); }} /></label>
      {(currentError || message) && <p ref={feedbackTarget} id={feedbackId} tabIndex={-1} role={currentError ? 'alert' : 'status'} className="session-notice">{currentError?.text ?? message}</p>}
      {createdItem && createdItem.userId === access?.userId && createdItem.organizationId === access?.organizationId && <p>ID mặt hàng: {createdItem.id}. Dùng ID này để đăng ký mã vạch.</p>}
      {savedRequest && allowed(savedRequest) && <div role="region" aria-label="Yêu cầu kho đã lưu"><p>ID yêu cầu kho đã lưu: <code>{savedRequest.id}</code>. Giữ mã này để tải trạng thái hiện tại; không cần tạo lại yêu cầu.</p><button type="button" disabled={busy} onClick={() => { clearError(); setLookup(savedRequest.id); }}>Xem mã yêu cầu kho đã lưu</button></div>}
      {pending && <div className="session-actions"><button disabled={busy || !allowed(pending)} onClick={() => void readback()}>Đọc kết quả yêu cầu kho</button>{retry && <button disabled={busy || !canWrite(pending)} onClick={() => void attempt(pending)}>Thử lại yêu cầu kho gốc</button>}</div>}
      {customer && <><h3>Mặt hàng khách hàng</h3><div className="session-actions"><button disabled={busy} onClick={() => void read(undefined, true)}>Tải mặt hàng khách hàng</button></div>
        {customerItems.length > 0 && <details><summary>ID mặt hàng khách hàng để đăng ký mã vạch</summary><dl>{customerItems.map(item => <div key={item.id}><dt>{item.sku} · {item.baseUom}</dt><dd>ID mặt hàng: <code>{item.id}</code></dd></div>)}</dl></details>}
        <form onInvalid={invalidDraft} onChangeCapture={clearError} aria-describedby={currentError?.kind === 'draft' ? feedbackId : undefined} onSubmit={e => { e.preventDefault(); send('/items', { organizationId: access!.organizationId, sku, description, baseUom: uom, reason }, 'customer'); }}>
          <label>Mã SKU mặt hàng<input required maxLength={64} value={sku} onChange={e => setSku(e.target.value)} /></label><label>Mô tả mặt hàng<input required maxLength={200} value={description} onChange={e => setDescription(e.target.value)} /></label>
          <label>Đơn vị tính cơ sở<input required maxLength={24} value={uom} onChange={e => setUom(e.target.value)} /></label><button disabled={blocked || !reason.trim()}>Tạo mặt hàng khách hàng</button></form>
        {items?.organizationId === access?.organizationId && items?.userId === access?.userId && <div className="session-actions"><button disabled={busy || items.page <= 1} onClick={() => void itemPage(items.page - 1)}>Trang mặt hàng trước</button><button disabled={busy || items.page * 50 >= items.total} onClick={() => void itemPage(items.page + 1)}>Trang mặt hàng sau</button><span>Trang mặt hàng {items.page}</span></div>}
        <BarcodeLookupPanel access={access} leaseId={leaseId} onUse={useBarcode} disabled={blocked} />
        <h3>Chỉ dẫn mới của khách hàng</h3><p id={`${feedbackId}-quantity-help`} className="catalog-help">Số lượng phải lớn hơn 0, dùng dấu chấm và tối đa 3 chữ số thập phân (ví dụ 1.5). Điền đầy đủ các trường bắt buộc.</p><form onInvalid={invalidDraft} onChangeCapture={clearError} aria-describedby={currentError?.kind === 'draft' ? feedbackId : undefined} onSubmit={e => { e.preventDefault(); send('/stock-requests', { organizationId: access!.organizationId, leaseId, leaseUnitId: grantId, type, lines: lines.map(({ itemId, quantity }) => ({ itemId, quantity })), reason, replacesRequestId: predecessor || null }, 'customer'); }}>
          <label>ID hợp đồng của yêu cầu<input required value={leaseId} onChange={e => setLeaseId(e.target.value)} /></label><label>ID quyền sử dụng khoang của yêu cầu<input required value={grantId} onChange={e => setGrantId(e.target.value)} /></label>
          <label>Loại xuất nhập<select value={type} onChange={e => setType(e.target.value)}><option value="Receipt">Nhập kho</option><option value="Issue">Xuất kho</option></select></label>
          {lines.map((line, index) => <div className="session-actions" key={line.key}><label>Mặt hàng {index + 1}<select required value={line.itemId} onChange={e => setLines(values => values.map(x => x.key === line.key ? { ...x, itemId: e.target.value } : x))}><option value="">Chọn mặt hàng khách hàng</option>{customerItems.map(item => <option key={item.id} value={item.id}>{item.sku} · {item.baseUom}</option>)}</select></label>
            <label>Số lượng {index + 1}<input required inputMode="decimal" aria-describedby={`${feedbackId}-quantity-help`} pattern="(?=.*[1-9])[0-9]+([.][0-9]{1,3})?" value={line.quantity} onChange={e => setLines(values => values.map(x => x.key === line.key ? { ...x, quantity: e.target.value } : x))} /></label>
            {lines.length > 1 && <button type="button" onClick={() => setLines(values => values.filter(x => x.key !== line.key))}>Xóa dòng {index + 1}</button>}</div>)}
          <button type="button" disabled={lines.length >= 50} onClick={() => setLines(values => [...values, { key: ++sequence.current, itemId: '', quantity: '' }])}>Thêm dòng mặt hàng</button>
          <label>ID yêu cầu trước đã từ chối (không bắt buộc)<input value={predecessor} onChange={e => setPredecessor(e.target.value)} /></label><button disabled={blocked || !reason.trim() || !customerItems.length}>Tạo yêu cầu nhập xuất</button></form></>}
      {!customer && <BarcodeLookupPanel access={access} />}
      <h3>Xem yêu cầu hiện có</h3><form onSubmit={e => void read(e)}><label>ID yêu cầu nhập xuất<input required aria-invalid={currentError?.kind === 'lookup' || undefined} aria-describedby={currentError?.kind === 'lookup' ? feedbackId : undefined} value={lookup} onChange={e => { setLookup(e.target.value); clearError(); }} /></label><button disabled={!access || busy || !lookup.trim()}>Tải yêu cầu</button></form>
      {request && <div className="booking-detail"><p>Trạng thái yêu cầu: <strong>{displayValue(request.state)}</strong> · {displayValue(request.type)}</p><p>Hợp đồng {request.leaseId} · Quyền sử dụng {request.leaseUnitId}</p>
        {request.replacesRequestId && <p>Thay thế yêu cầu đã từ chối {request.replacesRequestId}</p>}{request.clearanceAuthorizedAt && <p>Đã được quản lý cho phép xuất hàng giải phóng kho {request.clearanceAuthorizedAt}</p>}
        {request.lines.map(line => <p key={line.id}>{line.sku}: {line.quantity} {line.baseUom}</p>)}
        {request.lines.some(line => line.fulfilledQuantity !== undefined) && request.lines.map(line => <p key={`fulfilled-${line.id}`}>{line.sku} đã thực hiện: {line.fulfilledQuantity} · Còn lại: {line.remainingQuantity}</p>)}
        <div className="session-actions">{request.organizationId === access?.organizationId && <>{request.state === 'Draft' && <button disabled={blocked || !reason.trim()} onClick={() => send(`/stock-requests/${request.id}/submit`, { reason }, 'customer', request.id, request.version)}>Gửi yêu cầu</button>}{['Draft', 'Submitted', 'Approved'].includes(request.state) && <button disabled={blocked || !reason.trim()} onClick={() => send(`/stock-requests/${request.id}/cancel`, { reason }, 'customer', request.id, request.version)}>Hủy yêu cầu</button>}</>}
          {staff(request.facilityId) && ['Submitted', 'Approved'].includes(request.state) && <><button disabled={blocked || !reason.trim()} onClick={() => send(`/stock-requests/${request.id}/approve`, { reason }, 'staff', request.id, request.version)}>Phê duyệt yêu cầu</button><button disabled={blocked || !reason.trim()} onClick={() => send(`/stock-requests/${request.id}/reject`, { reason }, 'staff', request.id, request.version)}>Từ chối yêu cầu</button></>}
        </div>
        {canPost(request.facilityId) && request.state === 'Approved' && <div><h3>Giao dịch xuất nhập thực tế</h3><p className="catalog-help">Nhập số lượng thực tế dương theo đơn vị cơ sở. Với giao dịch từng phần, để trống dòng chưa quan sát. Mỗi lần ghi kho có mã định danh thực hiện riêng, không thể thay đổi.</p>
          {request.lines.map(line => <label key={line.id}>Đã ghi nhận {line.sku} số lượng<input inputMode="decimal" value={observed.find(x => x.requestLineId === line.id)?.quantity ?? ''} onChange={e => { setChecked(false); setObserved(values => values.map(x => x.requestLineId === line.id ? { ...x, quantity: e.target.value } : x)); }} /></label>)}
          <label className="check-label"><input type="checkbox" checked={checked} onChange={e => setChecked(e.target.checked)} />Đã kiểm tra thực tế mọi số lượng</label>
          <button disabled={blocked || !reason.trim() || !checked || request.lines.some(x => Number(x.fulfilledQuantity ?? '0') > 0) || observed.length !== request.lines.length || observed.some(x => !/^[0-9]+([.][0-9]{1,3})?$/.test(x.quantity) || Number(x.quantity) <= 0)} onClick={() => send(`/stock-requests/${request.id}/post`, { reason, lines: observed }, 'posting', request.id, request.version)}>Ghi nhận xuất nhập toàn bộ</button>
          <button disabled={blocked || !reason.trim() || !checked || !observed.some(x => x.quantity !== '') || observed.some(x => x.quantity !== '' && (!/^[0-9]+([.][0-9]{1,3})?$/.test(x.quantity) || Number(x.quantity) <= 0 || Number(x.quantity) > Number(request.lines.find(l => l.id === x.requestLineId)?.remainingQuantity ?? request.lines.find(l => l.id === x.requestLineId)?.quantity ?? '0')))} onClick={() => send(`/stock-requests/${request.id}/fulfillments`, { fulfillmentId: crypto.randomUUID(), reason, lines: observed.filter(x => x.quantity !== '') }, 'posting', request.id, request.version)}>Ghi nhận xuất nhập từng phần</button>
        </div>}
        </div>}
    </div></section>;
}
