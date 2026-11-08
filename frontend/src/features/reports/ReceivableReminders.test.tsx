import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import ReportsPanel from './ReportsPanel';
const access = { userId: 'finance', organizationId: 'org-a', organizationRole: '', facilityId: 'facility-a', facilityRoles: ['Finance'], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
const result = { observedAt: '2026-10-29T01:00:00Z', asOfDate: '2026-10-29', timeZone: 'Asia/Ho_Chi_Minh', scanned: 50, eligible: 3, created: 2, duplicates: 1, page: 0, nextPage: 1 };
afterEach(() => vi.unstubAllGlobals());
function panel() { return within(screen.getByRole('region', { name: "Nhắc thanh toán quá hạn" })); }
it('creates explicit Finance reminders and displays observed delivery counts without changing financial records', async () => {
 const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result })); vi.stubGlobal('fetch', mock);
 render(<ReportsPanel access={access} />); const user = userEvent.setup();
 await user.click(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" }));
 expect(await panel().findByText(/Đã tạo 2 nhắc nợ trong ứng dụng/)).toBeInTheDocument(); expect(panel().getByText(/2026-10-29T01:00:00Z/)).toBeInTheDocument();
 expect(mock.mock.calls[1][0]).toBe('/api/v1/organizations/org-a/facilities/facility-a/receivable-reminders');
 expect(mock.mock.calls[1][1].method).toBe('POST'); expect(JSON.parse(mock.mock.calls[1][1].body)).toMatchObject({ page: 0 });
 expect(panel().getByText(/trang nguồn tiếp theo: 1/)).toBeInTheDocument();
});
it('keeps original command and body across unknown reply, edited scope and restored Finance access', async () => {
 const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(Error('lost')).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result })); vi.stubGlobal('fetch', mock);
 const view = render(<ReportsPanel access={access} />); const user = userEvent.setup();
 await user.click(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" })); await panel().findByText(/Chưa xác định kết quả nhắc nợ/);
 expect(panel().getByLabelText("Nhắc nợ từ hạn thanh toán")).toBeDisabled();
 view.rerender(<ReportsPanel access={{ ...access, facilityId: 'facility-b' }} />);
 expect(panel().getByRole('button', { name: "Thử lại yêu cầu nhắc nợ gốc" })).toBeDisabled();
 view.rerender(<ReportsPanel access={{ ...access, facilityRoles: [] }} />);
 expect(panel().getByRole('button', { name: "Thử lại yêu cầu nhắc nợ gốc" })).toBeDisabled();
 view.rerender(<ReportsPanel access={access} />);
 await user.click(panel().getByRole('button', { name: "Thử lại yêu cầu nhắc nợ gốc" })); await panel().findByText(/Chưa xác định kết quả nhắc nợ/);
 await user.click(panel().getByRole('button', { name: "Thử lại yêu cầu nhắc nợ gốc" })); await panel().findByText(/Đã tạo 2/);
 expect(mock.mock.calls[1][1].headers['Idempotency-Key']).toBe(mock.mock.calls[5][1].headers['Idempotency-Key']);
 expect(mock.mock.calls[1][1].body).toBe(mock.mock.calls[5][1].body);
});
it('hides previous-scope results, disables invalid date intervals and source pages, and denies customer/manager controls', async () => {
 const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result })); vi.stubGlobal('fetch', mock);
 const view = render(<ReportsPanel access={access} />); const user = userEvent.setup();
 await user.click(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" })); await panel().findByText(/Đã tạo 2/);
 view.rerender(<ReportsPanel access={{ ...access, organizationId: 'org-b' }} />); expect(panel().queryByText(/Đã tạo 2/)).not.toBeInTheDocument();
 await user.clear(panel().getByLabelText("Trang dữ liệu nguồn nhắc nợ")); await user.type(panel().getByLabelText("Trang dữ liệu nguồn nhắc nợ"), '100');
 expect(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" })).toBeDisabled();
 view.rerender(<ReportsPanel access={{ ...access, facilityRoles: ['FacilityManager'] }} />);
 expect(screen.queryByRole('region', { name: "Nhắc thanh toán quá hạn" })).not.toBeInTheDocument();
 view.rerender(<ReportsPanel access={{ ...access, organizationRole: 'CustomerAdmin', facilityRoles: [] }} />);
 expect(screen.queryByRole('region', { name: "Nhắc thanh toán quá hạn" })).not.toBeInTheDocument();
});
it('definite first rejection permits a fresh command while preventing double submission during the pending request', async () => {
 let resolve!: (value: unknown) => void; const waiting = new Promise(r => { resolve = r; });
 const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockReturnValueOnce(waiting); vi.stubGlobal('fetch', mock);
 render(<ReportsPanel access={access} />); const user = userEvent.setup();
 await user.click(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" })); expect(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" })).toBeDisabled();
 resolve(reply({}, 422)); await panel().findByText(/Yêu cầu nhắc nợ bị từ chối/);
 await waitFor(() => expect(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" })).toBeEnabled());
});

it('retains a pending response under its original scope and rejects malformed intervals before POST', async () => {
 let resolve!: (value: unknown) => void; const waiting = new Promise(r => { resolve = r; });
 const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockReturnValueOnce(waiting); vi.stubGlobal('fetch', mock);
 const view = render(<ReportsPanel access={access} />); const user = userEvent.setup();
 await user.clear(panel().getByLabelText("Nhắc nợ từ hạn thanh toán")); expect(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" })).toBeDisabled();
 await user.type(panel().getByLabelText("Nhắc nợ từ hạn thanh toán"), '2026-10-01'); await user.clear(panel().getByLabelText("Nhắc nợ đến hạn thanh toán (không tính)"));
 await user.type(panel().getByLabelText("Nhắc nợ đến hạn thanh toán (không tính)"), '2026-11-01');
 await user.click(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" }));
 view.rerender(<ReportsPanel access={{ ...access, organizationId: 'org-b' }} />); resolve(reply({ result }));
 await waitFor(() => expect(panel().getByRole('button', { name: "Tạo nhắc thanh toán quá hạn" })).toBeEnabled());
 expect(panel().queryByText(/Đã tạo 2/)).not.toBeInTheDocument(); expect(mock).toHaveBeenCalledTimes(2);
 view.rerender(<ReportsPanel access={access} />); expect(panel().getByText(/Đã tạo 2/)).toBeInTheDocument();
});
