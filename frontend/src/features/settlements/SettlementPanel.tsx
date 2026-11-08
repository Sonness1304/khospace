import { displayValue, reasonLabel } from '../../locale/vi';
import { useEffect, useRef, useState } from 'react';
import type { WorkspaceAccess } from '../auth/workspace';
import { api, ApiError, command, isDefiniteRejection } from '../../api';

type Scope = { organizationId: string; facilityId: string };
type Summary = Scope & { id: string; state: string; version: string; canClose: boolean; blockers: string[]; netBilled: string; rentalPaid: string; outstanding: string; depositHeld: string; unappliedVerified: string };
type Statement = Scope & { id: string; rentalAccountId: string; total: string; netTotal: string; outstanding: string };
type HistoryItem = { id: string; kind: string; amount: string };
type History = { items: HistoryItem[]; adjustments: HistoryItem[]; total: number; adjustmentTotal: number; page: number };
type Bound<T> = { value: T; context: string };
type Pending = Scope & { userId: string; context: string; key: string; path: string; body: object; version?: string; expectedId?: string; uncertain?: boolean };
type Receipt = { result: { resourceId: string } };
const money = (value: string) => /^[0-9]+$/.test(value) && Number(value) > 0;
const positive = (value: string, scale: number) => new RegExp(`^[0-9]+([.][0-9]{1,${scale}})?$`).test(value) && Number(value) > 0;

export default function SettlementPanel({ access }: { access: WorkspaceAccess | null }) {
  const context = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId, access?.facilityRoles, access?.operatorAdmin]);
  const [owner, setOwner] = useState(context); const [accountId, setAccountId] = useState(''); const [statementId, setStatementId] = useState('');
  const [accountData, setAccountData] = useState<Bound<Summary> | null>(null); const [statementData, setStatementData] = useState<Bound<Statement> | null>(null); const [historyData, setHistoryData] = useState<Bound<History> | null>(null);
  const [reason, setReason] = useState(''); const [evidence, setEvidence] = useState(''); const [kind, setKind] = useState('Credit'); const [amount, setAmount] = useState(''); const [quantity, setQuantity] = useState(''); const [price, setPrice] = useState('');
  const [entryKind, setEntryKind] = useState('RefundDeposit'); const [entryAmount, setEntryAmount] = useState(''); const [reference, setReference] = useState(''); const [target, setTarget] = useState(''); const [receiptId, setReceiptId] = useState('');
  const [performed, setPerformed] = useState(() => new Date().toISOString().slice(0, 16)); const [approved, setApproved] = useState(false); const [confirmed, setConfirmed] = useState(false); const [closeConfirmed, setCloseConfirmed] = useState(false);
  const [message, setMessage] = useState<Bound<string> | null>(null); const [busy, setBusy] = useState(false); const [pending, setPending] = useState<Pending | null>(null); const [retry, setRetry] = useState(false);
  const controller = useRef<AbortController | null>(null); const active = useRef(false); const unresolved = useRef<Pending | null>(null);
  if (owner !== context) { setOwner(context); setAccountId(''); setStatementId(''); setReason(''); setEvidence(''); setAmount(''); setQuantity(''); setPrice(''); setEntryAmount(''); setReference(''); setTarget(''); setReceiptId(''); setApproved(false); setConfirmed(false); setCloseConfirmed(false); }
  useEffect(() => { const lifetime = new AbortController(); controller.current = lifetime; return () => lifetime.abort(); }, []);
  const finance = (facility: string) => !!access && (access.operatorAdmin || access.facilityId === facility && access.facilityRoles.includes('Finance'));
  const readable = (scope: Scope) => !!access && (scope.organizationId === access.organizationId || finance(scope.facilityId));
  const allowed = (request: Pending) => request.userId === access?.userId && finance(request.facilityId);
  const account = accountData?.context === context && readable(accountData.value) ? accountData.value : null;
  const statement = statementData?.context === context && readable(statementData.value) ? statementData.value : null;
  const history = historyData?.context === context ? historyData.value : null;
  const blocked = busy || !!pending;
  async function read(kind: 'account' | 'statement' | 'history', page = 1) {
    const lifetime = controller.current; if (!lifetime || active.current || !access) return; active.current = true; setBusy(true); setMessage(null); const bound = context;
    setApproved(false); setConfirmed(false); setCloseConfirmed(false);
    try {
      if (kind === 'account') { const value = await api<Summary>(`/rental-accounts/${encodeURIComponent(accountId.trim())}/settlement`, lifetime.signal); if (value.id !== accountId.trim() || !Array.isArray(value.blockers) || !value.version) throw Error('Chưa xác minh tài khoản'); if (!lifetime.signal.aborted) { setAccountData({ value, context: bound }); setHistoryData(null); } }
      else if (kind === 'statement') { const value = await api<Statement>(`/statements/${encodeURIComponent(statementId.trim())}`, lifetime.signal); if (value.id !== statementId.trim() || !value.rentalAccountId || !value.facilityId) throw Error('Chưa xác minh bảng kê'); if (!lifetime.signal.aborted) setStatementData({ value, context: bound }); }
      else { if (!account) return; const value = await api<History>(`/rental-accounts/${account.id}/settlement-history?page=${page}`, lifetime.signal); if (!Array.isArray(value.items) || !Array.isArray(value.adjustments)) throw Error('Chưa xác minh lịch sử'); if (!lifetime.signal.aborted) setHistoryData({ value, context: bound }); }
    } catch { if (!lifetime.signal.aborted) { setAccountData(null); setStatementData(null); setHistoryData(null); setMessage({ value: 'Chưa xác minh được dữ liệu quyết toán. Kiểm tra ID, quyền truy cập và kết nối hiện tại.', context: bound }); } }
    finally { active.current = false; if (!lifetime.signal.aborted) setBusy(false); }
  }
  function saved(result: Receipt, request: Pending) {
    if (typeof result.result?.resourceId !== 'string' || !result.result.resourceId || request.expectedId && request.expectedId !== result.result.resourceId) throw Error('Chưa xác minh mã định danh quyết toán');
    unresolved.current = null; setPending(null); setRetry(false); setAccountData(null); setStatementData(null); setHistoryData(null); setApproved(false); setConfirmed(false); setCloseConfirmed(false);
    setMessage({ value: `Đã lưu yêu cầu quyết toán: ${result.result.resourceId}. Tải lại tài khoản và bảng kê trước khi quyết định tiếp.`, context: request.context });
  }
  async function attempt(request: Pending) {
    const lifetime = controller.current; if (!lifetime || active.current || !allowed(request)) return; active.current = true; unresolved.current = request; setPending(request); setBusy(true); setRetry(false); setMessage(null);
    try { const value = await command<Receipt>(request.path, request.body, request.key, lifetime.signal, 'POST', request.version); if (!lifetime.signal.aborted) saved(value, request); }
    catch (error) { if (!lifetime.signal.aborted) {
      if (!request.uncertain && isDefiniteRejection(error)) { unresolved.current = null; setPending(null); setMessage({ value: 'Yêu cầu quyết toán bị từ chối. Tải phiên bản, số dư và chứng từ hiện tại; giữ bản nháp của bạn.', context: request.context }); }
      else { const unknown = { ...request, uncertain: true }; unresolved.current = unknown; setPending(unknown); setMessage({ value: 'Chưa xác định kết quả quyết toán. Đọc yêu cầu gốc đã lưu trước khi quyết định tiếp.', context: request.context }); }
    } } finally { active.current = false; if (!lifetime.signal.aborted) setBusy(false); }
  }
  function send(path: string, body: object, scope: Scope, expectedId?: string, version?: string) {
    if (!access || unresolved.current || !reason.trim()) return;
    void attempt({ ...scope, userId: access.userId, context, key: crypto.randomUUID(), path, body: structuredClone(body), expectedId, version });
  }
  async function readback() {
    const lifetime = controller.current; if (!lifetime || active.current || !pending || !allowed(pending)) return; active.current = true; setBusy(true);
    try { const value = await api<Receipt>(`/commands/${pending.key}?scopeId=${pending.organizationId}`, lifetime.signal); if (!lifetime.signal.aborted) saved(value, pending); }
    catch (error) { if (!lifetime.signal.aborted) { setRetry(error instanceof ApiError && error.status === 404); setMessage({ value: 'Chưa xác minh được kết quả quyết toán đã lưu. Giữ nguyên mã định danh yêu cầu gốc.', context: pending.context }); } }
    finally { active.current = false; if (!lifetime.signal.aborted) setBusy(false); }
  }
  return <section className="workspace-panel" aria-labelledby="settlement-title" aria-busy={busy}>
    <div className="panel-heading"><div><p className="eyebrow">ĐỐI SOÁT HÀNG VÀ TIỀN</p><h2 id="settlement-title">Điều chỉnh và quyết toán cuối cùng</h2></div></div>
    <div className="session-content"><p>Khoản thu đã phát hành được giữ nguyên. Điều chỉnh tạo thêm bản ghi liên kết. Bút toán hoàn tiền ghi nhận khoản hoàn thủ công đã hoàn tất; ứng dụng không thực hiện chuyển tiền.</p>
      {message?.context === context && <p role="status" className="session-notice">{message.value}</p>}
      {pending && <div className="session-actions"><button disabled={busy || !allowed(pending)} onClick={() => void readback()}>Đọc kết quả yêu cầu quyết toán</button>{retry && <button disabled={busy || !allowed(pending)} onClick={() => void attempt(pending)}>Thử lại yêu cầu quyết toán gốc</button>}</div>}
      <label>Lý do quyết toán<input maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label>
      <label>Tham chiếu chứng từ quyết toán<input maxLength={240} value={evidence} onChange={e => { setEvidence(e.target.value); setApproved(false); setConfirmed(false); }} /></label>
      <label>ID tài khoản thuê cần quyết toán<input value={accountId} onChange={e => { setAccountId(e.target.value); setCloseConfirmed(false); }} /></label>
      <button disabled={busy || !access || !accountId.trim()} onClick={() => void read('account')}>Kiểm tra điều kiện quyết toán</button>
      {account && <div className="booking-detail"><p>Tài khoản quyết toán {account.id} · {displayValue(account.state)}</p><p>Tổng thu sau điều chỉnh: {account.netBilled} VND · Tiền thuê đã trả: {account.rentalPaid} VND · Còn nợ: {account.outstanding} VND</p><p>Tiền bảo đảm đang giữ: {account.depositHeld} VND · Tiền đã xác minh chưa phân bổ: {account.unappliedVerified} VND</p>
        {account.blockers.length > 0 ? <ul>{account.blockers.map(value => <li key={value}>{reasonLabel(value)}</li>)}</ul> : <p>Hiện đã đạt mọi điều kiện quyết toán do máy chủ kiểm tra.</p>}
        <button disabled={busy} onClick={() => void read('history')}>Tải lịch sử quyết toán</button>
        {history && <div>{[...history.items, ...history.adjustments].map(value => <p key={value.id}>{value.id} · {displayValue(value.kind)} · {value.amount} VND</p>)}<button disabled={busy || history.page <= 1} onClick={() => void read('history', history.page - 1)}>Trang lịch sử quyết toán trước</button><button disabled={busy || history.page * 50 >= Math.max(history.total, history.adjustmentTotal)} onClick={() => void read('history', history.page + 1)}>Trang lịch sử quyết toán sau</button></div>}
        {finance(account.facilityId) && account.state !== 'Closed' && <>
          <fieldset><legend>Ghi nhận quyết định quyết toán</legend>
            <label>Loại bút toán quyết toán<select value={entryKind} onChange={e => { setEntryKind(e.target.value); setConfirmed(false); }}><option value="RefundDeposit">Hoàn tiền đặt cọc</option><option value="Deduction">Khấu trừ</option><option value="RefundUnapplied">Hoàn tiền chưa phân bổ</option></select></label>
            <label>Số tiền quyết toán (VND)<input inputMode="numeric" value={entryAmount} onChange={e => { setEntryAmount(e.target.value); setConfirmed(false); }} /></label>
            {entryKind === 'Deduction' && <label>ID bảng kê khấu trừ<input value={target} onChange={e => { setTarget(e.target.value); setConfirmed(false); }} /></label>}
            {entryKind === 'RefundUnapplied' && <label>ID phiếu thu nguồn hoàn tiền<input value={receiptId} onChange={e => { setReceiptId(e.target.value); setConfirmed(false); }} /></label>}
            <label>Mã tham chiếu giao dịch thủ công<input maxLength={120} value={reference} onChange={e => { setReference(e.target.value); setConfirmed(false); }} /></label>
            <label>Thời điểm thực hiện giao dịch (UTC)<input type="datetime-local" value={performed} onChange={e => { setPerformed(e.target.value); setConfirmed(false); }} /></label>
            <label className="check-label"><input type="checkbox" checked={confirmed} onChange={e => setConfirmed(e.target.checked)} />Tôi xác nhận khoản khấu trừ có chứng từ hoặc khoản hoàn tiền thủ công đã hoàn tất</label>
            <button disabled={blocked || !reason.trim() || !evidence.trim() || !reference.trim() || !performed || !money(entryAmount) || !confirmed || entryKind === 'Deduction' && !target.trim() || entryKind === 'RefundUnapplied' && !receiptId.trim()} onClick={() => send(`/rental-accounts/${account.id}/settlement-entries`, { kind: entryKind, amount: entryAmount, chargeStatementId: entryKind === 'Deduction' ? target : null, paymentReceiptId: entryKind === 'RefundUnapplied' ? receiptId : null, reference, evidenceReference: evidence, performedAt: `${performed}:00Z`, reason, confirmed: true }, account)}>Ghi bút toán quyết toán</button>
          </fieldset>
          <label className="check-label"><input type="checkbox" checked={closeConfirmed} onChange={e => setCloseConfirmed(e.target.checked)} />Tôi xác nhận hàng và tiền đã được xử lý xong; đóng vĩnh viễn</label>
          <button disabled={blocked || !account.canClose || !reason.trim() || !closeConfirmed} onClick={() => send(`/rental-accounts/${account.id}/close`, { reason, closureConfirmed: true }, account, account.id, account.version)}>Đóng tài khoản thuê đã quyết toán</button>
        </>}
      </div>}
      <label>ID bảng kê cần quyết toán<input value={statementId} onChange={e => { setStatementId(e.target.value); setApproved(false); }} /></label><button disabled={busy || !access || !statementId.trim()} onClick={() => void read('statement')}>Tải bảng kê cần điều chỉnh</button>
      {statement && <div className="booking-detail"><p>Bảng kê {statement.id} · Phát hành ban đầu: {statement.total} VND · Sau điều chỉnh: {statement.netTotal} VND · Còn nợ: {statement.outstanding} VND</p>
        {finance(statement.facilityId) && <fieldset><legend>Lập điều chỉnh có tham chiếu</legend>
          <label>Loại điều chỉnh khoản thu<select value={kind} onChange={e => { setKind(e.target.value); setApproved(false); }}><option value="Credit">Giảm trừ</option><option value="Supplement">Bổ sung</option><option value="Service">Dịch vụ</option></select></label>
          {kind === 'Service' ? <><label>Số lượng dịch vụ<input inputMode="decimal" value={quantity} onChange={e => { setQuantity(e.target.value); setApproved(false); }} /></label><label>Đơn giá dịch vụ (VND)<input inputMode="decimal" value={price} onChange={e => { setPrice(e.target.value); setApproved(false); }} /></label></> : <label>Số tiền điều chỉnh khoản thu (VND)<input inputMode="numeric" value={amount} onChange={e => { setAmount(e.target.value); setApproved(false); }} /></label>}
          <label className="check-label"><input type="checkbox" checked={approved} onChange={e => setApproved(e.target.checked)} />Tôi phê duyệt điều chỉnh khoản thu có tham chiếu này</label>
          <button disabled={blocked || !reason.trim() || !evidence.trim() || !approved || (kind === 'Service' ? !positive(quantity, 3) || !positive(price, 4) : !money(amount))} onClick={() => send(`/statements/${statement.id}/adjustments`, { kind, amount: kind === 'Service' ? null : amount, quantity: kind === 'Service' ? quantity : null, unitPrice: kind === 'Service' ? price : null, evidenceReference: evidence, reason, approved: true }, statement)}>Lập điều chỉnh khoản thu</button>
          <button disabled={blocked || !reason.trim() || !evidence.trim() || !approved} onClick={() => send(`/statements/${statement.id}/supplement-rent`, { evidenceReference: evidence, reason, approved: true }, statement, statement.id)}>Lập khoản thuê bổ sung chưa lên bảng kê</button>
        </fieldset>}
      </div>}
    </div>
  </section>;
}
