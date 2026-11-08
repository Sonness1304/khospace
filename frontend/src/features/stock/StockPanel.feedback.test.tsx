import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import StockPanel from './StockPanel';

const access = { userId: 'customer', organizationId: 'org-a', facilityId: 'facility-a', facilityRoles: [], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
const balances = { items: [{ itemId: 'item-a', sku: 'QA-BOX', baseUom: 'box', quantity: '3' }], total: 1 };
const ledger = { items: [], total: 0 };
const input = () => screen.getByLabelText('ID quyền sử dụng khoang xem tồn kho');
async function lookup() { const user = userEvent.setup(); await user.type(input(), 'grant-a'); await user.click(screen.getByRole('button', { name: 'Tải tồn kho' })); return user; }
afterEach(() => vi.unstubAllGlobals());

it('focuses associated readable failure and clears it on even a normalized-equivalent edit', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ detail: 'PRIVATE_SQL_SENTINEL' }, 503)));
  render(<StockPanel access={access} />); const user = await lookup();
  const error = await screen.findByRole('alert'); await waitFor(() => expect(error).toHaveFocus());
  expect(screen.getByRole('form', { name: 'Tra cứu tồn kho' })).toHaveAttribute('aria-describedby', error.id);
  expect(screen.queryByText('PRIVATE_SQL_SENTINEL')).not.toBeInTheDocument();
  await user.type(input(), ' '); expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

it('focuses verified results, hides them on draft edits, and never restores them when the draft returns', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(balances)).mockResolvedValueOnce(reply(ledger)));
  render(<StockPanel access={access} />); const user = await lookup();
  const result = await screen.findByRole('region', { name: 'Kết quả tồn kho' }); await waitFor(() => expect(result).toHaveFocus());
  await user.type(input(), ' '); expect(screen.queryByText('3 box')).not.toBeInTheDocument();
  await user.keyboard('{Backspace}'); expect(screen.queryByText('3 box')).not.toBeInTheDocument();
});

it.each([false, true])('does not revive a late read after a raw edit and return (rejected: %s)', async rejected => {
  let finish!: (value: ReturnType<typeof reply>) => void;
  const pending = new Promise<ReturnType<typeof reply>>(resolve => { finish = resolve; });
  vi.stubGlobal('fetch', vi.fn().mockReturnValueOnce(pending).mockResolvedValueOnce(reply(ledger)));
  render(<StockPanel access={access} />); const user = await lookup();
  await user.type(input(), ' '); await user.keyboard('{Backspace}');
  await act(async () => { finish(reply(balances, rejected ? 503 : 200)); });
  expect(screen.queryByText('3 box')).not.toBeInTheDocument(); expect(screen.queryByText(/Chưa xác minh được tồn kho/)).not.toBeInTheDocument(); expect(input()).toHaveFocus();
});

it('does not revive an in-flight read after leaving and returning to the original organization', async () => {
  let finish!: (value: ReturnType<typeof reply>) => void;
  const pending = new Promise<ReturnType<typeof reply>>(resolve => { finish = resolve; });
  vi.stubGlobal('fetch', vi.fn().mockReturnValueOnce(pending).mockResolvedValueOnce(reply(ledger)));
  const view = render(<StockPanel access={access} />); await lookup();
  view.rerender(<StockPanel access={{ ...access, organizationId: 'org-b' }} />); view.rerender(<StockPanel access={access} />);
  await act(async () => { finish(reply(balances)); }); expect(screen.queryByText('3 box')).not.toBeInTheDocument();
});

it('honors an initial selected grant but clears old results on a new external selection', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(balances)).mockResolvedValueOnce(reply(ledger)); vi.stubGlobal('fetch', fetchMock);
  const view = render(<StockPanel access={access} selectedId="grant-a" />); expect(input()).toHaveValue('grant-a');
  await userEvent.setup().click(screen.getByRole('button', { name: 'Tải tồn kho' })); await screen.findByText('3 box');
  view.rerender(<StockPanel access={access} selectedId="grant-b" />); expect(input()).toHaveValue('grant-b'); expect(screen.queryByText('3 box')).not.toBeInTheDocument();
});

it('allows an explicit GET-only retry after failure without resending any business command', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply(balances)).mockResolvedValueOnce(reply(ledger));
  vi.stubGlobal('fetch', fetchMock); render(<StockPanel access={access} />); const user = await lookup(); await screen.findByText(/Chưa xác minh được tồn kho/);
  await user.click(screen.getByRole('button', { name: 'Tải tồn kho' })); await screen.findByText('3 box'); expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(fetchMock).toHaveBeenCalledTimes(4); for (const [, options] of fetchMock.mock.calls) { expect(options.method).toBeUndefined(); expect(options.cache).toBe('no-store'); }
});

it('removes a staff-only view immediately when current staff permission is revoked', async () => {
  const staff = { ...access, organizationId: '', facilityRoles: ['Clerk'] };
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(balances)).mockResolvedValueOnce(reply(ledger)));
  const view = render(<StockPanel access={staff} />); const user = userEvent.setup(); await user.type(screen.getByLabelText('ID tổ chức khách hàng xem tồn kho'), 'org-a');
  await user.type(input(), 'grant-a'); await user.click(screen.getByRole('button', { name: 'Tải tồn kho' })); await screen.findByText('3 box');
  view.rerender(<StockPanel access={{ ...staff, facilityRoles: [] }} />); expect(screen.queryByText('3 box')).not.toBeInTheDocument();
});


it('invalidates pending feedback when the customer membership role changes', async () => {
  let finish!: (value: ReturnType<typeof reply>) => void;
  const pending = new Promise<ReturnType<typeof reply>>(resolve => { finish = resolve; });
  vi.stubGlobal('fetch', vi.fn().mockReturnValueOnce(pending).mockResolvedValueOnce(reply(ledger)));
  const view = render(<StockPanel access={{ ...access, organizationRole: 'CustomerAdmin' }} />); await lookup();
  view.rerender(<StockPanel access={{ ...access, organizationRole: 'CustomerMember' }} />);
  await act(async () => { finish(reply({}, 503)); }); expect(screen.queryByText(/Chưa xác minh được tồn kho/)).not.toBeInTheDocument();
});
