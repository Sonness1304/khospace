import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import IdentityOperationsPanel, { CredentialPanel } from './IdentityOperationsPanel';
const admin = { userId: 'admin', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: '', facilityRoles: [], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('queues an invitation and retains the original body/key through uncertain retry', async () => {
  const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(Error('lost'))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ id: 'invite-a' }));
  vi.stubGlobal('fetch', mock); render(<IdentityOperationsPanel access={admin} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("Email người được mời"), 'new@example.test'); await user.type(screen.getByLabelText("Lý do thay đổi quyền"), 'New colleague');
  await user.click(screen.getByRole('button', { name: "Tạo lời mời" })); await screen.findByText(/Chưa xác định kết quả/);
  await user.click(screen.getByRole('button', { name: "Thử lại yêu cầu quyền truy cập gốc" })); await screen.findByText(/Đã hoàn tất yêu cầu quyền truy cập/);
  expect(mock.mock.calls[1][1].body).toBe(mock.mock.calls[3][1].body);
  expect(mock.mock.calls[1][1].headers['Idempotency-Key']).toBe(mock.mock.calls[3][1].headers['Idempotency-Key']);
});
it('hides directory from customer members and only displays it for operator administrators', async () => {
  const view = render(<IdentityOperationsPanel access={{ ...admin, organizationRole: 'Member' }} />);
  expect(screen.queryByRole('button', { name: "Tạo lời mời" })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: "Tải danh sách nhân viên" })).not.toBeInTheDocument();
  view.rerender(<IdentityOperationsPanel access={{ ...admin, operatorAdmin: true }} />);
  expect(screen.getByRole('button', { name: "Tải danh sách nhân viên" })).toBeInTheDocument();
});
it('sends the exact staff directory version and clears foreign context', async () => {
  const mock = vi.fn().mockResolvedValueOnce(reply({ version: 'v1', total: 1, items: [{ id: 'staff', userName: 'staff@example.test', active: true, operatorAdmin: false }], grants: [] }))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ completed: true, version: 'v2' }));
  vi.stubGlobal('fetch', mock); const view = render(<IdentityOperationsPanel access={{ ...admin, facilityId: 'facility-a', operatorAdmin: true }} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Tải danh sách nhân viên" })); await user.click(await screen.findByRole('button', { name: "Chọn staff@example.test" }));
  await user.type(screen.getByLabelText("Lý do thay đổi quyền"), 'Assigned clerk'); await user.click(screen.getByRole('button', { name: "Cấp vai trò tại cơ sở" }));
  await screen.findByText(/Đã hoàn tất yêu cầu quyền truy cập/); expect(mock.mock.calls[2][1].headers['If-Match']).toBe('"v1"');
  view.rerender(<IdentityOperationsPanel access={{ ...admin, facilityId: 'facility-b', operatorAdmin: true }} />);
  expect(screen.queryByRole('button', { name: "Chọn staff@example.test" })).not.toBeInTheDocument();
});
it.each([401, 403, 404, 412, 429])('retains uncertain command after retry%s and refuses retry in another context', async status => {
  const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, status));
  vi.stubGlobal('fetch', mock); const view = render(<IdentityOperationsPanel access={admin} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("Email người được mời"), 'new@example.test'); await user.type(screen.getByLabelText("Lý do thay đổi quyền"), 'New colleague');
  await user.click(screen.getByRole('button', { name: "Tạo lời mời" })); await screen.findByText(/Chưa xác định kết quả/);
  await user.click(screen.getByRole('button', { name: "Thử lại yêu cầu quyền truy cập gốc" })); await screen.findByText(/Chưa xác định kết quả/);
  view.rerender(<IdentityOperationsPanel access={{ ...admin, userId: 'other' }} />);
  expect(screen.getByRole('button', { name: "Thử lại yêu cầu quyền truy cập gốc" })).toBeDisabled();
});
it('uses generic reset request feedback and clears redemption secrets', async () => {
  const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ message: 'generic' }, 202))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ completed: true }));
  vi.stubGlobal('fetch', mock); render(<CredentialPanel />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("Email khôi phục"), 'someone@example.test'); await user.click(screen.getByRole('button', { name: "Yêu cầu khôi phục" })); await screen.findByText(/Nếu tài khoản đủ điều kiện/);
  await user.type(screen.getByLabelText("Mã dùng một lần"), 'a'.repeat(64)); await user.type(screen.getByLabelText("Mật khẩu mới"), 'Synthetic_Password!42');
  await user.click(screen.getByRole('button', { name: "Đặt mật khẩu" })); await screen.findByText(/Đã đặt mật khẩu/);
  expect(screen.getByLabelText("Mã dùng một lần")).toHaveValue(''); expect(screen.getByLabelText("Mật khẩu mới")).toHaveValue('');
});
it('never reports an uncertain redemption as success or automatically retries secrets', async () => {
  const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(Error('offline'));
  vi.stubGlobal('fetch', mock); render(<CredentialPanel />); const user = userEvent.setup();
  await user.selectOptions(screen.getByLabelText("Mục đích cập nhật thông tin đăng nhập"), 'Invitation');
  await user.type(screen.getByLabelText("Mã dùng một lần"), 'b'.repeat(64)); await user.type(screen.getByLabelText("Mật khẩu mới"), 'Synthetic_Password!42');
  await user.click(screen.getByRole('button', { name: "Đặt mật khẩu" })); await screen.findByText(/Chưa xác nhận được/);
  expect(mock).toHaveBeenCalledTimes(2); expect(screen.getByLabelText("Mật khẩu mới")).toHaveValue('');
});
