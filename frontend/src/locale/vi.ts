// Presentation labels never replace protocol values sent to the API.
const labels: Record<string, string> = {
  GTIN: 'GTIN', Internal: 'Nội bộ', Active: 'Đang hoạt động', Disabled: 'Đã vô hiệu hóa',
  Draft: 'Bản nháp', Submitted: 'Đã gửi', Approved: 'Đã phê duyệt', Rejected: 'Đã từ chối',
  Completed: 'Đã hoàn tất', Fulfilled: 'Đã thực hiện xong', Cancelled: 'Đã hủy', Canceled: 'Đã hủy', Pending: 'Đang chờ', Held: 'Đang giữ chỗ',
  Confirmed: 'Đã xác nhận', Expired: 'Đã hết hạn', Scheduled: 'Đã lên lịch',
  ClearancePending: 'Chờ giải phóng khoang', Cleared: 'Đã giải phóng', Terminated: 'Đã chấm dứt',
  Open: 'Đang mở', Closed: 'Đã đóng', Recorded: 'Đã ghi nhận', Verified: 'Đã xác minh',
  Unverified: 'Chưa xác minh', PartPaid: 'Đã trả một phần', Paid: 'Đã thanh toán', Unpaid: 'Chưa thanh toán',
  Deposit: 'Tiền đặt cọc', Charge: 'Khoản thu', Receipt: 'Nhập kho', Issue: 'Xuất kho', Transfer: 'Chuyển kho',
  Reversal: 'Đảo giao dịch', CountAdjustment: 'Điều chỉnh kiểm đếm', Count: 'Kiểm đếm',
  RefundDeposit: 'Hoàn tiền đặt cọc', RefundUnapplied: 'Hoàn tiền chưa phân bổ', Deduction: 'Khấu trừ',
  Credit: 'Giảm trừ', Supplement: 'Bổ sung', Service: 'Dịch vụ', RentSupplement: 'Bổ sung tiền thuê',
  Inbound: 'Vào kho', Outbound: 'Ra kho', CheckedIn: 'Đã vào cổng', Departed: 'Đã rời cổng',
  Created: 'Đã tạo', Creating: 'Đang tạo', Ready: 'Sẵn sàng', Unknown: 'Chưa xác định',
  ReviewRequired: 'Cần đối soát', Failed: 'Không thành công', Abandoned: 'Đã hủy trước khi gửi',
  Sandbox: 'Thử nghiệm', Live: 'Thực tế', Retired: 'Đã ngừng sử dụng', Published: 'Công khai', Hidden: 'Ẩn',
  Member: 'Thành viên', CustomerAdmin: 'Quản lý khách hàng', Clerk: 'Nhân viên kho', Sales: 'Kinh doanh',
  Finance: 'Kế toán', FacilityManager: 'Quản lý kho', OperatorAdmin: 'Quản trị vận hành',
  BankTransfer: 'Chuyển khoản ngân hàng', Cash: 'Tiền mặt', Other: 'Khác', Daily: 'Theo ngày', CalendarMonth: 'Theo tháng dương lịch',
  Settled: 'Đã tất toán', Current: 'Chưa quá hạn', PaymentOverdue: 'Thanh toán quá hạn',
  LeaseEnding: 'Hợp đồng sắp hết hạn', StockAction: 'Yêu cầu nhập xuất cần xử lý',
};

export function displayValue(value: string | null | undefined): string {
  return value ? labels[value] ?? 'Chưa xác định' : 'Chưa xác định';
}

const reasons: Record<string, string> = {
  PHYSICAL_CLEARANCE_REQUIRED: 'Cần hoàn tất bàn giao và giải phóng khoang thực tế.',
  INVENTORY_CLEARANCE_REQUIRED: 'Cần xác minh hết hàng, không còn yêu cầu kho đang chờ và khoang đã được giải phóng.',
  UNBILLED_RENTAL_SEGMENTS: 'Còn kỳ thuê chưa lập bảng kê.', OUTSTANDING_CHARGES: 'Còn khoản thu chưa được tất toán.',
  DEPOSIT_NOT_SETTLED: 'Tiền đặt cọc chưa được quyết toán.', UNAPPLIED_FUNDS: 'Còn tiền chưa phân bổ.',
  UNVERIFIED_RECEIPTS: 'Còn phiếu thu chưa xác minh.', UNRESOLVED_ONLINE_PAYMENTS: 'Còn thanh toán trực tuyến chưa rõ kết quả.',
  LEASE_NOT_RENEWABLE: 'Trạng thái hợp đồng hiện tại không cho phép gia hạn.', REQUOTE_REQUIRED: 'Cần báo giá mới còn hiệu lực.',
  UNIT_INELIGIBLE: 'Khoang hoặc cơ sở không đủ điều kiện cho thuê.', SPACE_CONFLICT: 'Lịch thuê xung đột với lịch sử dụng khoang.',
  OTHER_PHYSICAL_OCCUPANCY: 'Khoang đang được khách hàng khác sử dụng thực tế.',
};
export function reasonLabel(value: string): string {
  return reasons[value] ?? 'Cần kiểm tra điều kiện nghiệp vụ hiện tại trước khi tiếp tục.';
}

const auditActions: Record<string, string> = {
  Login: 'Đăng nhập', Logout: 'Đăng xuất', CustomerRegistered: 'Đăng ký tài khoản khách hàng', GoogleTicketConsumed: 'Đã sử dụng phiên xác thực Google',
  FacilityCreated: 'Đã tạo cơ sở', FacilityChanged: 'Đã cập nhật cơ sở', ZoneCreated: 'Đã tạo khu vực',
  UnitCreated: 'Đã tạo khoang', UnitChanged: 'Đã cập nhật khoang', UnitRetired: 'Đã ngừng sử dụng khoang',
  RateSet: 'Đã đặt đơn giá', MaintenanceCreated: 'Đã tạo lịch bảo trì', QuoteCreated: 'Đã tạo báo giá',
  HoldCreated: 'Đã giữ chỗ', BookingConfirmed: 'Đã xác nhận đặt chỗ', RentalCancelled: 'Đã hủy thuê',
  LeaseCreated: 'Đã tạo hợp đồng', LeaseHandedOver: 'Đã bàn giao khoang', PhysicalClearance: 'Đã giải phóng khoang',
  LeaseTerminated: 'Đã chấm dứt hợp đồng', ItemCreated: 'Đã tạo mặt hàng', StockRequestCreated: 'Đã tạo yêu cầu nhập xuất',
  StockRequestSubmitted: 'Đã gửi yêu cầu nhập xuất', StockRequestApproved: 'Đã duyệt yêu cầu nhập xuất',
  StockRequestRejected: 'Đã từ chối yêu cầu nhập xuất', StockRequestCancelled: 'Đã hủy yêu cầu nhập xuất',
  ReceiptRecorded: 'Đã ghi phiếu thu', ReceiptVerified: 'Đã xác minh phiếu thu', ReceiptRejected: 'Đã từ chối phiếu thu',
  FundsAllocated: 'Đã phân bổ tiền', ZeroDepositVerified: 'Đã xác minh tiền đặt cọc bằng 0', StatementIssued: 'Đã lập bảng kê',
  StatementAdjusted: 'Đã điều chỉnh bảng kê', RentalSupplementIssued: 'Đã lập khoản thuê bổ sung',
  StockTransferred: 'Đã chuyển kho', StockReversed: 'Đã đảo giao dịch kho', StockCountRecorded: 'Đã ghi kiểm đếm',
  StockCountApproved: 'Đã duyệt kiểm đếm', StockCountCancelled: 'Đã hủy kiểm đếm',
  SettlementRecorded: 'Đã ghi quyết toán', RentalAccountClosed: 'Đã đóng tài khoản thuê',
  DocumentUploaded: 'Đã tải lên tài liệu', DocumentRevoked: 'Đã thu hồi tài liệu',
  MembershipAdded: 'Đã thêm thành viên', MembershipDeactivated: 'Đã vô hiệu hóa thành viên', MembershipChanged: 'Đã đổi thành viên',
  PaymentNotification: 'Đã nhận thông báo thanh toán',
};
export function auditActionLabel(value: string | undefined): string {
  return value ? auditActions[value] ?? 'Thao tác nghiệp vụ đã ghi nhận' : 'Thao tác nghiệp vụ đã ghi nhận';
}

export function noticeText(kind: string, message: string, aggregateId?: string): string {
  if (kind === 'LeaseEnding') {
    const match = /^Lease (\S+) ends on (\d{4}-\d{2}-\d{2})\. Review renewal or clearance\.$/.exec(message);
    return match ? `Hợp đồng ${match[1]} kết thúc ngày ${match[2]}. Kiểm tra việc gia hạn hoặc giải phóng khoang.`
      : `Hợp đồng${aggregateId ? ` ${aggregateId}` : ''} sắp hết hạn. Mở hợp đồng để kiểm tra thời hạn và phương án xử lý hiện tại.`;
  }
  if (kind === 'StockAction') {
    const match = /^Stock request (\S+) reached (\w+)\. Check current status before acting\.$/.exec(message);
    return match ? `Yêu cầu nhập xuất ${match[1]} chuyển sang trạng thái ${displayValue(match[2])}. Kiểm tra trạng thái hiện tại trước khi thao tác.`
      : `Yêu cầu nhập xuất${aggregateId ? ` ${aggregateId}` : ''} cần được xem xét. Mở yêu cầu để kiểm tra trạng thái hiện tại.`;
  }
  if (kind === 'PaymentOverdue') {
    // Keep money as the original decimal text; conversion to Number could lose precision.
    const match = /^Statement (\S+) was (\d+) days overdue on (\d{4}-\d{2}-\d{2}); due (\d{4}-\d{2}-\d{2}); outstanding VND ([0-9]+(?:\.[0-9]+)?)\. Observed (\S+)\. Check the current statement before paying\.$/.exec(message);
    return match ? `Bảng kê ${match[1]} quá hạn ${match[2]} ngày vào ${match[3]}; hạn thanh toán ${match[4]}; còn nợ ${match[5]} VND. Ghi nhận ${match[6]}. Kiểm tra bảng kê hiện tại trước khi thanh toán.`
      : `Bảng kê${aggregateId ? ` ${aggregateId}` : ''} có thông báo quá hạn. Kiểm tra số dư hiện tại trước khi thanh toán.`;
  }
  return 'Có thông báo nghiệp vụ mới. Mở hồ sơ liên quan để kiểm tra trạng thái hiện tại.';
}
