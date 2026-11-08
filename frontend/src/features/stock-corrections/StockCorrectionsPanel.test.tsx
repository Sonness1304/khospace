import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import StockCorrectionsPanel from './StockCorrectionsPanel';
const clerk = { userId: 'clerk', organizationId: '', facilityId: 'facility-a', facilityRoles: ['Clerk'], operatorAdmin: false };
const manager = { ...clerk, userId: 'manager', facilityRoles: ['FacilityManager'] };
const posting = { id: 'posting-a', organizationId: 'org-a', facilityId: 'facility-a', type: 'Transfer', reversedBy: null, entries: [{ id: 'leg-a', leaseUnitId: 'grant-a', itemId: 'item-a', delta: '-4' }, { id: 'leg-b', leaseUnitId: 'grant-b', itemId: 'item-a', delta: '4' }] };
const count = { id: 'count-a', organizationId: 'org-a', facilityId: 'facility-a', leaseUnitId: 'grant-a', itemId: 'item-a', state: 'Pending', createdBy: 'clerk', version: 'count-version', expectedQuantity: '10', countedQuantity: '8' };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
async function draft() {
  const user = userEvent.setup(); await user.type(screen.getByLabelText("ID tổ chức cần điều chỉnh"), 'org-a'); await user.type(screen.getByLabelText("ID quyền sử dụng khoang nguồn"), 'grant-a');
  await user.type(screen.getByLabelText("ID mặt hàng cần điều chỉnh"), 'item-a'); await user.type(screen.getByLabelText("Lý do điều chỉnh"), 'Observed correction'); return user;
}
it('posts a confirmed transfer with exact quantity and business identity', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockImplementationOnce(async (_url, options) => reply({ result: { resourceId: JSON.parse(options.body).transferId } }));
  vi.stubGlobal('fetch', fetchMock); render(<StockCorrectionsPanel access={clerk} />); const user = await draft();
  await user.type(screen.getByLabelText("ID quyền sử dụng khoang đích"), 'grant-b'); await user.type(screen.getByLabelText("Số lượng chuyển"), '4.125');
  expect(screen.getByRole('button', { name: "Ghi nhận chuyển kho" })).toBeDisabled(); await user.click(screen.getByLabelText("Đã kiểm tra thực tế cả hai đầu chuyển")); await user.click(screen.getByRole('button', { name: "Ghi nhận chuyển kho" }));
  expect(await screen.findByText(/Đã lưu điều chỉnh/)).toBeInTheDocument(); const body = JSON.parse(fetchMock.mock.calls[1][1].body); expect(body.quantity).toBe('4.125'); expect(body.transferId).toMatch(/^[a-f0-9-]{36}$/);
});
it('freezes unknown transfer payload through readback404 and retry401, and blocks foreign context', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504)).mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 401));
  vi.stubGlobal('fetch', fetchMock); const view = render(<StockCorrectionsPanel access={clerk} />); const user = await draft();
  await user.type(screen.getByLabelText("ID quyền sử dụng khoang đích"), 'grant-b'); await user.type(screen.getByLabelText("Số lượng chuyển"), '4'); await user.click(screen.getByLabelText("Đã kiểm tra thực tế cả hai đầu chuyển")); await user.click(screen.getByRole('button', { name: "Ghi nhận chuyển kho" }));
  await screen.findByText(/Chưa xác định kết quả điều chỉnh/); await user.clear(screen.getByLabelText("Số lượng chuyển")); await user.type(screen.getByLabelText("Số lượng chuyển"), '9');
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu điều chỉnh" })); await user.click(await screen.findByRole('button', { name: "Thử lại điều chỉnh gốc" })); await screen.findByText(/Chưa xác định kết quả điều chỉnh/);
  expect(fetchMock.mock.calls[4][1].body).toBe(fetchMock.mock.calls[1][1].body); expect(fetchMock.mock.calls[4][1].headers['Idempotency-Key']).toBe(fetchMock.mock.calls[1][1].headers['Idempotency-Key']);
  view.rerender(<StockCorrectionsPanel access={{ ...clerk, facilityId: 'facility-b' }} />); expect(screen.getByRole('button', { name: "Đọc kết quả yêu cầu điều chỉnh" })).toBeDisabled();
});
it('requires manager confirmation to reverse a read posting and shows both legs', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(posting)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'reversal-a' } }));
  vi.stubGlobal('fetch', fetchMock); render(<StockCorrectionsPanel access={manager} />); const user = await draft(); await user.type(screen.getByLabelText("ID chứng từ ghi kho"), 'posting-a'); await user.click(screen.getByRole('button', { name: "Tải chứng từ ghi kho" }));
  await screen.findByText(/Ghi kho posting-a/); expect(screen.getByText(/grant-b.*4/)).toBeInTheDocument(); expect(screen.getByRole('button', { name: "Đảo chứng từ ghi kho" })).toBeDisabled();
  await user.click(screen.getByLabelText("Tôi xác nhận đảo toàn bộ bằng bút toán bù trừ")); await user.click(screen.getByRole('button', { name: "Đảo chứng từ ghi kho" })); await screen.findByText(/Đã lưu điều chỉnh/);
  expect(fetchMock.mock.calls[2][0]).toBe('/api/v1/stock/postings/posting-a/reverse');
});
it('records a count observation without changing stock directly', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'count-a' } })); vi.stubGlobal('fetch', fetchMock);
  render(<StockCorrectionsPanel access={clerk} />); const user = await draft(); await user.type(screen.getByLabelText("Số lượng tồn dự kiến"), '10'); await user.type(screen.getByLabelText("Số lượng kiểm đếm"), '8'); await user.click(screen.getByRole('button', { name: "Lưu kết quả kiểm đếm" })); await screen.findByText(/Đã lưu điều chỉnh/);
  expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toMatchObject({ expectedQuantity: '10', countedQuantity: '8' }); expect(screen.queryByRole('button', { name: "Phê duyệt điều chỉnh kiểm đếm" })).not.toBeInTheDocument();
});
it('approves a different recorder count with its current version', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(count)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'count-a' } })); vi.stubGlobal('fetch', fetchMock);
  render(<StockCorrectionsPanel access={manager} />); const user = await draft(); await user.type(screen.getByLabelText("ID kết quả kiểm đếm"), 'count-a'); await user.click(screen.getByRole('button', { name: "Tải kết quả kiểm đếm" })); await screen.findByText(/Kiểm đếm count-a/);
  await user.click(screen.getByRole('button', { name: "Phê duyệt điều chỉnh kiểm đếm" })); await screen.findByText(/Đã lưu điều chỉnh/); expect(fetchMock.mock.calls[2][1].headers['If-Match']).toBe('"count-version"');
});
it('blocks self approval and hides records after access changes', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ ...count, createdBy: 'manager' }))); const view = render(<StockCorrectionsPanel access={manager} />); const user = await draft();
  await user.type(screen.getByLabelText("ID kết quả kiểm đếm"), 'count-a'); await user.click(screen.getByRole('button', { name: "Tải kết quả kiểm đếm" })); await screen.findByText(/Kiểm đếm count-a/); expect(screen.getByRole('button', { name: "Phê duyệt điều chỉnh kiểm đếm" })).toBeDisabled();
  view.rerender(<StockCorrectionsPanel access={{ ...manager, facilityId: 'facility-b' }} />); expect(screen.queryByText(/Kiểm đếm count-a/)).not.toBeInTheDocument();
});
it('keeps the draft after a definite conflict and never renders server error details', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ detail: 'SQL private sentinel' }, 409)));
  render(<StockCorrectionsPanel access={clerk} />); const user = await draft(); await user.type(screen.getByLabelText("Số lượng tồn dự kiến"), '10'); await user.type(screen.getByLabelText("Số lượng kiểm đếm"), '9'); await user.click(screen.getByRole('button', { name: "Lưu kết quả kiểm đếm" }));
  await screen.findByText(/Điều chỉnh bị từ chối/); expect(screen.getByLabelText("Số lượng kiểm đếm")).toHaveValue('9'); expect(screen.queryByText('SQL private sentinel')).not.toBeInTheDocument();
});
it('verifies an unknown count through saved-command readback without submitting it again', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(new TypeError('lost')).mockResolvedValueOnce(reply({ result: { resourceId: 'count-a' } })); vi.stubGlobal('fetch', fetchMock);
  render(<StockCorrectionsPanel access={clerk} />); const user = await draft(); await user.type(screen.getByLabelText("Số lượng tồn dự kiến"), '0'); await user.type(screen.getByLabelText("Số lượng kiểm đếm"), '0'); await user.click(screen.getByRole('button', { name: "Lưu kết quả kiểm đếm" })); await screen.findByText(/Chưa xác định kết quả điều chỉnh/);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu điều chỉnh" })); await screen.findByText(/Đã lưu điều chỉnh/); expect(fetchMock.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(1);
});
it('lists count observations with pagination and cancels a pending observation', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'count-a', state: 'Pending' }], total: 51, page: 1 })).mockResolvedValueOnce(reply({ items: [], total: 51, page: 2 }))
    .mockResolvedValueOnce(reply(count)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'count-a' } })); vi.stubGlobal('fetch', fetchMock);
  render(<StockCorrectionsPanel access={clerk} />); const user = await draft(); await user.click(screen.getByRole('button', { name: "Tải danh sách kiểm đếm" })); await screen.findByText("count-a · Đang chờ"); await user.click(screen.getByRole('button', { name: "Trang kiểm đếm sau" })); await waitFor(() => expect(fetchMock.mock.calls[1][0]).toContain('page=2'));
  await user.type(screen.getByLabelText("ID kết quả kiểm đếm"), 'count-a'); await user.click(screen.getByRole('button', { name: "Tải kết quả kiểm đếm" })); await screen.findByText(/Kiểm đếm count-a/); await user.click(screen.getByRole('button', { name: "Hủy kết quả kiểm đếm" })); await screen.findByText(/Đã lưu điều chỉnh/);
});
it('does not expose correction writes to customer-only access', () => {
  render(<StockCorrectionsPanel access={{ ...clerk, facilityRoles: [], organizationId: 'org-a' }} />); expect(screen.queryByRole('button', { name: "Ghi nhận chuyển kho" })).not.toBeInTheDocument();
});
it('shows reversal success for an operator without a selected facility', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(posting)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'reversal-a' } })));
  render(<StockCorrectionsPanel access={{ ...manager, userId: 'operator', facilityId: '', operatorAdmin: true }} />); const user = await draft();
  await user.type(screen.getByLabelText("ID chứng từ ghi kho"), 'posting-a'); await user.click(screen.getByRole('button', { name: "Tải chứng từ ghi kho" })); await screen.findByText(/Ghi kho posting-a/);
  await user.click(screen.getByLabelText("Tôi xác nhận đảo toàn bộ bằng bút toán bù trừ")); await user.click(screen.getByRole('button', { name: "Đảo chứng từ ghi kho" }));
  expect(await screen.findByText(/Đã lưu điều chỉnh: reversal-a/)).toBeInTheDocument();
});
it.each(["Phê duyệt điều chỉnh kiểm đếm", "Hủy kết quả kiểm đếm"])('shows %s success for an operator without a selected facility', async action => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(count)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'count-a' } })));
  render(<StockCorrectionsPanel access={{ ...manager, userId: 'operator', facilityId: '', operatorAdmin: true }} />); const user = await draft();
  await user.type(screen.getByLabelText("ID kết quả kiểm đếm"), 'count-a'); await user.click(screen.getByRole('button', { name: "Tải kết quả kiểm đếm" })); await screen.findByText(/Kiểm đếm count-a/);
  await user.click(screen.getByRole('button', { name: action })); expect(await screen.findByText(/Đã lưu điều chỉnh: count-a/)).toBeInTheDocument();
});
