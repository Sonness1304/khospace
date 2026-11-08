import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import DocumentsPanel from './DocumentsPanel';
const access = { userId: 'admin', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: '', facilityRoles: [], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
afterEach(() => vi.unstubAllGlobals());
it('lists private attachments with authorized download links and hides foreign context', async () => {
  const mock = vi.fn().mockResolvedValue(reply({ items: [{ id: 'doc-a', fileName: 'handover.pdf', contentType: 'application/pdf', bytes: 88, sha256: 'hash', revokedAt: null }], total: 1 }));
  vi.stubGlobal('fetch', mock); const view = render(<DocumentsPanel access={access} selectedLeaseId="lease-a" />); const user = userEvent.setup();
  await user.click(screen.getByRole('button', { name: "Tải danh sách tài liệu" })); expect(await screen.findByRole('link', { name: "Tải xuống handover.pdf" })).toHaveAttribute('href', '/api/v1/documents/doc-a/download');
  view.rerender(<DocumentsPanel access={{ ...access, organizationId: 'org-b' }} selectedLeaseId="lease-a" />);
  expect(screen.queryByRole('link', { name: "Tải xuống handover.pdf" })).not.toBeInTheDocument();
});
it.each([403, 404, 429])('preserves file payload and command identity through uncertain retry%s', async status => {
  const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(Error('lost'))
    .mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, status)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { id: 'doc' } }));
  vi.stubGlobal('fetch', mock); render(<DocumentsPanel access={access} selectedLeaseId="lease-a" />); const user = userEvent.setup();
  await user.upload(screen.getByLabelText("Tệp tài liệu riêng tư"), new File(['%PDF-1.4 obj %%EOF'], 'handover.pdf', { type: 'application/pdf' }));
  await user.type(screen.getByLabelText("Lý do thao tác tài liệu"), 'Handover evidence'); await user.click(screen.getByRole('button', { name: "Tải lên tài liệu riêng tư" })); await screen.findByText(/Chưa xác định kết quả tài liệu/);
  await user.click(screen.getByRole('button', { name: "Thử lại yêu cầu tài liệu gốc" })); await screen.findByText(/Chưa xác định kết quả tài liệu/); await user.click(screen.getByRole('button', { name: "Thử lại yêu cầu tài liệu gốc" })); await screen.findByText(/Đã hoàn tất yêu cầu tài liệu/);
  expect(mock.mock.calls[1][1].body).toBe(mock.mock.calls[5][1].body); expect(mock.mock.calls[1][1].headers['Idempotency-Key']).toBe(mock.mock.calls[5][1].headers['Idempotency-Key']);
});
it('rejects oversized files locally and hides revoke from ordinary members', async () => {
  const mock = vi.fn().mockResolvedValue(reply({ items: [{ id: 'doc', fileName: 'file.pdf', contentType: 'application/pdf', bytes: 1, sha256: 'hash', revokedAt: null }], total: 1 }));
  vi.stubGlobal('fetch', mock); render(<DocumentsPanel access={{ ...access, organizationRole: 'Member' }} selectedLeaseId="lease-a" />); const user = userEvent.setup();
  await user.upload(screen.getByLabelText("Tệp tài liệu riêng tư"), new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'large.pdf', { type: 'application/pdf' }));
  await screen.findByText("Chọn tệp đúng định dạng, tối đa 10 MiB."); expect(mock).not.toHaveBeenCalled();
  await user.click(screen.getByRole('button', { name: "Tải danh sách tài liệu" })); await screen.findByRole('link', { name: "Tải xuống file.pdf" }); expect(screen.queryByRole('button', { name: "Thu hồi file.pdf" })).not.toBeInTheDocument();
});
