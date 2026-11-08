import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import RenewalPreviewPanel from './RenewalPreviewPanel';
const access = { userId: 'admin', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: 'facility-a', facilityRoles: [], operatorAdmin: false };
const reply = (body: unknown) => ({ ok: true, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('reads current versions then previews added rent without offering an apply action', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ id: 'lease-a', version: 'lv1' })).mockResolvedValueOnce(reply({ id: 'quote-a', version: 'qv1' })).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ leaseId: 'lease-a', quoteId: 'quote-a', currentEnd: '2026-11-01', proposedEnd: '2026-11-03', addedRentalSubtotal: '1200', canApply: true, reasons: [], capacityReserved: false, applied: false }));
  vi.stubGlobal('fetch', fetchMock); const view = render(<RenewalPreviewPanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng cần gia hạn"), 'lease-a'); await user.type(screen.getByLabelText("ID báo giá kỳ bổ sung"), 'quote-a'); await user.click(screen.getByRole('button', { name: "Xem trước gia hạn" }));
  expect(await screen.findByText('1200 VND')).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Apply renewal' })).not.toBeInTheDocument();
  expect(JSON.parse(fetchMock.mock.calls[3][1].body)).toMatchObject({ expectedLeaseVersion: 'lv1', expectedQuoteVersion: 'qv1' });
  view.rerender(<RenewalPreviewPanel access={{ ...access, organizationId: 'org-b' }} />); expect(screen.queryByText('1200 VND')).not.toBeInTheDocument(); expect(screen.getByLabelText("ID hợp đồng cần gia hạn")).toHaveValue('');
});
it('shows an unavailable preview without claiming a saved renewal', async () => {
  vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline'))); render(<RenewalPreviewPanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID hợp đồng cần gia hạn"), 'lease-a'); await user.type(screen.getByLabelText("ID báo giá kỳ bổ sung"), 'quote-a'); await user.click(screen.getByRole('button', { name: "Xem trước gia hạn" })); expect(await screen.findByText(/Chưa xác minh được bản xem trước gia hạn/)).toBeInTheDocument();
});
