import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import SessionPanel from './SessionPanel';
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState(null, '', '/'); });
it('registers a customer with CSRF and uses the single newly created customer workspace', async () => {
  let signedIn = false;
  const mock = vi.fn(async (url: string, options?: RequestInit) => {
    if (url.endsWith('/providers')) return reply({ google: false });
    if (url.endsWith('/csrf')) return reply({ token: 'registration-csrf' });
    if (url.endsWith('/register')) { signedIn = true; return reply({ authenticated: true }); }
    if (url.endsWith('/me')) return signedIn ? reply({ userId: 'new', userName: 'linh@example.test', operatorAdmin: false,
      organizations: [{ id: 'new-org', name: 'Kho của Linh', role: 'CustomerAdmin' }], facilities: [] }) : reply({}, 401);
    throw Error(String(options));
  });
  vi.stubGlobal('fetch', mock); const changed = vi.fn(); const user = userEvent.setup(); render(<SessionPanel onAccessChange={changed} />);
  await user.click(await screen.findByRole('button', { name: 'Đăng ký' }));
  await user.type(screen.getByLabelText('Tên cá nhân hoặc doanh nghiệp'), 'Kho của Linh');
  await user.type(screen.getByLabelText('Email'), 'linh@example.test');
  await user.type(screen.getByLabelText('Mật khẩu'), 'WarehouseRent!42');
  await user.type(screen.getByLabelText('Nhập lại mật khẩu'), 'WarehouseRent!42');
  await user.click(screen.getByRole('button', { name: 'Tạo tài khoản' }));
  expect(await screen.findByText('linh@example.test')).toBeInTheDocument();
  expect(mock.mock.calls.find(([url]) => url.endsWith('/register'))?.[1]?.headers).toEqual(expect.objectContaining({ 'X-CSRF-Token': 'registration-csrf' }));
  await waitFor(() => expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ organizationId: 'new-org', organizationRole: 'CustomerAdmin', operatorAdmin: false })));
});
it('prevents mismatched passwords without sending registration and explains unavailable Google', async () => {
  const mock = vi.fn(async (url: string) => url.endsWith('/providers') ? reply({ google: false }) : reply({}, 401));
  vi.stubGlobal('fetch', mock); const user = userEvent.setup(); render(<SessionPanel />);
  await user.click(await screen.findByRole('button', { name: 'Đăng ký' }));
  await user.type(screen.getByLabelText('Tên cá nhân hoặc doanh nghiệp'), 'Linh'); await user.type(screen.getByLabelText('Email'), 'linh@example.test');
  await user.type(screen.getByLabelText('Mật khẩu'), 'WarehouseRent!42'); await user.type(screen.getByLabelText('Nhập lại mật khẩu'), 'Different!42');
  await user.click(screen.getByRole('button', { name: 'Tạo tài khoản' }));
  expect(await screen.findByText('Mật khẩu nhập lại chưa khớp.')).toBeInTheDocument();
  expect(mock.mock.calls.some(([url]) => url.endsWith('/register'))).toBe(false);
  expect(screen.getByRole('button', { name: 'Tiếp tục với Google' })).toBeDisabled();
  expect(screen.getByText(/Google chưa được cấu hình/)).toBeInTheDocument();
});
it('offers a same-origin Google route only when the server enables it and explains account collision', async () => {
  window.history.replaceState(null, '', '/?authError=google-link-required#account');
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.endsWith('/providers') ? reply({ google: true }) : reply({}, 401)));
  render(<SessionPanel />);
  expect(await screen.findByRole('link', { name: 'Tiếp tục với Google' })).toHaveAttribute('href', '/api/v1/auth/google');
  expect(screen.getByText(/Email này đã có tài khoản/)).toBeInTheDocument();
});

it('offers explicit login and signup tabs while clearing credentials on a mode change', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.endsWith('/providers') ? reply({ google: true }) : reply({}, 401)));
  const user = userEvent.setup(); render(<SessionPanel />);
  await user.type(await screen.findByLabelText('Mật khẩu'), 'SyntheticPass!42');
  await user.click(screen.getByRole('tab', { name: 'Đăng ký' }));
  expect(screen.getByRole('tab', { name: 'Đăng ký' })).toHaveAttribute('aria-selected', 'true');
  expect(screen.getByLabelText('Mật khẩu')).toHaveValue('');
  expect(screen.getByRole('link', { name: 'Tiếp tục với Google' })).toHaveAttribute('href', '/api/v1/auth/google');
  expect(screen.getByLabelText('Nhập lại mật khẩu')).toBeInTheDocument();
});

it('explains an insecure Google callback without hiding normal email login', async () => {
  window.history.replaceState(null, '', '/?authError=google-https-required#account');
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.endsWith('/providers') ? reply({ google: true }) : reply({}, 401)));
  render(<SessionPanel />);
  expect(await screen.findByLabelText('Email')).toBeInTheDocument();
  expect(screen.getByRole('status')).toHaveTextContent('HTTPS');
});


it('supports arrow, home and end keys with one authentication tab stop', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) => url.endsWith('/providers') ? reply({ google: false }) : reply({}, 401)));
  const user = userEvent.setup(); render(<SessionPanel />);
  const login = await screen.findByRole('tab', { name: 'Đăng nhập' });
  const register = screen.getByRole('tab', { name: 'Đăng ký' });
  login.focus(); await user.keyboard('{ArrowRight}');
  expect(register).toHaveFocus(); expect(register).toHaveAttribute('aria-selected', 'true');
  expect(login).toHaveAttribute('tabindex', '-1');
  await user.keyboard('{Home}'); expect(login).toHaveFocus();
  await user.keyboard('{End}'); expect(register).toHaveFocus();
  await user.keyboard('{ArrowRight}'); expect(login).toHaveFocus();
});
