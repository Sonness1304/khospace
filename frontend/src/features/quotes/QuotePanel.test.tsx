import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import QuotePanel from './QuotePanel';
const access = { userId: 'customer-a', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: '', facilityRoles: [], operatorAdmin: false };
const selection = { facilityId: 'facility-a', unitIds: ['unit-a'], startDate: '2028-01-31', endDate: '2028-02-02', verified: true };
const quote = { id: 'quote-a', organizationId: 'org-a', facilityId: 'facility-a', rentalSubtotal: '2122', requiredDeposit: '424', currency: 'VND', startDate: selection.startDate, endDate: selection.endDate,
  expiresAt: '2026-10-29T03:00:00Z', version: 'quote-version', reservation: false, lines: [
    { id: 'line-a', unitCode: 'A-01', startDate: '2028-01-31', endDate: '2028-02-01', days: 1, areaM2: '10.5', dailyRate: '101', amount: '1061' },
    { id: 'line-b', unitCode: 'A-01', startDate: '2028-02-01', endDate: '2028-02-02', days: 1, areaM2: '10.5', dailyRate: '101', amount: '1061' }] };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());

it('shows server monthly lines and separate deposit, never sends a hold', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'quote-a' } })).mockResolvedValueOnce(reply(quote));
  vi.stubGlobal('fetch', fetchMock); render(<QuotePanel access={access} selection={selection} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Nhận báo giá chính xác" }));
  expect(await screen.findByText('2122 VND')).toBeInTheDocument(); expect(screen.getByText('424 VND')).toBeInTheDocument();
  expect(screen.getAllByText('1061')).toHaveLength(2); expect(screen.getByText(/chưa giữ chỗ/)).toBeInTheDocument();
  expect(fetchMock.mock.calls.filter(([, init]) => init.method === 'POST')).toHaveLength(1);
  expect(fetchMock.mock.calls.some(([url]) => url.includes('hold'))).toBe(false);
  expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toEqual({ facilityId: 'facility-a', organizationId: 'org-a', unitIds: ['unit-a'], startDate: '2028-01-31', endDate: '2028-02-02' });
});

it('keeps known quote success when only detail readback fails and can read it later', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'quote-a' } }))
    .mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply(quote)));
  render(<QuotePanel access={access} selection={selection} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Nhận báo giá chính xác" }));
  expect(await screen.findByText(/Đã lưu báo giá nhưng/)).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: "Tải báo giá" })); expect(await screen.findByText('2122 VND')).toBeInTheDocument();
});

it('preserves unknown original through failed retry and never substitutes a different organization', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504))
    .mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 401));
  vi.stubGlobal('fetch', fetchMock); const view = render(<QuotePanel access={access} selection={selection} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Nhận báo giá chính xác" })); await screen.findByText(/Chưa xác định kết quả báo giá/);
  view.rerender(<QuotePanel access={{ ...access, organizationId: 'org-b' }} selection={selection} />);
  expect(screen.getByRole('button', { name: "Đọc kết quả yêu cầu báo giá" })).toBeDisabled();
  expect(screen.getByRole('button', { name: "Nhận báo giá chính xác" })).toBeDisabled();
  view.rerender(<QuotePanel access={access} selection={selection} />);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu báo giá" }));
  await user.click(await screen.findByRole('button', { name: "Thử lại báo giá gốc" }));
  expect(await screen.findByText(/Chưa xác định kết quả báo giá/)).toBeInTheDocument();
  const posts = fetchMock.mock.calls.filter(([, init]) => init.method === 'POST'); expect(posts).toHaveLength(2);
  expect(posts[0][1].body).toBe(posts[1][1].body); expect(posts[0][1].headers['Idempotency-Key']).toBe(posts[1][1].headers['Idempotency-Key']);
});

it('requires current customer-admin context and a freshly verified selection', () => {
  const view = render(<QuotePanel access={null} selection={selection} />);
  expect(screen.getByRole('button', { name: "Nhận báo giá chính xác" })).toBeDisabled();
  view.rerender(<QuotePanel access={{ ...access, organizationRole: 'Member' }} selection={selection} />);
  expect(screen.getByRole('button', { name: "Nhận báo giá chính xác" })).toBeDisabled();
  view.rerender(<QuotePanel access={access} selection={{ ...selection, verified: false }} />);
  expect(screen.getByRole('button', { name: "Nhận báo giá chính xác" })).toBeDisabled();
});

it('reads an unknown quote from its saved command without replaying or showing it in a different context', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(new TypeError('reply lost'))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'quote-a' } })).mockResolvedValueOnce(reply(quote));
  vi.stubGlobal('fetch', fetchMock); const view = render(<QuotePanel access={access} selection={selection} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Nhận báo giá chính xác" })); await screen.findByText(/Chưa xác định kết quả báo giá/);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu báo giá" })); expect(await screen.findByText('2122 VND')).toBeInTheDocument();
  expect(fetchMock.mock.calls.filter(([, init]) => init.method === 'POST')).toHaveLength(1);
  view.rerender(<QuotePanel access={{ ...access, organizationId: 'org-b' }} selection={selection} />);
  expect(screen.queryByText('2122 VND')).not.toBeInTheDocument();
});

it('can explicitly recover a frozen original after availability clears its current selection', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504))
    .mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'quote-a' } }))
    .mockResolvedValueOnce(reply(quote));
  vi.stubGlobal('fetch', fetchMock); const view = render(<QuotePanel access={access} selection={selection} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Nhận báo giá chính xác" })); await screen.findByText(/Chưa xác định kết quả báo giá/);
  view.rerender(<QuotePanel access={access} selection={{ ...selection, unitIds: [], verified: false }} />);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu báo giá" }));
  const retry = await screen.findByRole('button', { name: "Thử lại báo giá gốc" }); expect(retry).toBeEnabled(); await user.click(retry);
  expect(await screen.findByText("Đã lưu báo giá.")).toBeInTheDocument();
  const posts = fetchMock.mock.calls.filter(([, init]) => init.method === 'POST'); expect(posts).toHaveLength(2);
  expect(posts[1][1].body).toBe(posts[0][1].body); expect(screen.queryByText('2122 VND')).not.toBeInTheDocument();
});

it('lets assigned sales explicitly choose a customer organization and preserves its quote and hold scope', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'quote-a' } })).mockResolvedValueOnce(reply(quote))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'booking-a' } })).mockResolvedValueOnce(reply({ id: 'booking-a', organizationId: 'org-a', facilityId: 'facility-a', requiredDeposit: '424', depositVerification: 'Pending', state: 'Held', startDate: '2028-01-31', endDate: '2028-02-02', expiresAt: '2028-01-01T00:15:00Z', rentalAccountId: 'account-a', version: 'v1' }));
  vi.stubGlobal('fetch', fetchMock); render(<QuotePanel access={{ ...access, organizationId: '', organizationRole: undefined, facilityId: 'facility-a', facilityRoles: ['Sales'] }} selection={selection} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID tổ chức khách hàng cần báo giá"), 'org-a'); await user.click(screen.getByRole('button', { name: "Nhận báo giá chính xác" })); await screen.findByText('2122 VND');
  await user.click(screen.getByRole('button', { name: "Giữ các khoang đã chọn trong 15 phút" })); expect(await screen.findByText("Đang giữ chỗ")).toBeInTheDocument(); expect(JSON.parse(fetchMock.mock.calls[1][1].body).organizationId).toBe('org-a');
});

it('discloses nominal monthly prices and actual leap-month proration before booking', async () => {
  const monthly={...quote,rentalSubtotal:'2069',requiredDeposit:'414',lines:quote.lines.map((line,index)=>({...line,areaM2:'10',dailyRate:'0',billingBasis:'CalendarMonth',monthlyRate:'3100',calendarDays:index===0?31:29,amount:index===0?'1000':'1069'}))};
  vi.stubGlobal('fetch',vi.fn().mockResolvedValueOnce(reply({token:'csrf'})).mockResolvedValueOnce(reply({result:{resourceId:'quote-a'}})).mockResolvedValueOnce(reply(monthly)));
  render(<QuotePanel access={access} selection={selection}/>);const user=userEvent.setup();await user.click(screen.getByRole('button',{name:"Nhận báo giá chính xác"}));
  expect(await screen.findByText('2069 VND')).toBeInTheDocument();expect(screen.getByText('414 VND')).toBeInTheDocument();expect(screen.getByText("3100 VND/m²/tháng dương lịch · 1/29 ngày")).toBeInTheDocument();expect(screen.getByText("3100 VND/m²/tháng dương lịch · 1/31 ngày")).toBeInTheDocument();
});
