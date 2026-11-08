import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import BookingPanel from './BookingPanel';
const access = { userId: 'admin', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: '', facilityRoles: [], operatorAdmin: false };
const quote = { id: 'quote-a', version: 'quote-version', organizationId: 'org-a', facilityId: 'facility-a', expiresAt: '2028-01-01T00:00:00Z' };
const booking = { id: 'booking-a', quoteId: 'quote-a', organizationId: 'org-a', facilityId: 'facility-a', startDate: '2028-01-01', endDate: '2028-01-03', expiresAt: '2028-01-01T00:15:00Z', state: 'Held', version: 'booking-version', rentalAccountId: 'account-a', requiredDeposit: '424', refundNeeded: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('requests an explicit hold and reads server booking/account status without inventing verified funds', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } })).mockResolvedValueOnce(reply(booking));
  vi.stubGlobal('fetch', fetchMock); render(<BookingPanel access={access} quote={quote} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" }));
  expect(await screen.findByText("Đang giữ chỗ")).toBeInTheDocument(); expect(screen.getByText('424 VND')).toBeInTheDocument();
  expect(screen.getByText('booking-a')).toBeInTheDocument(); expect(screen.getByText('account-a')).toBeInTheDocument();
  expect(screen.getByText(/Trạng thái đặt cọc: Đang chờ/)).toBeInTheDocument();
  expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toEqual({ expectedQuoteVersion: 'quote-version' });
  expect(screen.queryByRole('button', { name: "Xác nhận đặt chỗ" })).not.toBeInTheDocument();
});
it('preserves cancellation uncertainty across failed retry and refuses another organization', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } })).mockResolvedValueOnce(reply(booking))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504)).mockResolvedValueOnce(reply({}, 404))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 401));
  vi.stubGlobal('fetch', fetchMock); const view = render(<BookingPanel access={access} quote={quote} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" })); await screen.findByText("Đang giữ chỗ");
  await user.type(screen.getByLabelText("Lý do hủy"), 'Synthetic cancel'); await user.click(screen.getByRole('button', { name: "Hủy thuê" }));
  await screen.findByText(/Chưa xác định kết quả yêu cầu thuê/);
  view.rerender(<BookingPanel access={{ ...access, organizationId: 'org-b' }} quote={null} />);
  expect(screen.getByRole('button', { name: "Đọc kết quả yêu cầu thuê" })).toBeDisabled(); expect(screen.queryByText('424 VND')).not.toBeInTheDocument();
  view.rerender(<BookingPanel access={access} quote={null} />);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu thuê" })); await user.click(await screen.findByRole('button', { name: "Thử lại yêu cầu thuê gốc" }));
  expect(await screen.findByText(/Chưa xác định kết quả yêu cầu thuê/)).toBeInTheDocument();
  const posts = fetchMock.mock.calls.filter(([, options]) => options.method === 'POST');
  expect(posts[1][1].body).toBe(posts[2][1].body); expect(posts[1][1].headers['Idempotency-Key']).toBe(posts[2][1].headers['Idempotency-Key']);
});
it('can recover an unknown hold from command readback after the quote selection disappears', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } })).mockResolvedValueOnce(reply(booking));
  vi.stubGlobal('fetch', fetchMock); const view = render(<BookingPanel access={access} quote={quote} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" })); await screen.findByText(/Chưa xác định kết quả yêu cầu thuê/);
  view.rerender(<BookingPanel access={access} quote={null} />);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu thuê" })); expect(await screen.findByText("Đang giữ chỗ")).toBeInTheDocument();
  expect(fetchMock.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(1);
});

it('keeps a known saved hold when status reload fails and recovers on explicit status read', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } }))
    .mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply({ ...booking, state: 'Expired' })));
  render(<BookingPanel access={access} quote={quote} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" }));
  expect(await screen.findByText(/Đã lưu yêu cầu thuê nhưng/)).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: "Tải trạng thái thuê" })); expect(await screen.findByText(/Không tải được trạng thái thuê/)).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: "Tải trạng thái thuê" })); expect(await screen.findByText("Đã hết hạn")).toBeInTheDocument();
});

it('distinguishes a definite initial rejection and allows a fresh explicit hold request', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 409)));
  render(<BookingPanel access={access} quote={quote} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" }));
  expect(await screen.findByText(/Yêu cầu thuê bị từ chối/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" })).toBeEnabled();
});

it('retries a frozen hold after readback404 with same identity even without the current quote', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(new TypeError('lost'))
    .mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } })).mockResolvedValueOnce(reply(booking));
  vi.stubGlobal('fetch', fetchMock); const view = render(<BookingPanel access={access} quote={quote} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" })); await screen.findByText(/Chưa xác định kết quả yêu cầu thuê/);
  view.rerender(<BookingPanel access={access} quote={null} />);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu thuê" })); await user.click(await screen.findByRole('button', { name: "Thử lại yêu cầu thuê gốc" }));
  expect(await screen.findByText("Đang giữ chỗ")).toBeInTheDocument();
  const posts = fetchMock.mock.calls.filter(([, options]) => options.method === 'POST'); expect(posts).toHaveLength(2);
  expect(posts[0][1].body).toBe(posts[1][1].body); expect(posts[0][1].headers['Idempotency-Key']).toBe(posts[1][1].headers['Idempotency-Key']);
});

it('displays refund review after cancellation and exposes staff confirmation only with matching current scope', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } })).mockResolvedValueOnce(reply(booking))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } }))
    .mockResolvedValueOnce(reply({ ...booking, state: 'Cancelled', refundNeeded: true }));
  vi.stubGlobal('fetch', fetchMock); render(<BookingPanel access={{ ...access, facilityId: 'facility-a', facilityRoles: ['Sales'] }} quote={quote} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" })); await screen.findByText("Đang giữ chỗ");
  expect(screen.getByRole('button', { name: "Xác nhận đặt chỗ" })).toBeDisabled();
  await user.type(screen.getByLabelText("Lý do hủy"), 'Synthetic cancellation');
  await user.click(screen.getByRole('button', { name: "Hủy thuê" }));
  expect(await screen.findByText("Đã hủy")).toBeInTheDocument(); expect(screen.getByText(/Tiền cần bộ phận tài chính kiểm tra/)).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: "Hủy thuê" })).not.toBeInTheDocument();
});

it('creates one lease from a confirmed booking and shows its ID without treating it as a booking ID', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } })).mockResolvedValueOnce(reply({ ...booking, state: 'Confirmed' }))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'lease-a' } }));
  vi.stubGlobal('fetch', fetchMock); render(<BookingPanel access={{ ...access, facilityId: 'facility-a', facilityRoles: ['Sales'] }} quote={quote} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" })); await screen.findByText("Đã xác nhận");
  await user.type(screen.getByLabelText("Lý do hủy"), 'Synthetic conversion reason'); await user.click(screen.getByRole('button', { name: "Tạo hợp đồng thuê" }));
  expect(await screen.findByText('lease-a')).toBeInTheDocument(); expect(screen.getByText(/Dùng ID này tại mục Hợp đồng/)).toBeInTheDocument();
  expect(fetchMock.mock.calls.some(([url]) => url === '/api/v1/bookings/lease-a')).toBe(false);
});

it('lets assigned sales read a booking without customer membership and confirm using real server deposit state', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ ...booking, depositVerification: 'Verified' })).mockResolvedValueOnce(reply({ token: 'csrf' }))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } })).mockResolvedValueOnce(reply({ ...booking, state: 'Confirmed', depositVerification: 'Verified' }));
  vi.stubGlobal('fetch', fetchMock); render(<BookingPanel access={{ ...access, organizationId: '', organizationRole: undefined, facilityId: 'facility-a', facilityRoles: ['Sales'] }} quote={null} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID đặt chỗ"), 'booking-a'); await user.click(screen.getByRole('button', { name: "Tải đặt chỗ" })); await screen.findByText("Đang giữ chỗ");
  await user.type(screen.getByLabelText("Lý do hủy"), 'Sales confirms verified deposit'); await user.click(screen.getByRole('button', { name: "Xác nhận đặt chỗ" }));
  expect(await screen.findByText("Đã xác nhận")).toBeInTheDocument(); expect(fetchMock.mock.calls[2][1].headers['If-Match']).toBe('"booking-version"');
});

it('binds a new sales hold to the new quote organization even while an earlier booking remains visible', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(booking)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504)).mockResolvedValueOnce(reply({}, 404));
  vi.stubGlobal('fetch', fetchMock); render(<BookingPanel access={{ ...access, organizationId: '', organizationRole: undefined, facilityId: 'facility-a', facilityRoles: ['Sales'] }} quote={{ ...quote, organizationId: 'org-b' }} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID đặt chỗ"), 'booking-a'); await user.click(screen.getByRole('button', { name: "Tải đặt chỗ" })); await screen.findByText("Đang giữ chỗ");
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" })); await screen.findByText(/Chưa xác định kết quả yêu cầu thuê/); await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu thuê" }));
  expect(fetchMock.mock.calls[3][0]).toContain('scopeId=org-b');
});
