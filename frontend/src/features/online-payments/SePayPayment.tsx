import { useEffect, useRef, useState } from 'react';
import { api } from '../../api';
import { displayValue } from '../../locale/vi';
export type PaymentIntent = { id: string; rentalAccountId: string; organizationId: string; facilityId: string; provider: string; method: string; mode: string; state: string; amount: string; checkoutUrl?: string; paymentReceiptId?: string; transactionId?: string; version: string; bankTransfer?: null | { bank: string; accountNumber: string; reference: string; expiresAt: string; qrUrl: string | null } };
function safeSePayQr(item: PaymentIntent): string | null {
  const bank = item.bankTransfer;
  if (item.provider !== 'SePay' || item.method !== 'SePayQr' || item.mode !== 'Live' || item.state !== 'Ready' || !bank || !bank.bank || !bank.accountNumber || !bank.reference || !/^\d+$/.test(item.amount) || Number(item.amount) <= 0 || !Number.isSafeInteger(Number(item.amount)) || !Number.isFinite(Date.parse(bank.expiresAt)) || Date.parse(bank.expiresAt) <= Date.now()) return null;
  try {
    const url = new URL(bank.qrUrl ?? '');
    if (url.origin !== 'https://vietqr.app' || url.pathname !== '/img' || url.username || url.password || url.port || url.hash || url.searchParams.size !== 4) return null;
    const expected = { bank: bank.bank, acc: bank.accountNumber, amount: item.amount, des: bank.reference };
    return Object.entries(expected).every(([key, value]) => url.searchParams.getAll(key).length === 1 && url.searchParams.get(key) === value) ? url.href : null;
  } catch { return null; }
}
export default function SePayPayment({ intent, account }: { intent: PaymentIntent; account: string }) {
  const [item, setItem] = useState(intent); const [notice, setNotice] = useState(''); const [stopped, setStopped] = useState(false); const [refreshing, setRefreshing] = useState(false);
  const [observedExpiry, setObservedExpiry] = useState<number | null>(null);
  const request = useRef<AbortController | null>(null); const alive = useRef(false); const manualRunning = useRef(false);
  const eligible = account === intent.rentalAccountId && intent.provider === 'SePay' && intent.method === 'SePayQr' && ['Live', 'Sandbox'].includes(intent.mode);
  const expiresAt = Date.parse(intent.bankTransfer?.expiresAt ?? '');
  const transferExpiry = Date.parse(item.bankTransfer?.expiresAt ?? '');
  useEffect(() => {
    if (!eligible || item.state !== 'Ready' || !Number.isFinite(transferExpiry)) return;
    // QR expiry outlives the polling budget and its request controller.
    let timer: ReturnType<typeof setTimeout>;
    function schedule() {
      timer = setTimeout(() => {
        if (Date.now() >= transferExpiry) setObservedExpiry(transferExpiry);
        else schedule();
      }, Math.max(0, Math.min(transferExpiry - Date.now(), 2147483647)));
    }
    schedule();
    return () => clearTimeout(timer);
  }, [eligible, item.state, transferExpiry]);
  useEffect(() => {
    alive.current = true; const controller = new AbortController(); request.current = controller;
    let timer: ReturnType<typeof setTimeout> | undefined; let expiry: ReturnType<typeof setTimeout> | undefined; let attempts = 0;
    const deadline = Math.min(Date.now() + 120000, Number.isFinite(expiresAt) ? expiresAt : Date.now());
    function stop(text: string) { if (controller.signal.aborted) return; setNotice(text); setStopped(true); if (timer) clearTimeout(timer); if (expiry) clearTimeout(expiry); controller.abort(); }
    async function poll() {
      if (controller.signal.aborted) return;
      if (Date.now() > deadline || attempts >= 24) { stop('Đã dừng tự kiểm tra sau tối đa 2 phút. Chưa xác nhận có tiền; bạn có thể đọc lại trạng thái.'); return; }
      attempts += 1;
      try {
        const value = await readStatus(controller.signal);
        if (controller.signal.aborted) return;
        setItem(value); setNotice('');
        if (value.state !== 'Ready') { setStopped(true); if (expiry) clearTimeout(expiry); return; }
      } catch { if (controller.signal.aborted) return; setNotice('Không đọc được trạng thái SePay. Chưa xác định kết quả; không tạo thanh toán thay thế.'); }
      if (!controller.signal.aborted) {
        if (attempts >= 24 || Date.now() >= deadline) stop('Đã dừng tự kiểm tra sau tối đa 2 phút. Chưa xác nhận có tiền; bạn có thể đọc lại trạng thái.');
        else timer = setTimeout(() => void poll(), Math.min(5000, deadline - Date.now()));
      }
    }
    if (eligible && intent.state === 'Ready' && deadline > Date.now()) {
      timer = setTimeout(() => void poll(), Math.min(5000, deadline - Date.now()));
      // Expiration must also abort an in-flight read, not wait for its response.
      expiry = setTimeout(() => stop('Mã chuyển khoản đã hết hạn hoặc hết thời gian tự kiểm tra. Chưa xác nhận có tiền; hãy đọc lại trạng thái.'), deadline - Date.now() + 1);
    }
    return () => { alive.current = false; controller.abort(); if (timer) clearTimeout(timer); if (expiry) clearTimeout(expiry); };
    // The parent remounts this component for each explicit read and draft/access epoch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  async function readStatus(signal: AbortSignal) {
    const value = await api<PaymentIntent>(`/payment-intents/${encodeURIComponent(intent.id)}`, signal);
    if (value.id !== intent.id || value.rentalAccountId !== account || value.organizationId !== intent.organizationId || value.facilityId !== intent.facilityId || value.provider !== 'SePay' || value.method !== 'SePayQr' || value.mode !== intent.mode || value.amount !== intent.amount || !['Ready', 'Paid', 'ReviewRequired', 'Failed'].includes(value.state)) throw Error('Unverified payment context');
    return value;
  }
  async function refresh() {
    if (!eligible || manualRunning.current) return;
    manualRunning.current = true; setRefreshing(true); request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    try { const value = await readStatus(controller.signal); if (alive.current && !controller.signal.aborted) { setItem(value); setNotice(''); } }
    catch { if (alive.current && !controller.signal.aborted) setNotice('Không đọc được trạng thái SePay. Chưa xác định kết quả; không tạo thanh toán thay thế.'); }
    finally { manualRunning.current = false; if (alive.current && !controller.signal.aborted) setRefreshing(false); }
  }
  useEffect(() => () => request.current?.abort(), []);
  const expired = observedExpiry === transferExpiry || !Number.isFinite(transferExpiry) || transferExpiry <= Date.now();
  const qr = eligible && !expired ? safeSePayQr(item) : null;
  return <div className="booking-detail" aria-label="Chi tiết chuyển khoản SePay">
    <p>ID thanh toán: <code>{item.id}</code></p><p aria-live="polite" aria-atomic="true">Trạng thái thanh toán: {displayValue(item.state)}</p>
    <p>Chuyển khoản QR (SePay) · {displayValue(item.mode)} · {item.amount} VND</p>
    {item.mode === 'Sandbox' && <p role="note">CHỈ THỬ NGHIỆM - không chuyển tiền thật. Không hiển thị mã QR có thể quét.</p>}
    {!eligible && <p role="note">Chọn đúng tài khoản thuê rồi tải lại thanh toán để xem QR và kiểm tra trạng thái.</p>}
    {eligible && item.bankTransfer && <dl style={{ overflowWrap: 'anywhere' }}><dt>Ngân hàng</dt><dd>{item.bankTransfer.bank}</dd><dt>Số tài khoản nhận</dt><dd>{item.bankTransfer.accountNumber}</dd><dt>Số tiền chuyển (VND)</dt><dd>{item.amount}</dd><dt>Nội dung chuyển khoản</dt><dd>{item.bankTransfer.reference}</dd><dt>Hạn chuyển khoản</dt><dd>{item.bankTransfer.expiresAt}</dd></dl>}
    {qr && <img src={qr} alt="Mã QR chuyển khoản SePay" referrerPolicy="no-referrer" style={{ maxWidth: '100%', width: 280, height: 'auto' }} />}
    {eligible && item.mode === 'Live' && item.state === 'Ready' && !qr && <p role="note">Chưa xác minh được mã QR hoặc mã đã hết hạn. Không chuyển tiền theo ảnh không hợp lệ.</p>}
    {item.state === 'Paid' && (item.mode === 'Sandbox' ? <p role="status">Thử nghiệm đã ghi nhận - không có tiền thật/phiếu thu thật.</p> : <p role="status">{item.paymentReceiptId ? 'Thanh toán đã được xác nhận tự động. Tiền chưa được phân bổ vào tiền cọc hoặc tiền thuê.' : 'Chưa xác minh được phiếu thu SePay. Đọc lại trạng thái hoặc liên hệ tài chính; chưa xác nhận thanh toán thành công.'}</p>)}
    {item.state === 'ReviewRequired' && <p role="status">Cần tài chính đối soát. Chưa xác nhận thanh toán thành công hoặc phiếu thu; không chuyển tiền lần nữa.</p>}
    {item.mode === 'Live' && item.paymentReceiptId && <p>ID phiếu thu đã ghi: {item.paymentReceiptId}</p>}
    {notice && <p role="status" aria-live="polite">{notice}</p>}
    {eligible && item.state === 'Ready' && (stopped || expired) && <button disabled={refreshing} onClick={() => void refresh()}>Đọc lại trạng thái SePay</button>}
  </div>;
}
