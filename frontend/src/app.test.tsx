import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import App from './App';
vi.mock('./features/auth/SessionPanel', () => ({ default: () => null }));
vi.mock('./features/facilities/FacilitiesPanel', () => ({ default: () => null }));
vi.mock('./features/leases/LeasePanel', () => ({ default: () => null }));

const reply = (status: string, ok = true) => ({ ok, json: async () => ({ status, observedAt: '2026-10-28T03:00:00Z' }) });

function healthFetch(mock: (url: string, options: RequestInit) => unknown) {
  vi.stubGlobal('fetch', (url: string, options: RequestInit) => url.includes('/public/facilities') ? Promise.resolve({ ok: true, json: async () => ({ items: [], nextCursor: null }) }) : mock(url, options));
}

afterEach(() => { vi.unstubAllGlobals(); vi.useRealTimers(); window.history.replaceState(null, '', '/'); });

describe('KhoSpace foundation', () => {
  it('provides a named navigation with a real target for each workspace link', async () => {
    healthFetch(vi.fn().mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('ready')));
    render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống')); await screen.findByText('Dữ liệu sẵn sàng');
    const navigation = screen.getByRole('navigation', { name: 'Điều hướng chính' });
    const links = navigation.querySelectorAll('a[href^="#"]');
    expect(links.length).toBeGreaterThan(0);
    for (const link of links) {
      const id = link.getAttribute('href')!.slice(1);
      expect(document.getElementById(id)).not.toBeNull();
    }
  });
  it('updates the current page and exposes the selected workspace when navigation is activated', async () => {
    healthFetch(vi.fn().mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('ready')));
    const user = userEvent.setup(); render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống')); await screen.findByText('Dữ liệu sẵn sàng');
    const link = screen.getByRole('link', { name: 'Tìm kho & báo giá' });
    link.focus(); await user.keyboard('{Enter}');
    expect(link).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('main')).toHaveFocus();
    expect(screen.queryByRole('link', { name: 'Quản lý' })).not.toBeInTheDocument();
    expect(document.getElementById('catalog')).not.toHaveAttribute('hidden');
    expect(document.getElementById('overview')).toHaveAttribute('hidden');
  });
  it('restores workspace navigation from the URL and responds to browser history changes', async () => {
    window.history.replaceState(null, '', '/#catalog');
    healthFetch(vi.fn().mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('ready')));
    render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống')); await screen.findByText('Dữ liệu sẵn sàng');
    expect(screen.getByRole('link', { name: 'Tìm kho & báo giá' })).toHaveAttribute('aria-current', 'page');
    expect(document.getElementById('catalog')).not.toHaveAttribute('hidden');
    window.history.replaceState(null, '', '/#overview');
    await act(async () => { window.dispatchEvent(new HashChangeEvent('hashchange')); });
    expect(screen.getByRole('link', { name: 'Tổng quan' })).toHaveAttribute('aria-current', 'page');
    expect(document.getElementById('overview')).not.toHaveAttribute('hidden');
  });
  it('preserves refresh keyboard focus while preventing duplicate requests', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('ready'))
      .mockImplementation(() => new Promise(() => {}));
    healthFetch(fetchMock);
    const user = userEvent.setup(); render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống'));
    await screen.findByText('Dữ liệu sẵn sàng');
    const refresh = screen.getByRole('button', { name: 'Kiểm tra lại kết nối' });
    refresh.focus(); await user.keyboard('{Enter}');
    expect(refresh).toHaveFocus(); expect(refresh).not.toBeDisabled();
    expect(refresh).toHaveAttribute('aria-disabled', 'true');
    await user.keyboard('{Enter}'); expect(fetchMock).toHaveBeenCalledTimes(4);
  });
  it('keeps a verified API observation when SQL alone times out', async () => {
    vi.useFakeTimers();
    healthFetch(vi.fn().mockResolvedValueOnce(reply('live')).mockImplementationOnce((_url, options) =>
      new Promise((_resolve, reject) => options.signal.addEventListener('abort', () => reject(new DOMException('Timeout', 'AbortError'))))));
    render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống'));
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByText('Ứng dụng đã kết nối')).toBeInTheDocument();
    expect(screen.getByText('Đang kiểm tra dữ liệu')).toBeInTheDocument();
    await act(async () => { vi.advanceTimersByTime(8000); });
    expect(screen.getByText('Dữ liệu chưa sẵn sàng')).toBeInTheDocument();
    expect(screen.getByText('Ứng dụng đã kết nối')).toBeInTheDocument();
  });
  it('does not turn a completed healthy readback into a timeout later', async () => {
    vi.useFakeTimers();
    healthFetch(vi.fn().mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('ready')));
    render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống'));
    await act(async () => { await Promise.resolve(); });
    expect(screen.getByText('Dữ liệu sẵn sàng')).toBeInTheDocument();
    await act(async () => { vi.advanceTimersByTime(8000); });
    expect(screen.getByText('Dữ liệu sẵn sàng')).toBeInTheDocument();
  });
  it('starts checking, then displays Ứng dụng đã kết nối and Dữ liệu sẵn sàng from separate responses', async () => {
    healthFetch(vi.fn().mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('ready')));
    render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống'));
    expect(screen.getByText('Đang kiểm tra kết nối')).toBeInTheDocument();
    expect(await screen.findByText('Dữ liệu sẵn sàng')).toBeInTheDocument();
    expect(screen.getByText('Ứng dụng đã kết nối')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Thêm không gian/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Khám phá kho trống/ })).toHaveAttribute('href', '#catalog');
  });

  it('distinguishes a live API from unavailable SQL and recovers only on readback', async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('unavailable', false))
      .mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('ready'));
    healthFetch(fetchMock);
    const user = userEvent.setup();
    render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống'));
    expect(await screen.findByText('Dữ liệu chưa sẵn sàng')).toBeInTheDocument();
    expect(screen.getByText('Ứng dụng đã kết nối')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Kiểm tra lại kết nối' }));
    expect(await screen.findByText('Dữ liệu sẵn sàng')).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(4);
    for (const [, options] of fetchMock.mock.calls) expect(options.method).toBe('GET');
  });

  it('shows a connection failure without claiming the database is ready', async () => {
    healthFetch(vi.fn().mockRejectedValue(new TypeError('network down')));
    render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống'));
    expect(await screen.findByText('Chưa kết nối ứng dụng')).toBeInTheDocument();
    expect(screen.queryByText('Dữ liệu sẵn sàng')).not.toBeInTheDocument();
  });

  it('treats malformed health responses as unavailable rather than ready', async () => {
    healthFetch(vi.fn().mockResolvedValue({ ok: true, json: async () => ({ status: 'anything' }) }));
    render(<App />); fireEvent.click(screen.getByText('Trạng thái hệ thống'));
    expect(await screen.findByText('Chưa kết nối ứng dụng')).toBeInTheDocument();
    expect(screen.getByText('Dữ liệu chưa sẵn sàng')).toBeInTheDocument();
  });
});

 it('keeps diagnostics collapsed and provides account access without technical setup instructions', async () => {
  healthFetch(vi.fn().mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('ready')));
  const user = userEvent.setup(); render(<App />);
  expect(screen.getByText('Trạng thái hệ thống').closest('details')).not.toHaveAttribute('open');
  await user.click(screen.getByRole('link', { name: 'Đăng nhập / Đăng ký' }));
  expect(screen.getByRole('heading', { name: 'Tài khoản' })).toBeInTheDocument();
  expect(document.getElementById('account')).not.toHaveAttribute('hidden');
 });
 it('opens mobile navigation and closes it after choosing a real destination', async () => {
  healthFetch(vi.fn().mockResolvedValueOnce(reply('live')).mockResolvedValueOnce(reply('ready')));
  const user = userEvent.setup(); render(<App />);
  const toggle = screen.getByRole('button', { name: 'Mở menu' });
  expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await user.click(toggle); expect(toggle).toHaveAttribute('aria-expanded', 'true');
  await user.click(screen.getByRole('link', { name: 'Tìm kho & báo giá' }));
  expect(toggle).toHaveAttribute('aria-expanded', 'false');
  expect(document.getElementById('catalog')).not.toHaveAttribute('hidden');
 });

it('keeps guest discovery primary and explains account access on its dedicated page', async () => {
  healthFetch(vi.fn().mockResolvedValue(reply('live')));
  const user = userEvent.setup(); render(<App />);
  expect(screen.getByRole('link', { name: /Khám phá kho trống/ })).toBeInTheDocument();
  expect(screen.getByText('Chỉ cần một tài khoản.')).not.toBeVisible();
  await user.click(screen.getByRole('link', { name: 'Đăng nhập / Đăng ký' }));
  expect(screen.getByText('Chỉ cần một tài khoản.')).toBeVisible();
  expect(screen.getByRole('link', { name: 'Xem kho trước khi đăng ký' })).toHaveAttribute('href', '#catalog');
});


it('implements the supplied KhoSpace discovery hierarchy without removing account access', async () => {
  healthFetch(vi.fn().mockResolvedValue(reply('live')));
  render(<App />);
  expect(screen.getByRole('heading', { name: /Thêm không gian.*Bớt bận tâm/ })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: /KhoSpace/ })).toHaveAttribute('href', '#overview');
  expect(screen.getByRole('link', { name: 'Chọn diện tích' })).toHaveAttribute('href', '#planner');
  expect(screen.getByRole('link', { name: 'Đăng nhập / Đăng ký' })).toHaveAttribute('href', '#account');
});

it('focuses home sections from other routes and preserves the local planner draft', async () => {
  Element.prototype.scrollIntoView = vi.fn();
  healthFetch(vi.fn().mockResolvedValue(reply('live')));
  const user = userEvent.setup(); render(<App />);
  await user.click(screen.getByRole('button', { name: 'Tăng Thùng carton' }));
  await user.click(screen.getByRole('link', { name: 'Đăng nhập / Đăng ký' }));
  await user.click(screen.getByRole('link', { name: 'Chọn diện tích' }));
  expect(document.getElementById('planner')).toHaveFocus();
  expect(screen.getByRole('link', { name: 'Chọn diện tích' })).toHaveAttribute('aria-current', 'page');
  expect(screen.getByLabelText('Số lượng Thùng carton')).toHaveTextContent('7');
  await user.click(screen.getByRole('link', { name: 'Hướng dẫn' }));
  expect(document.getElementById('faq')).toHaveFocus();
  expect(screen.getByRole('link', { name: 'Hướng dẫn' })).toHaveAttribute('aria-current', 'page');
});


describe('mobile navigation dismissal', () => {
  it('closes with Escape from a navigation link and returns focus without changing route', async () => {
    healthFetch(vi.fn().mockResolvedValue(reply('live')));
    const user = userEvent.setup(); render(<App />);
    const toggle = screen.getByRole('button', { name: 'Mở menu' });
    await user.click(toggle);
    const destination = screen.getByRole('link', { name: 'Tìm kho & báo giá' });
    destination.focus();
    await user.keyboard('{Escape}');
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(toggle).toHaveFocus();
    expect(window.location.hash).toBe('');
    expect(document.getElementById('overview')).not.toHaveAttribute('hidden');
    await user.keyboard('{Enter}');
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
  });

  it('closes with Escape on the toggle and does not intercept other keys', async () => {
    healthFetch(vi.fn().mockResolvedValue(reply('live')));
    const user = userEvent.setup(); render(<App />);
    const toggle = screen.getByRole('button', { name: 'Mở menu' });
    await user.click(toggle);
    await user.keyboard('{ArrowRight}');
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    await user.keyboard('{Escape}');
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(toggle).toHaveFocus();
  });

  it('does not consume Escape when the menu is closed', async () => {
    healthFetch(vi.fn().mockResolvedValue(reply('live')));
    const user = userEvent.setup(); render(<App />);
    const toggle = screen.getByRole('button', { name: 'Mở menu' });
    toggle.focus();
    expect(fireEvent.keyDown(toggle, { key: 'Escape', cancelable: true })).toBe(true);
    expect(toggle).toHaveFocus();
    await user.keyboard('{Enter}');
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
  });

  it('does not steal focus or consume Escape from content outside the header', async () => {
    healthFetch(vi.fn().mockResolvedValue(reply('live')));
    const user = userEvent.setup(); render(<App />);
    const toggle = screen.getByRole('button', { name: 'Mở menu' });
    await user.click(toggle);
    const content = screen.getByRole('main'); content.focus();
    expect(fireEvent.keyDown(content, { key: 'Escape', cancelable: true })).toBe(true);
    expect(content).toHaveFocus();
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
  });
});


it('does not focus a hidden mobile toggle when navigation is still expanded after a desktop resize', async () => {
  healthFetch(vi.fn().mockResolvedValue(reply('live')));
  const user = userEvent.setup(); render(<App />);
  const toggle = screen.getByRole('button', { name: 'Mở menu' });
  await user.click(toggle);
  toggle.style.display = 'none';
  const destination = screen.getByRole('link', { name: 'Tìm kho & báo giá' }); destination.focus();
  await user.keyboard('{Escape}');
  expect(toggle).toHaveAttribute('aria-expanded', 'false');
  expect(destination).toHaveFocus();
});

it('respects Escape already handled by an inner navigation control', async () => {
  healthFetch(vi.fn().mockResolvedValue(reply('live')));
  const user = userEvent.setup(); render(<App />);
  const toggle = screen.getByRole('button', { name: 'Mở menu' });
  await user.click(toggle);
  const destination = screen.getByRole('link', { name: 'Tìm kho & báo giá' });
  destination.addEventListener('keydown', event => event.preventDefault(), { once: true });
  destination.focus(); await user.keyboard('{Escape}');
  expect(toggle).toHaveAttribute('aria-expanded', 'true');
  expect(destination).toHaveFocus();
});
