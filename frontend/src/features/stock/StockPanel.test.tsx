import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import StockPanel from './StockPanel';
const access = { userId: 'customer', organizationId: 'org-a', facilityId: 'facility-a', facilityRoles: [], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('reads scoped balances and source-linked ledger without permitting direct balance editing', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [{ itemId: 'item-a', sku: 'BOX', baseUom: 'box', quantity: '3' }], total: 1 }))
    .mockResolvedValueOnce(reply({ items: [{ id: 'entry-a', sku: 'BOX', baseUom: 'box', delta: '-7', stockPostingId: 'posting-a', postedAt: '2026-10-28T00:00:00Z' }], total: 1 }));
  vi.stubGlobal('fetch', fetchMock); const view = render(<StockPanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID quyền sử dụng khoang xem tồn kho"), 'grant-a'); await user.click(screen.getByRole('button', { name: "Tải tồn kho" }));
  expect(await screen.findByText('3 box')).toBeInTheDocument(); expect(screen.getByText(/posting-a/)).toBeInTheDocument(); expect(fetchMock.mock.calls[0][0]).toContain('organizationId=org-a');
  view.rerender(<StockPanel access={{ ...access, organizationId: 'org-b' }} />); expect(screen.queryByText('3 box')).not.toBeInTheDocument();
});
it('hides old balances when readback fails and allows independent ledger pagination', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ items: [], total: 0 })).mockResolvedValueOnce(reply({ items: [], total: 51 }))
    .mockResolvedValueOnce(reply({ items: [], total: 0 })).mockResolvedValueOnce(reply({ items: [], total: 51 })).mockResolvedValueOnce(reply({}, 503)).mockResolvedValueOnce(reply({}, 503));
  vi.stubGlobal('fetch', fetchMock); render(<StockPanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID quyền sử dụng khoang xem tồn kho"), 'grant-a'); await user.click(screen.getByRole('button', { name: "Tải tồn kho" }));
  await user.click(await screen.findByRole('button', { name: "Trang nhật ký sau" })); expect(fetchMock.mock.calls[3][0]).toContain('page=2');
  await user.click(screen.getByRole('button', { name: "Tải tồn kho" })); expect(await screen.findByText(/Chưa xác minh được tồn kho/)).toBeInTheDocument();
});
