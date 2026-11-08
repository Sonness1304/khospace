import { displayValue } from '../../locale/vi';
import PriceBasisNote from '../pricing/PriceBasis';
import type { PriceBasis } from '../pricing/priceLabel';
import { useEffect, useLayoutEffect, useRef, useState, type FormEvent } from 'react';
import type { WorkspaceAccess } from '../auth/workspace';
import { api, ApiError, command, isDefiniteRejection } from '../../api';
type Scope = { id: string; organizationId: string; facilityId: string };
type Account = Scope & { bookingId: string; leaseId?: string; leaseActivatedAt?: string; state: string; version: string; requiredDeposit: string; depositHeld: string; billed: string; rentalPaid: string; outstanding: string; unappliedVerified: string; refundNeeded: boolean;
  zeroDepositVerifiedAt?: string; receipts: { id: string; reference: string; state: string; amount: string }[]; statements: { id: string; periodStart: string; total: string }[]; receiptTotal: number; statementTotal: number; receiptsPage?: number; statementsPage?: number };
type Payment = Scope & { rentalAccountId: string; state: string; version: string; reference: string; method: string; amount: string; allocated: string; unapplied: string; allocations: { id: string; targetType: string; amount: string }[] };
type Statement = Scope & { rentalAccountId: string; periodStart: string; dueDate: string; total: string; paid: string; outstanding: string; paymentState: string; overdue: boolean; lines: ({ id: string; startDate: string; endDate: string; amount: string } & PriceBasis)[] };
type Kind = 'account' | 'receipt' | 'statement';
type Pending = { context: string; key: string; userId: string; organizationId: string; facilityId: string; path: string; body: object; resultKind: Kind; version?: string; expectedId?: string; uncertain?: boolean };
type Receipt = { result: { resourceId: string } };
export default function FinancePanel({ access, selectedId }: { access: WorkspaceAccess | null; selectedId?: string }) {
  const [externalSelection, setExternalSelection] = useState(selectedId);
  const [accountId, setAccountId] = useState(''); const [receiptId, setReceiptId] = useState(''); const [statementId, setStatementId] = useState('');
  const [accountData, setAccountData] = useState<{ value: Account; userId: string; context: string } | null>(null); const [receiptData, setReceiptData] = useState<{ value: Payment; userId: string; context: string } | null>(null); const [statementData, setStatementData] = useState<{ value: Statement; userId: string; context: string } | null>(null);
  const [amount, setAmount] = useState(''); const [reference, setReference] = useState(''); const [method, setMethod] = useState('BankTransfer'); const [receivedAt, setReceivedAt] = useState(''); const [evidence, setEvidence] = useState(''); const [checked, setChecked] = useState(false);
  const [targetType, setTargetType] = useState('Deposit'); const [targetId, setTargetId] = useState(''); const [allocation, setAllocation] = useState(''); const [period, setPeriod] = useState(''); const [due, setDue] = useState('');
  const [reason, setReason] = useState(''); const [busy, setBusy] = useState(false); const [message, setMessage] = useState(''); const [pending, setPending] = useState<Pending | null>(null); const [retry, setRetry] = useState(false);
  const active = useRef(false); const unresolved = useRef<Pending | null>(null); const lifecycle = useRef<AbortController | null>(null);
  if (externalSelection !== selectedId) { setExternalSelection(selectedId); setAccountId(selectedId ?? ''); }
  const context = JSON.stringify([access?.userId, access?.organizationId, access?.facilityId]); const visibleContext = useRef(context);
  useLayoutEffect(() => { visibleContext.current = context; }, [context]);
  const [draftContext, setDraftContext] = useState(context);
  if (draftContext !== context) {
    setDraftContext(context); setAccountId(''); setReceiptId(''); setStatementId(''); setAmount(''); setReference(''); setMethod('BankTransfer'); setReceivedAt(''); setEvidence(''); setChecked(false);
    setTargetType('Deposit'); setTargetId(''); setAllocation(''); setPeriod(''); setDue(''); setReason(''); setMessage('');
  }
  useEffect(() => { const controller = new AbortController(); lifecycle.current = controller; return () => controller.abort(); }, []);
  const finance = (facilityId: string) => !!access && (access.operatorAdmin || access.facilityId === facilityId && access.facilityRoles.includes('Finance'));
  const allowed = (value: { userId: string; organizationId: string; facilityId: string }) => value.userId === access?.userId && (value.organizationId === access.organizationId || finance(value.facilityId));
  const visible = <T extends Scope,>(data: { value: T; userId: string; context: string } | null) => data && data.context === context && allowed({ ...data.value, userId: data.userId }) ? data.value : null;
  const account = visible(accountData); const payment = visible(receiptData); const statement = visible(statementData);
  async function load(kind: Kind, id: string, userId: string, signal: AbortSignal, receiptPage = 1, statementPage = 1) {
    const expectedContext = visibleContext.current;
    const path = kind === 'account' ? 'rental-accounts' : kind === 'receipt' ? 'receipts' : 'statements'; const value = await api<Account | Payment | Statement>(`/${path}/${encodeURIComponent(id)}${kind === 'account' ? `?receiptsPage=${receiptPage}&statementsPage=${statementPage}` : ''}`, signal);
    if (!value.id || !value.organizationId || !value.facilityId) throw Error('Chưa xác minh dữ liệu tài chính');
    if (!signal.aborted && expectedContext === visibleContext.current) {
      if (kind === 'account') { if (!Array.isArray((value as Account).receipts) || !Array.isArray((value as Account).statements)) throw Error('Chưa xác minh danh sách tài khoản'); setAccountData({ value: value as Account, userId, context: expectedContext }); setAccountId(id); }
      else if (kind === 'receipt') { setReceiptData({ value: value as Payment, userId, context: expectedContext }); setReceiptId(id); setChecked(false); }
      else { setStatementData({ value: value as Statement, userId, context: expectedContext }); setStatementId(id); }
    }
  }
  async function accountPage(receiptsPage: number, statementsPage: number) {
    const controller = lifecycle.current; if (!controller || active.current || !access || !account) return; active.current = true; setBusy(true);
    try { await load('account', account.id, access.userId, controller.signal, receiptsPage, statementsPage); } catch { if (!controller.signal.aborted) setMessage('Không tải được trang tài khoản tài chính.'); } finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function read(kind: Kind, event?: FormEvent, id?: string) {
    event?.preventDefault(); const controller = lifecycle.current; if (!controller || active.current || !access) return;
    active.current = true; setBusy(true); setMessage('');
    try { await load(kind, id ?? (kind === 'account' ? accountId : kind === 'receipt' ? receiptId : statementId).trim(), access.userId, controller.signal); }
    catch { if (!controller.signal.aborted) { if (kind === 'account') setAccountData(null); else if (kind === 'receipt') setReceiptData(null); else setStatementData(null); setMessage('Chưa xác minh được dữ liệu tài chính. Kiểm tra chủ sở hữu, quyền truy cập và kết nối hiện tại.'); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  async function saved(result: Receipt, value: Pending, signal: AbortSignal) {
    if (!result.result?.resourceId || value.expectedId && result.result.resourceId !== value.expectedId) throw Error('Chưa xác minh mã định danh yêu cầu tài chính');
    if (signal.aborted) return;
    unresolved.current = null; setPending(null); setRetry(false); setMessage('Đã lưu yêu cầu tài chính. Chưa thực hiện chuyển tiền bên ngoài.');
    if (value.context !== visibleContext.current) return;
    try { await load(value.resultKind, result.result.resourceId, value.userId, signal); if (account && value.resultKind !== 'account') await load('account', account.id, value.userId, signal); }
    catch { if (!signal.aborted) setMessage('Đã lưu yêu cầu tài chính nhưng chưa đọc lại được kết quả hiện tại. Vui lòng tải lại dữ liệu.'); }
  }
  async function attempt(value: Pending) {
    const controller = lifecycle.current; if (!controller || active.current || !allowed(value) || !finance(value.facilityId)) return;
    active.current = true; unresolved.current = value; setPending(value); setBusy(true); setRetry(false); setMessage('');
    try { const result = await command<Receipt>(value.path, value.body, value.key, controller.signal, 'POST', value.version); await saved(result, value, controller.signal); }
    catch (error) { if (!controller.signal.aborted) {
      if (!value.uncertain && isDefiniteRejection(error)) { unresolved.current = null; setPending(null); setMessage('Yêu cầu tài chính bị từ chối. Tải phiên bản mới và kiểm tra tiền đã xác minh, đích phân bổ và trạng thái thuê.'); }
      else { const unknown = { ...value, uncertain: true }; unresolved.current = unknown; setPending(unknown); setMessage('Chưa xác định kết quả yêu cầu tài chính. Đọc kết quả đã lưu trước khi gửi yêu cầu tài chính khác.'); }
    } } finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  function send(scope: Scope, path: string, body: object, resultKind: Kind, version?: string, expectedId?: string) {
    if (!access || unresolved.current || !reason.trim()) return;
    void attempt({ context, key: crypto.randomUUID(), userId: access.userId, organizationId: scope.organizationId, facilityId: scope.facilityId, path, body, resultKind, version, expectedId });
  }
  async function readback() {
    const controller = lifecycle.current; if (!controller || active.current || !pending || !allowed(pending)) return;
    active.current = true; setBusy(true);
    try { const result = await api<Receipt>(`/commands/${pending.key}?scopeId=${pending.organizationId}`, controller.signal); await saved(result, pending, controller.signal); }
    catch (error) { if (!controller.signal.aborted) { setRetry(error instanceof ApiError && error.status === 404); setMessage('Chưa xác minh được kết quả tài chính đã lưu. Giữ nguyên mã định danh yêu cầu gốc.'); } }
    finally { active.current = false; if (!controller.signal.aborted) setBusy(false); }
  }
  const blocked = busy || !!pending;
  return <section className="workspace-panel" aria-labelledby="finance-title" aria-busy={busy}>
    <div className="panel-heading"><div><p className="eyebrow">TÁCH BẠCH TIỀN VÀ KHOẢN THU</p><h2 id="finance-title">Tài chính thuê kho</h2></div></div>
    <div className="session-content"><p className="catalog-help">Phiếu thu thủ công phải được tài chính xác minh trước khi phân bổ. Tiền bảo đảm hoàn lại được tách khỏi tiền thuê. Tiền đến muộn không khôi phục chỗ đã hết hạn hoặc bị hủy; hệ thống không thực hiện hoàn tiền hay chuyển tiền bên ngoài tại đây.</p>
      <label>Lý do thao tác tài chính<input maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label>
      {message && <p role="status" className="session-notice">{message}</p>}{pending && <div className="session-actions"><button disabled={busy || !allowed(pending)} onClick={() => void readback()}>Đọc kết quả yêu cầu tài chính</button>{retry && <button disabled={busy || !allowed(pending) || !finance(pending.facilityId)} onClick={() => void attempt(pending)}>Thử lại yêu cầu tài chính gốc</button>}</div>}
      <form onSubmit={e => void read('account', e)}><label>ID tài khoản thuê cần xử lý tài chính<input required value={accountId} onChange={e => setAccountId(e.target.value)} /></label><button disabled={!access || busy || !accountId.trim()}>Tải tài khoản thuê</button></form>
      {account && <div className="booking-detail"><p>Tài khoản {account.id} · {displayValue(account.state)}</p><p>Tiền đặt cọc hoàn lại yêu cầu: {account.requiredDeposit} VND</p><p>Tiền bảo đảm hoàn lại đang giữ: {account.depositHeld} VND</p><p>Tiền thuê đã lập bảng kê: {account.billed} VND</p><p>Tiền thuê còn nợ: {account.outstanding} VND</p><p>Tiền đã xác minh chưa phân bổ: {account.unappliedVerified} VND</p>
        {account.refundNeeded && <p role="status">Cần tài chính kiểm tra hoặc hoàn tiền. Xem lịch sử quyết toán để tra chứng từ hoàn tiền thủ công.</p>}
        <div className="session-actions">{account.receipts.map(x => <button key={x.id} disabled={busy} onClick={() => void read('receipt', undefined, x.id)}>{x.reference} · {x.amount} VND · {displayValue(x.state)}</button>)}{account.statements.map(x => <button key={x.id} disabled={busy} onClick={() => void read('statement', undefined, x.id)}>Bảng kê {x.periodStart} · {x.total} VND</button>)}</div>
        <div className="session-actions"><button disabled={busy || (account.receiptsPage ?? 1) <= 1} onClick={() => void accountPage((account.receiptsPage ?? 1) - 1, account.statementsPage ?? 1)}>Trang phiếu thu tài khoản trước</button><button disabled={busy || (account.receiptsPage ?? 1) * 50 >= account.receiptTotal} onClick={() => void accountPage((account.receiptsPage ?? 1) + 1, account.statementsPage ?? 1)}>Trang phiếu thu tài khoản sau</button><button disabled={busy || (account.statementsPage ?? 1) <= 1} onClick={() => void accountPage(account.receiptsPage ?? 1, (account.statementsPage ?? 1) - 1)}>Trang bảng kê tài khoản trước</button><button disabled={busy || (account.statementsPage ?? 1) * 50 >= account.statementTotal} onClick={() => void accountPage(account.receiptsPage ?? 1, (account.statementsPage ?? 1) + 1)}>Trang bảng kê tài khoản sau</button></div>
        {finance(account.facilityId) && <><h3>Ghi nhận tiền đã nhận</h3><p className="catalog-help">Phiếu thu được gắn với tài khoản thuê này. Đối chiếu mã tham chiếu và phương thức với chứng từ; tệp khách hàng tải lên không tự xác nhận đã thanh toán.</p><form onSubmit={e => { e.preventDefault(); send(account, '/receipts', { rentalAccountId: account.id, amount, reference, method, receivedAt: new Date(receivedAt).toISOString(), evidenceReference: evidence, reason }, 'receipt'); }}>
          <label>Số tiền đã nhận (VND)<input required inputMode="numeric" pattern="[0-9]+" value={amount} onChange={e => setAmount(e.target.value)} /></label><label>Mã tham chiếu phiếu thu<input required maxLength={120} value={reference} onChange={e => setReference(e.target.value)} /></label>
          <label>Phương thức thu tiền<select value={method} onChange={e => setMethod(e.target.value)}><option value="BankTransfer">Chuyển khoản ngân hàng</option><option value="Cash">Tiền mặt</option><option value="Other">Khác</option></select></label><label>Thời điểm nhận (giờ địa phương)<input required type="datetime-local" value={receivedAt} onChange={e => setReceivedAt(e.target.value)} /></label><label>Tham chiếu chứng từ<input required maxLength={240} value={evidence} onChange={e => setEvidence(e.target.value)} /></label><button disabled={blocked || !reason.trim()}>Ghi phiếu thu thủ công</button></form>
          {account.requiredDeposit === '0' && !account.zeroDepositVerifiedAt && <button disabled={blocked || !reason.trim()} onClick={() => send(account, `/rental-accounts/${account.id}/verify-zero-deposit`, { reason }, 'account', account.version, account.id)}>Xác minh tiền đặt cọc làm tròn bằng 0</button>}
          {account.leaseId && <form onSubmit={e => { e.preventDefault(); send(account, `/leases/${account.leaseId}/statements`, { periodStart: period, dueDate: due, reason }, 'statement'); }}><h3>Lập bảng kê tháng đã thỏa thuận</h3><label>Ngày đầu tháng của bảng kê<input required type="date" value={period} onChange={e => setPeriod(e.target.value)} /></label><label>Hạn thanh toán bảng kê<input required type="date" value={due} onChange={e => setDue(e.target.value)} /></label><button disabled={blocked || !reason.trim() || !account.leaseActivatedAt}>Lập bảng kê tiền thuê</button><p>Yêu cầu hợp đồng đã kích hoạt thực tế; dùng dữ liệu đã chốt theo lịch thỏa thuận và hạn thanh toán cụ thể.</p></form>}
        </>}
      </div>}
      <form onSubmit={e => void read('receipt', e)}><label>ID phiếu thu thanh toán<input required value={receiptId} onChange={e => setReceiptId(e.target.value)} /></label><button disabled={!access || busy || !receiptId.trim()}>Tải phiếu thu thanh toán</button></form>
      {payment && <div className="booking-detail"><p>Trạng thái phiếu thu: <strong>{displayValue(payment.state)}</strong> · {payment.reference}</p><p>Tài khoản thuê liên kết: {payment.rentalAccountId}</p><p>Đã nhận: {payment.amount} VND · Đã phân bổ: {payment.allocated} VND · Chưa phân bổ: {payment.unapplied} VND</p>
        {payment.allocations.map(x => <p key={x.id}>{displayValue(x.targetType)} phân bổ: {x.amount} VND</p>)}
        {finance(payment.facilityId) && <>{payment.state === 'Recorded' && <><label className="check-label"><input type="checkbox" checked={checked} onChange={e => setChecked(e.target.checked)} />Đã kiểm tra chứng từ phiếu thu độc lập</label><button disabled={blocked || !reason.trim() || !checked} onClick={() => send(payment, `/receipts/${payment.id}/verify`, { reason }, 'receipt', payment.version, payment.id)}>Xác minh phiếu thu thủ công</button><button disabled={blocked || !reason.trim() || !checked} onClick={() => send(payment, `/receipts/${payment.id}/reject`, { reason }, 'receipt', payment.version, payment.id)}>Từ chối phiếu thu đã ghi</button></>}
          {payment.state === 'Verified' && <form onSubmit={e => { e.preventDefault(); send(payment, `/receipts/${payment.id}/allocations`, { targetType, targetId: targetType === 'Deposit' ? payment.rentalAccountId : targetId, amount: allocation, reason }, 'receipt', payment.version, payment.id); }}><label>Đích phân bổ tiền<select value={targetType} onChange={e => setTargetType(e.target.value)}><option value="Deposit">Tiền đặt cọc</option><option value="Charge">Khoản thu</option></select></label>{targetType === 'Charge' && <label>ID bảng kê nhận phân bổ<input required value={targetId} onChange={e => setTargetId(e.target.value)} /></label>}<label>Số tiền phân bổ (VND)<input required inputMode="numeric" pattern="[0-9]+" value={allocation} onChange={e => setAllocation(e.target.value)} /></label><button disabled={blocked || !reason.trim()}>Phân bổ tiền đã xác minh</button></form>}
        </>}
      </div>}
      <form onSubmit={e => void read('statement', e)}><label>ID bảng kê tiền thuê<input required value={statementId} onChange={e => setStatementId(e.target.value)} /></label><button disabled={!access || busy || !statementId.trim()}>Tải bảng kê tiền thuê</button></form>
      {statement && <div className="booking-detail"><p>Bảng kê {statement.periodStart} · Đến hạn {statement.dueDate} · {displayValue(statement.paymentState)}{statement.overdue ? ' · Quá hạn' : ''}</p><p>Khoản thu đã lập: {statement.total} VND · Đã trả: {statement.paid} VND · Còn nợ: {statement.outstanding} VND</p>{statement.lines.map(x => <p key={x.id}>{x.startDate} đến {x.endDate} · <PriceBasisNote value={x} /> · {x.amount} VND</p>)}</div>}
    </div></section>;
}
