import { StrictMode } from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import { discoveryDate, estimateStorage } from './sizeAdvisor';
import StoragePlanner from './StoragePlanner';
import DiscoveryHome from './DiscoveryHome';
import FacilitiesPanel from '../facilities/FacilitiesPanel';

const response = (body: unknown) => ({ ok: true, json: async () => body });
const facilities = { items: [{ id: 'f1', name: 'Kho thử nghiệm', address: 'Địa chỉ fixture', permittedGoods: 'Hàng khô' }], nextCursor: null };
const units = { items: [2, 4, 6, 8].map((area, i) => ({ id: `u${i}`, code: `K-${i}`, areaM2: String(area), billingBasis: i === 0 ? 'CalendarMonth' : 'Daily', dailyRate: '1000', monthlyRate: '25000', currency: 'VND' })), nextCursor: null };
const preset = { id: 'p1', facilityId: 'f1', startDate: '2028-01-01', endDate: '2028-02-01', minArea: '6' };
afterEach(() => { vi.unstubAllGlobals(); vi.restoreAllMocks(); });

it('estimates empty, default, capped and nonfinite quantities without promising real fit', () => {
  expect(estimateStorage({})).toEqual({ count: 0, footprint: 0, estimated: 0, recommended: 0 });
  expect(estimateStorage({ box: 6, sofa: 1 })).toMatchObject({ count: 7, recommended: 6 });
  expect(estimateStorage({ box: 1 })).toMatchObject({ footprint: 0.24, recommended: 2 });
  expect(estimateStorage({ box: -4, sofa: Infinity, bike: NaN, desk: 1.9 })).toMatchObject({ count: 1, recommended: 2 });
  expect(estimateStorage({ box: 100 })).toMatchObject({ count: 50, recommended: 16 });
  expect(estimateStorage({ sofa: 50 })).toMatchObject({ recommended: 104 });
  vi.useFakeTimers(); vi.setSystemTime(new Date('2028-01-01T18:00:00Z'));
  expect(discoveryDate()).toBe('2028-01-02'); expect(discoveryDate(1)).toBe('2028-01-03'); vi.useRealTimers();
});

it('changes planner counts locally, resets and applies only an explicit nonempty suggestion', async () => {
  const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock);
  const apply = vi.fn(); const user = userEvent.setup(); render(<StoragePlanner onApply={apply} />);
  expect(screen.getByLabelText('Số lượng Thùng carton')).toHaveTextContent('6');
  await user.click(screen.getByRole('button', { name: 'Tăng Thùng carton' }));
  await user.click(screen.getByRole('button', { name: 'Giảm Thùng carton' }));
  expect(apply).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: /Xem kho phù hợp/ })); expect(apply).toHaveBeenLastCalledWith(6);
  await user.click(screen.getByRole('button', { name: 'Xóa lựa chọn' }));
  expect(screen.getByRole('button', { name: /Xem kho phù hợp/ })).toBeDisabled();
  expect(screen.getByRole('button', { name: 'Giảm Thùng carton' })).toBeDisabled();
  expect(screen.getByText(/không cam kết mọi đồ đạc đều chứa vừa/)).toBeInTheDocument();
  expect(fetchMock).not.toHaveBeenCalled();
});

it('bounds planner quantities and caps illustration symbols without capping the calculation', async () => {
  const user = userEvent.setup(); const { container } = render(<StoragePlanner onApply={vi.fn()} />);
  const increment = screen.getByRole('button', { name: 'Tăng Thùng carton' });
  for (let i = 6; i < 50; i++) await user.click(increment);
  expect(increment).toBeDisabled(); expect(screen.getByLabelText('Số lượng Thùng carton')).toHaveTextContent('50');
  expect(container.querySelectorAll('.ks-plan-object.box')).toHaveLength(8);
});

it('hands real facility and dates to catalog; planner may hand off without a facility', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response(facilities)));
  const onSearch = vi.fn(); const user = userEvent.setup(); render(<DiscoveryHome onSearch={onSearch} />);
  await user.click(screen.getByRole('button', { name: /Xem kho phù hợp/ }));
  expect(onSearch).toHaveBeenLastCalledWith(expect.objectContaining({ facilityId: '', minArea: '6' }));
  await user.selectOptions(await screen.findByLabelText('Cơ sở muốn thuê'), 'f1');
  await user.selectOptions(screen.getByLabelText('Diện tích cần thuê'), '12');
  await user.click(screen.getByRole('button', { name: /Tìm kho phù hợp/ }));
  expect(onSearch).toHaveBeenLastCalledWith(expect.objectContaining({ facilityId: 'f1', minArea: '12', startDate: discoveryDate(1), endDate: discoveryDate(2) }));
});

it('shows honest catalog failure and ignores a facility response after unmount', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('fixture offline')));
  const { unmount } = render(<DiscoveryHome onSearch={vi.fn()} />);
  expect(await screen.findByText(/Chưa tải được cơ sở kho/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /Tìm kho phù hợp/ })).toBeDisabled(); unmount();
  let resolve!: (value: unknown) => void;
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => new Promise(done => { resolve = done; })));
  const view = render(<DiscoveryHome onSearch={vi.fn()} />); view.unmount();
  await act(async () => resolve(response(facilities)));
});

function catalogMock() {
  return vi.fn().mockImplementation((url: string) => Promise.resolve(response(url.includes('/availability?') ? units : facilities)));
}

it('applies a quick-search preset in StrictMode, with authoritative dated results and no writes', async () => {
  const fetchMock = catalogMock(); vi.stubGlobal('fetch', fetchMock);
  render(<StrictMode><FacilitiesPanel access={null} preset={preset} /></StrictMode>);
  await screen.findByRole('heading', { name: 'K-0' });
  expect(screen.getByLabelText('Cơ sở kho')).toHaveValue('f1');
  expect(screen.getByLabelText('Ngày bắt đầu')).toHaveValue(preset.startDate);
  expect(screen.getByLabelText('Diện tích tối thiểu (m²)')).toHaveValue(6);
  expect(fetchMock.mock.calls.some(([url]) => url.includes('facilityId=f1&startDate=2028-01-01&endDate=2028-02-01&minArea=6'))).toBe(true);
  expect(fetchMock.mock.calls.every(([, options]) => !options.method || options.method === 'GET')).toBe(true);
});

it('prefills advisor area but does not query availability without a chosen facility', async () => {
  const fetchMock = catalogMock(); vi.stubGlobal('fetch', fetchMock);
  render(<FacilitiesPanel access={null} preset={{ ...preset, facilityId: '' }} />);
  await screen.findByRole('option', { name: 'Kho thử nghiệm' });
  expect(screen.getByLabelText('Diện tích tối thiểu (m²)')).toHaveValue(6);
  expect(fetchMock.mock.calls.some(([url]) => url.includes('/availability'))).toBe(false);
});

it('rejects late preset results after criteria changes, and accepts the next preset', async () => {
  let finish!: (value: unknown) => void;
  const fetchMock = vi.fn().mockImplementation((url: string) => url.includes('/availability') ? new Promise(resolve => { finish = resolve; }) : Promise.resolve(response(facilities)));
  vi.stubGlobal('fetch', fetchMock); const user = userEvent.setup();
  const view = render(<FacilitiesPanel access={null} preset={preset} />);
  await waitFor(() => expect(finish).toBeTypeOf('function'));
  await user.clear(screen.getByLabelText('Diện tích tối thiểu (m²)'));
  await user.type(screen.getByLabelText('Diện tích tối thiểu (m²)'), '20');
  await act(async () => finish(response(units)));
  expect(screen.queryByRole('heading', { name: 'K-0' })).not.toBeInTheDocument();
  fetchMock.mockImplementation((url: string) => Promise.resolve(response(url.includes('/availability') ? units : facilities)));
  view.rerender(<FacilitiesPanel access={null} preset={{ ...preset, id: 'p2', minArea: '2' }} />);
  await screen.findByRole('heading', { name: 'K-0' });
});

it('keeps a failed preset retryable with the entered criteria intact', async () => {
  const mock = vi.fn().mockImplementation((url: string) => url.includes('/availability') ? Promise.reject(new TypeError('offline')) : Promise.resolve(response(facilities)));
  vi.stubGlobal('fetch', mock); render(<FacilitiesPanel access={null} preset={preset} />);
  await screen.findByText(/Chưa hoàn tất tìm kiếm/);
  expect(screen.getByLabelText('Ngày bắt đầu')).toHaveValue(preset.startDate);
  expect(screen.getByRole('button', { name: 'Tìm khoang còn trống' })).toBeEnabled();
});

it('compares at most three units, sorts safely and disables stale quote selection', async () => {
  vi.stubGlobal('fetch', catalogMock()); const user = userEvent.setup();
  render(<FacilitiesPanel access={null} preset={preset} />); await screen.findByRole('heading', { name: 'K-0' });
  for (let i = 0; i < 4; i++) await user.click(screen.getByRole('checkbox', { name: `So sánh K-${i}` }));
  expect(screen.getByText(/Chỉ so sánh tối đa 3/)).toBeInTheDocument();
  expect(screen.getByRole('checkbox', { name: 'So sánh K-3' })).not.toBeChecked();
  expect(screen.getByRole('table')).toHaveTextContent('tháng dương lịch');
  await user.click(screen.getByRole('checkbox', { name: 'So sánh K-1' }));
  await user.click(screen.getByRole('checkbox', { name: 'So sánh K-3' }));
  await user.selectOptions(screen.getByLabelText('Sắp xếp theo'), 'code');
  await user.click(screen.getByRole('checkbox', { name: 'K-0' }));
  await user.type(screen.getByLabelText('Diện tích tối thiểu (m²)'), '0');
  expect(screen.getByRole('checkbox', { name: 'K-0' })).toBeDisabled();
  expect(screen.getByText(/Điều kiện tìm kiếm đã thay đổi/)).toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Xóa so sánh' })); expect(screen.queryByRole('table')).not.toBeInTheDocument();
});

it('opens real unit details and chooses a quote only explicitly; stale details cannot choose', async () => {
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value: function (this: HTMLDialogElement) { this.open = true; } });
  Object.defineProperty(HTMLDialogElement.prototype, 'close', { configurable: true, value: function (this: HTMLDialogElement) { this.open = false; } });
  vi.stubGlobal('fetch', catalogMock()); const user = userEvent.setup();
  const view = render(<FacilitiesPanel access={null} preset={preset} />); await screen.findByRole('heading', { name: 'K-0' });
  await user.click(screen.getByRole('button', { name: 'Chi tiết K-0' }));
  expect(screen.getByRole('dialog')).toHaveTextContent('Kho thử nghiệm');
  await user.click(screen.getByRole('button', { name: 'Chọn nhận báo giá' }));
  expect(screen.getByRole('checkbox', { name: 'K-0' })).toBeChecked(); expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  await user.click(screen.getByRole('button', { name: 'Chi tiết K-0' }));
  await user.click(screen.getByRole('button', { name: 'Đóng' }));
  await user.type(screen.getByLabelText('Diện tích tối thiểu (m²)'), '0');
  await user.click(screen.getByRole('button', { name: 'Chi tiết K-0' }));
  expect(screen.getByRole('button', { name: 'Chọn nhận báo giá' })).toBeDisabled();
  view.rerender(<FacilitiesPanel access={null} preset={preset} active={false} />);
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  view.rerender(<FacilitiesPanel access={null} preset={preset} active />);
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  expect(screen.getByRole('checkbox', { name: 'K-0' })).toBeChecked();
  view.unmount();
});
