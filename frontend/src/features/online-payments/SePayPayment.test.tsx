import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import OnlinePaymentPanel from './OnlinePaymentPanel';
const access = { userId: 'u', organizationId: 'o', organizationRole: 'CustomerAdmin', facilityId: 'f', facilityRoles: [] as string[], operatorAdmin: false };
const bankTransfer = { bank: 'ICB', accountNumber: '123456', reference: 'SEVQRTEST1', expiresAt: '2099-01-01T00:00:00Z', qrUrl: 'https://vietqr.app/img?bank=ICB&acc=123456&amount=10000&des=SEVQRTEST1' };
const intent = { id: 'i', rentalAccountId: 'a', organizationId: 'o', facilityId: 'f', provider: 'SePay', method: 'SePayQr', mode: 'Live', state: 'Ready', amount: '10000', version: '1', bankTransfer };
const reply = (value: unknown) => ({ ok: true, status: 200, json: async () => value });
async function load(value = intent) {
 const fetcher = vi.fn().mockResolvedValue(reply(value)); vi.stubGlobal('fetch', fetcher);
 const view = render(<OnlinePaymentPanel access={access} />);
 fireEvent.change(screen.getByLabelText('ID tài khoản thuê thanh toán trực tuyến'), { target: { value: 'a' } });
 fireEvent.change(screen.getByLabelText('ID thanh toán trực tuyến'), { target: { value: 'i' } });
 await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Tải thanh toán trực tuyến' })); });
 return { ...view, fetcher };
}
afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); window.history.replaceState({}, '', '/'); });
it('keeps unavailable SePay disabled and labels its configured mode', async () => {
 vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ items: [{ method: 'SePayQr', mode: 'Disabled', available: false }] })));
 render(<OnlinePaymentPanel access={access} />);
 await act(async () => { fireEvent.click(screen.getByText('Tải phương thức thanh toán')); });
 expect(screen.getByRole('option', { name: /Chuyển khoản QR \(SePay\).*Đã vô hiệu hóa/ })).toBeDisabled();
 expect(screen.getByText('Tạo thanh toán trực tuyến')).toBeDisabled();
});
it('renders only a consistent Live bank QR', async () => {
 await load(); expect(screen.getByRole('img', { name: 'Mã QR chuyển khoản SePay' })).toHaveAttribute('src', bankTransfer.qrUrl);
 expect(screen.getByText('SEVQRTEST1')).toBeInTheDocument();
});
it.each(['https://evil.test/img?bank=ICB', bankTransfer.qrUrl + '&amount=1', bankTransfer.qrUrl.replace('10000', '10001'), bankTransfer.qrUrl.replace('/img?', '/other?'), bankTransfer.qrUrl + '&url=https://evil.test'])('rejects unsafe QR %s', async qrUrl => {
 await load({ ...intent, bankTransfer: { ...bankTransfer, qrUrl } }); expect(screen.queryByRole('img')).not.toBeInTheDocument();
});
it('never renders a Sandbox QR and describes simulated Paid without real receipt', async () => {
 await load({ ...intent, mode: 'Sandbox', state: 'Paid' }); expect(screen.queryByRole('img')).not.toBeInTheDocument();
 expect(screen.getByText(/Thử nghiệm đã ghi nhận - không có tiền thật\/phiếu thu thật/)).toBeInTheDocument();
});
it('polls Ready through a server GET to Paid then stops without a POST', async () => {
 vi.useFakeTimers(); const { fetcher } = await load(); fetcher.mockResolvedValue(reply({ ...intent, state: 'Paid', paymentReceiptId: 'receipt' }));
 await act(async () => { await vi.advanceTimersByTimeAsync(5000); });
 expect(screen.getByText(/ID phiếu thu đã ghi: receipt/)).toBeInTheDocument();
 await act(async () => { await vi.advanceTimersByTimeAsync(30000); });
 expect(fetcher).toHaveBeenCalledTimes(2); expect(fetcher.mock.calls[1][1].cache).toBe('no-store');
 expect(fetcher.mock.calls.every(([, init]) => !init.method)).toBe(true);
});
it.each(['ReviewRequired', 'Failed'])('stops terminal %s', async state => {
 vi.useFakeTimers(); const { fetcher } = await load(); fetcher.mockResolvedValue(reply({ ...intent, state }));
 await act(async () => { await vi.advanceTimersByTimeAsync(120000); }); expect(fetcher).toHaveBeenCalledTimes(2);
 expect(screen.queryByRole('img')).not.toBeInTheDocument();
});
it('limits polling to 24 attempts and allows only explicit bounded refresh', async () => {
 vi.useFakeTimers(); const { fetcher } = await load(); await act(async () => { await vi.advanceTimersByTimeAsync(180000); });
 expect(fetcher).toHaveBeenCalledTimes(25); expect(screen.getByRole('button', { name: 'Đọc lại trạng thái SePay' })).toBeEnabled();
 await act(async () => { fireEvent.click(screen.getByText('Đọc lại trạng thái SePay')); }); expect(fetcher).toHaveBeenCalledTimes(26);
 await act(async () => { await vi.advanceTimersByTimeAsync(60000); }); expect(fetcher).toHaveBeenCalledTimes(26);
});
it.each(['account', 'user', 'role', 'scope', 'unmount', 'amount', 'id', 'reason', 'method'])('aborts pending polling on %s including late failed reads', async kind => {
 vi.useFakeTimers(); const view = await load(); let reject!: (reason: Error) => void;
 view.fetcher.mockImplementation(() => new Promise((_, r) => { reject = r; }));
 await act(async () => { await vi.advanceTimersByTimeAsync(5000); }); const signal = view.fetcher.mock.calls[1][1].signal as AbortSignal;
 if (kind === 'unmount') view.unmount();
 else if (kind === 'user') { view.rerender(<OnlinePaymentPanel access={{ ...access, userId: 'other' }} />); view.rerender(<OnlinePaymentPanel access={access} />); }
 else if (kind === 'role') view.rerender(<OnlinePaymentPanel access={{ ...access, organizationRole: 'CustomerMember' }} />);
 else if (kind === 'scope') view.rerender(<OnlinePaymentPanel access={{ ...access, organizationId: 'other' }} />);
 else { const labels: Record<string, string> = { account: 'ID tài khoản thuê thanh toán trực tuyến', amount: 'Số tiền thanh toán trực tuyến (VND)', id: 'ID thanh toán trực tuyến', reason: 'Lý do thanh toán trực tuyến', method: 'Phương thức trực tuyến' }; fireEvent.change(screen.getByLabelText(labels[kind]), { target: { value: kind === 'method' ? 'VnpayCard' : 'other' } }); }
 expect(signal.aborted).toBe(true); await act(async () => { reject(Error('late failure')); });
 expect(screen.queryByText(/Không đọc được trạng thái SePay/)).not.toBeInTheDocument();
});
it('does not create or infer Paid from a successful browser return', async () => {
 window.history.replaceState({}, '', '/?paid=true&state=Paid&code=00'); const { fetcher } = await load();
 expect(screen.queryByText(/Thanh toán đã ghi nhận/)).not.toBeInTheDocument(); expect(fetcher).toHaveBeenCalledTimes(1);
});
it('never reads or revives a late create result after draft switches and back', async () => {
 let resolve!: (value: unknown) => void;
 const fetcher = vi.fn().mockImplementation((url: string) => url.endsWith('/payment-methods') ? Promise.resolve(reply({ items: [{ method: 'SePayQr', available: true, mode: 'Sandbox' }] })) : url.endsWith('/auth/csrf') ? Promise.resolve(reply({ token: 'csrf' })) : new Promise(r => { resolve = r; }));
 vi.stubGlobal('fetch', fetcher); render(<OnlinePaymentPanel access={access} />);
 await act(async () => { fireEvent.click(screen.getByText('Tải phương thức thanh toán')); });
 for (const [label, value] of [['ID tài khoản thuê thanh toán trực tuyến', 'a'], ['Phương thức trực tuyến', 'SePayQr'], ['Số tiền thanh toán trực tuyến (VND)', '10000'], ['Lý do thanh toán trực tuyến', 'fixture']]) fireEvent.change(screen.getByLabelText(label), { target: { value } });
 await act(async () => { fireEvent.click(screen.getByText('Tạo thanh toán trực tuyến')); });
 fireEvent.change(screen.getByLabelText('ID tài khoản thuê thanh toán trực tuyến'), { target: { value: 'b' } });
 fireEvent.change(screen.getByLabelText('ID tài khoản thuê thanh toán trực tuyến'), { target: { value: 'a' } });
 await act(async () => { resolve(reply({ result: { resourceId: 'i' } })); });
 expect(fetcher).toHaveBeenCalledTimes(3); expect(screen.queryByText(/Đã lưu yêu cầu thanh toán/)).not.toBeInTheDocument();
});
it('expires without an extra transfer or scannable QR and aborts the in-flight read', async () => {
 vi.useFakeTimers(); const { fetcher } = await load({ ...intent, bankTransfer: { ...bankTransfer, expiresAt: new Date(Date.now() + 6000).toISOString() } });
 fetcher.mockImplementation(() => new Promise(() => {}));
 await act(async () => { await vi.advanceTimersByTimeAsync(6001); });
 expect((fetcher.mock.calls[1][1].signal as AbortSignal).aborted).toBe(true); expect(screen.queryByRole('img')).not.toBeInTheDocument(); expect(fetcher).toHaveBeenCalledTimes(2);
});
it('rejects a Paid server response from the wrong account', async () => {
 vi.useFakeTimers(); const { fetcher } = await load(); fetcher.mockResolvedValue(reply({ ...intent, state: 'Paid', rentalAccountId: 'other' }));
 await act(async () => { await vi.advanceTimersByTimeAsync(5000); }); expect(screen.queryByText(/Thanh toán đã được xác nhận tự động/)).not.toBeInTheDocument(); expect(screen.getByText(/Không đọc được trạng thái SePay/)).toBeInTheDocument();
});
it('does not confirm Live Paid without a committed receipt identity', async () => {
 await load({ ...intent, state: 'Paid' });
 expect(screen.getByText(/Chưa xác minh được phiếu thu SePay/)).toBeInTheDocument();
 expect(screen.queryByText(/Thanh toán đã được xác nhận tự động/)).not.toBeInTheDocument();
});
it('manual Paid read cannot regress when an aborted poll responds Ready later', async () => {
 vi.useFakeTimers(); const { fetcher } = await load(); let late!: (value: unknown) => void;
 fetcher.mockImplementationOnce(() => new Promise(resolve => { late = resolve; }));
 await act(async () => { await vi.advanceTimersByTimeAsync(120001); });
 fetcher.mockResolvedValue(reply({ ...intent, state: 'Paid', paymentReceiptId: 'committed-receipt' }));
 await act(async () => { fireEvent.click(screen.getByText('Đọc lại trạng thái SePay')); });
 expect(screen.getByText(/Thanh toán đã được xác nhận tự động/)).toBeInTheDocument();
 await act(async () => { late(reply(intent)); await vi.advanceTimersByTimeAsync(30000); });
 expect(screen.getByText(/ID phiếu thu đã ghi: committed-receipt/)).toBeInTheDocument();
 expect(screen.queryByRole('img')).not.toBeInTheDocument(); expect(fetcher).toHaveBeenCalledTimes(3);
});
it('preserves CSRF and idempotency and aborts a created-intent read on input edits', async () => {
 let resolve!: (value: unknown) => void;
 const fetcher = vi.fn().mockImplementation((url: string, init: RequestInit) => url.endsWith('/payment-methods') ? Promise.resolve(reply({ items: [{ method: 'SePayQr', available: true, mode: 'Sandbox' }] })) : url.endsWith('/auth/csrf') ? Promise.resolve(reply({ token: 'csrf' })) : init.method === 'POST' ? Promise.resolve(reply({ result: { resourceId: 'i' } })) : new Promise(r => { resolve = r; }));
 vi.stubGlobal('fetch', fetcher); render(<OnlinePaymentPanel access={access} />);
 await act(async () => { fireEvent.click(screen.getByText('Tải phương thức thanh toán')); });
 for (const [label, value] of [['ID tài khoản thuê thanh toán trực tuyến', 'a'], ['Phương thức trực tuyến', 'SePayQr'], ['Số tiền thanh toán trực tuyến (VND)', '10000'], ['Lý do thanh toán trực tuyến', 'fixture']]) fireEvent.change(screen.getByLabelText(label), { target: { value } });
 await act(async () => { fireEvent.click(screen.getByText('Tạo thanh toán trực tuyến')); });
 const post = fetcher.mock.calls[2][1]; expect(JSON.parse(post.body as string)).toEqual({ rentalAccountId: 'a', method: 'SePayQr', amount: '10000', reason: 'fixture' });
 expect(post.headers).toMatchObject({ 'X-CSRF-Token': 'csrf', 'Idempotency-Key': expect.any(String) });
 fireEvent.change(screen.getByLabelText('ID tài khoản thuê thanh toán trực tuyến'), { target: { value: 'b' } });
 expect((fetcher.mock.calls[3][1].signal as AbortSignal).aborted).toBe(true);
 await act(async () => { resolve(reply({ ...intent, mode: 'Sandbox' })); }); expect(screen.queryByLabelText('Chi tiết chuyển khoản SePay')).not.toBeInTheDocument();
 expect(fetcher.mock.calls.filter(([, init]) => init.method === 'POST')).toHaveLength(1);
});
it('removes the QR at actual expiry even after the polling budget stopped', async () => {
 vi.useFakeTimers(); const expiry = new Date(Date.now() + 900000).toISOString();
 const { fetcher } = await load({ ...intent, bankTransfer: { ...bankTransfer, expiresAt: expiry } });
 expect(screen.getByRole('img', { name: 'Mã QR chuyển khoản SePay' })).toBeInTheDocument();
 await act(async () => { await vi.advanceTimersByTimeAsync(120001); });
 expect(fetcher).toHaveBeenCalledTimes(25); expect(screen.getByRole('img', { name: 'Mã QR chuyển khoản SePay' })).toBeInTheDocument();
 await act(async () => { await vi.advanceTimersByTimeAsync(780000); });
 expect(screen.queryByRole('img')).not.toBeInTheDocument(); expect(fetcher).toHaveBeenCalledTimes(25);
 expect(fetcher.mock.calls.every(([, init]) => !init.method)).toBe(true);
});
