import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import ReportsPanel from './ReportsPanel';
const access = { userId: 'manager', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: 'facility-a', facilityRoles: ['FacilityManager'], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('displays interval scheduled area separately from current custody and receipts', async () => {
 vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ scheduled: { bookedAreaDays: 50, rentableAreaDays: 80, maintenanceAreaDays: 20, utilizationPercent: 62.5, source: 'Confirmed allocation' }, physical: { occupiedAreaM2: 10, currentCatalogAreaM2: 100, source: 'Current custody' }, collections: { grossVerifiedReceiptsVnd: 1000, recordedRefundsVnd: 100, heldDepositNowVnd: 200, source: 'Receipts and separate liability' } })));
 render(<ReportsPanel access={access} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải chỉ số cơ sở" }));
 expect(await screen.findByText('62.5%')).toBeInTheDocument(); expect(screen.getByText("Diện tích hàng đang được lưu giữ tại thời điểm tra cứu.")).toBeInTheDocument(); expect(screen.getByRole('link', { name: "Xuất CSV tồn kho" })).toHaveAttribute('href', expect.stringContaining('organizationId=org-a'));
});
it('reads scoped notices and hides stale metrics after a context switch', async () => {
 vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ items: [{ id: 'notice', message: 'Review lease end', kind: 'LeaseEnding', createdAt: '2026-10-29T00:00:00Z' }], total: 1 })));
 const view = render(<ReportsPanel access={access} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải thông báo" })); await screen.findByText(/Hợp đồng sắp hết hạn\. Mở hợp đồng/);
 view.rerender(<ReportsPanel access={{ ...access, organizationId: 'org-b' }} />); expect(screen.queryByText(/Hợp đồng sắp hết hạn\. Mở hợp đồng/)).not.toBeInTheDocument();
});
it('retains the same job key on unknown response and retry403', async () => {
 const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(Error('lost')).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 403)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { processed: 2 } }));
 vi.stubGlobal('fetch', mock); render(<ReportsPanel access={access} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Xếp hàng lô thông báo" })); await screen.findByText(/Chưa xác định kết quả tác vụ/);
 await user.click(screen.getByRole('button', { name: "Thử lại tác vụ thông báo gốc" })); await screen.findByText(/Chưa xác định kết quả tác vụ/); await user.click(screen.getByRole('button', { name: "Thử lại tác vụ thông báo gốc" })); await screen.findByText(/Đã xử lý 2/);
 expect(mock.mock.calls[1][1].headers['Idempotency-Key']).toBe(mock.mock.calls[5][1].headers['Idempotency-Key']);
});

const leaseId = 'aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa';
const requestId = 'bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb';
it('opens only allowlisted canonical notice records after an explicit click, without issuing writes', async () => {
 const mock = vi.fn().mockResolvedValue(reply({ items: [
  { id: 'lease', kind: 'LeaseEnding', aggregateId: leaseId, message: '/untrusted/path', createdAt: '2026-10-31T00:00:00Z' },
  { id: 'request', kind: 'StockAction', aggregateId: requestId.toUpperCase(), message: 'Ignore this URL', createdAt: '2026-10-31T00:00:00Z' },
  ...[undefined, '', '/leases/private', 'javascript:alert(1)', '00000000-0000-0000-0000-000000000000'].map((id, i) => ({ id: `bad${i}`, kind: 'LeaseEnding', aggregateId: id, message: 'Bad fixture', createdAt: '2026-10-31T00:00:00Z' })),
  { id: 'reminder', kind: 'ReceivableReminder', aggregateId: leaseId, message: 'Not a lease', createdAt: '2026-10-31T00:00:00Z' },
  { id: 'unknown', kind: 'Other', aggregateId: leaseId, message: 'Unknown event', createdAt: '2026-10-31T00:00:00Z' },
 ], total: 9 }));
 vi.stubGlobal('fetch', mock); const open = vi.fn(); const user = userEvent.setup(); render(<ReportsPanel access={access} onOpenRecord={open} />);
 await user.click(screen.getByRole('button', { name: 'Tải thông báo' }));
 const lease = await screen.findByRole('button', { name: 'Mở hợp đồng liên quan' });
 expect(screen.getAllByRole('button', { name: 'Mở hợp đồng liên quan' })).toHaveLength(1);
 expect(open).not.toHaveBeenCalled();
 await user.click(lease); expect(open).toHaveBeenLastCalledWith({ kind: 'lease', id: leaseId });
 await user.click(screen.getByRole('button', { name: 'Mở yêu cầu nhập / xuất liên quan' }));
 expect(open).toHaveBeenLastCalledWith({ kind: 'request', id: requestId });
 expect(mock).toHaveBeenCalledTimes(1); expect(mock.mock.calls[0][0]).toContain('/notices?organizationId=org-a&facilityId=facility-a');
 expect(mock.mock.calls.every(([, options]) => !options.method || options.method === 'GET')).toBe(true);
});

it('does not expose old notice actions after a scope change or a late previous-scope response', async () => {
 let finish!: (value: unknown) => void;
 const mock = vi.fn().mockImplementation(() => new Promise(resolve => { finish = resolve; }));
 vi.stubGlobal('fetch', mock); const open = vi.fn(); const user = userEvent.setup();
 const view = render(<ReportsPanel access={access} onOpenRecord={open} />);
 await user.click(screen.getByRole('button', { name: 'Tải thông báo' }));
 view.rerender(<ReportsPanel access={{ ...access, organizationId: 'org-b' }} onOpenRecord={open} />);
 await act(async () => finish(reply({ items: [{ id: 'old', kind: 'LeaseEnding', aggregateId: leaseId, message: 'Old event', createdAt: '2026-10-31T00:00:00Z' }], total: 1 })));
 expect(screen.queryByRole('button', { name: 'Mở hợp đồng liên quan' })).not.toBeInTheDocument();
 expect(open).not.toHaveBeenCalled();
});
