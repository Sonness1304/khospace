import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import LeasePanel from './LeasePanel';
const manager = { userId: 'manager-a', organizationId: '', facilityId: 'facility-a', facilityRoles: ['FacilityManager'], operatorAdmin: false };
const lease = { id: 'lease-a', bookingId: 'booking-a', rentalAccountId: 'account-a', organizationId: 'org-a', facilityId: 'facility-a', state: 'Scheduled', version: 'lease-version',
  startDate: '2028-01-01', endDate: '2028-01-03', units: [{ id: 'grant-a', unitId: 'unit-a' }], lines: [] };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('requires explicit termination confirmation and keeps custody separate from settlement', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ ...lease, state: 'Active' })).mockResolvedValueOnce(reply({ token: 'csrf' }))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'lease-a' } })).mockResolvedValueOnce(reply({ ...lease, state: 'ClearancePending', terminatedAt: '2026-10-28T10:00:00Z', terminationReason: 'Stop rental' }));
  vi.stubGlobal('fetch', fetchMock); render(<LeasePanel access={manager} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng thuê"), 'lease-a'); await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" })); await screen.findByText("Đang hoạt động");
  expect(screen.getByRole('button', { name: "Chấm dứt hợp đồng thuê" })).toBeDisabled();
  await user.type(screen.getByLabelText("Lý do chấm dứt"), 'Stop rental'); await user.click(screen.getByLabelText("Tôi xác nhận chấm dứt ngay; hàng hóa và quyết toán vẫn cần xử lý"));
  await user.click(screen.getByRole('button', { name: "Chấm dứt hợp đồng thuê" })); await screen.findByText("Chờ giải phóng khoang");
  expect(fetchMock.mock.calls[2][0]).toBe('/api/v1/leases/lease-a/terminate');
  expect(JSON.parse(fetchMock.mock.calls[2][1].body)).toEqual({ reason: 'Stop rental', terminationConfirmed: true });
  expect(fetchMock.mock.calls[2][1].headers['If-Match']).toBe('"lease-version"');
  expect(screen.queryByRole('button', { name: "Chấm dứt hợp đồng thuê" })).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: "Ghi nhận giải phóng khoang thực tế" })).toBeInTheDocument();
  expect(screen.getByText(/Đã ghi nhận chấm dứt/)).toBeInTheDocument();
});
it('freezes termination identity after an unknown result and retains it across retry401', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ ...lease, state: 'Active' })).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504))
    .mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 401));
  vi.stubGlobal('fetch', fetchMock); const view = render(<LeasePanel access={manager} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng thuê"), 'lease-a'); await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" })); await screen.findByText("Đang hoạt động");
  await user.type(screen.getByLabelText("Lý do chấm dứt"), 'Original termination'); await user.click(screen.getByLabelText("Tôi xác nhận chấm dứt ngay; hàng hóa và quyết toán vẫn cần xử lý"));
  await user.click(screen.getByRole('button', { name: "Chấm dứt hợp đồng thuê" })); await screen.findByText(/Chưa xác định kết quả bàn giao thực tế/);
  await user.clear(screen.getByLabelText("Lý do chấm dứt")); await user.type(screen.getByLabelText("Lý do chấm dứt"), 'Changed draft');
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu bàn giao" })); await user.click(await screen.findByRole('button', { name: "Thử lại yêu cầu bàn giao gốc" }));
  await screen.findByText(/Chưa xác định kết quả bàn giao thực tế/); const posts = fetchMock.mock.calls.filter(([, options]) => options.method === 'POST'); expect(posts).toHaveLength(2);
  expect(posts[0][1].body).toBe(posts[1][1].body); expect(posts[0][1].headers['Idempotency-Key']).toBe(posts[1][1].headers['Idempotency-Key']);
  view.rerender(<LeasePanel access={{ ...manager, facilityId: 'foreign' }} />); expect(screen.getByRole('button', { name: "Đọc kết quả yêu cầu bàn giao" })).toBeDisabled(); expect(screen.queryByText("Đang hoạt động")).not.toBeInTheDocument();
});
it('requires manager inspection before handover and sends the current lease version', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(lease)).mockResolvedValueOnce(reply({ token: 'csrf' }))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'lease-a' } })).mockResolvedValueOnce(reply({ ...lease, state: 'Active' }));
  vi.stubGlobal('fetch', fetchMock); render(<LeasePanel access={manager} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng thuê"), 'lease-a'); await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" }));
  await screen.findByText("Đã lên lịch"); expect(screen.getByRole('button', { name: "Ghi nhận bàn giao" })).toBeDisabled();
  await user.type(screen.getByLabelText("Lý do kiểm tra"), 'Synthetic inspected handover'); await user.click(screen.getByLabelText("Đã hoàn tất kiểm tra thực tế"));
  await user.click(screen.getByRole('button', { name: "Ghi nhận bàn giao" })); expect(await screen.findByText("Đang hoạt động")).toBeInTheDocument();
  expect(fetchMock.mock.calls[2][1].headers['If-Match']).toBe('"lease-version"'); expect(JSON.parse(fetchMock.mock.calls[2][1].body).inspectionConfirmed).toBe(true);
});
it('preserves physical-command uncertainty and cannot read or retry under foreign facility context', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ ...lease, state: 'ClearancePending' })).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504)));
  const view = render(<LeasePanel access={manager} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng thuê"), 'lease-a'); await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" })); await screen.findByText("Chờ giải phóng khoang");
  await user.type(screen.getByLabelText("Lý do kiểm tra"), 'Synthetic clearance'); await user.click(screen.getByLabelText("Đã hoàn tất kiểm tra thực tế"));
  await user.click(screen.getByRole('button', { name: "Ghi nhận giải phóng khoang thực tế" })); await screen.findByText(/Chưa xác định kết quả bàn giao thực tế/);
  view.rerender(<LeasePanel access={{ ...manager, facilityId: 'facility-b' }} />);
  expect(screen.queryByText("Chờ giải phóng khoang")).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: "Đọc kết quả yêu cầu bàn giao" })).toBeDisabled();
});
it('does not expose manager controls for a customer and preserves a safe lookup error', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply(lease)));
  render(<LeasePanel access={{ ...manager, facilityId: '', facilityRoles: [], organizationId: 'org-a' }} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng thuê"), 'lease-a'); await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" }));
  expect(await screen.findByText(/Chưa xác minh được hợp đồng thuê/)).toBeInTheDocument(); expect(screen.getByLabelText("ID hợp đồng thuê")).toHaveValue('lease-a');
  await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" })); expect(await screen.findByText("Đã lên lịch")).toBeInTheDocument(); expect(screen.queryByLabelText("Lý do kiểm tra")).not.toBeInTheDocument();
});

it('keeps known physical success when only its readback fails', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(lease)).mockResolvedValueOnce(reply({ token: 'csrf' }))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'lease-a' } })).mockResolvedValueOnce(reply({}, 503)));
  render(<LeasePanel access={manager} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng thuê"), 'lease-a'); await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" })); await screen.findByText("Đã lên lịch");
  await user.type(screen.getByLabelText("Lý do kiểm tra"), 'Known saved handover'); await user.click(screen.getByLabelText("Đã hoàn tất kiểm tra thực tế"));
  await user.click(screen.getByRole('button', { name: "Ghi nhận bàn giao" })); expect(await screen.findByText(/Đã lưu yêu cầu bàn giao thực tế nhưng/)).toBeInTheDocument();
  expect(screen.queryByText(/Yêu cầu bàn giao bị từ chối/)).not.toBeInTheDocument();
});

it('keeps original physical uncertainty after retry401 and never changes its inspection payload', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(lease)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504))
    .mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 401));
  vi.stubGlobal('fetch', fetchMock); render(<LeasePanel access={manager} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng thuê"), 'lease-a'); await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" })); await screen.findByText("Đã lên lịch");
  await user.type(screen.getByLabelText("Lý do kiểm tra"), 'Original inspection'); await user.click(screen.getByLabelText("Đã hoàn tất kiểm tra thực tế"));
  await user.click(screen.getByRole('button', { name: "Ghi nhận bàn giao" })); await screen.findByText(/Chưa xác định kết quả bàn giao thực tế/);
  await user.clear(screen.getByLabelText("Lý do kiểm tra")); await user.type(screen.getByLabelText("Lý do kiểm tra"), 'Changed draft');
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu bàn giao" })); await user.click(await screen.findByRole('button', { name: "Thử lại yêu cầu bàn giao gốc" }));
  expect(await screen.findByText(/Chưa xác định kết quả bàn giao thực tế/)).toBeInTheDocument();
  const posts = fetchMock.mock.calls.filter(([, options]) => options.method === 'POST'); expect(posts).toHaveLength(2);
  expect(posts[0][1].body).toBe(posts[1][1].body); expect(posts[0][1].headers['Idempotency-Key']).toBe(posts[1][1].headers['Idempotency-Key']);
});

it('can verify the original physical command receipt and display preserved monthly lines', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(lease)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(new TypeError('lost'))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'lease-a' } })).mockResolvedValueOnce(reply({ ...lease, state: 'Active', lines: [{ id: 'month-a', startDate: '2028-01-01', endDate: '2028-01-03', amount: '2121' }] }));
  vi.stubGlobal('fetch', fetchMock); render(<LeasePanel access={manager} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng thuê"), 'lease-a'); await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" })); await screen.findByText("Đã lên lịch");
  await user.type(screen.getByLabelText("Lý do kiểm tra"), 'Lost handover reply'); await user.click(screen.getByLabelText("Đã hoàn tất kiểm tra thực tế"));
  await user.click(screen.getByRole('button', { name: "Ghi nhận bàn giao" })); await screen.findByText(/Chưa xác định kết quả bàn giao thực tế/);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu bàn giao" })); expect(await screen.findByText("Đang hoạt động")).toBeInTheDocument();
  expect(screen.getByText('2121 VND')).toBeInTheDocument(); expect(fetchMock.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(1);
});

it('shows a definite initial physical rejection while preserving the inspection draft', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(lease)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 409)));
  render(<LeasePanel access={{ ...manager, operatorAdmin: true }} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng thuê"), 'lease-a'); await user.click(screen.getByRole('button', { name: "Tải hợp đồng thuê" })); await screen.findByText("Đã lên lịch");
  await user.type(screen.getByLabelText("Lý do kiểm tra"), 'Keep inspected draft'); await user.click(screen.getByLabelText("Đã hoàn tất kiểm tra thực tế"));
  await user.click(screen.getByRole('button', { name: "Ghi nhận bàn giao" })); expect(await screen.findByText(/Yêu cầu bàn giao bị từ chối/)).toBeInTheDocument();
  expect(screen.getByLabelText("Lý do kiểm tra")).toHaveValue('Keep inspected draft');
});
