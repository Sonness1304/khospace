import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import FinancePanel from './FinancePanel';
const access = { userId: 'finance', organizationId: '', facilityId: 'facility-a', facilityRoles: ['Finance'], operatorAdmin: false };
const account = { id: 'account-a', organizationId: 'org-a', facilityId: 'facility-a', bookingId: 'booking-a', leaseId: 'lease-a', leaseActivatedAt: '2026-10-28T00:00:00Z', state: 'Open', version: 'account-version', requiredDeposit: '20', depositHeld: '20', billed: '100', rentalPaid: '0', outstanding: '100', unappliedVerified: '100', refundNeeded: false, receipts: [], statements: [], receiptTotal: 0, statementTotal: 0 };
const receipt = { id: 'receipt-a', rentalAccountId: 'account-a', organizationId: 'org-a', facilityId: 'facility-a', state: 'Recorded', version: 'receipt-version', reference: 'SYNTHETIC', method: 'BankTransfer', amount: '100', allocated: '0', unapplied: '100', allocations: [] };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('directs finance to existing manual-refund history without denying recorded payments', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply({ ...account, refundNeeded: true })));
  render(<FinancePanel access={access} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("ID tài khoản thuê cần xử lý tài chính"), 'account-a'); await user.click(screen.getByRole('button', { name: "Tải tài khoản thuê" }));
  expect(await screen.findByText(/Xem lịch sử quyết toán để tra chứng từ hoàn tiền thủ công/)).toBeInTheDocument(); expect(screen.queryByText(/No refund is marked paid/)).not.toBeInTheDocument();
});
it('rejects a recorded receipt only after evidence confirmation and preserves its identity', async () => {
  const mock = vi.fn().mockResolvedValueOnce(reply(receipt)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'receipt-a' } })).mockResolvedValueOnce(reply({ ...receipt, state: 'Rejected' })); vi.stubGlobal('fetch', mock); render(<FinancePanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID phiếu thu thanh toán"), 'receipt-a'); await user.click(screen.getByRole('button', { name: "Tải phiếu thu thanh toán" })); await screen.findByText("Đã ghi nhận"); expect(screen.getByRole('button', { name: "Từ chối phiếu thu đã ghi" })).toBeDisabled();
  await user.type(screen.getByLabelText("Lý do thao tác tài chính"), 'Evidence proves no actual receipt'); await user.click(screen.getByLabelText("Đã kiểm tra chứng từ phiếu thu độc lập")); await user.click(screen.getByRole('button', { name: "Từ chối phiếu thu đã ghi" })); await screen.findByText("Đã từ chối"); expect(mock.mock.calls[2][0]).toBe('/api/v1/receipts/receipt-a/reject');
});
it('shows refundable security separately and requires explicit finance receipt verification', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(account)).mockResolvedValueOnce(reply(receipt)).mockResolvedValueOnce(reply({ token: 'csrf' }))
    .mockResolvedValueOnce(reply({ result: { resourceId: 'receipt-a' } })).mockResolvedValueOnce(reply({ ...receipt, state: 'Verified' })).mockResolvedValueOnce(reply(account));
  vi.stubGlobal('fetch', fetchMock); render(<FinancePanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID tài khoản thuê cần xử lý tài chính"), 'account-a'); await user.click(screen.getByRole('button', { name: "Tải tài khoản thuê" }));
  expect(await screen.findByText("Tiền bảo đảm hoàn lại đang giữ: 20 VND")).toBeInTheDocument(); expect(screen.getByText("Tiền thuê còn nợ: 100 VND")).toBeInTheDocument();
  await user.type(screen.getByLabelText("ID phiếu thu thanh toán"), 'receipt-a'); await user.click(screen.getByRole('button', { name: "Tải phiếu thu thanh toán" })); await screen.findByText("Đã ghi nhận");
  await user.type(screen.getByLabelText("Lý do thao tác tài chính"), 'Matched synthetic bank evidence'); await user.click(screen.getByLabelText("Đã kiểm tra chứng từ phiếu thu độc lập")); await user.click(screen.getByRole('button', { name: "Xác minh phiếu thu thủ công" }));
  expect(await screen.findByText("Đã xác minh")).toBeInTheDocument(); expect(fetchMock.mock.calls[3][1].headers['If-Match']).toBe('"receipt-version"');
});
it('retains an unknown original verification through retry401 and hides data under foreign context', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(receipt)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 504)).mockResolvedValueOnce(reply({}, 404))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 401));
  vi.stubGlobal('fetch', fetchMock); const view = render(<FinancePanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID phiếu thu thanh toán"), 'receipt-a'); await user.click(screen.getByRole('button', { name: "Tải phiếu thu thanh toán" })); await screen.findByText("Đã ghi nhận");
  await user.type(screen.getByLabelText("Lý do thao tác tài chính"), 'Original verified evidence'); await user.click(screen.getByLabelText("Đã kiểm tra chứng từ phiếu thu độc lập")); await user.click(screen.getByRole('button', { name: "Xác minh phiếu thu thủ công" })); await screen.findByText(/Chưa xác định kết quả yêu cầu tài chính/);
  await user.click(screen.getByRole('button', { name: "Đọc kết quả yêu cầu tài chính" })); await user.click(await screen.findByRole('button', { name: "Thử lại yêu cầu tài chính gốc" }));
  expect(await screen.findByText(/Chưa xác định kết quả yêu cầu tài chính/)).toBeInTheDocument(); expect(fetchMock.mock.calls[5][1].body).toBe(fetchMock.mock.calls[2][1].body);
  view.rerender(<FinancePanel access={{ ...access, facilityId: 'facility-b' }} />); expect(screen.queryByText("Đã ghi nhận")).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: "Đọc kết quả yêu cầu tài chính" })).toBeDisabled();
});
it('keeps customer finance read-only and reports unavailable account without exposing old balances', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(account)).mockResolvedValueOnce(reply({}, 503)));
  render(<FinancePanel access={{ ...access, userId: 'customer', organizationId: 'org-a', facilityRoles: [] }} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID tài khoản thuê cần xử lý tài chính"), 'account-a'); await user.click(screen.getByRole('button', { name: "Tải tài khoản thuê" })); await screen.findByText("Tiền thuê còn nợ: 100 VND");
  expect(screen.queryByRole('button', { name: "Ghi phiếu thu thủ công" })).not.toBeInTheDocument(); await user.click(screen.getByRole('button', { name: "Tải tài khoản thuê" }));
  expect(await screen.findByText(/Chưa xác minh được dữ liệu tài chính/)).toBeInTheDocument(); expect(screen.queryByText("Tiền thuê còn nợ: 100 VND")).not.toBeInTheDocument();
});

it('records an exact whole-VND string without marking the new receipt verified', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(account)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'receipt-a' } }))
    .mockResolvedValueOnce(reply(receipt)).mockResolvedValueOnce(reply(account));
  vi.stubGlobal('fetch', fetchMock); render(<FinancePanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID tài khoản thuê cần xử lý tài chính"), 'account-a'); await user.click(screen.getByRole('button', { name: "Tải tài khoản thuê" })); await screen.findByText("Tiền thuê còn nợ: 100 VND");
  await user.type(screen.getByLabelText("Lý do thao tác tài chính"), 'Record bank transfer for review'); await user.type(screen.getByLabelText("Số tiền đã nhận (VND)"), '9007199254740993'); await user.type(screen.getByLabelText("Mã tham chiếu phiếu thu"), 'BANK-SYNTHETIC');
  await user.type(screen.getByLabelText("Thời điểm nhận (giờ địa phương)"), '2026-10-28T09:00'); await user.type(screen.getByLabelText("Tham chiếu chứng từ"), 'Synthetic ledger line 7'); await user.click(screen.getByRole('button', { name: "Ghi phiếu thu thủ công" }));
  expect(await screen.findByText("Đã ghi nhận")).toBeInTheDocument(); expect(JSON.parse(fetchMock.mock.calls[2][1].body).amount).toBe('9007199254740993');
});
it('allocates rent to an explicit charge target rather than the refundable deposit', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ ...receipt, state: 'Verified' })).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'receipt-a' } })).mockResolvedValueOnce(reply({ ...receipt, state: 'Verified', allocated: '1' }));
  vi.stubGlobal('fetch', fetchMock); render(<FinancePanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID phiếu thu thanh toán"), 'receipt-a'); await user.click(screen.getByRole('button', { name: "Tải phiếu thu thanh toán" })); await screen.findByText("Đã xác minh");
  await user.type(screen.getByLabelText("Lý do thao tác tài chính"), 'Pay one VND toward issued rent'); await user.selectOptions(screen.getByLabelText("Đích phân bổ tiền"), 'Charge'); await user.type(screen.getByLabelText("ID bảng kê nhận phân bổ"), 'statement-a'); await user.type(screen.getByLabelText("Số tiền phân bổ (VND)"), '1'); await user.click(screen.getByRole('button', { name: "Phân bổ tiền đã xác minh" }));
  expect(await screen.findByText(/Đã lưu yêu cầu tài chính/)).toBeInTheDocument(); expect(JSON.parse(fetchMock.mock.calls[2][1].body)).toMatchObject({ targetType: 'Charge', targetId: 'statement-a', amount: '1' });
});
it('issues a selected scheduled month only after activation and shows outstanding and overdue text', async () => {
  const statement = { id: 'statement-a', organizationId: 'org-a', facilityId: 'facility-a', rentalAccountId: 'account-a', periodStart: '2026-10-01', dueDate: '2026-10-01', total: '100', paid: '1', outstanding: '99', paymentState: 'PartPaid', overdue: true, lines: [{ id: 'line-a', startDate: '2026-10-28', endDate: '2026-11-01', amount: '100' }] };
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(account)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'statement-a' } })).mockResolvedValueOnce(reply(statement)).mockResolvedValueOnce(reply(account));
  vi.stubGlobal('fetch', fetchMock); render(<FinancePanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID tài khoản thuê cần xử lý tài chính"), 'account-a'); await user.click(screen.getByRole('button', { name: "Tải tài khoản thuê" })); await screen.findByText("Tiền thuê còn nợ: 100 VND");
  await user.type(screen.getByLabelText("Lý do thao tác tài chính"), 'Issue agreed October snapshots'); await user.type(screen.getByLabelText("Ngày đầu tháng của bảng kê"), '2026-10-01'); await user.type(screen.getByLabelText("Hạn thanh toán bảng kê"), '2026-10-01'); await user.click(screen.getByRole('button', { name: "Lập bảng kê tiền thuê" }));
  expect(await screen.findByText(/Đã trả một phần.*Quá hạn/)).toBeInTheDocument(); expect(JSON.parse(fetchMock.mock.calls[2][1].body).periodStart).toBe('2026-10-01');
});

it('clears entered finance identifiers and evidence when the authenticated context changes', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(reply(account))); const view = render(<FinancePanel access={access} />); const user = userEvent.setup();
  await user.type(screen.getByLabelText("ID tài khoản thuê cần xử lý tài chính"), 'account-a'); await user.click(screen.getByRole('button', { name: "Tải tài khoản thuê" })); await screen.findByText("Tiền thuê còn nợ: 100 VND");
  await user.type(screen.getByLabelText("Mã tham chiếu phiếu thu"), 'PRIVATE-CUSTOMER-A'); await user.type(screen.getByLabelText("Tham chiếu chứng từ"), 'PRIVATE-EVIDENCE-A');
  view.rerender(<FinancePanel access={{ ...access, userId: 'different-finance', facilityId: 'facility-b' }} />);
  expect(screen.getByLabelText("ID tài khoản thuê cần xử lý tài chính")).toHaveValue(''); expect(screen.queryByText("Tiền thuê còn nợ: 100 VND")).not.toBeInTheDocument();
});

it('paginates account receipts and statements independently and explicitly verifies mathematically zero security', async () => {
  const page = { ...account, requiredDeposit: '0', depositHeld: '0', receiptTotal: 51, statementTotal: 51, receiptsPage: 1, statementsPage: 1 };
  const fetchMock = vi.fn().mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply({ ...page, receiptsPage: 2 })).mockResolvedValueOnce(reply({ ...page, receiptsPage: 2, statementsPage: 2 }))
    .mockResolvedValueOnce(reply({ ...page, receiptsPage: 1, statementsPage: 2 })).mockResolvedValueOnce(reply(page)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'account-a' } })).mockResolvedValueOnce(reply({ ...page, zeroDepositVerifiedAt: '2026-10-28' }));
  vi.stubGlobal('fetch', fetchMock); render(<FinancePanel access={access} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("ID tài khoản thuê cần xử lý tài chính"), 'account-a'); await user.click(screen.getByRole('button', { name: "Tải tài khoản thuê" })); await screen.findByText("Tiền bảo đảm hoàn lại đang giữ: 0 VND");
  await user.click(screen.getByRole('button', { name: "Trang phiếu thu tài khoản sau" })); await user.click(screen.getByRole('button', { name: "Trang bảng kê tài khoản sau" })); await user.click(screen.getByRole('button', { name: "Trang phiếu thu tài khoản trước" })); await user.click(screen.getByRole('button', { name: "Trang bảng kê tài khoản trước" }));
  await user.type(screen.getByLabelText("Lý do thao tác tài chính"), 'Approve zero under agreed rounding'); await user.click(screen.getByRole('button', { name: "Xác minh tiền đặt cọc làm tròn bằng 0" })); expect(await screen.findByText(/Đã lưu yêu cầu tài chính/)).toBeInTheDocument(); expect(fetchMock.mock.calls[2][0]).toContain('statementsPage=2');
});
it('keeps refund-needed funds and issued statement details visible without finance write controls', async () => {
  const statement = { id: 'statement-a', organizationId: 'org-a', facilityId: 'facility-a', periodStart: '2026-10-01', dueDate: '2026-11-01', paymentState: 'Paid', overdue: false, total: '100', paid: '100', outstanding: '0', lines: [] };
  const fetchMock = vi.fn().mockResolvedValueOnce(reply({ ...account, refundNeeded: true, receipts: [{ id: 'receipt-a', reference: 'Bank A', amount: '100', state: 'Verified' }], statements: [{ id: 'statement-a', periodStart: '2026-10-01', total: '100' }] })).mockResolvedValueOnce(reply(statement)).mockResolvedValueOnce(reply({ ...receipt, state: 'Verified', allocations: [{ id: 'allocation-a', targetType: 'Deposit', amount: '20' }] }));
  vi.stubGlobal('fetch', fetchMock); render(<FinancePanel access={{ ...access, userId: 'customer', organizationId: 'org-a', facilityRoles: [] }} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("ID tài khoản thuê cần xử lý tài chính"), 'account-a'); await user.click(screen.getByRole('button', { name: "Tải tài khoản thuê" })); expect(await screen.findByText(/Cần tài chính kiểm tra hoặc hoàn tiền/)).toBeInTheDocument(); await user.click(screen.getByRole('button', { name: "Bảng kê 2026-10-01 · 100 VND" })); await screen.findByText(/Khoản thu đã lập: 100 VND.*Còn nợ: 0 VND/); await user.click(screen.getByRole('button', { name: "Bank A · 100 VND · Đã xác minh" })); expect(await screen.findByText("Tiền đặt cọc phân bổ: 20 VND")).toBeInTheDocument();
});
