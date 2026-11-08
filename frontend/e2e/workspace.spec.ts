import { expect, test } from '@playwright/test';

// Real browser, real seeded Demo host only. This suite never starts a server or writes business data.
test('seeded customer can navigate the responsive workspace with keyboard and CSS zoom emulation', async ({ page }) => {
  await page.goto('/#account');
  await page.getByLabel('Email').fill('admin.a@example.test');
  await page.getByLabel('Mật khẩu').fill('WarehouseRent_Demo!42');
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await page.getByRole('link', { name: 'Tài khoản của tôi' }).click();
  await expect(page.getByText('Bạn đang đăng nhập bằng', { exact: false })).toBeVisible();
  await expect(page.getByRole('group', { name: 'Không gian của bạn' })).toContainText('Synthetic Customer A');
  await expect(page.getByRole('combobox', { name: 'Không gian của bạn' })).toHaveCount(0);
  await expect(page.getByText(/Vai trò hiện tại: Chủ không gian/)).toBeVisible();
  for (const width of [390, 768, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    if (width < 768) await page.getByRole('button', { name: 'Mở menu' }).click();
    await expect(page.getByRole('navigation', { name: 'Điều hướng chính' })).toBeVisible();
  }
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.evaluate(() => { document.body.style.zoom = '200%'; });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  // Reload retains the authenticated session but resets focus before testing the first tab stop.
  await page.reload();
  await page.getByRole('link', { name: 'Tài khoản của tôi' }).click();
  await expect(page.getByText('Bạn đang đăng nhập bằng', { exact: false })).toBeVisible();
  await page.keyboard.press('Control+Home'); await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Đến nội dung chính' })).toBeFocused();
  await page.keyboard.press('Enter'); await expect(page.locator('main')).toBeFocused();
  await page.getByRole('button', { name: 'Đăng xuất' }).click();
  await expect(page.getByRole('heading', { name: 'Chào mừng trở lại' })).toBeVisible();
});
