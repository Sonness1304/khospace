import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import StockRequestPanel from './StockRequestPanel';
const customer = { userId: 'customer', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: 'facility-a', facilityRoles: [] as string[], operatorAdmin: false };
const clerk = { ...customer, userId: 'clerk', organizationId: '', facilityRoles: ['Clerk'] };
const request = { id: 'request-a', organizationId: 'org-a', facilityId: 'facility-a', leaseId: 'lease-a', leaseUnitId: 'grant-a', type: 'Receipt', state: 'Submitted', version: 'v1', lines: [{ id: 'line-a', itemId: 'item-a', sku: 'BOX', baseUom: 'box', quantity: '100' }] };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('posts only the positive observed partial subset with a frozen fulfillment identity', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ ...request, state: 'Approved', lines: [{ ...request.lines[0], fulfilledQuantity: '40', remainingQuantity: '60' }] })).mockResolvedValueOnce(reply({ token: 'csrf' }))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'request-a', postingId: 'partial-a' } })).mockResolvedValueOnce(reply({ ...request, state: 'Fulfilled' }));
  vi.stubGlobal('fetch', fetchMock); render(<StockRequestPanel access={clerk} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("ID yêu cầu nhập xuất"), 'request-a'); await user.click(screen.getByRole('button', { name: "Tải yêu cầu" })); await screen.findByText("Đã phê duyệt");
  await user.type(screen.getByLabelText("Lý do thao tác kho"), 'Remaining sixty arrived'); await user.click(screen.getByLabelText("Đã kiểm tra thực tế mọi số lượng")); await user.type(screen.getByLabelText("Đã ghi nhận BOX số lượng"), '60'); expect(screen.getByRole('button', { name: "Ghi nhận xuất nhập từng phần" })).toBeDisabled(); await user.click(screen.getByLabelText("Đã kiểm tra thực tế mọi số lượng")); await user.click(screen.getByRole('button', { name: "Ghi nhận xuất nhập từng phần" }));
  await screen.findByText("Đã thực hiện xong"); expect(fetchMock.mock.calls[2][0]).toBe('/api/v1/stock-requests/request-a/fulfillments'); const payload = JSON.parse(fetchMock.mock.calls[2][1].body); expect(payload.fulfillmentId).toMatch(/^[a-f0-9-]{36}$/); expect(payload.lines).toEqual([{ requestLineId: 'line-a', quantity: '60' }]);
});
it('shows scoped request quantities and sends clerk approval using its current version', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(request)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'request-a' } })).mockResolvedValueOnce(reply({ ...request, state: 'Approved' }));
  vi.stubGlobal('fetch', fetchMock); render(<StockRequestPanel access={clerk} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID yêu cầu nhập xuất"), 'request-a'); await user.click(screen.getByRole('button', { name: "Tải yêu cầu" }));
  await screen.findByText("Đã gửi"); await user.type(screen.getByLabelText("Lý do thao tác kho"), 'Assigned clerk approval'); await user.click(screen.getByRole('button', { name: "Phê duyệt yêu cầu" }));
  expect(await screen.findByText("Đã phê duyệt")).toBeInTheDocument(); expect(fetchMock.mock.calls[2][1].headers['If-Match']).toBe('"v1"');
  expect(screen.queryByRole('button', { name: 'Post stock' })).not.toBeInTheDocument();
});
it('requires explicit customer replacement and never silently changes observed quantity', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'item-a', sku: 'BOX', baseUom: 'box' }], total: 1 })).mockResolvedValueOnce(reply({ token: 'csrf' }))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'replacement-a' } })).mockResolvedValueOnce(reply({ ...request, id: 'replacement-a', state: 'Draft', replacesRequestId: 'original-a' }));
  vi.stubGlobal('fetch', fetchMock); render(<StockRequestPanel access={customer} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Tải mặt hàng khách hàng" })); await user.type(screen.getByLabelText("ID hợp đồng của yêu cầu"), 'lease-a'); await user.type(screen.getByLabelText("ID quyền sử dụng khoang của yêu cầu"), 'grant-a');
  await user.selectOptions(screen.getByLabelText("Mặt hàng 1"), 'item-a'); await user.type(screen.getByLabelText("Số lượng 1"), '85'); await user.type(screen.getByLabelText("ID yêu cầu trước đã từ chối (không bắt buộc)"), 'original-a');
  await user.type(screen.getByLabelText("Lý do thao tác kho"), 'Authorize replacement for 85 observed boxes'); await user.click(screen.getByRole('button', { name: "Tạo yêu cầu nhập xuất" }));
  expect(await screen.findByText("Bản nháp")).toBeInTheDocument(); const payload = JSON.parse(fetchMock.mock.calls[2][1].body);
  expect(payload.replacesRequestId).toBe('original-a'); expect(payload.lines).toEqual([{ itemId: 'item-a', quantity: '85' }]);
});
it('keeps an unknown original approval after a failed retry and hides a foreign facility request', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(request)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504))
    .mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 401));
  vi.stubGlobal('fetch', fetchMock); const view = render(<StockRequestPanel access={clerk} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID yêu cầu nhập xuất"), 'request-a'); await user.click(screen.getByRole('button', { name: "Tải yêu cầu" })); await screen.findByText("Đã gửi");
  await user.type(screen.getByLabelText("Lý do thao tác kho"), 'Original approval'); await user.click(screen.getByRole('button', { name: "Phê duyệt yêu cầu" })); await screen.findByText(/Chưa xác định kết quả yêu cầu kho/);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu kho" })); await user.click(await screen.findByRole('button', { name: "Thử lại yêu cầu kho gốc" }));
  expect(await screen.findByText(/Chưa xác định kết quả yêu cầu kho/)).toBeInTheDocument(); expect(fetchMock.mock.calls[5][1].body).toBe(fetchMock.mock.calls[2][1].body);
  view.rerender(<StockRequestPanel access={{ ...clerk, facilityId: 'facility-b' }} />); expect(screen.queryByText("Đã gửi")).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: "Đọc kết quả yêu cầu kho" })).toBeDisabled();
});

it('requires observed quantities and physical confirmation for one complete clerk posting', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ ...request, state: 'Approved' })).mockResolvedValueOnce(reply({ token: 'csrf' }))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'request-a', postingId: 'posting-a' } })).mockResolvedValueOnce(reply({ ...request, state: 'Fulfilled' }));
  vi.stubGlobal('fetch', fetchMock); render(<StockRequestPanel access={clerk} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID yêu cầu nhập xuất"), 'request-a'); await user.click(screen.getByRole('button', { name: "Tải yêu cầu" })); await screen.findByText("Đã phê duyệt");
  const button = screen.getByRole('button', { name: "Ghi nhận xuất nhập toàn bộ" }); expect(button).toBeDisabled();
  await user.type(screen.getByLabelText("Lý do thao tác kho"), 'Received exactly 100 boxes'); await user.type(screen.getByLabelText("Đã ghi nhận BOX số lượng"), '100');
  expect(button).toBeDisabled(); await user.click(screen.getByLabelText("Đã kiểm tra thực tế mọi số lượng")); await user.click(button);
  expect(await screen.findByText("Đã thực hiện xong")).toBeInTheDocument(); expect(JSON.parse(fetchMock.mock.calls[2][1].body).lines).toEqual([{ requestLineId: 'line-a', quantity: '100' }]);
});

it('clears foreign draft lease and item identifiers when organization changes without erasing an unknown command', async () => {
  const view = render(<StockRequestPanel access={customer} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("ID hợp đồng của yêu cầu"), 'private-lease-a'); await user.type(screen.getByLabelText("ID quyền sử dụng khoang của yêu cầu"), 'private-grant-a');
  view.rerender(<StockRequestPanel access={{ ...customer, organizationId: 'org-b' }} />); expect(screen.getByLabelText("ID hợp đồng của yêu cầu")).toHaveValue(''); expect(screen.getByLabelText("ID quyền sử dụng khoang của yêu cầu")).toHaveValue('');
});

it('composes lines across catalog pages and removes a draft line without changing any saved request', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ id: 'item-a', sku: 'A', baseUom: 'box' }], total: 51 })).mockResolvedValueOnce(reply({ items: [{ id: 'item-b', sku: 'B', baseUom: 'box' }], total: 51 })).mockResolvedValueOnce(reply({}, 503));
  vi.stubGlobal('fetch', fetchMock); render(<StockRequestPanel access={customer} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải mặt hàng khách hàng" })); await user.selectOptions(await screen.findByLabelText("Mặt hàng 1"), 'item-a');
  await user.click(screen.getByRole('button', { name: "Thêm dòng mặt hàng" })); await user.click(screen.getByRole('button', { name: "Trang mặt hàng sau" })); await user.selectOptions(await screen.findByLabelText("Mặt hàng 2"), 'item-b'); expect(screen.getByLabelText("Mặt hàng 1")).toHaveValue('item-a');
  await user.click(screen.getByRole('button', { name: "Xóa dòng 2" })); expect(screen.queryByLabelText("Mặt hàng 2")).not.toBeInTheDocument(); await user.click(screen.getByRole('button', { name: "Trang mặt hàng trước" })); expect(await screen.findByText(/Không tải được trang mặt hàng khách hàng/)).toBeInTheDocument();
});

it('announces an explicit lookup failure, focuses its associated feedback and clears it for a new ID', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({}, 403)));
  render(<StockRequestPanel access={customer} />); const user = userEvent.setup();
  const lookup = screen.getByLabelText('ID yêu cầu nhập xuất');
  await user.type(lookup, 'request-a'); await user.click(screen.getByRole('button', { name: 'Tải yêu cầu' }));
  const alert = await screen.findByRole('alert');
  expect(alert).toHaveTextContent('Chưa xác minh được dữ liệu kho'); expect(alert).toHaveFocus();
  expect(lookup).toHaveAttribute('aria-invalid', 'true'); expect(lookup).toHaveAccessibleDescription(alert.textContent!);
  await user.type(lookup, '-new'); expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(lookup).not.toHaveAttribute('aria-invalid'); expect(lookup).not.toHaveAttribute('aria-describedby');
});

it('explains invalid draft quantities in Vietnamese without sending a command', async () => {
  const fetchMock = vi.fn().mockResolvedValue(reply({ items: [{ id: 'item-a', sku: 'BOX', baseUom: 'box' }], total: 1 }));
  vi.stubGlobal('fetch', fetchMock); render(<StockRequestPanel access={customer} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải mặt hàng khách hàng' }));
  await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Customer instruction');
  await user.type(screen.getByLabelText('ID hợp đồng của yêu cầu'), 'lease-a');
  await user.type(screen.getByLabelText('ID quyền sử dụng khoang của yêu cầu'), 'grant-a');
  await user.selectOptions(screen.getByLabelText('Mặt hàng 1'), 'item-a');
  const quantity = screen.getByLabelText('Số lượng 1'); await user.type(quantity, '1,5');
  expect(quantity).toHaveAccessibleDescription(/dấu chấm.*3 chữ số/);
  await user.click(screen.getByRole('button', { name: 'Tạo yêu cầu nhập xuất' }));
  expect(await screen.findByRole('alert')).toHaveTextContent(/số lượng.*dấu chấm/i);
  expect(screen.getByRole('alert')).toHaveFocus(); expect(fetchMock.mock.calls).toHaveLength(1);
});

it('does not move focus or expose stale lookup feedback after changing notice intent', async () => {
  let reject!: (error: Error) => void;
  vi.stubGlobal('fetch', vi.fn(() => new Promise((_resolve, fail) => { reject = fail; })));
  const view = render(<StockRequestPanel access={customer} selectedId="request-a" noticeKey="notice-a" />);
  const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: 'Tải yêu cầu' }));
  view.rerender(<StockRequestPanel access={customer} selectedId="request-b" noticeKey="notice-b" />);
  const lookup = screen.getByLabelText('ID yêu cầu nhập xuất'); fireEvent.focus(lookup); lookup.focus();
  reject(new Error('late failure')); await screen.findByText(/Thông báo là lịch sử/);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(lookup).toHaveFocus(); expect(lookup).toHaveValue('request-b');
});

it('does not revive invalid draft feedback after leaving and returning to the original scope', async () => {
  const view = render(<StockRequestPanel access={customer} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Fixture reason');
  await user.click(screen.getByRole('button', { name: 'Tạo mặt hàng khách hàng' }));
  expect(await screen.findByRole('alert')).toHaveFocus();
  view.rerender(<StockRequestPanel access={{ ...customer, organizationId: 'org-b' }} />);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  const reason = screen.getByLabelText('Lý do thao tác kho'); reason.focus();
  view.rerender(<StockRequestPanel access={customer} />);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(reason).toHaveFocus();
  expect(reason).toHaveValue('');
});

it('does not revive rejected item feedback when a notice is opened and then cleared', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 409)));
  const view = render(<StockRequestPanel access={customer} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Fixture reason');
  await user.type(screen.getByLabelText('Mã SKU mặt hàng'), 'BOX');
  await user.type(screen.getByLabelText('Mô tả mặt hàng'), 'Fixture boxes');
  await user.type(screen.getByLabelText('Đơn vị tính cơ sở'), 'box');
  await user.click(screen.getByRole('button', { name: 'Tạo mặt hàng khách hàng' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Yêu cầu kho bị từ chối');
  view.rerender(<StockRequestPanel access={customer} selectedId="request-a" noticeKey="notice-a" />);
  const lookup = screen.getByLabelText('ID yêu cầu nhập xuất'); lookup.focus();
  view.rerender(<StockRequestPanel access={customer} />);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(lookup).toHaveFocus();
});

it.each(['request-a ', 'REQUEST-A'])('invalidates a pending lookup failure after explicit normalized-equivalent edit to %s', async edited => {
  let reject!: (error: Error) => void;
  vi.stubGlobal('fetch', vi.fn(() => new Promise((_resolve, fail) => { reject = fail; })));
  render(<StockRequestPanel access={customer} selectedId="request-a" />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải yêu cầu' }));
  const lookup = screen.getByLabelText('ID yêu cầu nhập xuất');
  fireEvent.change(lookup, { target: { value: edited } }); lookup.focus();
  await act(async () => { reject(new Error('late lookup failure')); });
  expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(lookup).toHaveFocus();
  expect(lookup).not.toHaveAttribute('aria-invalid'); expect(lookup).toHaveValue(edited);
});

it.each([false, true])('retains a successful new request receipt after draft edits while the command is pending (read failure: %s)', async failedRead => {
  let complete!: (response: ReturnType<typeof reply>) => void;
  const fetchMock = vi.fn(async (url: string) => {
    if (url.includes('/items?')) return reply({ items: [{ id: 'item-a', sku: 'BOX', baseUom: 'box' }], total: 1 });
    if (url.endsWith('/auth/csrf')) return reply({ token: 'csrf' });
    if (url.endsWith('/stock-requests')) return new Promise<ReturnType<typeof reply>>(resolve => { complete = resolve; });
    if (url.endsWith('/stock-requests/request-created')) return failedRead ? reply({}, 503) : reply({ ...request, id: 'request-created', state: 'Draft' });
    throw Error('Unexpected fixture route: ' + url);
  });
  vi.stubGlobal('fetch', fetchMock); const view = render(<StockRequestPanel access={customer} />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải mặt hàng khách hàng' }));
  await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Original customer instruction');
  await user.type(screen.getByLabelText('ID hợp đồng của yêu cầu'), 'lease-a');
  await user.type(screen.getByLabelText('ID quyền sử dụng khoang của yêu cầu'), 'grant-a');
  await user.selectOptions(screen.getByLabelText('Mặt hàng 1'), 'item-a'); await user.type(screen.getByLabelText('Số lượng 1'), '10');
  await user.click(screen.getByRole('button', { name: 'Tạo yêu cầu nhập xuất' }));
  await user.type(screen.getByLabelText('Lý do thao tác kho'), ' edited'); await user.clear(screen.getByLabelText('Số lượng 1'));
  await user.type(screen.getByLabelText('Số lượng 1'), '12');
  await act(async () => { complete(reply({ result: { resourceId: 'request-created' } })); });
  const receipt = screen.getByLabelText('Yêu cầu kho đã lưu');
  expect(receipt).toHaveTextContent('request-created'); expect(screen.queryByRole('button', { name: 'Đọc kết quả yêu cầu kho' })).not.toBeInTheDocument();
  if (!failedRead) expect(screen.getByText('Bản nháp')).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Xem mã yêu cầu kho đã lưu' }));
  expect(screen.getByLabelText('ID yêu cầu nhập xuất')).toHaveValue('request-created');
  const post = fetchMock.mock.calls.filter(([url]) => url.endsWith('/stock-requests'));
  expect(post).toHaveLength(1);
  view.rerender(<StockRequestPanel access={{ ...customer, organizationId: 'org-b' }} />);
  expect(screen.queryByLabelText('Yêu cầu kho đã lưu')).not.toBeInTheDocument();
});

it('retains a known request receipt without replacing a newer notice selection', async () => {
  let complete!: (response: ReturnType<typeof reply>) => void;
  const fetchMock = vi.fn(async (url: string) => {
    if (url.endsWith('/stock-requests/request-a')) return reply({ ...request, state: 'Draft' });
    if (url.endsWith('/auth/csrf')) return reply({ token: 'csrf' });
    if (url.endsWith('/submit')) return new Promise<ReturnType<typeof reply>>(resolve => { complete = resolve; });
    throw Error('Unexpected fixture route: ' + url);
  });
  vi.stubGlobal('fetch', fetchMock); const view = render(<StockRequestPanel access={customer} selectedId="request-a" noticeKey="notice-a" />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải yêu cầu' })); await screen.findByText('Bản nháp');
  await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Original submission'); await user.click(screen.getByRole('button', { name: 'Gửi yêu cầu' }));
  view.rerender(<StockRequestPanel access={customer} selectedId="request-b" noticeKey="notice-b" />);
  const lookup = screen.getByLabelText('ID yêu cầu nhập xuất'); lookup.focus();
  await act(async () => { complete(reply({ result: { resourceId: 'request-a' } })); });
  expect(screen.getByLabelText('Yêu cầu kho đã lưu')).toHaveTextContent('request-a');
  expect(lookup).toHaveValue('request-b'); expect(lookup).toHaveFocus();
  expect(screen.getByRole('status')).toHaveTextContent('Thông báo là lịch sử');
  expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/submit'))).toHaveLength(1);
});

it.each([
  ['0', false], ['0.000', false], ['1,5', false], ['-1', false], ['0.0001', false],
  ['0.001', true], ['1', true], ['1.5', true], ['001.500', true], ['000.001', true],
] as const)('native draft quantity %s has positive three-decimal validity %s', (value, valid) => {
  render(<StockRequestPanel access={customer} />); const quantity = screen.getByLabelText('Số lượng 1') as HTMLInputElement;
  fireEvent.change(quantity, { target: { value } });
  expect(quantity.validity.valid).toBe(valid);
  expect(quantity).toHaveAccessibleDescription(/lớn hơn 0.*dấu chấm.*3 chữ số/);
});
