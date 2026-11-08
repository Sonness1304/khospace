import { expect, it } from 'vitest';
import { displayValue, noticeText, reasonLabel } from '../../locale/vi';

it('presents protocol states and roles in Vietnamese with safe unknown fallbacks', () => {
  expect(displayValue('PartPaid')).toBe('Đã trả một phần');
  expect(displayValue('Finance')).toBe('Kế toán');
  expect(displayValue('ReviewRequired')).toBe('Cần đối soát');
  expect(displayValue('INTERNAL_DIAGNOSTIC')).toBe('Chưa xác định');
  expect(reasonLabel('UNRESOLVED_ONLINE_PAYMENTS')).toContain('chưa rõ kết quả');
  expect(reasonLabel('RAW_SERVER_ERROR')).not.toContain('RAW_SERVER_ERROR');
});

it('localizes historical overdue notices without changing exact money text or observed identity', () => {
  const message = 'Statement statement-a was 12 days overdue on 2026-10-29; due 2026-10-17; outstanding VND 9007199254740993.1234. Observed 2026-10-29T01:00:00.0000000+00:00. Check the current statement before paying.';
  const result = noticeText('PaymentOverdue', message, 'statement-a');
  expect(result).toContain('Bảng kê statement-a quá hạn 12 ngày');
  expect(result).toContain('9007199254740993.1234 VND');
  expect(result).toContain('2026-10-29T01:00:00.0000000+00:00');
  expect(result).toContain('Kiểm tra bảng kê hiện tại trước khi thanh toán.');
});

it('shows Vietnamese lease and stock notice details while withholding unknown server message text', () => {
  expect(noticeText('LeaseEnding', 'Lease lease-a ends on 2026-11-01. Review renewal or clearance.')).toContain('Hợp đồng lease-a kết thúc ngày 2026-11-01');
  expect(noticeText('StockAction', 'Stock request request-a reached Approved. Check current status before acting.')).toContain('Đã phê duyệt');
  expect(noticeText('LeaseEnding', 'raw server diagnostic', 'lease-a')).toContain('Hợp đồng lease-a sắp hết hạn');
  expect(noticeText('UnknownNotice', 'raw server diagnostic')).not.toContain('raw server diagnostic');
});
