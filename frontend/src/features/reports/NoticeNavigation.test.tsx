import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import LeasePanel from '../leases/LeasePanel';
import StockRequestPanel from '../stock-requests/StockRequestPanel';
import type { WorkspaceAccess } from '../auth/workspace';

const access: WorkspaceAccess = { userId: 'manager-a', organizationId: 'org-a', facilityId: 'facility-a', facilityRoles: ['FacilityManager', 'Clerk'], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status === 200, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
for (const kind of ['lease', 'request'] as const) {
 const field = kind === 'lease' ? 'ID hợp đồng thuê' : 'ID yêu cầu nhập xuất';
 const readButton = kind === 'lease' ? 'Tải hợp đồng thuê' : 'Tải yêu cầu';
 const state = kind === 'lease' ? 'Đang hoạt động' : 'Đã gửi';
 const value = { id: `${kind}-a`, bookingId: 'booking-a', rentalAccountId: 'account-a', organizationId: 'org-a', facilityId: 'facility-a',
  state: kind === 'lease' ? 'Active' : 'Submitted', version: 'v1', startDate: '2028-01-01', endDate: '2028-02-01',
  units: [], lines: [], type: 'Receipt', leaseId: 'lease-a', leaseUnitId: 'grant-a' };
 function Panel(props: { access: WorkspaceAccess; selectedId: string; noticeKey: string }) {
  return kind === 'lease' ? <LeasePanel {...props} /> : <StockRequestPanel {...props} />;
 }
 it.each(['receipt', 'retry'])(`${kind}: successful %s of A preserves verified notice B and settles the original command`, async recovery => {
  const other = { ...value, id: `${kind}-b`, rentalAccountId: 'account-b', leaseId: 'lease-b' };
  const mock = vi.fn().mockResolvedValueOnce(reply(value)).mockResolvedValueOnce(reply({ token: 'csrf' }))
   .mockResolvedValueOnce(reply({}, 504)).mockResolvedValueOnce(reply(other));
  if (recovery === 'retry') mock.mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' }));
  mock.mockResolvedValueOnce(reply({ result: { resourceId: value.id } })).mockResolvedValueOnce(reply(value));
  vi.stubGlobal('fetch', mock); const user = userEvent.setup();
  const view = render(<Panel access={access} selectedId={value.id} noticeKey="n1" />);
  await user.click(screen.getByRole('button', { name: readButton })); await screen.findByText(state);
  if (kind === 'lease') {
   await user.type(screen.getByLabelText('Lý do chấm dứt'), 'Original command reason');
   await user.click(screen.getByLabelText('Tôi xác nhận chấm dứt ngay; hàng hóa và quyết toán vẫn cần xử lý'));
   await user.click(screen.getByRole('button', { name: 'Chấm dứt hợp đồng thuê' }));
  } else {
   await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Original command reason');
   await user.click(screen.getByRole('button', { name: 'Phê duyệt yêu cầu' }));
  }
  await screen.findByText(kind === 'lease' ? /Chưa xác định kết quả bàn giao thực tế/ : /Chưa xác định kết quả yêu cầu kho/);
  view.rerender(<Panel access={access} selectedId={other.id} noticeKey="n2" />);
  await user.click(screen.getByRole('button', { name: readButton })); await screen.findByText(state);
  const receiptButton = kind === 'lease' ? 'Đọc kết quả yêu cầu bàn giao' : 'Đọc kết quả yêu cầu kho';
  await user.click(screen.getByRole('button', { name: receiptButton }));
  if (recovery === 'retry') await user.click(await screen.findByRole('button', { name: kind === 'lease' ? 'Thử lại yêu cầu bàn giao gốc' : 'Thử lại yêu cầu kho gốc' }));
  expect(screen.queryByRole('button', { name: receiptButton })).not.toBeInTheDocument();
  expect(screen.getByLabelText(field)).toHaveValue(other.id);
  expect(screen.getByText(state)).toBeInTheDocument();
  expect(screen.getByText(kind === 'lease' ? /Tài khoản thuê account-b/ : /Hợp đồng lease-b/)).toBeInTheDocument();
  expect(mock.mock.calls.filter(([url]) => url === `/api/v1/${kind === 'lease' ? 'leases' : 'stock-requests'}/${value.id}`)).toHaveLength(1);
  const posts = mock.mock.calls.filter(([, options]) => options.method === 'POST');
  expect(posts).toHaveLength(recovery === 'retry' ? 2 : 1);
  if (recovery === 'retry') {
   expect(posts[1][0]).toBe(posts[0][0]); expect(posts[1][1].body).toBe(posts[0][1].body);
   expect(posts[1][1].headers['Idempotency-Key']).toBe(posts[0][1].headers['Idempotency-Key']);
   expect(posts[1][1].headers['If-Match']).toBe(posts[0][1].headers['If-Match']);
  }
 });
 it(`${kind}: a notice preselects the ID but requires a fresh explicit read; repeated notice clears old detail`, async () => {
  const mock = vi.fn().mockResolvedValue(reply(value)); vi.stubGlobal('fetch', mock);
  const user = userEvent.setup(); const view = render(<Panel access={access} selectedId={value.id} noticeKey="n1" />);
  expect(screen.getByLabelText(field)).toHaveValue(value.id); expect(mock).not.toHaveBeenCalled();
  expect(screen.getByText(/Thông báo là lịch sử/)).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: readButton })); await screen.findByText(state);
  view.rerender(<Panel access={access} selectedId={value.id} noticeKey="n2" />);
  expect(screen.queryByText(state)).not.toBeInTheDocument(); expect(mock).toHaveBeenCalledTimes(1);
  await user.click(screen.getByRole('button', { name: readButton })); await screen.findByText(state);
  expect(mock.mock.calls.every(([, options]) => !options.method || options.method === 'GET')).toBe(true);
 });
 it(`${kind}: a late previous-record read cannot replace the new notice ID or publish its detail`, async () => {
  let finish!: (value: unknown) => void;
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => new Promise(resolve => { finish = resolve; })));
  const user = userEvent.setup(); const view = render(<Panel access={access} selectedId={value.id} noticeKey="n1" />);
  await user.click(screen.getByRole('button', { name: readButton }));
  view.rerender(<Panel access={access} selectedId={`${kind}-b`} noticeKey="n2" />);
  await act(async () => finish(reply(value)));
  expect(screen.getByLabelText(field)).toHaveValue(`${kind}-b`);
  expect(screen.queryByText(state)).not.toBeInTheDocument();
  expect(screen.queryByText('Đã xác minh trạng thái hợp đồng.')).not.toBeInTheDocument();
 });
 it.each([403, 404])(`${kind}: failed current read%s never reuses a previous notice detail`, async status => {
  const mock = vi.fn().mockResolvedValueOnce(reply(value)).mockResolvedValueOnce(reply({}, status));
  vi.stubGlobal('fetch', mock); const user = userEvent.setup(); const view = render(<Panel access={access} selectedId={value.id} noticeKey="n1" />);
  await user.click(screen.getByRole('button', { name: readButton })); await screen.findByText(state);
  view.rerender(<Panel access={access} selectedId={`${kind}-b`} noticeKey="n2" />);
  await user.click(screen.getByRole('button', { name: readButton }));
  await screen.findByText(kind === 'lease' ? /Chưa xác minh được hợp đồng thuê/ : /Chưa xác minh được dữ liệu kho/);
  expect(screen.queryByText(state)).not.toBeInTheDocument();
 });
 it(`${kind}: a notice navigation preserves the original uncertain mutation and retry identity`, async () => {
  const mock = vi.fn().mockResolvedValueOnce(reply(value)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504))
   .mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 403));
  vi.stubGlobal('fetch', mock); const user = userEvent.setup(); const view = render(<Panel access={access} selectedId={value.id} noticeKey="n1" />);
  await user.click(screen.getByRole('button', { name: readButton })); await screen.findByText(state);
  if (kind === 'lease') {
   await user.type(screen.getByLabelText('Lý do chấm dứt'), 'Original command reason');
   await user.click(screen.getByLabelText('Tôi xác nhận chấm dứt ngay; hàng hóa và quyết toán vẫn cần xử lý'));
   await user.click(screen.getByRole('button', { name: 'Chấm dứt hợp đồng thuê' }));
  } else {
   await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Original command reason');
   await user.click(screen.getByRole('button', { name: 'Phê duyệt yêu cầu' }));
  }
  await screen.findByText(kind === 'lease' ? /Chưa xác định kết quả bàn giao thực tế/ : /Chưa xác định kết quả yêu cầu kho/);
  view.rerender(<Panel access={access} selectedId={`${kind}-b`} noticeKey="n2" />);
  await user.click(screen.getByRole('button', { name: kind === 'lease' ? 'Đọc kết quả yêu cầu bàn giao' : 'Đọc kết quả yêu cầu kho' }));
  await user.click(await screen.findByRole('button', { name: kind === 'lease' ? 'Thử lại yêu cầu bàn giao gốc' : 'Thử lại yêu cầu kho gốc' }));
  await screen.findByText(kind === 'lease' ? /Chưa xác định kết quả bàn giao thực tế/ : /Chưa xác định kết quả yêu cầu kho/);
  const posts = mock.mock.calls.filter(([, options]) => options.method === 'POST');
  expect(posts).toHaveLength(2); expect(posts[0][0]).toContain(`/${value.id}/`);
  expect(posts[0][0]).toBe(posts[1][0]); expect(posts[0][1].body).toBe(posts[1][1].body);
  expect(posts[0][1].headers['Idempotency-Key']).toBe(posts[1][1].headers['Idempotency-Key']);
 });
}
