import { expect, test } from '@playwright/test';

test('real foundation health, readback and responsive UI A', async ({ page, request }, testInfo) => {
  const live = await request.get('/health/live');
  expect(live.status()).toBe(200);
  const ready = await request.get('/health/ready');
  expect([200, 503]).toContain(ready.status());
  const businessWrites: string[] = [];
  page.on('request', req => { if (req.method() !== 'GET') businessWrites.push(req.url()); });
  await page.goto('/');
  await page.getByText('Trạng thái hệ thống', { exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Thêm không gian. Bớt bận tâm.' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Kết nối ứng dụng' }).locator('strong')).toContainText('Ứng dụng đã kết nối');
  await expect(page.getByRole('region', { name: 'Kết nối dữ liệu' }).locator('strong')).toContainText(ready.ok() ? 'Dữ liệu sẵn sàng' : 'Dữ liệu chưa sẵn sàng');
  for (const [width, height] of [[1920, 1080], [1440, 900], [1366, 768], [1024, 768], [768, 1024], [390, 844], [320, 844]]) {
    await page.setViewportSize({ width, height });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await expect(page.getByRole('button', { name: 'Kiểm tra lại kết nối' })).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath(`foundation-${width}.png`), fullPage: true });
  }
  await page.keyboard.press('Control+Home');
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Đến nội dung chính' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('main')).toBeFocused();
  await page.getByRole('button', { name: 'Kiểm tra lại kết nối' }).focus();
  await expect(page.getByRole('button', { name: 'Kiểm tra lại kết nối' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('region', { name: 'Kết nối ứng dụng' }).locator('strong')).toContainText('Ứng dụng đã kết nối');
  expect(businessWrites).toEqual([]);
});

test('fixture: SQL failure and retry do not create business commands', async ({ page }) => {
  let recover = false;
  const writes: string[] = [];
  page.on('request', request => { if (request.method() !== 'GET') writes.push(request.url()); });
  await page.route('**/api/**', route => route.fulfill({ status: route.request().url().endsWith('/auth/me') ? 401 : 200, contentType: 'application/json', body: JSON.stringify({ items: [], google: false }) }));
  await page.route('**/health/live', route => route.fulfill({ contentType: 'application/json', body: JSON.stringify({ status: 'live', observedAt: '2026-10-29T00:00:00Z' }) }));
  await page.route('**/health/ready', route => route.fulfill({ status: recover ? 200 : 503,
    contentType: 'application/json', body: JSON.stringify({ status: recover ? 'ready' : 'unavailable', observedAt: new Date().toISOString() }) }));
  await page.goto('/');
  await page.getByText('Trạng thái hệ thống', { exact: true }).click();
  await expect(page.getByRole('region', { name: 'Kết nối dữ liệu' }).locator('strong')).toContainText('Dữ liệu chưa sẵn sàng');
  await expect(page.getByRole('alert')).toContainText('Ứng dụng đang chạy');
  recover = true;
  await page.getByRole('button', { name: 'Kiểm tra lại kết nối' }).click();
  await expect(page.getByRole('region', { name: 'Kết nối dữ liệu' }).locator('strong')).toContainText('Dữ liệu sẵn sàng');
  await expect(page.getByRole('alert')).toHaveCount(0);
  expect(writes).toEqual([]);
});
