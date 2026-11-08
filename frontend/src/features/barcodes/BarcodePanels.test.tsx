import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, expect, it, vi } from 'vitest';
import BarcodeRegistryPanel, { BarcodeLookupPanel } from './BarcodePanels';
import StockRequestPanel from '../stock-requests/StockRequestPanel';
const access = { userId: 'customer', organizationId: 'org-a', organizationRole: 'CustomerAdmin', facilityId: 'facility-a', facilityRoles: [] as string[], operatorAdmin: false };
const reply = (body: unknown, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
const found = { leaseId: 'lease-a', organizationId: 'org-a', facilityId: 'facility-a', itemId: 'item-a', sku: 'BOX', description: 'Synthetic boxes', baseUom: 'box', kind: 'GTIN', canonicalValue: '04006381333931', selectionOnly: true };
afterEach(() => vi.unstubAllGlobals());
it('exposes readable IDs for loaded customer items so first-label registration needs no developer tools', async () => {
 vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ items: [{ id: 'item-visible-uuid', sku: 'BOX', baseUom: 'box' }], total: 1 })));
 render(<StockRequestPanel access={access} />); const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: "Tải mặt hàng khách hàng" }));
 await user.click(await screen.findByText("ID mặt hàng khách hàng để đăng ký mã vạch")); expect(screen.getByText('item-visible-uuid', { selector: 'code' })).toBeVisible();
});
it.each([false, true])('retains the created item ID when its list readback fails=%s', async failed => {
 const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'item-new-uuid' } })).mockResolvedValueOnce(reply({ items: [], total: 0 }, failed ? 500 : 200)); vi.stubGlobal('fetch', mock);
 const view = render(<StockRequestPanel access={access} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("Lý do thao tác kho"), 'Synthetic first label'); await user.type(screen.getByLabelText("Mã SKU mặt hàng"), 'NEW'); await user.type(screen.getByLabelText("Mô tả mặt hàng"), 'Synthetic box'); await user.type(screen.getByLabelText("Đơn vị tính cơ sở"), 'box');
 await user.click(screen.getByRole('button', { name: "Tạo mặt hàng khách hàng" })); await screen.findByText(/ID mặt hàng: item-new-uuid/);
 expect(mock).toHaveBeenCalledTimes(3);
 view.rerender(<StockRequestPanel access={{ ...access, organizationId: 'org-b' }} />); expect(screen.queryByText(/ID mặt hàng: item-new-uuid/)).not.toBeInTheDocument();
 view.rerender(<StockRequestPanel access={{ ...access, userId: 'other-customer' }} />); expect(screen.queryByText(/ID mặt hàng: item-new-uuid/)).not.toBeInTheDocument();
});
it.each(['leaseId', 'organizationId'])('rejects an accessible but mismatched lookup %s', async field => {
 vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply({ ...found, [field]: 'other' })));
 render(<BarcodeLookupPanel access={access} leaseId="lease-a" onUse={vi.fn()} />); const user = userEvent.setup();
 await user.type(screen.getByLabelText("Mã vạch được quét"), '4006381333931{Enter}'); await screen.findByText(/Không thể xác minh mã vạch/);
 expect(screen.queryByRole('button', { name: "Dùng mặt hàng trong yêu cầu" })).not.toBeInTheDocument();
});
it('invalidates a successful result when its code changes and honors a blocked draft', async () => {
 vi.stubGlobal('fetch', vi.fn().mockResolvedValue(reply(found))); const selected = vi.fn();
 const view = render(<BarcodeLookupPanel access={access} leaseId="lease-a" onUse={selected} />); const user = userEvent.setup();
 await user.type(screen.getByLabelText("Mã vạch được quét"), '4006381333931{Enter}'); await screen.findByText('BOX · box');
 view.rerender(<BarcodeLookupPanel access={access} leaseId="lease-a" onUse={selected} disabled />); expect(screen.getByRole('button', { name: "Dùng mặt hàng trong yêu cầu" })).toBeDisabled();
 await user.type(screen.getByLabelText("Mã vạch được quét"), '2'); expect(screen.queryByText('BOX · box')).not.toBeInTheDocument(); expect(selected).not.toHaveBeenCalled();
});
it('scanner Enter performs only a scoped lookup and explicit use selects an item', async () => {
 const mock = vi.fn().mockResolvedValue(reply(found)); const selected = vi.fn(); vi.stubGlobal('fetch', mock);
 render(<BarcodeLookupPanel access={access} leaseId="lease-a" onUse={selected} />); const user = userEvent.setup();
 await user.type(screen.getByLabelText("Mã vạch được quét"), '4006381333931{Enter}'); await screen.findByText('BOX · box'); expect(selected).not.toHaveBeenCalled();
 await user.click(screen.getByRole('button', { name: "Dùng mặt hàng trong yêu cầu" })); expect(selected).toHaveBeenCalledWith(expect.objectContaining({ itemId: 'item-a', organizationId: 'org-a' }));
 expect(mock).toHaveBeenCalledTimes(1); expect(mock.mock.calls[0][0]).toContain('/leases/lease-a/barcode-lookup?kind=GTIN&value=4006381333931'); expect(mock.mock.calls[0][1].method).toBeUndefined();
});
it('hides a successful lookup when code or context changes and ignores an old async result', async () => {
 let finish!: (value: unknown) => void; const mock = vi.fn().mockImplementationOnce(() => new Promise(resolve => { finish = resolve; })); vi.stubGlobal('fetch', mock);
 const view = render(<BarcodeLookupPanel access={access} leaseId="lease-a" onUse={vi.fn()} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("Mã vạch được quét"), '4006381333931{Enter}');
 view.rerender(<BarcodeLookupPanel access={{ ...access, organizationId: 'org-b' }} leaseId="lease-b" onUse={vi.fn()} />); finish(reply(found));
 expect(screen.queryByText('BOX · box')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: "Dùng mặt hàng trong yêu cầu" })).not.toBeInTheDocument();
});
it('assigned staff lookup never offers customer request creation and inaccessible lookup shows safe feedback', async () => {
 const mock = vi.fn().mockResolvedValueOnce(reply(found)).mockResolvedValueOnce(reply({}, 404)); vi.stubGlobal('fetch', mock);
 render(<BarcodeLookupPanel access={{ ...access, userId: 'clerk', organizationId: '', organizationRole: undefined, facilityRoles: ['Clerk'] }} onUse={vi.fn()} />); const user = userEvent.setup();
 await user.type(screen.getByLabelText("ID hợp đồng để tra mã vạch"), 'lease-a'); await user.type(screen.getByLabelText("Mã vạch được quét"), '4006381333931{Enter}'); await screen.findByText('BOX · box'); expect(screen.queryByRole('button', { name: "Dùng mặt hàng trong yêu cầu" })).not.toBeInTheDocument();
 await user.clear(screen.getByLabelText("Mã vạch được quét")); await user.type(screen.getByLabelText("Mã vạch được quét"), 'not-found{Enter}'); await screen.findByText(/Không thể xác minh mã vạch/);
});
it('lookup selection fills the request item but never changes quantity or creates stock automatically', async () => {
 const mock = vi.fn().mockResolvedValue(reply(found)); vi.stubGlobal('fetch', mock); render(<StockRequestPanel access={access} />); const user = userEvent.setup();
 await user.type(screen.getByLabelText("ID hợp đồng của yêu cầu"), 'lease-a'); await user.type(screen.getByLabelText("Mã vạch được quét"), '4006381333931{Enter}'); await screen.findByText('BOX · box'); await user.click(screen.getByRole('button', { name: "Dùng mặt hàng trong yêu cầu" }));
 expect(screen.getByLabelText("Mặt hàng 1")).toHaveValue('item-a'); expect(screen.getByLabelText("Số lượng 1")).toHaveValue(''); expect(mock).toHaveBeenCalledTimes(1);
 await user.type(screen.getByLabelText("Số lượng 1"), '7'); await user.click(screen.getByRole('button', { name: "Dùng mặt hàng trong yêu cầu" })); expect(screen.getByLabelText("Số lượng 1")).toHaveValue('7'); expect(screen.queryByLabelText("Mặt hàng 2")).not.toBeInTheDocument(); expect(mock).toHaveBeenCalledTimes(1);
});
it('reads further pages and invalidates a label list when the target item changes', async () => {
 const row = { id: 'barcode-a', itemId: 'item-a', kind: 'Internal', canonicalValue: 'PAGE-1', active: false, version: 'v1' };
 const mock = vi.fn().mockResolvedValueOnce(reply({ items: [row], total: 51 })).mockResolvedValueOnce(reply({ items: [{ ...row, id: 'barcode-b', canonicalValue: 'PAGE-2' }], total: 51 })); vi.stubGlobal('fetch', mock);
 render(<BarcodeRegistryPanel access={access} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("ID mặt hàng đăng ký mã vạch"), 'item-a'); await user.click(screen.getByRole('button', { name: "Tải mã vạch mặt hàng" })); await screen.findByRole('button', { name: "Kích hoạt PAGE-1" });
 await user.click(screen.getByRole('button', { name: "Trang mã vạch sau" })); await screen.findByRole('button', { name: "Kích hoạt PAGE-2" }); expect(mock.mock.calls[1][0]).toContain('page=2');
 await user.type(screen.getByLabelText("ID mặt hàng đăng ký mã vạch"), '-other'); expect(screen.queryByRole('button', { name: "Kích hoạt PAGE-2" })).not.toBeInTheDocument();
});
it.each([401, 403, 404, 429])('keeps the exact unknown alias command after retry%s and blocks foreign-context retry', async status => {
 const mock = vi.fn().mockResolvedValueOnce(reply({ token: 'csrf' })).mockRejectedValueOnce(Error('lost')).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, status)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'barcode-a' } })); vi.stubGlobal('fetch', mock);
 const view = render(<BarcodeRegistryPanel access={access} />); const user = userEvent.setup(); await user.type(screen.getByLabelText("ID mặt hàng đăng ký mã vạch"), 'item-a'); await user.type(screen.getByLabelText("Giá trị mã vạch"), '4006381333931'); await user.type(screen.getByLabelText("Lý do thay đổi mã vạch"), 'Register printed fixture');
 await user.click(screen.getByRole('button', { name: "Đăng ký mã vạch" })); await screen.findByText(/Chưa xác định kết quả thao tác mã vạch/);
 await user.click(screen.getByRole('button', { name: "Thử lại yêu cầu mã vạch gốc" })); await screen.findByText(/Chưa xác định kết quả thao tác mã vạch/);
 view.rerender(<BarcodeRegistryPanel access={{ ...access, organizationId: 'org-b' }} />); expect(screen.getByRole('button', { name: "Thử lại yêu cầu mã vạch gốc" })).toBeDisabled();
 view.rerender(<BarcodeRegistryPanel access={access} />); await user.click(screen.getByRole('button', { name: "Thử lại yêu cầu mã vạch gốc" })); await screen.findByText(/Đã hoàn tất thao tác mã vạch/);
 expect(mock.mock.calls[1][1].body).toBe(mock.mock.calls[5][1].body); expect(mock.mock.calls[1][1].headers['Idempotency-Key']).toBe(mock.mock.calls[5][1].headers['Idempotency-Key']);
});
it('shows paginated alias status and changes only the selected original version', async () => {
 const row = { id: 'barcode-a', itemId: 'item-a', kind: 'Internal', canonicalValue: 'BOX-01', active: true, version: 'v1' };
 const mock = vi.fn().mockResolvedValueOnce(reply({ items: [row], total: 1 })).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({ result: { resourceId: 'barcode-a' } })); vi.stubGlobal('fetch', mock); render(<BarcodeRegistryPanel access={access} />); const user = userEvent.setup();
 await user.type(screen.getByLabelText("ID mặt hàng đăng ký mã vạch"), 'item-a'); await user.click(screen.getByRole('button', { name: "Tải mã vạch mặt hàng" })); await screen.findByRole('button', { name: "Vô hiệu hóa BOX-01" }); await user.type(screen.getByLabelText("Lý do thay đổi mã vạch"), 'Retire printed label'); await user.click(screen.getByRole('button', { name: "Vô hiệu hóa BOX-01" })); await screen.findByText(/Đã hoàn tất thao tác mã vạch/);
 expect(mock.mock.calls[2][1].headers['If-Match']).toBe('"v1"'); expect(JSON.parse(mock.mock.calls[2][1].body).active).toBe(false);
});
it('members can read aliases but cannot register or change them; definite errors release a fresh command', async () => {
 const mock = vi.fn().mockResolvedValueOnce(reply({}, 404)).mockResolvedValueOnce(reply({ token: 'csrf' })).mockResolvedValueOnce(reply({}, 422)); vi.stubGlobal('fetch', mock); const view = render(<BarcodeRegistryPanel access={{ ...access, organizationRole: 'Member' }} />); const user = userEvent.setup();
 await user.type(screen.getByLabelText("ID mặt hàng đăng ký mã vạch"), 'missing'); await user.click(screen.getByRole('button', { name: "Tải mã vạch mặt hàng" })); await screen.findByText(/Không tải được danh sách mã vạch/); expect(screen.queryByRole('button', { name: "Đăng ký mã vạch" })).not.toBeInTheDocument();
 view.rerender(<BarcodeRegistryPanel access={access} />); await user.type(screen.getByLabelText("Giá trị mã vạch"), 'invalid'); await user.type(screen.getByLabelText("Lý do thay đổi mã vạch"), 'Invalid value'); await user.click(screen.getByRole('button', { name: "Đăng ký mã vạch" })); await screen.findByText(/Thay đổi mã vạch bị từ chối/); expect(screen.queryByRole('button', { name: "Thử lại yêu cầu mã vạch gốc" })).not.toBeInTheDocument();
});
