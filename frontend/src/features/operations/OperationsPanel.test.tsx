import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import OperationsPanel from './OperationsPanel';
const access = { userId: 'manager', organizationId: '', facilityId: 'facility-a', facilityRoles: ['FacilityManager'], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('shows contractual and physical states separately and opens selected lease with its account', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'unit-a', code: 'A-01', areaM2: '10', schedules: [{ id: 'allocation', state: 'Confirmed', startDate: '2026-11-01', endDate: '2026-11-03', organizationName: 'Customer A' }], custody: [{ id: 'occupancy', state: 'ClearancePending', scheduledEnd: '2026-10-28', leaseUnitId: 'grant-a' }] }], total: 1 }))
    .mockResolvedValueOnce(reply({ items: [{ id: 'lease-a', rentalAccountId: 'account-a', organizationName: 'Customer A', state: 'Active', startDate: '2026-11-01', endDate: '2026-11-03' }], total: 1 }));
  vi.stubGlobal('fetch', fetchMock); const select = vi.fn(); const view = render(<OperationsPanel access={access} onSelect={select} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Tải lịch kho" })); expect(await screen.findByText("Đã xác nhận")).toBeInTheDocument(); expect(screen.getByText("Chờ giải phóng khoang")).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: "Tải danh sách hợp đồng" })); await user.click(await screen.findByRole('button', { name: "Mở hợp đồng lease-a" })); expect(select).toHaveBeenCalledWith({ kind: 'lease', id: 'lease-a', accountId: 'account-a' });
  view.rerender(<OperationsPanel access={{ ...access, facilityId: 'facility-b' }} onSelect={select} />); expect(screen.queryByText('Customer A')).not.toBeInTheDocument();
});
it('paginates rental lists and hides customer-inaccessible calendar and facility audit actions', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'booking-a', rentalAccountId: 'account-a', organizationName: 'Customer A', state: 'Held' }], total: 51 })).mockResolvedValueOnce(reply({ items: [], total: 51 }));
  vi.stubGlobal('fetch', fetchMock); render(<OperationsPanel access={{ ...access, organizationId: 'org-a', facilityRoles: [] }} onSelect={vi.fn()} />); const user = userEvent.setup();
  expect(screen.queryByRole('button', { name: "Tải lịch kho" })).not.toBeInTheDocument(); await user.click(screen.getByRole('button', { name: "Tải danh sách đặt chỗ" })); await user.click(await screen.findByRole('button', { name: "Trang kết quả sau" }));
  expect(fetchMock.mock.calls[1][0]).toContain('page=2'); expect(fetchMock.mock.calls[1][0]).toContain('organizationId=org-a'); expect(await screen.findByText("Không có bản ghi phù hợp trên trang này.")).toBeInTheDocument();
});

it('denies foreign context visibility even if an earlier list reply arrives after switching', async () => {
  let resolve: (value: unknown) => void = () => {}; vi.stubGlobal('fetch', vi.fn().mockImplementation(() => new Promise(r => { resolve = r; })));
  const view = render(<OperationsPanel access={access} onSelect={vi.fn()} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải danh sách hợp đồng" }));
  view.rerender(<OperationsPanel access={{ ...access, facilityId: 'facility-b' }} onSelect={vi.fn()} />); resolve(reply({ items: [{ id: 'private-a', organizationName: 'Private customer A', state: 'Active' }], total: 1 }));
  await new Promise(r => setTimeout(r, 0)); expect(screen.queryByText(/Private customer A/)).not.toBeInTheDocument();
});

it('shows audited reasons and an error without keeping old list data', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'audit-a', action: 'StockPosted', actorId: 'clerk-a', correlationId: 'correlation-a', reason: 'Observed all cartons', at: '2026-10-28' }], total: 1 })).mockResolvedValueOnce(reply({}, 503));
  vi.stubGlobal('fetch', fetchMock); render(<OperationsPanel access={access} onSelect={vi.fn()} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải nhật ký cơ sở" })); expect(await screen.findByText("Observed all cartons")).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: "Tải nhật ký cơ sở" })); expect(await screen.findByText(/Chưa xác minh được danh sách làm việc/)).toBeInTheDocument(); expect(screen.queryByText("Observed all cartons")).not.toBeInTheDocument();
});
it('lists stock requests and opens a customer instruction without granting posting rights', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'request-a', state: 'Submitted' }], total: 1 }))); const select = vi.fn(); render(<OperationsPanel access={{ ...access, organizationId: 'org-a', facilityRoles: [] }} onSelect={select} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Tải yêu cầu nhập xuất" })); await user.click(await screen.findByRole('button', { name: "Mở yêu cầu request-a" })); expect(select).toHaveBeenCalledWith({ kind: 'request', id: 'request-a' });
});

it('shows and independently pages affected future bookings for overdue occupancy', async () => {
  const affected = (id: string) => ({ bookingId: id, unitId: 'unit-a', organizationId: 'org-a', startDate: '2026-11-01', endDate: '2026-11-03' });
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'occupancy-a', code: 'A-01' }], total: 1, affectedBookings: [affected('booking-a')], affectedTotal: 51, affectedPage: 1 })).mockResolvedValueOnce(reply({ items: [{ id: 'occupancy-a', code: 'A-01' }], total: 1, affectedBookings: [affected('booking-b')], affectedTotal: 51, affectedPage: 2 }));
  vi.stubGlobal('fetch', fetchMock); const select = vi.fn(); render(<OperationsPanel access={access} onSelect={select} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Tải xung đột quá hạn" }));
  await user.click(await screen.findByRole('button', { name: "Mở đặt chỗ bị ảnh hưởng booking-a" })); expect(select).toHaveBeenCalledWith({ kind: 'booking', id: 'booking-a' });
  await user.click(screen.getByRole('button', { name: "Trang đặt chỗ bị ảnh hưởng sau" }));
  expect(await screen.findByRole('button', { name: "Mở đặt chỗ bị ảnh hưởng booking-b" })).toBeInTheDocument(); expect(fetchMock.mock.calls[1][0]).toContain('affectedPage=2');
});
