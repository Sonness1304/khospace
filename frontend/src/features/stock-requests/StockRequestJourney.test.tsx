import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import StockRequestPanel from './StockRequestPanel';

const customer = { userId: 'customer', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: 'facility-a', facilityRoles: [] as string[], operatorAdmin: false };
const clerk = { ...customer, userId: 'clerk', organizationId: '', facilityRoles: ['Clerk'] };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());

// Stateful mock HTTP is a UI fixture, not an implementation of server authorization or stock rules.
function fixture(initialState = 'Submitted', postingStatus = 200) {
  let state = initialState; let revision = 1; let fulfilled = '0'; let type = 'Receipt';
  const detail = () => ({ id: 'request-a', organizationId: 'org-a', facilityId: 'facility-a', leaseId: 'lease-a', leaseUnitId: 'grant-a',
    type, state, version: `v${revision}`, lines: [{ id: 'line-a', itemId: 'item-a', sku: 'BOX', baseUom: 'box', quantity: '10', fulfilledQuantity: fulfilled, remainingQuantity: String(10 - Number(fulfilled)) }] });
  const requests = vi.fn(async (url: string, options: RequestInit = {}) => {
    if (url.endsWith('/auth/csrf')) return reply({ token: 'fixture-csrf' });
    if (url.endsWith('/items') && options.method === 'POST') return reply({ result: { resourceId: 'item-a' } });
    if (url.includes('/items?')) return reply({ items: [{ id: 'item-a', sku: 'BOX', baseUom: 'box' }], total: 1 });
    if (url.endsWith('/stock-requests') && options.method === 'POST') {
      type = JSON.parse(options.body as string).type; state = 'Draft'; return reply({ result: { resourceId: 'request-a' } });
    }
    if (url.includes('/commands/')) return reply({ result: { resourceId: 'request-a' } });
    if (url.endsWith('/stock-requests/request-a')) return reply(detail());
    if (options.method === 'POST' && /\/stock-requests\/request-a\/(submit|approve|post|fulfillments)$/.test(url)) {
      if (url.endsWith('/submit')) state = 'Submitted';
      else if (url.endsWith('/approve')) state = 'Approved';
      else {
        if (postingStatus === 409) return reply({}, 409);
        fulfilled = url.endsWith('/post') ? '10' : String(Number(fulfilled) + Number(JSON.parse(options.body as string).lines[0].quantity));
        state = fulfilled === '10' ? 'Fulfilled' : 'Approved';
      }
      revision++; return reply({ result: { resourceId: 'request-a' } }, /\/(post|fulfillments)$/.test(url) ? postingStatus : 200);
    }
    throw Error('Unexpected fixture route: ' + url);
  });
  vi.stubGlobal('fetch', requests);
  return { requests, posts: () => requests.mock.calls.filter(([, options]) => options?.method === 'POST') };
}
async function loadSubmitted() {
  const user = userEvent.setup(); const view = render(<StockRequestPanel access={clerk} />);
  await user.type(screen.getByLabelText('ID yêu cầu nhập xuất'), 'request-a');
  await user.click(screen.getByRole('button', { name: 'Tải yêu cầu' })); await screen.findByText('Đã gửi');
  await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Synthetic warehouse observation');
  await user.click(screen.getByRole('button', { name: 'Phê duyệt yêu cầu' })); await screen.findByText('Đã phê duyệt');
  expect(screen.getByLabelText('Đã ghi nhận BOX số lượng')).toHaveValue('');
  expect(screen.getByRole('button', { name: 'Ghi nhận xuất nhập toàn bộ' })).toBeDisabled();
  return { user, view };
}
async function observe(user: ReturnType<typeof userEvent.setup>, quantity: string) {
  await user.type(screen.getByLabelText('Đã ghi nhận BOX số lượng'), quantity);
  await user.click(screen.getByLabelText('Đã kiểm tra thực tế mọi số lượng'));
}
function assertHeaders(posts: ReturnType<ReturnType<typeof fixture>['posts']>) {
  for (const [, options] of posts) expect(options?.headers).toMatchObject({ 'X-CSRF-Token': 'fixture-csrf', 'Idempotency-Key': expect.any(String) });
}

it.each(['Receipt', 'Issue'])('fixture customer item -> %s draft -> explicit submit never posts stock', async type => {
  const flow = fixture('Draft'); const user = userEvent.setup(); render(<StockRequestPanel access={customer} />);
  await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Synthetic customer instruction');
  await user.type(screen.getByLabelText('Mã SKU mặt hàng'), 'BOX'); await user.type(screen.getByLabelText('Mô tả mặt hàng'), 'Fixture boxes');
  await user.type(screen.getByLabelText('Đơn vị tính cơ sở'), 'box'); await user.click(screen.getByRole('button', { name: 'Tạo mặt hàng khách hàng' }));
  await screen.findByText(/ID mặt hàng: item-a. Dùng ID/);
  await user.type(screen.getByLabelText('ID hợp đồng của yêu cầu'), 'lease-a'); await user.type(screen.getByLabelText('ID quyền sử dụng khoang của yêu cầu'), 'grant-a');
  await user.selectOptions(screen.getByLabelText('Loại xuất nhập'), type); await user.selectOptions(screen.getByLabelText('Mặt hàng 1'), 'item-a');
  await user.type(screen.getByLabelText('Số lượng 1'), '10'); await user.click(screen.getByRole('button', { name: 'Tạo yêu cầu nhập xuất' }));
  await screen.findByText('Bản nháp'); expect(flow.posts()).toHaveLength(2);
  expect(screen.queryByRole('button', { name: 'Phê duyệt yêu cầu' })).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Gửi yêu cầu' })); await screen.findByText('Đã gửi');
  const posts = flow.posts(); expect(posts.map(([url]) => url)).toEqual(['/api/v1/items', '/api/v1/stock-requests', '/api/v1/stock-requests/request-a/submit']);
  expect(JSON.parse(posts[1][1]!.body as string)).toEqual({ organizationId: 'org-a', leaseId: 'lease-a', leaseUnitId: 'grant-a', type, lines: [{ itemId: 'item-a', quantity: '10' }], reason: 'Synthetic customer instruction', replacesRequestId: null });
  expect(posts[2][1]?.headers).toMatchObject({ 'If-Match': '"v1"' }); assertHeaders(posts);
});

it('fixture clerk approve -> explicit complete post uses refreshed version and never auto-posts', async () => {
  const flow = fixture(); const { user } = await loadSubmitted(); expect(flow.posts()).toHaveLength(1);
  await observe(user, '10'); await user.click(screen.getByRole('button', { name: 'Ghi nhận xuất nhập toàn bộ' })); await screen.findByText('Đã thực hiện xong');
  const posts = flow.posts(); expect(posts.map(([url]) => url)).toEqual(['/api/v1/stock-requests/request-a/approve', '/api/v1/stock-requests/request-a/post']);
  expect(posts[1][1]?.headers).toMatchObject({ 'If-Match': '"v2"' }); assertHeaders(posts);
  expect(JSON.parse(posts[1][1]!.body as string).lines).toEqual([{ requestLineId: 'line-a', quantity: '10' }]);
  expect(screen.queryByRole('button', { name: 'Ghi nhận xuất nhập toàn bộ' })).not.toBeInTheDocument();
});

it('fixture clerk partial posting refreshes remaining quantity and needs new physical confirmation', async () => {
  const flow = fixture(); const { user } = await loadSubmitted(); await observe(user, '4');
  await user.click(screen.getByRole('button', { name: 'Ghi nhận xuất nhập từng phần' })); await screen.findByText('BOX đã thực hiện: 4 · Còn lại: 6');
  expect(screen.getByLabelText('Đã kiểm tra thực tế mọi số lượng')).not.toBeChecked();
  expect(screen.getByLabelText('Đã ghi nhận BOX số lượng')).toHaveValue('');
  expect(screen.getByRole('button', { name: 'Ghi nhận xuất nhập toàn bộ' })).toBeDisabled();
  await observe(user, '7'); expect(screen.getByRole('button', { name: 'Ghi nhận xuất nhập từng phần' })).toBeDisabled();
  await user.clear(screen.getByLabelText('Đã ghi nhận BOX số lượng')); await observe(user, '6');
  await user.click(screen.getByRole('button', { name: 'Ghi nhận xuất nhập từng phần' })); await screen.findByText('Đã thực hiện xong');
  const posts = flow.posts(); expect(posts.map(([url]) => url)).toEqual(['/api/v1/stock-requests/request-a/approve', '/api/v1/stock-requests/request-a/fulfillments', '/api/v1/stock-requests/request-a/fulfillments']);
  const payloads = posts.slice(1).map(([, options]) => JSON.parse(options!.body as string));
  expect(payloads.map(x => x.lines)).toEqual([[{ requestLineId: 'line-a', quantity: '4' }], [{ requestLineId: 'line-a', quantity: '6' }]]);
  expect(payloads[0].fulfillmentId).not.toBe(payloads[1].fulfillmentId); expect(posts[2][1]?.headers).toMatchObject({ 'If-Match': '"v3"' }); assertHeaders(posts);
});

it('fixture rejected posting focuses failure without claiming fulfillment or auto retry', async () => {
  const flow = fixture('Submitted', 409); const { user } = await loadSubmitted(); await observe(user, '10');
  await user.click(screen.getByRole('button', { name: 'Ghi nhận xuất nhập toàn bộ' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Ghi kho bị từ chối'); expect(screen.getByRole('alert')).toHaveFocus();
  expect(screen.getByText('Đã phê duyệt')).toBeInTheDocument(); expect(screen.queryByText('Đã thực hiện xong')).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Đọc kết quả yêu cầu kho' })).not.toBeInTheDocument(); expect(flow.posts()).toHaveLength(2);
});

it('fixture unknown posting stays blocked until readback and foreign scope cannot resolve it', async () => {
  const flow = fixture('Submitted', 504); const { user, view } = await loadSubmitted(); await observe(user, '10');
  await user.click(screen.getByRole('button', { name: 'Ghi nhận xuất nhập toàn bộ' })); await screen.findByText(/Chưa xác định kết quả yêu cầu kho/);
  expect(screen.getByRole('button', { name: 'Ghi nhận xuất nhập toàn bộ' })).toBeDisabled();
  view.rerender(<StockRequestPanel access={{ ...clerk, facilityId: 'foreign-facility' }} />);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(screen.queryByText('Đã phê duyệt')).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Đọc kết quả yêu cầu kho' })).toBeDisabled();
  view.rerender(<StockRequestPanel access={clerk} />);
  await user.click(screen.getByRole('button', { name: 'Đọc kết quả yêu cầu kho' }));
  // Returning to the original scope does not auto-select its old request.
  await user.type(screen.getByLabelText('ID yêu cầu nhập xuất'), 'request-a'); await user.click(screen.getByRole('button', { name: 'Tải yêu cầu' }));
  await screen.findByText('Đã thực hiện xong'); expect(flow.posts()).toHaveLength(2);
  expect(flow.requests.mock.calls.find(([url]) => url.includes('/commands/'))?.[0]).toContain('scopeId=org-a');
});
