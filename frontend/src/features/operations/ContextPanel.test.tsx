import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import ContextPanel from './ContextPanel';
afterEach(() => vi.unstubAllGlobals());
it('lets operator explicitly select a facility from paginated authorized metadata', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => ({ items: [{ id: 'facility-a', name: 'Warehouse A' }], total: 51 }) }));
  const changed = vi.fn(); render(<ContextPanel access={{ userId: 'operator', organizationId: '', facilityId: '', facilityRoles: [], operatorAdmin: true }} onChange={changed} />); const user = userEvent.setup();
  await screen.findByLabelText("Cơ sở đang làm việc"); expect(changed).not.toHaveBeenCalled(); await user.selectOptions(await screen.findByLabelText("Cơ sở đang làm việc"), 'facility-a');
  expect(changed).toHaveBeenCalledWith(expect.objectContaining({ facilityId: 'facility-a', operatorAdmin: true })); expect(screen.getByRole('button', { name: "Trang cơ sở sau" })).toBeEnabled();
});

it('uses public cursor pages for customers and keeps a safe error on failure', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce({ ok: true, json: async () => ({ items: [{ id: 'facility-a', name: 'A' }], nextCursor: 'A' }) }).mockResolvedValueOnce({ ok: true, json: async () => ({ items: [{ id: 'facility-b', name: 'B' }], nextCursor: null }) }).mockRejectedValueOnce(new Error('offline'));
  vi.stubGlobal('fetch', fetchMock); render(<ContextPanel access={{ userId: 'customer', organizationId: 'org-a', facilityId: '', facilityRoles: [], operatorAdmin: false }} onChange={vi.fn()} />); const user = userEvent.setup();
  await screen.findByLabelText("Cơ sở đang làm việc"); await user.click(await screen.findByRole('button', { name: "Trang cơ sở sau" })); await screen.findByRole('option', { name: 'B' }); expect(fetchMock.mock.calls[1][0]).toContain('after=A');
  await user.click(screen.getByRole('button', { name: "Trang cơ sở trước" })); expect(await screen.findByText(/Không tải được cơ sở có thể chọn/)).toBeInTheDocument();
});

it('returns to the correct public cursor when paging backwards', async () => {
  const reply = (id: string, nextCursor: string | null) => ({ ok: true, json: async () => ({ items: [{ id, name: id }], nextCursor }) });
  const fetchMock = vi.fn().mockResolvedValueOnce(reply('first', 'A')).mockResolvedValueOnce(reply('second', 'B')).mockResolvedValueOnce(reply('third', null)).mockResolvedValueOnce(reply('second', 'B')).mockResolvedValueOnce(reply('first', 'A'));
  vi.stubGlobal('fetch', fetchMock);
  render(<ContextPanel access={{ userId: 'customer', organizationId: 'org-a', facilityId: '', facilityRoles: [], operatorAdmin: false }} onChange={vi.fn()} />);
  const user = userEvent.setup();
  await screen.findByLabelText("Cơ sở đang làm việc");
  await user.click(await screen.findByRole('button', { name: "Trang cơ sở sau" }));
  await screen.findByRole('option', { name: 'second' });
  await user.click(screen.getByRole('button', { name: "Trang cơ sở sau" }));
  await screen.findByRole('option', { name: 'third' });
  await user.click(screen.getByRole('button', { name: "Trang cơ sở trước" }));
  await screen.findByRole('option', { name: 'second' });
  await user.click(screen.getByRole('button', { name: "Trang cơ sở trước" }));
  await screen.findByRole('option', { name: 'first' });
  expect(fetchMock.mock.calls[3][0]).toContain('after=A');
  expect(fetchMock.mock.calls[4][0]).toBe('/api/v1/public/facilities');
});

it('presents facility context in Vietnamese before choosing a facility', () => {
  render(<ContextPanel access={null} onChange={vi.fn()} />);
  expect(screen.getByRole('region', { name: 'Ngữ cảnh cơ sở kho' })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Tải danh sách cơ sở' })).toBeDisabled();
});

it('reloads choices for a new identity and ignores a late result from the old scope', async () => {
  let resolveOld!: (value: unknown) => void;
  const fetchMock = vi.fn().mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }))
    .mockResolvedValueOnce({ ok: true, json: async () => ({ items: [{ id: 'new-facility', name: 'Kho mới' }], nextCursor: null }) });
  vi.stubGlobal('fetch', fetchMock); const changed = vi.fn();
  const { rerender } = render(<ContextPanel access={{ userId: 'old-user', organizationId: 'old-org', facilityId: '', facilityRoles: [], operatorAdmin: false }} onChange={changed} />);
  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
  rerender(<ContextPanel access={{ userId: 'new-user', organizationId: 'new-org', facilityId: '', facilityRoles: [], operatorAdmin: false }} onChange={changed} />);
  expect(await screen.findByRole('option', { name: 'Kho mới' })).toBeInTheDocument();
  await act(async () => resolveOld({ ok: true, json: async () => ({ items: [{ id: 'old-facility', name: 'Kho cũ' }], nextCursor: null }) }));
  expect(screen.queryByRole('option', { name: 'Kho cũ' })).not.toBeInTheDocument();
  expect(screen.getByLabelText('Cơ sở đang làm việc')).toHaveValue('');
  expect(changed).not.toHaveBeenCalled();
});
