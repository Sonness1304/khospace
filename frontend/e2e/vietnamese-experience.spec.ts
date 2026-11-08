import { expect, test } from '@playwright/test';

// Isolated browser fixture: every API request is intercepted. No owner data or Google account is used.
const me = { userId: 'fixture-customer', userName: 'khach@example.test', operatorAdmin: false,
  organizations: [{ id: 'fixture-org', name: 'Cửa hàng của tôi', role: 'CustomerAdmin' }], facilities: [] };
test.beforeEach(async ({ page }) => {
  await page.route('**/api/**', route => route.fulfill({ status: route.request().url().endsWith('/auth/me') ? 401 : 200,
    contentType: 'application/json', body: JSON.stringify(route.request().url().endsWith('/auth/providers') ? { google: false } : { token: 'fixture-csrf', items: [] }) }));
  await page.route('**/health/*', route => route.fulfill({ contentType: 'application/json', body: JSON.stringify({ status: route.request().url().endsWith('/ready') ? 'ready' : 'live', observedAt: '2026-10-29T00:00:00Z' }) }));
});

test('fixture: Vietnamese guest layout, mobile navigation, zoom and keyboard work without overflow', async ({ page }, testInfo) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Thêm không gian. Bớt bận tâm.' })).toBeVisible();
  await expect(page.getByRole('navigation').getByRole('link')).toHaveCount(5);
  expect(await page.locator('html').getAttribute('lang')).toBe('vi');
  for (const width of [1440, 1024, 768, 390, 320]) {
    await page.setViewportSize({ width, height: 900 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    if (width < 768) {
      await page.getByRole('button', { name: 'Mở menu' }).click();
      await expect(page.getByRole('navigation', { name: 'Điều hướng chính' })).toBeVisible();
      await page.getByRole('link', { name: 'Tài khoản', exact: true }).click();
      await expect(page.getByRole('button', { name: 'Mở menu' })).toHaveAttribute('aria-expanded', 'false');
      await expect(page.getByLabel('Email', { exact: true })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.goto('/');
    }
    await page.screenshot({ path: testInfo.outputPath(`vietnamese-${width}.png`), fullPage: true });
  }
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.evaluate(() => { document.body.style.zoom = '200%'; });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await page.reload(); await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: 'Đến nội dung chính' })).toBeFocused();
  await page.keyboard.press('Enter'); await expect(page.locator('main')).toBeFocused();
  expect(errors).toEqual([]);
});

test('fixture: registration validates confirmation and publishes only the new customer workspace', async ({ page }) => {
  let registered = false; const registrations: unknown[] = [];
  await page.route('**/api/v1/auth/me', route => route.fulfill({ status: registered ? 200 : 401, contentType: 'application/json', body: JSON.stringify(registered ? me : {}) }));
  await page.route('**/api/v1/auth/register', route => {
    registrations.push(route.request().postDataJSON()); expect(route.request().headers()['x-csrf-token']).toBe('fixture-csrf');
    registered = true; return route.fulfill({ contentType: 'application/json', body: JSON.stringify({ authenticated: true }) });
  });
  await page.goto('/#account'); await page.getByRole('button', { name: 'Đăng ký', exact: true }).click();
  await page.getByLabel('Tên cá nhân hoặc doanh nghiệp').fill('Cửa hàng của tôi');
  await page.getByLabel('Email', { exact: true }).fill('khach@example.test');
  await page.getByLabel('Mật khẩu', { exact: true }).fill('SyntheticCustomer!42');
  await page.getByLabel('Nhập lại mật khẩu').fill('DifferentPassword!42');
  await page.getByRole('button', { name: 'Tạo tài khoản' }).click();
  await expect(page.getByRole('status')).toContainText('Mật khẩu nhập lại chưa khớp'); expect(registrations).toHaveLength(0);
  await page.getByLabel('Nhập lại mật khẩu').fill('SyntheticCustomer!42');
  await page.getByRole('button', { name: 'Tạo tài khoản' }).click();
  await expect(page.getByRole('group', { name: 'Không gian của bạn' })).toContainText('Cửa hàng của tôi');
  await expect(page.getByRole('combobox', { name: 'Không gian của bạn' })).toHaveCount(0);
  expect(registrations).toEqual([{ name: 'Cửa hàng của tôi', email: 'khach@example.test', password: 'SyntheticCustomer!42' }]);
  await expect(page.getByText('Vai trò hiện tại: Chủ không gian')).toBeVisible();
  await expect(page.getByLabel('Kho được phân công')).toHaveCount(0);
});

test('fixture: normal email login shows an actionable error and succeeds on a deliberate retry', async ({ page }) => {
  let signedIn = false; let attempts = 0;
  await page.route('**/api/v1/auth/me', route => route.fulfill({ status: signedIn ? 200 : 401, contentType: 'application/json', body: JSON.stringify(signedIn ? me : {}) }));
  await page.route('**/api/v1/auth/login', route => { attempts++; signedIn = attempts === 2;
    return route.fulfill({ status: signedIn ? 200 : 401, contentType: 'application/json', body: JSON.stringify({ authenticated: signedIn }) }); });
  await page.goto('/#account'); await page.getByLabel('Email', { exact: true }).fill('khach@example.test');
  await page.getByLabel('Mật khẩu', { exact: true }).fill('WrongPassword!42');
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('Email hoặc mật khẩu chưa đúng');
  await expect(page.getByLabel('Mật khẩu', { exact: true })).toHaveValue(''); expect(attempts).toBe(1);
  await page.getByLabel('Mật khẩu', { exact: true }).fill('SyntheticCustomer!42');
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Đăng xuất' })).toBeVisible(); expect(attempts).toBe(2);
});

test('fixture: configured Google uses the same-origin challenge and collision explains local login', async ({ page }) => {
  await page.route('**/api/v1/auth/providers', route => route.fulfill({ contentType: 'application/json', body: JSON.stringify({ google: true }) }));
  await page.goto('/?authError=google-link-required#account');
  await expect(page.getByRole('link', { name: 'Tiếp tục với Google' })).toHaveAttribute('href', '/api/v1/auth/google');
  await expect(page.getByRole('status')).toContainText('Hãy đăng nhập bằng mật khẩu');
  await expect(page.getByLabel('Email', { exact: true })).toBeVisible();
});

test('fixture: diagnostics stay collapsed, failures recover on readback without business writes', async ({ page }) => {
  let recovered = false; const writes: string[] = [];
  page.on('request', request => { if (request.method() !== 'GET') writes.push(request.url()); });
  await page.route('**/health/ready', route => route.fulfill({ status: recovered ? 200 : 503, contentType: 'application/json', body: JSON.stringify({ status: recovered ? 'ready' : 'unavailable', observedAt: '2026-10-29T00:00:00Z' }) }));
  await page.goto('/#account');
  await expect(page.locator('.system-status')).not.toHaveAttribute('open');
  await page.getByText('Trạng thái hệ thống', { exact: true }).click();
  await expect(page.getByRole('region', { name: 'Kết nối dữ liệu' })).toContainText('Dữ liệu chưa sẵn sàng');
  recovered = true; await page.getByRole('button', { name: 'Kiểm tra lại kết nối' }).click();
  await expect(page.getByRole('region', { name: 'Kết nối dữ liệu' })).toContainText('Dữ liệu sẵn sàng'); expect(writes).toEqual([]);
});


test('fixture: account layout stays readable, keyboard tabs work and route changes preserve input', async ({ page }, testInfo) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto('/#account');
  await page.getByLabel('Email', { exact: true }).fill('preserved@example.test');
  const login = page.getByRole('tab', { name: 'Đăng nhập', exact: true });
  await login.focus(); await page.keyboard.press('ArrowRight');
  await expect(page.getByRole('tab', { name: 'Đăng ký', exact: true })).toBeFocused();
  await page.keyboard.press('Home'); await expect(login).toBeFocused();
  for (const width of [1440, 390, 320]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(page.getByLabel('Email', { exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.evaluate(() => window.scrollTo(0, 0));
    if (width < 768) expect((await page.locator('.session-panel').boundingBox())!.y).toBeLessThan(450);
    await page.screenshot({ path: testInfo.outputPath(`account-${width}.png`), fullPage: true });
  }
  await page.getByRole('button', { name: 'Mở menu' }).click();
  await page.getByRole('link', { name: 'Tìm kho & báo giá', exact: true }).click();
  await expect(page.locator('main')).toBeFocused();
  await page.getByRole('link', { name: 'Đăng nhập / Đăng ký', exact: true }).click();
  await expect(page.getByLabel('Email', { exact: true })).toHaveValue('preserved@example.test');
  await expect(page.locator('main')).toBeFocused();
  await page.goBack(); await expect(page.getByRole('heading', { name: 'Tìm kho & báo giá', exact: true })).toBeVisible();
  await expect(page.locator('main')).toBeFocused();
  await page.goForward(); await expect(page.getByLabel('Email', { exact: true })).toHaveValue('preserved@example.test');
  await page.evaluate(() => document.fonts.ready);
  expect(await page.evaluate(() => document.fonts.check('16px "Be Vietnam Pro"'))).toBe(true);
});

test('fixture: customer and staff accounts retain navigation to their scoped management tools', async ({ page }) => {
  let session = me;
  await page.route('**/api/v1/auth/me', route => route.fulfill({ contentType:'application/json', body:JSON.stringify(session) }));
  for (const role of ['CustomerAdmin', 'Member']) {
    session = { ...me, organizations: [{ ...me.organizations[0], role }] };
    await page.goto('/#account');
    await expect(page.getByText('Bạn đang đăng nhập bằng', { exact:false })).toBeVisible();
    await page.getByRole('navigation', { name:'Điều hướng công việc' }).getByRole('link', { name:'Quản lý', exact:true }).click();
    await expect(page.getByRole('heading', { name:'Quản lý', exact:true })).toBeVisible();
    if (role === 'CustomerAdmin') await expect(page.getByText('Mời đồng nghiệp của khách hàng', { exact:true })).toBeVisible();
  }
  session = { ...me, operatorAdmin:true, organizations:[] };
  await page.goto('/#account');
  await expect(page.getByRole('navigation', { name:'Điều hướng công việc' }).getByRole('link', { name:'Quản lý', exact:true })).toBeVisible();
});
