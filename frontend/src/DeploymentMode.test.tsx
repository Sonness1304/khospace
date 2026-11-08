import { render, screen } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import App from './App';

afterEach(() => { vi.unstubAllEnvs(); vi.unstubAllGlobals(); });
it('explicit frontend-only deployment explains unavailable services and never mounts credential forms', async () => {
  vi.stubEnv('VITE_KHOSPACE_MODE', 'frontend-only');
  const fetchMock = vi.fn().mockResolvedValue({ ok: false, status: 503 });
  vi.stubGlobal('fetch', fetchMock);
  window.history.replaceState(null, '', '/#account');
  render(<App />);
  expect(screen.getByRole('region', { name: 'Trạng thái triển khai' })).toHaveTextContent('Bản giao diện — chưa kết nối máy chủ');
  expect(screen.queryByLabelText(/Mật khẩu/)).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: /Tiếp tục với Google/ })).not.toBeInTheDocument();
  expect(screen.getByRole('heading', { name: 'Tài khoản chưa khả dụng trên bản giao diện' })).toBeInTheDocument();
  await screen.findByText('Chưa kết nối ứng dụng');
  expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/auth/'))).toBe(false);
});
