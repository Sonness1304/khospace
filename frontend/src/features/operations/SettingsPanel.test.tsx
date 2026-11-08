import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import SettingsPanel from './SettingsPanel';
const manager = { userId: 'manager', organizationId: '', facilityId: 'facility-a', facilityRoles: ['FacilityManager'], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('loads exact facility version and creates a scoped zone with a reason', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ facility: { id: 'facility-a', code: 'A', name: 'Warehouse A', address: 'Demo', permittedGoods: 'Dry goods', published: true, version: 'v1' }, zones: [], units: [] })).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'zone-a' } }));
  vi.stubGlobal('fetch', fetchMock); render(<SettingsPanel access={manager} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải thiết lập cơ sở" })); await screen.findByDisplayValue('Warehouse A');
  await user.type(screen.getByLabelText("Lý do thay đổi thiết lập"), 'Configure dry goods zone'); await user.type(screen.getByLabelText("Mã khu vực mới"), 'Z-02'); await user.click(screen.getByRole('button', { name: "Tạo khu vực" }));
  expect(await screen.findByText(/Đã lưu dữ liệu: zone-a/)).toBeInTheDocument(); expect(fetchMock.mock.calls[2][0]).toContain('/facilities/facility-a/zones');
});
it('retains unknown settings command, recovers by original facility scope and hides foreign drafts', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504)).mockResolvedValueOnce(reply({ result: { resourceId: 'zone-a' } }));
  vi.stubGlobal('fetch', fetchMock); const view = render(<SettingsPanel access={manager} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("Lý do thay đổi thiết lập"), 'Create zone safely'); await user.type(screen.getByLabelText("Mã khu vực mới"), 'Z-02'); await user.click(screen.getByRole('button', { name: "Tạo khu vực" })); await screen.findByText(/Chưa xác định kết quả yêu cầu thiết lập/);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu thiết lập" })); expect(await screen.findByText(/Đã lưu dữ liệu: zone-a/)).toBeInTheDocument();
  view.rerender(<SettingsPanel access={{ ...manager, facilityId: 'facility-b' }} />); expect(screen.getByLabelText("Mã khu vực mới")).toHaveValue('');
});
it('customer admin reads current members and versioned changes without operator roles', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'member-a', userId: 'member@example.test', role: 'Member', active: true, version: 'mv1' }], total: 1 }))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ resourceId: 'member-a' }));
  vi.stubGlobal('fetch', fetchMock); render(<SettingsPanel access={{ ...manager, organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityRoles: [] }} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Tải thành viên tổ chức" })); await user.click(await screen.findByRole('button', { name: "Sửa thành viên member@example.test" })); await user.type(screen.getByLabelText("Lý do thay đổi thiết lập"), 'Promote existing customer member'); await user.selectOptions(screen.getByLabelText("Vai trò thành viên khách hàng"), 'CustomerAdmin'); await user.click(screen.getByRole('button', { name: "Cập nhật thành viên đã chọn" }));
  expect(await screen.findByText(/Đã lưu dữ liệu: member-a/)).toBeInTheDocument(); expect(fetchMock.mock.calls[2][1].headers['If-Match']).toBe('"mv1"'); expect(screen.queryByRole('button', { name: "Tạo cơ sở kho" })).not.toBeInTheDocument();
});

it('retries the original unknown settings payload after negative readback, retaining uncertainty after401', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504)).mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 401));
  vi.stubGlobal('fetch', fetchMock); render(<SettingsPanel access={manager} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("Lý do thay đổi thiết lập"), 'Original zone creation'); await user.type(screen.getByLabelText("Mã khu vực mới"), 'ORIGINAL'); await user.click(screen.getByRole('button', { name: "Tạo khu vực" })); await screen.findByText(/Chưa xác định kết quả yêu cầu thiết lập/);
  await user.clear(screen.getByLabelText("Mã khu vực mới")); await user.type(screen.getByLabelText("Mã khu vực mới"), 'CHANGED'); await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu thiết lập" })); await user.click(await screen.findByRole('button', { name: "Thử lại yêu cầu thiết lập gốc" }));
  expect(await screen.findByText(/Chưa xác định kết quả yêu cầu thiết lập/)).toBeInTheDocument(); expect(fetchMock.mock.calls[4][1].body).toBe(fetchMock.mock.calls[1][1].body);
});
it('creates an operator facility in global scope then shows definite rejection without losing draft', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 422)); vi.stubGlobal('fetch', fetchMock); render(<SettingsPanel access={{ ...manager, operatorAdmin: true }} />); const user = userEvent.setup();
  for (const [label, value] of [["Lý do thay đổi thiết lập", 'New synthetic site'], ["Mã cơ sở kho", 'C'], ["Tên cơ sở kho", 'Warehouse C'], ["Địa chỉ cơ sở kho", 'Demo address'], ["Quy định hàng hóa được phép", 'Dry goods only']]) await user.type(screen.getByLabelText(label), value);
  await user.click(screen.getByRole('button', { name: "Tạo cơ sở kho" })); expect(await screen.findByText(/Thiết lập bị từ chối/)).toBeInTheDocument(); expect(screen.getByLabelText("Tên cơ sở kho")).toHaveValue('Warehouse C'); expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toMatchObject({ code: 'C', name: 'Warehouse C' });
});
it('shows read errors and no settings actions for a regular customer member', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({}, 503))); const view = render(<SettingsPanel access={manager} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải thiết lập cơ sở" })); expect(await screen.findByText(/Chưa xác minh được thiết lập/)).toBeInTheDocument();
  view.rerender(<SettingsPanel access={{ ...manager, organizationId: 'org-a', facilityRoles: [] }} />); expect(screen.getByText(/không có quyền thiết lập/)).toBeInTheDocument();
});

it('allows an active member to read back their own unknown self-demotion without allowing a retry', async () => {
  const admin = { ...manager, userId: 'admin@example.test', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityRoles: [] };
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'self', userId: admin.userId, role: 'CustomerAdmin', active: true, version: 'v1' }], total: 1 })).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504)).mockResolvedValueOnce(reply({ resourceId: 'self' }));
  vi.stubGlobal('fetch', fetchMock); const view = render(<SettingsPanel access={admin} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Tải thành viên tổ chức" })); await user.click(await screen.findByRole('button', { name: "Sửa thành viên admin@example.test" })); await user.type(screen.getByLabelText("Lý do thay đổi thiết lập"), 'Demote self'); await user.selectOptions(screen.getByLabelText("Vai trò thành viên khách hàng"), 'Member'); await user.click(screen.getByRole('button', { name: "Cập nhật thành viên đã chọn" })); await screen.findByText(/Chưa xác định kết quả yêu cầu thiết lập/);
  view.rerender(<SettingsPanel access={{ ...admin, organizationRole: 'Member' }} />);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu thiết lập" })); expect(await screen.findByText(/Đã lưu dữ liệu: self/)).toBeInTheDocument();
});
