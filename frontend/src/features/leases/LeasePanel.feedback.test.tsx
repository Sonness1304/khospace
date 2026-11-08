import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import LeasePanel from './LeasePanel';

const manager = { userId: 'manager-a', organizationId: '', facilityId: 'facility-a', facilityRoles: ['FacilityManager'], operatorAdmin: false };
const lease = { id: 'lease-a', bookingId: 'booking-a', rentalAccountId: 'account-a', organizationId: 'org-a', facilityId: 'facility-a', state: 'Scheduled', version: 'lease-version',
  startDate: '2028-01-01', endDate: '2028-01-03', units: [{ id: 'grant-a', unitId: 'unit-a' }], lines: [] };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());

it('announces a failed explicit lookup, associates its ID, and returns focus for correction', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({}, 404)));
  render(<LeasePanel access={manager} selectedId="lease-a" />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải hợp đồng thuê' }));
  const error = await screen.findByRole('alert'); const input = screen.getByLabelText('ID hợp đồng thuê');
  expect(error).toHaveTextContent(/Kiểm tra ID, quyền truy cập và kết nối/);
  expect(input).toHaveAttribute('aria-invalid', 'true'); expect(input).toHaveAccessibleDescription(error.textContent!);
  await waitFor(() => expect(input).toHaveFocus());
  await user.type(input, '-corrected');
  expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(input).not.toHaveAttribute('aria-invalid', 'true');
  expect(input).not.toHaveAttribute('aria-describedby');
});

it('makes verified details reachable after explicit lookup without moving focus again after handover readback', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(lease)).mockResolvedValueOnce(reply({ token: 'csrf' }))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'lease-a' } })).mockResolvedValueOnce(reply({ ...lease, state: 'Active' })));
  render(<LeasePanel access={manager} selectedId="lease-a" />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải hợp đồng thuê' }));
  const detail = await screen.findByRole('region', { name: 'Chi tiết hợp đồng thuê' });
  await waitFor(() => expect(detail).toHaveFocus());
  await user.type(screen.getByLabelText('Lý do kiểm tra'), 'Synthetic verified inspection');
  await user.click(screen.getByLabelText('Đã hoàn tất kiểm tra thực tế'));
  const handover = screen.getByRole('button', { name: 'Ghi nhận bàn giao' }); await user.click(handover);
  await screen.findByText('Đang hoạt động'); expect(detail).not.toHaveFocus();
});

it('clears stale lookup feedback on a new notice and never treats a notice as an explicit read', async () => {
  const fetchMock = vi.fn().mockResolvedValue(reply({}, 404)); vi.stubGlobal('fetch', fetchMock);
  const view = render(<LeasePanel access={manager} selectedId="lease-a" />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải hợp đồng thuê' })); await screen.findByText(/Chưa xác minh được hợp đồng thuê/);
  const button = screen.getByRole('button', { name: 'Tải hợp đồng thuê' }); button.focus();
  view.rerender(<LeasePanel access={manager} selectedId="lease-a" noticeKey="notice-new" />);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(screen.getByLabelText('ID hợp đồng thuê')).not.toHaveAttribute('aria-invalid', 'true');
  expect(screen.getByText(/Thông báo là lịch sử/)).toBeInTheDocument(); expect(button).toHaveFocus(); expect(fetchMock).toHaveBeenCalledTimes(1);
});

it('clears stale lookup feedback and invalid state when the access context changes', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({}, 403)));
  const view = render(<LeasePanel access={manager} selectedId="lease-a" />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải hợp đồng thuê' })); await screen.findByText(/Chưa xác minh được hợp đồng thuê/);
  view.rerender(<LeasePanel access={{ ...manager, facilityId: 'facility-b' }} selectedId="lease-a" />);
  expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(screen.queryByText(/Chưa xác minh được hợp đồng thuê/)).not.toBeInTheDocument();
  expect(screen.getByLabelText('ID hợp đồng thuê')).toHaveValue(''); expect(screen.getByLabelText('ID hợp đồng thuê')).not.toHaveAttribute('aria-invalid', 'true');
});

it('does not revive or focus a failed lookup after the user edits even a normalized-equivalent ID', async () => {
  let resolve!: (value: ReturnType<typeof reply>) => void;
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => new Promise<ReturnType<typeof reply>>(done => { resolve = done; })));
  render(<LeasePanel access={manager} selectedId="lease-a" />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải hợp đồng thuê' }));
  const input = screen.getByLabelText('ID hợp đồng thuê'); await user.type(input, ' ');
  await act(async () => resolve(reply({}, 404)));
  expect(screen.queryByText(/Chưa xác minh được hợp đồng thuê/)).not.toBeInTheDocument(); expect(input).not.toHaveAttribute('aria-invalid', 'true'); expect(input).toHaveFocus();
});

it('does not steal focus for success after the user edits the lookup during the request', async () => {
  let resolve!: (value: ReturnType<typeof reply>) => void;
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => new Promise<ReturnType<typeof reply>>(done => { resolve = done; })));
  render(<LeasePanel access={manager} selectedId="lease-a" />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: 'Tải hợp đồng thuê' }));
  const input = screen.getByLabelText('ID hợp đồng thuê'); await user.type(input, ' ');
  await act(async () => resolve(reply(lease)));
  expect(input).toHaveFocus(); expect(screen.queryByText('Đã xác minh trạng thái hợp đồng.')).not.toBeInTheDocument();
});
