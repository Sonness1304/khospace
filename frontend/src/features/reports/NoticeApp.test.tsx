import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import App from '../../App';
const fixture = vi.hoisted(() => ({
 access: { userId: 'fixture-user', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: 'facility-a', facilityRoles: [], operatorAdmin: false },
 lease: 'aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa', request: 'bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb',
}));
vi.mock('../auth/SessionPanel', () => ({ default: ({ onAccessChange }: { onAccessChange: (value: typeof fixture.access) => void }) => <div><button onClick={() => onAccessChange(fixture.access)}>Chọn tài khoản QA</button><button onClick={() => onAccessChange({ ...fixture.access, userId: 'other', organizationId: 'org-b', facilityId: 'facility-b' })}>Đổi phạm vi QA</button></div> }));
afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState(null, '', '/'); });
it('routes scoped notice intents to retained read forms, preserving drafts, history and no-write behavior', async () => {
 const mock = vi.fn().mockImplementation((url: string) => {
  const body = url.includes('/notices?') ? { items: [
   { id: 'n1', kind: 'LeaseEnding', aggregateId: fixture.lease, message: 'fixture', createdAt: '2026-10-31T00:00:00Z' },
   { id: 'n2', kind: 'StockAction', aggregateId: fixture.request, message: 'fixture', createdAt: '2026-10-31T00:00:00Z' },
  ], total: 2 } : url === `/api/v1/leases/${fixture.lease}` ? { id: fixture.lease, organizationId: 'org-a', facilityId: 'facility-a', state: 'Active', units: [], lines: [], startDate: '2028-01-01', endDate: '2028-02-01' }
   : url.includes('/health/') ? { status: url.endsWith('/ready') ? 'ready' : 'live' } : { items: [], nextCursor: null, total: 0 };
  return Promise.resolve({ ok: true, status: 200, json: async () => body });
 });
 vi.stubGlobal('fetch', mock); const user = userEvent.setup(); render(<App />);
 await user.click(screen.getByRole('link', { name: 'Đăng nhập / Đăng ký' }));
 await user.click(screen.getByRole('button', { name: 'Chọn tài khoản QA' }));
 await user.click(screen.getByRole('link', { name: 'Tổng quan' }));
 await user.click(screen.getByRole('button', { name: 'Tải thông báo' }));
 await user.click(await screen.findByRole('button', { name: 'Mở hợp đồng liên quan' }));
 expect(window.location.hash).toBe('#leases'); expect(screen.getByRole('main')).toHaveFocus();
 expect(screen.getByLabelText('ID hợp đồng thuê')).toHaveValue(fixture.lease);
 expect(mock.mock.calls.filter(([url]) => url === `/api/v1/leases/${fixture.lease}`)).toHaveLength(0);
 await user.click(screen.getByRole('button', { name: 'Tải hợp đồng thuê' }));
 await screen.findByText('Đang hoạt động');
 await user.click(screen.getByRole('link', { name: 'Tổng quan' }));
 await user.click(screen.getByRole('button', { name: 'Mở hợp đồng liên quan' }));
 expect(screen.queryByText('Đang hoạt động')).not.toBeInTheDocument();
 await user.click(screen.getByRole('link', { name: 'Tổng quan' }));
 await user.click(screen.getByRole('button', { name: 'Mở yêu cầu nhập / xuất liên quan' }));
 expect(window.location.hash).toBe('#requests'); expect(screen.getByRole('main')).toHaveFocus();
 expect(screen.getByLabelText('ID yêu cầu nhập xuất')).toHaveValue(fixture.request);
 expect(mock.mock.calls.filter(([url]) => url === `/api/v1/stock-requests/${fixture.request}`)).toHaveLength(0);
 await user.type(screen.getByLabelText('Lý do thao tác kho'), 'Keep this draft');
 await user.click(screen.getByRole('link', { name: 'Tổng quan' }));
 window.history.back();
 await waitFor(() => expect(window.location.hash).toBe('#requests'));
 await waitFor(() => expect(screen.getByRole('main')).toHaveFocus());
 expect(screen.getByLabelText('Lý do thao tác kho')).toHaveValue('Keep this draft');
 await user.click(screen.getByRole('link', { name: 'Tài khoản của tôi' }));
 await user.click(screen.getByRole('button', { name: 'Đổi phạm vi QA' }));
 await user.click(screen.getByRole('link', { name: 'Tổng quan' }));
 expect(screen.queryByRole('button', { name: 'Mở hợp đồng liên quan' })).not.toBeInTheDocument();
 await user.click(screen.getByRole('link', { name: 'Yêu cầu nhập / xuất' }));
 expect(screen.getByLabelText('ID yêu cầu nhập xuất')).toHaveValue('');
 expect(mock.mock.calls.every(([, options]) => !options.method || options.method === 'GET')).toBe(true);
});
