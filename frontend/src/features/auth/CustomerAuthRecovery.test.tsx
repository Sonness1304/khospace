import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import SessionPanel from './SessionPanel';
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => { vi.unstubAllGlobals(); window.history.replaceState(null, '', '/'); });
function registrationFetch(outcome: number | 'lost') {
  let registered = false;
  const fetchMock = vi.fn(async (url: string) => {
    if (url.endsWith('/providers')) return reply({ google: false });
    if (url.endsWith('/csrf')) return reply({ token: 'csrf' });
    if (url.endsWith('/register')) { if (outcome === 'lost') { registered = true; throw new TypeError('reply lost'); } return reply({}, outcome); }
    return registered ? reply({ userId: 'new', userName: 'khach@example.test', operatorAdmin: false,
      organizations: [{ id: 'new-org', name: 'Cửa hàng', role: 'CustomerAdmin' }], facilities: [] }) : reply({}, 401);
  });
  vi.stubGlobal('fetch', fetchMock); return fetchMock;
}
async function submitRegistration() {
  const user = userEvent.setup(); render(<SessionPanel />);
  await user.click(await screen.findByRole('button', { name: 'Đăng ký' }));
  await user.type(screen.getByLabelText('Tên cá nhân hoặc doanh nghiệp'), 'Cửa hàng');
  await user.type(screen.getByLabelText('Email'), 'khach@example.test');
  await user.type(screen.getByLabelText('Mật khẩu', { exact: true }), 'SyntheticCustomer!42');
  await user.type(screen.getByLabelText('Nhập lại mật khẩu'), 'SyntheticCustomer!42');
  await user.click(screen.getByRole('button', { name: 'Tạo tài khoản' })); return user;
}
it.each([[409, 'Email này đã có tài khoản.'], [400, 'Chưa tạo được tài khoản.'], [429, 'Bạn thao tác quá nhanh.']] as const)('explains registration rejection %s and clears secret fields', async (status, text) => {
  const mock = registrationFetch(status); await submitRegistration();
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(text));
  expect(screen.getByRole('alert')).toHaveFocus();
  expect(screen.getByRole('tabpanel')).toHaveAttribute('aria-describedby', screen.getByRole('alert').id);
  expect(screen.getByLabelText('Mật khẩu', { exact: true })).toHaveValue('');
  expect(screen.getByLabelText('Nhập lại mật khẩu')).toHaveValue('');
  expect(mock.mock.calls.filter(([url]) => url.endsWith('/register'))).toHaveLength(1);
});
it('reads back a lost registration response once instead of creating a second workspace', async () => {
  const mock = registrationFetch('lost'); await submitRegistration();
  expect(await screen.findByText('khach@example.test')).toBeInTheDocument();
  expect(screen.getByText('Chưa xác nhận được yêu cầu. Trạng thái đăng nhập đã được kiểm tra lại.')).toBeInTheDocument();
  expect(mock.mock.calls.filter(([url]) => url.endsWith('/register'))).toHaveLength(1);
  expect(mock.mock.calls.filter(([url]) => url.endsWith('/me'))).toHaveLength(2);
});
it('lets the user reveal and conceal the password and clears it when switching modes', async () => {
  registrationFetch(400); const user = userEvent.setup(); render(<SessionPanel />);
  const password = await screen.findByLabelText('Mật khẩu', { exact: true });
  await user.type(password, 'SyntheticCustomer!42'); await user.click(screen.getByRole('button', { name: 'Hiện mật khẩu' }));
  expect(password).toHaveAttribute('type', 'text'); await user.click(screen.getByRole('button', { name: 'Ẩn mật khẩu' }));
  expect(password).toHaveAttribute('type', 'password'); await user.click(screen.getByRole('button', { name: 'Đăng ký' }));
  expect(screen.getByLabelText('Mật khẩu', { exact: true })).toHaveValue('');
});
it('recovers a previously unavailable session by an explicit retry while provider availability fails separately', async () => {
  let calls = 0;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    if (url.endsWith('/providers')) throw Error('offline');
    if (++calls === 1) throw Error('offline'); return reply({}, 401);
  }));
  const user = userEvent.setup(); render(<SessionPanel />); await user.click(await screen.findByRole('button', { name: 'Thử lại kết nối' }));
  expect(await screen.findByLabelText('Email')).toBeInTheDocument();
  expect(screen.getByText('Tạm chưa kết nối được Google. Hãy dùng email và mật khẩu.')).toBeInTheDocument();
});
