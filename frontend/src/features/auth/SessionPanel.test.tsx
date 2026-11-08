import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import SessionPanel from './SessionPanel';

const me = { userId: 'synthetic', userName: 'member@example.test', operatorAdmin: false,
  organizations: [{ id: 'org-a', name: 'Customer A', role: 'Member' }, { id: 'org-b', name: 'Customer B', role: 'CustomerAdmin' }], facilities: [] };
const response = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
function stubSessionFetch(mock: (url: string, options: RequestInit) => unknown) {
  vi.stubGlobal('fetch', (url: string, options: RequestInit) => url === '/api/v1/auth/providers' ? Promise.resolve(response({ google: false })) : mock(url, options));
}
afterEach(() => vi.unstubAllGlobals());

it('explains an account with no workspace without offering a selector or organization privileges', async () => {
  const onAccessChange = vi.fn();
  stubSessionFetch(vi.fn().mockResolvedValue(response({ ...me, organizations: [] })));
  render(<SessionPanel onAccessChange={onAccessChange} />);
  expect(await screen.findByText('Bạn chưa được thêm vào không gian khách hàng.')).toBeInTheDocument();
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  expect(screen.queryByText(/Vai trò hiện tại:/)).not.toBeInTheDocument();
  await waitFor(() => expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: '', organizationRole: undefined })));
});

it.each([['CustomerAdmin', 'Chủ không gian'], ['Member', 'Thành viên']])(
  'shows one workspace as static text and automatically publishes the unchanged %s protocol role', async (role, label) => {
    const onAccessChange = vi.fn();
    stubSessionFetch(vi.fn().mockResolvedValue(response({ ...me, organizations: [{ id: 'org-only', name: 'Kho của Linh', role }] })));
    render(<SessionPanel onAccessChange={onAccessChange} />);
    const workspace = await screen.findByRole('group', { name: 'Không gian của bạn' });
    expect(within(workspace).getByText('Kho của Linh')).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    expect(screen.getByText(`Vai trò hiện tại: ${label}`)).toBeInTheDocument();
    await waitFor(() => expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: 'org-only', organizationRole: role, operatorAdmin: false })));
  },
);

it('requires an explicit choice for multiple workspaces and clears the selected role when deselected', async () => {
  const onAccessChange = vi.fn();
  stubSessionFetch(vi.fn().mockResolvedValue(response(me)));
  const user = userEvent.setup(); render(<SessionPanel onAccessChange={onAccessChange} />);
  const selector = await screen.findByRole('combobox', { name: 'Không gian của bạn' });
  expect(selector).toHaveValue('');
  expect(screen.getByRole('option', { name: 'Chọn không gian' })).toHaveValue('');
  expect(screen.queryByRole('group', { name: 'Không gian của bạn' })).not.toBeInTheDocument();
  expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: '', organizationRole: undefined }));
  await user.selectOptions(selector, 'org-b');
  expect(screen.getByText('Vai trò hiện tại: Chủ không gian')).toBeInTheDocument();
  expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: 'org-b', organizationRole: 'CustomerAdmin' }));
  await user.selectOptions(selector, 'org-a');
  expect(screen.getByText('Vai trò hiện tại: Thành viên')).toBeInTheDocument();
  expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: 'org-a', organizationRole: 'Member' }));
  await user.selectOptions(selector, '');
  expect(screen.queryByText(/Vai trò hiện tại:/)).not.toBeInTheDocument();
  expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: '', organizationRole: undefined }));
});

it.each(['none', 'multiple'] as const)('withholds the single workspace during refresh and requires fresh scope after transition to %s', async membership => {
  const onAccessChange = vi.fn();
  let completeRefresh!: (result: ReturnType<typeof response>) => void;
  const refresh = new Promise<ReturnType<typeof response>>(resolve => { completeRefresh = resolve; });
  stubSessionFetch(vi.fn().mockResolvedValueOnce(response({ ...me, organizations: [me.organizations[1]] })).mockReturnValueOnce(refresh));
  const user = userEvent.setup(); render(<SessionPanel onAccessChange={onAccessChange} />);
  expect(await screen.findByRole('group', { name: 'Không gian của bạn' })).toHaveTextContent('Customer B');
  expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: 'org-b', organizationRole: 'CustomerAdmin' }));
  await user.click(screen.getByRole('button', { name: 'Làm mới tài khoản' }));
  expect(screen.getByText('Đang kiểm tra tài khoản…')).toBeInTheDocument();
  expect(screen.queryByText(/Vai trò hiện tại:/)).not.toBeInTheDocument();
  expect(screen.queryByRole('group', { name: 'Không gian của bạn' })).not.toBeInTheDocument();
  expect(onAccessChange).toHaveBeenLastCalledWith(null);
  await act(async () => completeRefresh(response({ ...me, organizations: membership === 'none' ? [] : me.organizations })));
  expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: '', organizationRole: undefined }));
  if (membership === 'none') {
    expect(screen.getByText('Bạn chưa được thêm vào không gian khách hàng.')).toBeInTheDocument();
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  } else {
    const selector = screen.getByRole('combobox', { name: 'Không gian của bạn' });
    expect(selector).toHaveValue('');
    await user.selectOptions(selector, 'org-a');
    expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: 'org-a', organizationRole: 'Member' }));
  }
  expect(screen.queryByText('Vai trò hiện tại: Chủ không gian')).not.toBeInTheDocument();
});

it('does not restore single-workspace privileges when refresh becomes unavailable', async () => {
  const onAccessChange = vi.fn();
  stubSessionFetch(vi.fn().mockResolvedValueOnce(response({ ...me, organizations: [me.organizations[1]] })).mockRejectedValueOnce(new TypeError('offline')));
  const user = userEvent.setup(); render(<SessionPanel onAccessChange={onAccessChange} />);
  await screen.findByRole('group', { name: 'Không gian của bạn' });
  await user.click(screen.getByRole('button', { name: 'Làm mới tài khoản' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Chưa kết nối được tài khoản');
  expect(screen.queryByText(/Vai trò hiện tại:/)).not.toBeInTheDocument();
  expect(screen.queryByRole('group', { name: 'Không gian của bạn' })).not.toBeInTheDocument();
  expect(onAccessChange).toHaveBeenLastCalledWith(null);
});

it('signs in with a fresh CSRF token, selects explicit context, and removes it after logout', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(response({}, 401))
    .mockResolvedValueOnce(response({ token: 'csrf-login' })).mockResolvedValueOnce(response({ authenticated: true }))
    .mockResolvedValueOnce(response(me)).mockResolvedValueOnce(response({ token: 'csrf-logout' })).mockResolvedValueOnce(response(null, 204))
    .mockResolvedValueOnce(response({}, 401));
  stubSessionFetch(fetchMock);
  const user = userEvent.setup(); render(<SessionPanel />);
  await user.type(await screen.findByLabelText('Email'), 'member@example.test');
  await user.type(screen.getByLabelText('Mật khẩu'), 'Synthetic!42');
  await user.click(screen.getByRole('button', { name: 'Đăng nhập' }));
  expect(await screen.findByText('member@example.test')).toBeInTheDocument();
  const context = screen.getByRole('combobox', { name: 'Không gian của bạn' });
  expect(context).toHaveValue('');
  await user.selectOptions(context, 'org-a'); expect(screen.getByText('Vai trò hiện tại: Thành viên')).toBeInTheDocument();
  await user.selectOptions(context, 'org-b'); expect(screen.getByText('Vai trò hiện tại: Chủ không gian')).toBeInTheDocument();
  expect(fetchMock.mock.calls[2][1].headers['X-CSRF-Token']).toBe('csrf-login');
  expect(fetchMock.mock.calls[2][1].credentials).toBe('same-origin');
  await user.click(screen.getByRole('button', { name: 'Đăng xuất' }));
  expect(await screen.findByLabelText('Email')).toBeInTheDocument();
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  expect(fetchMock.mock.calls[5][1].headers['X-CSRF-Token']).toBe('csrf-logout');
});

it('clears revoked organization context on a fresh session readback', async () => {
  stubSessionFetch(vi.fn().mockResolvedValueOnce(response(me)).mockResolvedValueOnce(response({ ...me, organizations: [] })));
  const user = userEvent.setup(); render(<SessionPanel />);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Không gian của bạn' }), 'org-a');
  await user.click(screen.getByRole('button', { name: 'Làm mới tài khoản' }));
  await waitFor(() => expect(screen.queryByText('Vai trò hiện tại: Thành viên')).not.toBeInTheDocument());
  expect(screen.getByText('Bạn chưa được thêm vào không gian khách hàng.')).toBeInTheDocument();
});

it('reports an unavailable session service without presenting a fake signed-out success', async () => {
  stubSessionFetch(vi.fn().mockRejectedValue(new TypeError('offline'))); render(<SessionPanel />);
  expect(await screen.findByText(/Chưa kết nối được tài khoản/)).toBeInTheDocument();
  expect(screen.queryByLabelText('Email')).not.toBeInTheDocument();
});

it('reads back a lost logout response without replaying the mutation or retaining selected context', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(response(me)).mockResolvedValueOnce(response({ token: 'token' }))
    .mockRejectedValueOnce(new TypeError('reply lost')).mockResolvedValueOnce(response({}, 401));
  stubSessionFetch(fetchMock);
  const user = userEvent.setup(); render(<SessionPanel />);
  await user.selectOptions(await screen.findByRole('combobox', { name: 'Không gian của bạn' }), 'org-a');
  await user.click(screen.getByRole('button', { name: 'Đăng xuất' }));
  expect(await screen.findByLabelText('Email')).toBeInTheDocument();
  expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  expect(fetchMock.mock.calls.filter(([url]) => url === '/api/v1/auth/logout')).toHaveLength(1);
  expect(screen.getByText(/Chưa xác nhận được yêu cầu/)).toBeInTheDocument();
});

it('publishes explicit organization and facility scope, then clears it after revocation', async () => {
  const onAccessChange = vi.fn();
  const scoped = { ...me, operatorAdmin: true, facilities: [{ id: 'facility-a', role: 'FacilityManager' }] };
  stubSessionFetch(vi.fn().mockResolvedValueOnce(response(scoped)).mockResolvedValueOnce(response({}, 401)));
  const user = userEvent.setup(); render(<SessionPanel onAccessChange={onAccessChange} />);
  await user.selectOptions(await screen.findByLabelText('Kho được phân công'), 'facility-a');
  await user.selectOptions(screen.getByLabelText('Không gian của bạn'), 'org-a');
  expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: 'org-a', facilityId: 'facility-a', facilityRoles: ['FacilityManager'], operatorAdmin: true }));
  await user.click(screen.getByRole('button', { name: 'Làm mới tài khoản' }));
  await screen.findByLabelText('Email'); expect(onAccessChange).toHaveBeenLastCalledWith(null);
});

it('clears the password and presents a generic invalid-login response', async () => {
  stubSessionFetch(vi.fn().mockResolvedValueOnce(response({}, 401)).mockResolvedValueOnce(response({ token: 'csrf' })).mockResolvedValueOnce(response({}, 401)));
  const user = userEvent.setup(); render(<SessionPanel />);
  await user.type(await screen.findByLabelText('Email'), 'synthetic@example.test'); await user.type(screen.getByLabelText('Mật khẩu'), 'Wrong!42');
  await user.click(screen.getByRole('button', { name: 'Đăng nhập' }));
  expect(await screen.findByText('Email hoặc mật khẩu chưa đúng. Vui lòng kiểm tra lại.')).toBeInTheDocument(); expect(screen.getByLabelText('Mật khẩu')).toHaveValue('');
});

it.each([400, 401, 403, 409, 422, 429])('clears an expired session after rejected logout HTTP %s without replay', async status => {
  const onAccessChange = vi.fn();
  const fetchMock = vi.fn().mockResolvedValueOnce(response({ ...me, operatorAdmin: true }))
    .mockResolvedValueOnce(response({ token: 'fresh' })).mockResolvedValueOnce(response({}, status))
    .mockResolvedValueOnce(response({}, 401));
  stubSessionFetch(fetchMock);
  const user = userEvent.setup(); render(<SessionPanel onAccessChange={onAccessChange} />);
  await user.click(await screen.findByRole('button', { name: 'Đăng xuất' }));
  expect(await screen.findByLabelText('Email')).toBeInTheDocument();
  await waitFor(() => expect(onAccessChange).toHaveBeenLastCalledWith(null));
  expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/logout'))).toHaveLength(1);
  expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/me'))).toHaveLength(2);
});

it('withholds stale privileges when rejected logout cannot read back the session', async () => {
  const onAccessChange = vi.fn();
  const fetchMock = vi.fn().mockResolvedValueOnce(response({ ...me, operatorAdmin: true }))
    .mockResolvedValueOnce(response({ token: 'fresh' })).mockResolvedValueOnce(response({}, 403))
    .mockRejectedValue(new TypeError('offline'));
  stubSessionFetch(fetchMock);
  const user = userEvent.setup(); render(<SessionPanel onAccessChange={onAccessChange} />);
  await user.click(await screen.findByRole('button', { name: 'Đăng xuất' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Chưa kết nối được tài khoản');
  expect(screen.queryByText('Đã đăng nhập')).not.toBeInTheDocument();
  await waitFor(() => expect(onAccessChange).toHaveBeenLastCalledWith(null));
  expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/logout'))).toHaveLength(1);
  expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/me'))).toHaveLength(2);
});

it('uses fresh privileges after rejected logout when the session is still valid', async () => {
  const onAccessChange = vi.fn();
  const fetchMock = vi.fn().mockResolvedValueOnce(response({ ...me, operatorAdmin: true }))
    .mockResolvedValueOnce(response({ token: 'fresh' })).mockResolvedValueOnce(response({}, 400))
    .mockResolvedValueOnce(response({ ...me, organizations: [], operatorAdmin: false }));
  stubSessionFetch(fetchMock);
  const user = userEvent.setup(); render(<SessionPanel onAccessChange={onAccessChange} />);
  await user.click(await screen.findByRole('button', { name: 'Đăng xuất' }));
  await waitFor(() => expect(onAccessChange).toHaveBeenLastCalledWith(expect.objectContaining({ operatorAdmin: false })));
  expect(fetchMock.mock.calls.filter(([url]) => url.endsWith('/logout'))).toHaveLength(1);
});
