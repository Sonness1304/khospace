import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import ReportsPanel from './ReportsPanel';
const access = { userId: 'finance', organizationId: 'org-a', facilityId: 'facility-a', facilityRoles: ['Finance'], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
const data = (nextPage: number | null = null) => ({ schemaVersion: 'P2-RECEIVABLES-V1', observedAt: '2026-10-29T04:00:00Z', asOfDate: '2026-10-29', timeZone: 'Asia/Ho_Chi_Minh', organizationName: 'Synthetic customer', page: nextPage === null ? 1 : 0, nextPage,
 summary: { matchedStatements: 51, openStatements: 1, effectiveChargeVnd: '9007199254740993', effectivePaidVnd: '0', outstandingVnd: '9007199254740993', currentVnd: '0', overdue1To30Vnd: '9007199254740993', overdue31To60Vnd: '0', overdue61To90Vnd: '0', overdueOver90Vnd: '0' },
 items: [{ statementId: nextPage === null ? 'statement-b' : 'statement-a', leaseId: 'lease-a', rentalAccountId: 'account-a', periodStart: '2026-10-01', dueDate: '2026-10-01', originalVnd: '9007199254740993', additionsVnd: '0', creditsVnd: '0', allocationsVnd: '0', deductionsVnd: '0', releasesVnd: '0', effectiveChargeVnd: '9007199254740993', effectivePaidVnd: '0', outstandingVnd: '9007199254740993', daysOverdue: 28, bucket: 'Overdue1To30' }], source: 'Current balances, not historical revenue' });
afterEach(() => vi.unstubAllGlobals());
it('reads current aging with precise VND strings and a scoped fresh CSV link', async () => {
 const fetcher = vi.fn().mockResolvedValue(reply(data(1))); vi.stubGlobal('fetch', fetcher);
 render(<ReportsPanel access={access} />); const user = userEvent.setup();
 await user.click(screen.getByRole('button', { name: "Tải công nợ" }));
 expect(await screen.findByText('statement-a')).toBeInTheDocument(); expect(screen.getAllByText('9007199254740993').length).toBeGreaterThan(0);
 expect(screen.queryByText('9007199254740992')).not.toBeInTheDocument();
 expect(screen.getByRole('link', { name: "Xuất CSV công nợ" })).toHaveAttribute('href', expect.stringContaining('organizationId=org-a&facilityId=facility-a'));
 expect(fetcher.mock.calls[0][0]).toContain('/reports/receivables?'); expect(screen.getByText(/Tệp CSV tạo ảnh chụp dữ liệu mới/)).toBeInTheDocument();
});
it('paginates rows while keeping whole-range totals and clears data when due dates change', async () => {
 const fetcher = vi.fn().mockResolvedValueOnce(reply(data(1))).mockResolvedValueOnce(reply(data())); vi.stubGlobal('fetch', fetcher);
 render(<ReportsPanel access={access} />); const user = userEvent.setup();
 await user.click(screen.getByRole('button', { name: "Tải công nợ" })); await screen.findByText('statement-a');
 await user.click(screen.getByRole('button', { name: "Trang công nợ sau" })); await screen.findByText('statement-b');
 expect(fetcher.mock.calls[1][0]).toContain('page=1');
 await user.clear(screen.getByLabelText("Hạn thanh toán từ")); expect(screen.queryByText('statement-b')).not.toBeInTheDocument();
 expect(screen.getByRole('button', { name: "Tải công nợ" })).toBeDisabled();
});
it('does not expose financial reports to customer admins or managers and hides data on grant revocation', async () => {
 vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(data(1))));
 const view = render(<ReportsPanel access={{ ...access, facilityRoles: ['FacilityManager'], organizationRole: 'CustomerAdmin' }} />);
 expect(screen.queryByRole('button', { name: "Tải công nợ" })).not.toBeInTheDocument();
 view.rerender(<ReportsPanel access={access} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải công nợ" })); await screen.findByText('statement-a');
 view.rerender(<ReportsPanel access={{ ...access, facilityRoles: [] }} />); expect(screen.queryByText('statement-a')).not.toBeInTheDocument(); expect(screen.queryByRole('link', { name: "Xuất CSV công nợ" })).not.toBeInTheDocument();
});
it('discards a late response after workspace change and after interval change', async () => {
 let resolve: (value: unknown) => void = () => {}; const fetcher = vi.fn().mockImplementation(() => new Promise(r => { resolve = r; })); vi.stubGlobal('fetch', fetcher);
 const view = render(<ReportsPanel access={access} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải công nợ" }));
 view.rerender(<ReportsPanel access={{ ...access, organizationId: 'org-b' }} />);
 await act(async () => { resolve(reply(data(1))); }); expect(screen.queryByText('statement-a')).not.toBeInTheDocument();
 await user.click(screen.getByRole('button', { name: "Tải công nợ" })); await user.clear(screen.getByLabelText("Hạn thanh toán từ"));
 await act(async () => { resolve(reply(data(1))); }); expect(screen.queryByText('statement-a')).not.toBeInTheDocument();
});
it('shows reconciliation failure and refuses ranges over366days without a request', async () => {
 const fetcher = vi.fn().mockResolvedValue(reply({}, 409)); vi.stubGlobal('fetch', fetcher);
 render(<ReportsPanel access={access} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải công nợ" }));
 expect(await screen.findByText(/Hồ sơ tài chính cần đối soát/)).toBeInTheDocument();
 await user.clear(screen.getByLabelText("Hạn thanh toán từ")); await user.type(screen.getByLabelText("Hạn thanh toán từ"), '2020-01-01');
 await waitFor(() => expect(screen.getByRole('button', { name: "Tải công nợ" })).toBeDisabled()); expect(fetcher).toHaveBeenCalledTimes(1);
});
it('can read again after leaving and returning to a workspace while an old request is pending', async () => {
 const fetcher = vi.fn().mockImplementationOnce(() => new Promise(() => {})).mockResolvedValueOnce(reply(data(1))); vi.stubGlobal('fetch', fetcher);
 const view = render(<ReportsPanel access={access} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải công nợ" }));
 view.rerender(<ReportsPanel access={{ ...access, organizationId: 'org-b' }} />); view.rerender(<ReportsPanel access={access} />);
 expect(screen.getByRole('button', { name: "Tải công nợ" })).toBeEnabled();
 await user.click(screen.getByRole('button', { name: "Tải công nợ" })); expect(await screen.findByText('statement-a')).toBeInTheDocument();
});
it('shows empty results and generic failure while missing scope disables staff reads', async () => {
 const fetcher = vi.fn().mockResolvedValueOnce(reply({ ...data(), page: 0, items: [] })).mockRejectedValueOnce(Error('offline')); vi.stubGlobal('fetch', fetcher);
 const view = render(<ReportsPanel access={null} />); expect(screen.queryByRole('button', { name: "Tải công nợ" })).not.toBeInTheDocument();
 view.rerender(<ReportsPanel access={{ ...access, operatorAdmin: true, organizationId: '' }} />); expect(screen.getByRole('button', { name: "Tải công nợ" })).toBeDisabled();
 view.rerender(<ReportsPanel access={access} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải công nợ" }));
 expect(await screen.findByText(/Không có bảng kê đã phát hành phù hợp/)).toBeInTheDocument(); expect(screen.getByRole('button', { name: "Trang công nợ trước" })).toBeDisabled(); expect(screen.getByRole('button', { name: "Trang công nợ sau" })).toBeDisabled();
 await user.click(screen.getByRole('button', { name: "Tải công nợ" })); expect(await screen.findByText(/Không tải được công nợ/)).toBeInTheDocument(); expect(screen.queryByRole('link', { name: "Xuất CSV công nợ" })).not.toBeInTheDocument();
});
