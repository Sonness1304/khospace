import { expect, test } from '@playwright/test';

// Isolated synthetic QA only. All API/health routes are intercepted; no owner session or data.
test.beforeEach(async ({ page }) => {
  await page.route('**/health/*', route => route.fulfill({ json: { status: route.request().url().endsWith('/ready') ? 'ready' : 'live' } }));
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname;
    const body = path.endsWith('/public/facilities') ? { items: [{ id: 'f1', name: 'Kho QA tổng hợp', address: 'Địa chỉ thử nghiệm', permittedGoods: 'Hàng khô' }], nextCursor: null }
      : path.endsWith('/availability') ? { items: [2, 4, 6, 8].map((area, i) => ({ id: `u${i}`, code: `QA-${i}`, areaM2: String(area), billingBasis: i ? 'Daily' : 'CalendarMonth', dailyRate: '1000', monthlyRate: '25000', currency: 'VND' })), nextCursor: null }
      : { items: [], google: false };
    return route.fulfill({ status: path.endsWith('/auth/me') ? 401 : 200, json: body });
  });
});

test('fixture: quick search preserves criteria; cards compare and native dialog restores keyboard focus', async ({ page }, testInfo) => {
  const writes: string[] = []; page.on('request', req => { if (req.method() !== 'GET') writes.push(req.url()); });
  await page.goto('/');
  await page.getByLabel('Cơ sở muốn thuê').selectOption('f1');
  await page.getByLabel('Ngày chuyển vào', { exact: true }).fill('2028-01-01');
  await page.getByLabel('Ngày kết thúc tìm nhanh').fill('2028-02-01');
  await page.getByLabel('Diện tích cần thuê').selectOption('6');
  await page.getByRole('button', { name: /Tìm kho phù hợp/ }).click();
  await expect(page.getByRole('heading', { name: 'QA-0', exact: true })).toBeVisible();
  await expect(page.getByLabel('Diện tích tối thiểu (m²)')).toHaveValue('6');
  const detail = page.getByRole('button', { name: 'Chi tiết QA-0' });
  await detail.click(); await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Đóng', exact: true })).toBeFocused();
  await page.keyboard.press('Tab'); await expect(page.getByRole('button', { name: 'Chọn nhận báo giá' })).toBeFocused();
  await page.keyboard.press('Shift+Tab'); await expect(page.getByRole('button', { name: 'Đóng', exact: true })).toBeFocused();
  await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toHaveCount(0); await expect(detail).toBeFocused();
  await detail.click(); await page.getByRole('button', { name: 'Chọn nhận báo giá' }).click();
  await expect(page.getByRole('checkbox', { name: 'QA-0', exact: true })).toBeChecked();
  for (let i = 0; i < 4; i++) await page.getByRole('checkbox', { name: `So sánh QA-${i}`, exact: true }).click();
  await expect(page.getByText('Chỉ so sánh tối đa 3 khoang.', { exact: false })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'So sánh QA-3', exact: true })).not.toBeChecked();
  for (const width of [1440, 768, 390, 320]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`catalog-${width}.png`), fullPage: true });
  }
  await page.getByLabel('Diện tích tối thiểu (m²)').fill('12');
  await expect(page.getByRole('checkbox', { name: 'QA-0', exact: true })).toBeDisabled();
  await detail.click(); await expect(page.getByRole('button', { name: 'Chọn nhận báo giá' })).toBeDisabled();
  expect(writes).toEqual([]);
});

test('fixture: planner navigation, reset and browser history preserve account and local drafts', async ({ page }, testInfo) => {
  await page.goto('/#account');
  await page.getByLabel('Email', { exact: true }).fill('draft@example.test');
  await page.getByRole('link', { name: 'Chọn diện tích', exact: true }).click();
  await expect(page.locator('#planner')).toBeFocused();
  await page.getByRole('button', { name: 'Tăng Thùng carton' }).click();
  await expect(page.getByLabel('Số lượng Thùng carton')).toHaveText('7');
  await page.getByRole('button', { name: /Xem kho phù hợp/ }).click();
  await expect(page.getByLabel('Diện tích tối thiểu (m²)')).toHaveValue('6');
  await expect(page.getByRole('button', { name: 'Tìm khoang còn trống' })).toBeDisabled();
  await page.goBack(); await expect(page.locator('#planner')).toBeFocused();
  await expect(page.getByLabel('Số lượng Thùng carton')).toHaveText('7');
  await page.getByRole('link', { name: 'Hướng dẫn', exact: true }).click();
  await expect(page.locator('#faq')).toBeFocused();
  await page.goBack(); await expect(page.locator('#planner')).toBeFocused();
  await page.screenshot({ path: testInfo.outputPath('planner-desktop.png'), fullPage: true });
  await page.getByRole('button', { name: 'Xóa lựa chọn' }).click();
  await expect(page.getByRole('button', { name: /Xem kho phù hợp/ })).toBeDisabled();
  await page.getByRole('link', { name: 'Đăng nhập / Đăng ký', exact: true }).click();
  await expect(page.getByLabel('Email', { exact: true })).toHaveValue('draft@example.test');
});

test('fixture regression: browser Back closes modal top layer without losing catalog state', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('Cơ sở muốn thuê').selectOption('f1');
  await page.getByRole('button', { name: /Tìm kho phù hợp/ }).click();
  await page.getByRole('checkbox', { name: 'QA-0', exact: true }).check();
  await page.getByRole('button', { name: 'Chi tiết QA-0' }).click();
  await expect(page.locator('dialog:modal')).toHaveCount(1);
  await page.goBack();
  await expect(page.getByRole('heading', { name: 'Thêm không gian. Bớt bận tâm.' })).toBeVisible();
  await expect(page.locator('dialog:modal')).toHaveCount(0);
  await expect(page.locator('main')).toBeFocused();
  await page.goForward();
  await expect(page.locator('dialog:modal')).toHaveCount(0);
  await expect(page.getByRole('checkbox', { name: 'QA-0', exact: true })).toBeChecked();
  await page.getByRole('link', { name: 'Đăng nhập / Đăng ký', exact: true }).click();
  await expect(page.getByLabel('Email', { exact: true })).toBeVisible();
});

test('fixture regression: same-view section navigation has a single current menu item', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('link', { name: 'Tổng quan', exact: true })).toHaveAttribute('aria-current', 'page');
  await page.getByRole('link', { name: 'Chọn diện tích', exact: true }).click();
  await expect(page.locator('#workspace-navigation a[aria-current]')).toHaveCount(1);
  await expect(page.getByRole('link', { name: 'Chọn diện tích', exact: true })).toHaveAttribute('aria-current', 'page');
  await page.getByRole('link', { name: 'Hướng dẫn', exact: true }).click();
  await expect(page.getByRole('link', { name: 'Hướng dẫn', exact: true })).toHaveAttribute('aria-current', 'page');
  await page.goBack(); await expect(page.getByRole('link', { name: 'Chọn diện tích', exact: true })).toHaveAttribute('aria-current', 'page');
});
