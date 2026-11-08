import { expect, test } from '@playwright/test';

// Isolated intercepted fixture only. Never attach to the owner's profile or credentials.
// Added while local-browser control was disabled; execution must remain explicitly pending.
test('fixture: notice navigation binds the record, requires current read and preserves history without writes', async ({ page }) => {
 const leaseId = 'aaaaaaaa-1111-4111-8111-aaaaaaaaaaaa';
 const requestId = 'bbbbbbbb-2222-4222-8222-bbbbbbbbbbbb';
 const writes: string[] = []; const recordReads: string[] = [];
 page.on('request', request => {
  if (request.method() !== 'GET') writes.push(request.url());
  if (new URL(request.url()).pathname === `/api/v1/leases/${leaseId}`) recordReads.push(request.url());
 });
 await page.route('**/health/*', route => route.fulfill({ json: { status: route.request().url().endsWith('/ready') ? 'ready' : 'live' } }));
 await page.route('**/api/**', route => {
  const path = new URL(route.request().url()).pathname;
  const body = path.endsWith('/auth/me') ? { userId: 'fixture-user', userName: 'qa@example.test', operatorAdmin: false,
   organizations: [{ id: 'org-a', name: 'Khách hàng QA', role: 'CustomerAdmin' }],
   facilities: [{ id: 'facility-a', name: 'Kho QA', role: 'FacilityManager' }] }
   : path.endsWith('/notices') ? { items: [
    { id: 'n1', kind: 'LeaseEnding', aggregateId: leaseId, message: 'fixture', createdAt: '2026-10-31T00:00:00Z' },
    { id: 'n2', kind: 'StockAction', aggregateId: requestId, message: 'fixture', createdAt: '2026-10-31T00:00:00Z' },
   ], total: 2 } : path === `/api/v1/leases/${leaseId}` ? { id: leaseId, organizationId: 'org-a', facilityId: 'facility-a', state: 'Active', units: [], lines: [], startDate: '2028-01-01', endDate: '2028-02-01' }
    : { items: [], total: 0, google: false };
  return route.fulfill({ json: body });
 });
 await page.goto('/#account');
 await expect(page.getByRole('group', { name: 'Không gian của bạn' })).toContainText('Khách hàng QA');
 await page.getByLabel('Kho được phân công').selectOption('facility-a');
 await page.getByRole('link', { name: 'Tổng quan', exact: true }).click();
 await page.getByRole('button', { name: 'Tải thông báo' }).click();
 await page.getByRole('button', { name: 'Mở hợp đồng liên quan' }).click();
 await expect(page.locator('main')).toBeFocused();
 await expect(page.getByLabel('ID hợp đồng thuê')).toHaveValue(leaseId);
 expect(recordReads).toHaveLength(0);
 await page.getByRole('button', { name: 'Tải hợp đồng thuê' }).click();
 await expect(page.getByText('Đang hoạt động', { exact: true })).toBeVisible();
 await page.goBack();
 await page.getByRole('button', { name: 'Mở hợp đồng liên quan' }).click();
 await expect(page.getByText('Đang hoạt động', { exact: true })).toHaveCount(0);
 expect(recordReads).toHaveLength(1);
 await page.goBack();
 await page.getByRole('button', { name: 'Mở yêu cầu nhập / xuất liên quan' }).click();
 await expect(page.getByLabel('ID yêu cầu nhập xuất')).toHaveValue(requestId);
 for (const width of [1440, 768, 390, 320]) {
  await page.setViewportSize({ width, height: 900 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
 }
 expect(writes).toEqual([]);
});
