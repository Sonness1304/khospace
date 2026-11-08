import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import FacilitiesPanel from './FacilitiesPanel';
const facilities = { items: [{ id: 'facility-a', name: 'Facility A', address: 'Synthetic A', permittedGoods: 'Dry goods' }, { id: 'facility-b', name: 'Facility B', address: 'Synthetic B', permittedGoods: 'Dry goods' }], nextCursor: null };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());

it('lets visitors search dated units without displaying manager commands', async () => {
  const calls = vi.fn().mockResolvedValueOnce(reply(facilities)).mockResolvedValueOnce(reply({ items: [{ id: 'unit-a', code: 'A-01', areaM2: '10.5', dailyRate: '101', currency: 'VND' }], nextCursor: null, reservation: false }));
  vi.stubGlobal('fetch', calls); const user = userEvent.setup(); render(<FacilitiesPanel access={null} />);
  await user.selectOptions(await screen.findByLabelText("Cơ sở kho"), 'facility-a');
  await user.click(screen.getByRole('button', { name: "Tìm khoang còn trống" }));
  expect(await screen.findByText('A-01')).toBeInTheDocument();
  expect(screen.getByText(/Tình trạng còn trống chỉ để tham khảo/)).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: "Chặn lịch bảo trì" })).not.toBeInTheDocument();
  expect(calls.mock.calls[1][0]).toContain('facilityId=facility-a');
});

it('quotes only explicitly selected search units for the current customer organization', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(facilities)).mockResolvedValueOnce(reply({ items: [{ id: 'unit-a', code: 'A-01', areaM2: '10.5', dailyRate: '101', currency: 'VND' }], nextCursor: null }))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 422));
  vi.stubGlobal('fetch', fetchMock); const user = userEvent.setup();
  render(<FacilitiesPanel access={{ userId: 'customer-a', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: '', facilityRoles: [], operatorAdmin: false }} />);
  await user.selectOptions(await screen.findByLabelText("Cơ sở kho"), 'facility-a');
  await user.click(screen.getByRole('button', { name: "Tìm khoang còn trống" }));
  await user.click(await screen.findByRole('checkbox', { name: 'A-01' })); await user.click(screen.getByRole('checkbox', { name: 'A-01' }));
  expect(screen.getByRole('button', { name: "Nhận báo giá chính xác" })).toBeDisabled();
  await user.click(screen.getByRole('checkbox', { name: 'A-01' }));
  expect(screen.getByText('Đã chọn 1 khoang · 10,5 m²')).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: "Nhận báo giá chính xác" }));
  expect(await screen.findByText(/Yêu cầu báo giá bị từ chối/)).toBeInTheDocument();
  expect(JSON.parse(fetchMock.mock.calls[3][1].body)).toMatchObject({ organizationId: 'org-a', facilityId: 'facility-a', unitIds: ['unit-a'] });
});

it('ignores a stale facility search response after switching facilities', async () => {
  let finish: (value: unknown) => void = () => {};
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(facilities)).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })));
  const user = userEvent.setup(); render(<FacilitiesPanel access={null} />);
  await user.selectOptions(await screen.findByLabelText("Cơ sở kho"), 'facility-a');
  await user.click(screen.getByRole('button', { name: "Tìm khoang còn trống" }));
  await user.selectOptions(screen.getByLabelText("Cơ sở kho"), 'facility-b');
  finish(reply({ items: [{ id: 'old-unit', code: 'FOREIGN-OLD', areaM2: '20', dailyRate: '100', currency: 'VND' }], nextCursor: null }));
  await waitFor(() => expect(screen.queryByText('FOREIGN-OLD')).not.toBeInTheDocument());
});

it('preserves user dates after a rejected search and shows a safe error', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(facilities)).mockResolvedValueOnce(reply({ code: 'INVALID_DATES', detail: 'Internal SQL sentinel' }, 422)));
  const user = userEvent.setup(); render(<FacilitiesPanel access={null} />);
  await user.selectOptions(await screen.findByLabelText("Cơ sở kho"), 'facility-a');
  const date = screen.getByLabelText("Ngày bắt đầu"); await user.clear(date); await user.type(date, '2028-01-31');
  const end = screen.getByLabelText("Ngày kết thúc (không tính)"); await user.clear(end); await user.type(end, '2028-02-02');
  await user.click(screen.getByRole('button', { name: "Tìm khoang còn trống" }));
  expect(await screen.findByText(/Chưa hoàn tất tìm kiếm/)).toBeInTheDocument();
  expect(date).toHaveValue('2028-01-31'); expect(screen.queryByText("Nội bộ SQL sentinel")).not.toBeInTheDocument();
});

const access = { userId: 'manager', organizationId: '', facilityId: 'facility-a', facilityRoles: ['FacilityManager'], operatorAdmin: false };
const catalog = { facility: facilities.items[0], zones: [{ id: 'zone-a', code: 'DRY' }, { id: 'zone-b', code: 'DRY-2' }],
  units: [{ id: 'unit-a', code: 'A-01', areaM2: '10.5', zoneId: 'zone-a', published: true, version: 'version-one' },
    { id: 'unit-b', code: 'A-02', areaM2: '20', zoneId: 'zone-a', published: false, version: 'version-two' }] };
function managerFetch(writeReply: () => Promise<unknown> = async () => reply({ status: 'Completed' })) {
  return vi.fn().mockImplementation((url: string, options: RequestInit) => {
    if (url === '/api/v1/public/facilities') return Promise.resolve(reply(facilities));
    if (url.includes('/catalog')) return Promise.resolve(reply(catalog));
    if (url.endsWith('/auth/csrf')) return Promise.resolve(reply({ token: 'csrf' }));
    if (options.method === 'POST' || options.method === 'PATCH') return writeReply();
    return Promise.resolve(reply({}, 404));
  });
}
async function openManager() {
  const user = userEvent.setup();
  await user.selectOptions(await screen.findByLabelText("Cơ sở kho"), 'facility-a');
  await user.click(screen.getByText("Quản lý cơ sở đã chọn"));
  await screen.findByRole('button', { name: "Chỉnh sửa A-01" });
  return user;
}

it('requires retirement confirmation and submits the selected version with a permanent reason', async () => {
  const fetchMock = managerFetch(); vi.stubGlobal('fetch', fetchMock); render(<FacilitiesPanel access={access} />); const user = await openManager();
  await user.click(screen.getByRole('button', { name: "Chỉnh sửa A-01" }));
  expect(screen.getByRole('button', { name: "Ngừng sử dụng khoang đã chọn" })).toBeDisabled();
  await user.type(screen.getByLabelText("Lý do"), 'Permanently withdraw empty unit');
  await user.click(screen.getByLabelText("Tôi xác nhận ngừng sử dụng vĩnh viễn khoang trống đã chọn"));
  await user.click(screen.getByRole('button', { name: "Ngừng sử dụng khoang đã chọn" })); await screen.findByText(/Đã lưu yêu cầu/);
  const writes = fetchMock.mock.calls.filter(([url]) => url.endsWith('/retire')); expect(writes).toHaveLength(1);
  expect(writes[0][0]).toBe('/api/v1/units/unit-a/retire'); expect(writes[0][1].headers['If-Match']).toBe('"version-one"');
  expect(JSON.parse(writes[0][1].body)).toEqual({ reason: 'Permanently withdraw empty unit', retirementConfirmed: true });
});

it('retains retired history while disabling reuse controls', async () => {
  const fetchMock = managerFetch(); fetchMock.mockImplementation((url: string) => Promise.resolve(reply(url.includes('/catalog') ? { ...catalog, units: [{ ...catalog.units[0], retiredAt: '2026-10-28T10:00:00Z', retirementReason: 'Withdrawn' }] } : facilities)));
  vi.stubGlobal('fetch', fetchMock); render(<FacilitiesPanel access={access} />); const user = await openManager();
  await user.click(screen.getByRole('button', { name: "Chỉnh sửa A-01" })); await user.type(screen.getByLabelText("Lý do"), 'Cannot republish');
  expect(screen.getByText(/Đã ngừng sử dụng vĩnh viễn/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: "Cập nhật khoang đã chọn" })).toBeDisabled();
  expect(screen.getByRole('button', { name: "Ngừng sử dụng khoang đã chọn" })).toBeDisabled();
});

it('creates a unit using exact decimal strings, fresh CSRF and one command identity', async () => {
  const fetchMock = managerFetch(); vi.stubGlobal('fetch', fetchMock); render(<FacilitiesPanel access={access} />);
  const user = await openManager();
  await user.type(screen.getByLabelText("Mã khoang"), 'A-NEW');
  await user.type(screen.getByLabelText("Diện tích (m²)"), '12.25');
  await user.selectOptions(screen.getByLabelText("Khu vực"), 'zone-b');
  await user.click(screen.getByLabelText("Công khai khoang đủ điều kiện"));
  await user.type(screen.getByLabelText("Lý do"), 'Create synthetic unit');
  await user.click(screen.getByRole('button', { name: "Tạo khoang" }));
  expect(await screen.findByText(/Đã lưu yêu cầu/)).toBeInTheDocument();
  const writes = fetchMock.mock.calls.filter(([, options]) => options.method === 'POST'); expect(writes).toHaveLength(1);
  expect(JSON.parse(writes[0][1].body)).toEqual({ code: 'A-NEW', areaM2: '12.25', zoneId: 'zone-b', published: true, reason: 'Create synthetic unit' });
  expect(writes[0][1].headers['X-CSRF-Token']).toBe('csrf'); expect(writes[0][1].headers['Idempotency-Key']).toMatch(/^[\da-f-]{36}$/);
});

it('keeps a rejected edit draft and sends its version without automatic mutation retry', async () => {
  const fetchMock = managerFetch(async () => reply({}, 412)); vi.stubGlobal('fetch', fetchMock); render(<FacilitiesPanel access={access} />);
  const user = await openManager(); await user.click(screen.getByRole('button', { name: "Chỉnh sửa A-01" }));
  await user.clear(screen.getByLabelText("Diện tích (m²)")); await user.type(screen.getByLabelText("Diện tích (m²)"), '15');
  await user.type(screen.getByLabelText("Lý do"), 'Keep draft on stale edit');
  await user.click(screen.getByRole('button', { name: "Cập nhật khoang đã chọn" }));
  expect(await screen.findByText(/Yêu cầu bị từ chối/)).toBeInTheDocument(); expect(screen.getByLabelText("Diện tích (m²)")).toHaveValue(15);
  const writes = fetchMock.mock.calls.filter(([, options]) => options.method === 'PATCH'); expect(writes).toHaveLength(1);
  expect(writes[0][1].headers['If-Match']).toBe('"version-one"');
});

it('sets a dated rate using the selected resource version and exact input', async () => {
  const fetchMock = managerFetch(); vi.stubGlobal('fetch', fetchMock); render(<FacilitiesPanel access={access} />);
  const user = await openManager(); await user.click(screen.getByRole('button', { name: "Chỉnh sửa A-01" }));
  await user.type(screen.getByLabelText("Đơn giá ngày (VND/m²)"), '101.0001'); await user.type(screen.getByLabelText("Lý do"), 'New dated rate');
  await user.click(screen.getByRole('button', { name: "Đặt đơn giá theo thời gian" }));
  expect(await screen.findByText(/Đã lưu yêu cầu/)).toBeInTheDocument();
  const [, options] = fetchMock.mock.calls.find(([url]) => url.endsWith('/unit-a/rates'))!;
  expect(JSON.parse(options.body).dailyRate).toBe('101.0001'); expect(options.headers['If-Match']).toBe('"version-one"');
});

it('keeps an unknown maintenance identity, blocks another mutation, and reads back without replay', async () => {
  const fetchMock = managerFetch(async () => { throw new TypeError('reply lost'); });
  let reads = 0;
  const base = fetchMock.getMockImplementation()!;
  fetchMock.mockImplementation((url, options) => url.includes('/commands/') ? Promise.resolve(reply({ status: 'Completed' }, ++reads === 1 ? 404 : 200)) : base(url, options));
  vi.stubGlobal('fetch', fetchMock); render(<FacilitiesPanel access={access} />); const user = await openManager();
  await user.click(screen.getByRole('checkbox', { name: "A-01 · Công khai" }));
  await user.click(screen.getByRole('checkbox', { name: "A-02 · Ẩn" }));
  await user.click(screen.getByRole('checkbox', { name: "A-02 · Ẩn" }));
  await user.type(screen.getByLabelText("Lý do"), 'Planned maintenance');
  await user.click(screen.getByRole('button', { name: "Chặn lịch bảo trì" }));
  expect(await screen.findByText(/Chưa xác định kết quả yêu cầu/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: "Chặn lịch bảo trì" })).toBeDisabled();
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu" }));
  expect(await screen.findByText(/Chưa xác minh được kết quả đã lưu/)).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu" }));
  expect(await screen.findByText("Đã xác minh yêu cầu đã lưu.")).toBeInTheDocument();
  expect(fetchMock.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(1);
  const commandKey = fetchMock.mock.calls.find(([, options]) => options.method === 'POST')![1].headers['Idempotency-Key'];
  expect(fetchMock.mock.calls.filter(([url]) => url.includes(`/commands/${commandKey}?scopeId=facility-a`))).toHaveLength(2);
});

it.each([500, 502, 504])('retains a mutation identity on HTTP%s because commit outcome can be unknown', async status => {
  vi.stubGlobal('fetch', managerFetch(async () => reply({}, status))); render(<FacilitiesPanel access={access} />);
  const user = await openManager(); await user.click(screen.getByRole('checkbox', { name: "A-01 · Công khai" }));
  await user.type(screen.getByLabelText("Lý do"), 'Unknown upstream reply');
  await user.click(screen.getByRole('button', { name: "Chặn lịch bảo trì" }));
  expect(await screen.findByText(/Chưa xác định kết quả yêu cầu/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: "Đọc kết quả yêu cầu" })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: "Chặn lịch bảo trì" })).toBeDisabled();
});

it('retries only the original frozen payload and command identity after readback finds no receipt', async () => {
  let writes = 0;
  const fetchMock = managerFetch(async () => reply({}, ++writes === 1 ? 504 : 200));
  vi.stubGlobal('fetch', fetchMock); render(<FacilitiesPanel access={access} />); const user = await openManager();
  await user.click(screen.getByRole('checkbox', { name: "A-01 · Công khai" })); await user.type(screen.getByLabelText("Lý do"), 'Frozen original');
  await user.click(screen.getByRole('button', { name: "Chặn lịch bảo trì" }));
  await screen.findByText(/Chưa xác định kết quả yêu cầu/); await user.clear(screen.getByLabelText("Lý do")); await user.type(screen.getByLabelText("Lý do"), 'Changed draft');
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu" }));
  await user.click(await screen.findByRole('button', { name: "Thử lại yêu cầu gốc" }));
  expect(await screen.findByText(/Đã lưu yêu cầu/)).toBeInTheDocument();
  const posts = fetchMock.mock.calls.filter(([, options]) => options.method === 'POST'); expect(posts).toHaveLength(2);
  expect(posts[0][1].headers['Idempotency-Key']).toBe(posts[1][1].headers['Idempotency-Key']);
  expect(posts[1][1].body).toBe(posts[0][1].body); expect(JSON.parse(posts[1][1].body).reason).toBe('Frozen original');
});

it.each([401, 429, 422])('does not erase the unresolved original when its explicit retry gets HTTP%s', async status => {
  let writes = 0; const fetchMock = managerFetch(async () => reply({}, ++writes === 1 ? 504 : status));
  vi.stubGlobal('fetch', fetchMock); render(<FacilitiesPanel access={access} />); const user = await openManager();
  await user.click(screen.getByRole('checkbox', { name: "A-01 · Công khai" })); await user.type(screen.getByLabelText("Lý do"), 'Original outcome still unknown');
  await user.click(screen.getByRole('button', { name: "Chặn lịch bảo trì" })); await screen.findByText(/Chưa xác định kết quả yêu cầu/);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu" })); await user.click(await screen.findByRole('button', { name: "Thử lại yêu cầu gốc" }));
  expect(await screen.findByText(/Chưa xác định kết quả yêu cầu/)).toBeInTheDocument();
  expect(screen.queryByText(/Yêu cầu bị từ chối/)).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: "Chặn lịch bảo trì" })).toBeDisabled();
  expect(screen.getByRole('button', { name: "Đọc kết quả yêu cầu" })).toBeInTheDocument();
});

it('keeps known save success when only the follow-up catalog reload fails', async () => {
  const fetchMock = managerFetch(); const base = fetchMock.getMockImplementation()!; let catalogs = 0;
  fetchMock.mockImplementation((url, options) => url.includes('/catalog') && ++catalogs > 1 ? Promise.resolve(reply({}, 503)) : base(url, options));
  vi.stubGlobal('fetch', fetchMock); render(<FacilitiesPanel access={access} />); const user = await openManager();
  await user.click(screen.getByRole('checkbox', { name: "A-01 · Công khai" })); await user.type(screen.getByLabelText("Lý do"), 'Known committed result');
  await user.click(screen.getByRole('button', { name: "Chặn lịch bảo trì" }));
  expect(await screen.findByText(/Đã lưu yêu cầu nhưng/)).toBeInTheDocument();
  expect(screen.queryByText(/Yêu cầu bị từ chối/)).not.toBeInTheDocument();
  expect(fetchMock.mock.calls.filter(([, options]) => options.method === 'POST')).toHaveLength(1);
});

it('removes manager controls on scope change and ignores a late manager catalog', async () => {
  let finish: (value: unknown) => void = () => {};
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(facilities)).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })));
  const user = userEvent.setup(); const view = render(<FacilitiesPanel access={access} />);
  await user.selectOptions(await screen.findByLabelText("Cơ sở kho"), 'facility-a');
  view.rerender(<FacilitiesPanel access={null} />); finish(reply(catalog));
  await waitFor(() => expect(screen.queryByText("Quản lý cơ sở đã chọn")).not.toBeInTheDocument());
  expect(screen.queryByText('A-02')).not.toBeInTheDocument();
});

it('pages whole-unit search and marks results stale after input changes', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(facilities)).mockResolvedValueOnce(reply({ items: [], nextCursor: 'A-50' }))
    .mockResolvedValueOnce(reply({ items: [], nextCursor: null }));
  vi.stubGlobal('fetch', fetchMock); const user = userEvent.setup(); render(<FacilitiesPanel access={null} />);
  await user.selectOptions(await screen.findByLabelText("Cơ sở kho"), 'facility-a');
  await user.clear(screen.getByLabelText("Diện tích tối thiểu (m²)")); await user.type(screen.getByLabelText("Diện tích tối thiểu (m²)"), '20');
  await user.click(screen.getByRole('button', { name: "Tìm khoang còn trống" }));
  expect(await screen.findByText("Không có khoang phù hợp. Hãy thử đổi ngày thuê hoặc giảm diện tích tối thiểu rồi tìm lại.")).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: "Trang khoang sau" }));
  await waitFor(() => expect(fetchMock.mock.calls[2][0]).toContain('after=A-50'));
  await user.clear(screen.getByLabelText("Diện tích tối thiểu (m²)")); await user.type(screen.getByLabelText("Diện tích tối thiểu (m²)"), '25');
  expect(screen.getByText(/Điều kiện tìm kiếm đã thay đổi/)).toBeInTheDocument();
});

it('reports a failed catalog and denied manager read safely', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValueOnce(Error('offline')).mockResolvedValueOnce(reply({}, 403)));
  render(<FacilitiesPanel access={null} />); expect(await screen.findByText(/Không tải được danh mục cơ sở kho/)).toBeInTheDocument();
});

it('shows monthly catalog rates with an explicit month basis', async () => {
  vi.stubGlobal('fetch',vi.fn().mockResolvedValueOnce(reply(facilities)).mockResolvedValueOnce(reply({items:[{id:'unit-a',code:'MONTH-01',areaM2:'10',dailyRate:'0',billingBasis:'CalendarMonth',monthlyRate:'3100',currency:'VND'}],nextCursor:null})));
  const user=userEvent.setup();render(<FacilitiesPanel access={null}/>);
  await user.selectOptions(await screen.findByLabelText("Cơ sở kho"),'facility-a');await user.click(screen.getByRole('button',{name:"Tìm khoang còn trống"}));
  expect(await screen.findByText("3100 VND/m²/tháng dương lịch")).toBeInTheDocument();
});

it('retains the original monthly tariff body and version across unknown and rejected retries', async () => {
  const fetchMock=managerFetch(async()=>{throw Error('lost response');});vi.stubGlobal('fetch',fetchMock);
  render(<FacilitiesPanel access={access}/>);const user=await openManager();await user.click(screen.getByRole('button',{name:"Chỉnh sửa A-01"}));
  await user.selectOptions(screen.getByLabelText("Cơ sở tính giá"),'CalendarMonth');await user.type(screen.getByLabelText("Đơn giá tháng (VND/m²/tháng dương lịch)"),'3100');await user.type(screen.getByLabelText("Lý do"),'Monthly original');
  await user.click(screen.getByRole('button',{name:"Đặt đơn giá theo thời gian"}));await screen.findByText(/Chưa xác định kết quả yêu cầu/);
  await user.selectOptions(screen.getByLabelText("Cơ sở tính giá"),'Daily');await user.clear(screen.getByLabelText("Đơn giá ngày (VND/m²)"));await user.type(screen.getByLabelText("Đơn giá ngày (VND/m²)"),'999');
  await user.click(screen.getByRole('button',{name:"Đọc kết quả yêu cầu"}));await screen.findByRole('button',{name:"Thử lại yêu cầu gốc"});
  await user.click(screen.getByRole('button',{name:"Thử lại yêu cầu gốc"}));await screen.findByText(/Chưa xác định kết quả yêu cầu/);
  const writes=fetchMock.mock.calls.filter(([url,options])=>url.endsWith('/rates')&&options.method==='POST');expect(writes).toHaveLength(2);
  expect(writes[0][1].body).toBe(writes[1][1].body);expect(writes[0][1].headers['Idempotency-Key']).toBe(writes[1][1].headers['Idempotency-Key']);expect(writes[1][1].headers['If-Match']).toBe('"version-one"');
  expect(JSON.parse(writes[1][1].body)).toMatchObject({billingBasis:'CalendarMonth',monthlyRate:'3100',dailyRate:'0'});
});

it('clears displayed monthly rate history on selected unit and workspace changes', async () => {
  const base=managerFetch();const fetchMock=vi.fn().mockImplementation((url:string,options:RequestInit)=>url.includes('/rates?')?Promise.resolve(reply({items:[{id:'history-a',billingBasis:'CalendarMonth',dailyRate:'0',monthlyRate:'3100',salesValidFrom:'2028-01-01',salesValidTo:null}],nextPage:null})):base(url,options));vi.stubGlobal('fetch',fetchMock);
  const view=render(<FacilitiesPanel access={access}/>);const user=await openManager();await user.click(screen.getByRole('button',{name:"Chỉnh sửa A-01"}));await user.click(screen.getByRole('button',{name:"Xem lịch sử đơn giá đã chọn"}));await screen.findByText(/history-a/);
  await user.click(screen.getByRole('button',{name:"Chỉnh sửa A-02"}));expect(screen.queryByText(/history-a/)).not.toBeInTheDocument();
  await user.click(screen.getByRole('button',{name:"Xem lịch sử đơn giá đã chọn"}));await screen.findByText(/history-a/);view.rerender(<FacilitiesPanel access={{...access,facilityId:'facility-b'}}/>);expect(screen.queryByText(/history-a/)).not.toBeInTheDocument();
});
