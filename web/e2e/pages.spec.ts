import { test, expect } from '@playwright/test'

const bootstrap = {
  requiresToken: true,
  title: 'Hsin',
  logoUrl: null,
  backgroundLeftUrl: '/background-left.png',
  backgroundRightUrl: '/background-right.png',
  mobileBackgroundUrl: '/background-mobile.png',
  passwordPrompt: '请输入房间密码',
}

test('password page hides background and authenticated room shows it', async ({ page }) => {
  let status: 'idle' | 'live' = 'idle'
  await page.route('**/api/bootstrap', route => route.fulfill({ json: bootstrap }))
  await page.route('**/background-left.png', route => route.fulfill({
    contentType: 'image/svg+xml',
    body: '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect width="100" height="100" fill="#4f987d"/><rect width="50" height="50" fill="#a3c1db"/></svg>',
  }))
  await page.route('**/background-right.png', route => route.fulfill({
    contentType: 'image/svg+xml',
    body: '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect width="100" height="100" fill="#d58c62"/></svg>',
  }))
  await page.route('**/background-mobile.png', route => route.fulfill({
    contentType: 'image/svg+xml',
    body: '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><rect width="100" height="100" fill="#8065a8"/></svg>',
  }))
  await page.route('**/api/access', route => route.fulfill({ status: 204 }))
  await page.route('**/api/room', route => route.fulfill({ json: { status, generation: status, whepUrl: status === 'live' ? 'http://127.0.0.1:24516/live/whep' : null, idleMessage: '直播尚未开始', idleIconUrl: null } }))
  await page.route('**/api/announcement', route => route.fulfill({ body: '# 公告\n\n欢迎观看', contentType: 'text/markdown' }))
  await page.goto('/')
  await expect(page.getByRole('textbox', { name: '房间密码' })).toBeVisible()
  expect(await page.locator('.password-wrap').evaluate(element => getComputedStyle(element).backgroundColor)).toBe(await page.locator('body').evaluate(element => getComputedStyle(element).backgroundColor))
  await expect(page.locator('.app-background-left')).toHaveCount(0)
  await expect(page.locator('.app-background-right')).toHaveCount(0)
  await expect(page.locator('.app-background-mobile')).toHaveCount(0)
  await expect(page.locator('.app-background-left')).toHaveCount(0)
  const initialTheme = await page.locator('html').getAttribute('data-theme')
  expect(['fantasy', 'sunset']).toContain(initialTheme)
  await page.screenshot({ path: '../agent_tmp/password-desktop.png' })
  await page.getByRole('textbox', { name: '房间密码' }).fill('secret')
  await page.getByRole('button', { name: '进入房间' }).click()
  await expect(page.getByText('直播尚未开始')).toBeVisible()
  await expect(page.locator('.app-background-left')).toHaveCSS('background-image', /background-left\.png/)
  await expect(page.locator('.app-background-right')).toHaveCSS('background-image', /background-right\.png/)
  expect(await page.locator('.app-background-left').evaluate(element => getComputedStyle(element).position)).toBe('fixed')
  expect(await page.locator('.app-background-right').evaluate(element => getComputedStyle(element).position)).toBe('fixed')
  expect(await page.locator('.app-background-left').evaluate(element => getComputedStyle(element).height)).toBe('720px')
  expect(await page.locator('.app-background-right').evaluate(element => getComputedStyle(element).height)).toBe('720px')
  await expect(page.locator('.app-background-mobile')).toHaveCount(1)
  await expect(page.locator('.app-background-left')).toHaveCSS('background-image', /background-left\.png/)
  expect(await page.locator('.app-background-left').evaluate(element => getComputedStyle(element, '::after').backgroundColor)).toBe('rgb(0, 0, 0)')
  expect(await page.locator('.app-background-left').evaluate(element => getComputedStyle(element, '::after').opacity)).toBe(initialTheme === 'sunset' ? '0.7' : '0.15')
  await expect(page.locator('.player-shell')).toHaveClass(/is-idle/)
  await expect(page.locator('.player-shell')).toHaveClass(/card/)
  await page.getByRole('button', { name: '调试数据' }).click()
  await expect(page.getByRole('complementary', { name: '调试信息' })).toBeVisible()
  await page.setViewportSize({ width: 1700, height: 4000 })
  const tallWindow = await page.locator('.debug-card').evaluate(element => ({ visible: element.clientHeight, content: element.scrollHeight }))
  expect(tallWindow.content).toBeLessThanOrEqual(tallWindow.visible)
  await page.setViewportSize({ width: 1700, height: 480 })
  const shortWindow = await page.locator('.debug-card').evaluate(element => ({ visible: element.clientHeight, content: element.scrollHeight }))
  expect(shortWindow.content).toBeGreaterThan(shortWindow.visible)
  await page.setViewportSize({ width: 1700, height: 900 })
  expect(await page.locator('.app-background-left').evaluate(element => getComputedStyle(element).height)).toBe('900px')
  expect(await page.locator('.app-background-right').evaluate(element => getComputedStyle(element).height)).toBe('900px')
  expect(await page.locator('.player-layout').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length)).toBe(1)
  expect(await page.locator('.debug-metric-grid').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length)).toBe(2)
  const contentBox = await page.locator('.app-main').boundingBox()
  const debugBox = await page.getByRole('complementary', { name: '调试信息' }).boundingBox()
  const rightBackgroundBox = await page.locator('.app-background-right').boundingBox()
  expect(contentBox && debugBox && debugBox.x >= contentBox.x + contentBox.width).toBe(true)
  expect(debugBox && rightBackgroundBox && Math.abs((debugBox.x + debugBox.width / 2) - (rightBackgroundBox.x + rightBackgroundBox.width / 2))).toBeLessThan(1)
  expect(debugBox && Math.abs(debugBox.y + debugBox.height - 876)).toBeLessThan(1)
  const debugSurface = await page.locator('.debug-card').evaluate(element => {
    const style = getComputedStyle(element)
    return { background: style.backgroundColor, filter: style.backdropFilter }
  })
  expect(debugSurface.background).toMatch(/0\.7\)/)
  expect(debugSurface.filter).toBe('none')
  const metricColors = await page.locator('.metric-sparkline').first().evaluate(element => {
    const probe = document.createElement('span')
    document.body.append(probe)
    probe.style.color = 'var(--color-info)'
    const info = getComputedStyle(probe).color
    probe.style.color = 'var(--color-base-300)'
    const base300 = getComputedStyle(probe).color
    probe.style.color = 'var(--color-base-content)'
    const content = getComputedStyle(probe).color
    probe.remove()
    const metric = getComputedStyle(element)
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path')
    element.querySelector('svg')?.append(path)
    const pathColor = getComputedStyle(path).stroke
    path.remove()
    return { background: metric.backgroundColor, color: metric.color, border: metric.borderTopColor, info, base300, content, path: pathColor }
  })
  expect(metricColors.background).toBe('rgba(0, 0, 0, 0)')
  expect(metricColors.color).toBe(metricColors.content)
  expect(metricColors.border).toBe(metricColors.base300)
  expect(metricColors.path).toBe(metricColors.info)
  expect(await page.locator('.metric-sparkline-label').first().textContent()).toMatch(/帧率 不可用/)
  const leftBackgroundWidth = await page.locator('.app-background-left').evaluate(element => element.getBoundingClientRect().width)
  const rightBackgroundWidth = await page.locator('.app-background-right').evaluate(element => element.getBoundingClientRect().width)
  expect(Math.abs(leftBackgroundWidth - rightBackgroundWidth)).toBeLessThan(1)
  const headerWidth = await page.locator('.app-header').evaluate(element => element.getBoundingClientRect().width)
  const mainWidth = await page.locator('.app-main').evaluate(element => element.getBoundingClientRect().width)
  const footerWidth = await page.locator('.app-footer').evaluate(element => element.getBoundingClientRect().width)
  const mainBox = await page.locator('.app-main').boundingBox()
  const themeButtonBox = await page.locator('.theme-switch-position button').boundingBox()
  const mainBackground = await page.locator('.app-main').evaluate(element => getComputedStyle(element).backgroundColor)
  const footerBackground = await page.locator('.app-footer').evaluate(element => getComputedStyle(element).backgroundColor)
  const pageBackground = await page.locator('body').evaluate(element => getComputedStyle(element).backgroundColor)
  expect(headerWidth).toBe(mainWidth)
  expect(footerWidth).toBe(mainWidth)
  expect(footerBackground).toBe(mainBackground)
  expect(mainBackground).toBe(pageBackground)
  expect(mainBackground).not.toBe('rgb(5, 7, 9)')
  expect(await page.locator('.player-stage').evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe(mainBackground)
  expect(await page.locator('.player-controls').evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe(mainBackground)
  expect(mainBox && themeButtonBox && Math.abs((themeButtonBox.x + themeButtonBox.width) - (mainBox.x + mainBox.width - 16))).toBeLessThan(1)
  await page.screenshot({ path: '../agent_tmp/room-wide-debug.png' })
  await page.setViewportSize({ width: 1500, height: 900 })
  expect(await page.locator('.debug-panel-position').evaluate(element => getComputedStyle(element).position)).toBe('fixed')
  expect(await page.locator('.debug-metric-grid').evaluate(element => getComputedStyle(element).gridTemplateColumns.split(' ').length)).toBe(1)
  await page.setViewportSize({ width: 1100, height: 800 })
  expect(await page.locator('.debug-panel-position').evaluate(element => getComputedStyle(element).position)).toBe('static')
  const belowDebugBox = await page.getByRole('complementary', { name: '调试信息' }).boundingBox()
  const playerBox = await page.getByRole('region', { name: '直播画面' }).boundingBox()
  expect(belowDebugBox && playerBox && belowDebugBox.y > playerBox.y).toBe(true)
  await page.screenshot({ path: '../agent_tmp/room-desktop.png' })
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.locator('.app-background-mobile')).toBeVisible()
  expect(await page.locator('.app-background-mobile').evaluate(element => getComputedStyle(element).position)).toBe('fixed')
  expect(await page.locator('.app-background-mobile').evaluate(element => getComputedStyle(element).height)).toBe('844px')
  await expect(page.locator('.app-background-mobile')).toHaveCSS('background-size', 'cover')
  await expect(page.locator('.app-background-mobile')).toHaveCSS('background-position', '50% 50%')
  expect(await page.locator('.app-background-mobile').evaluate(element => getComputedStyle(element, '::after').display)).toBe('block')
  await expect(page.locator('.app-background-left')).toBeHidden()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  const mobileDebugBox = await page.getByRole('complementary', { name: '调试信息' }).boundingBox()
  const mobilePlayerBox = await page.getByRole('region', { name: '直播画面' }).boundingBox()
  expect(mobileDebugBox && mobilePlayerBox && mobileDebugBox.y > mobilePlayerBox.y).toBe(true)
  expect(await page.locator('.debug-card').evaluate(element => element.scrollHeight <= element.clientHeight)).toBe(true)
  await page.screenshot({ path: '../agent_tmp/room-mobile.png' })
  await expect(page.getByRole('link', { name: 'Hsin' })).toHaveAttribute('href', 'https://github.com/Yukari316/Hsin')
  const originalTheme = await page.locator('html').getAttribute('data-theme')
  const originalControlBackground = await page.locator('.player-controls').evaluate(element => getComputedStyle(element).backgroundColor)
  await page.getByRole('button', { name: /切换.*主题/ }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', originalTheme === 'sunset' ? 'fantasy' : 'sunset')
  expect(await page.locator('.app-main').evaluate(element => getComputedStyle(element).backgroundColor)).toBe(await page.locator('body').evaluate(element => getComputedStyle(element).backgroundColor))
  expect(await page.locator('.app-main').evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe('rgb(5, 7, 9)')
  expect(await page.locator('.app-background-mobile').evaluate(element => getComputedStyle(element, '::after').backgroundColor)).toBe('rgb(0, 0, 0)')
  expect(await page.locator('.app-background-mobile').evaluate(element => getComputedStyle(element, '::after').opacity)).toBe(originalTheme === 'sunset' ? '0.15' : '0.7')
  expect(await page.locator('.player-controls').evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe(originalControlBackground)
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('data-theme', originalTheme === 'sunset' ? 'fantasy' : 'sunset')
  expect(await page.locator('.password-wrap').evaluate(element => getComputedStyle(element).backgroundColor)).toBe(await page.locator('body').evaluate(element => getComputedStyle(element).backgroundColor))
  await page.getByRole('textbox', { name: '房间密码' }).fill('secret')
  await page.getByRole('button', { name: '进入房间' }).click()
  await expect(page.getByText('直播尚未开始')).toBeVisible()
  status = 'live'
  await expect(page.locator('.player-shell')).not.toHaveClass(/is-idle/)
  await expect(page.locator('.player-stage')).toHaveCSS('background-color', 'rgb(0, 0, 0)')
  await page.getByRole('button', { name: '网页全屏' }).click()
  await expect(page.locator('.player-shell')).toHaveClass(/is-page-fullscreen/)
  await expect(page.locator('.app-background-mobile')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.locator('.player-shell')).not.toHaveClass(/is-page-fullscreen/)
})

test('anonymous access skips password and failed entry marks the field', async ({ page }) => {
  await page.route('**/api/bootstrap', route => route.fulfill({ json: { ...bootstrap, requiresToken: false, backgroundLeftUrl: null, backgroundRightUrl: null, mobileBackgroundUrl: null } }))
  await page.route('**/api/room', route => route.fulfill({ json: { status: 'idle', generation: 'a', whepUrl: null, idleMessage: '直播尚未开始', idleIconUrl: null } }))
  await page.route('**/api/announcement', route => route.fulfill({ body: '' }))
  await page.goto('/')
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.getByText('直播尚未开始')).toBeVisible()
  await expect(page.getByRole('textbox', { name: '房间密码' })).toHaveCount(0)
  await expect(page.locator('.app-background-mobile')).toHaveCount(0)
  await expect(page.locator('.app-background-mobile')).toHaveCount(0)
  await page.route('**/api/bootstrap', route => route.fulfill({ json: bootstrap }))
  await page.route('**/api/access', route => route.fulfill({ status: 401, json: { error: 'Unauthorized' } }))
  await page.reload()
  await page.getByRole('textbox', { name: '房间密码' }).fill('wrong')
  await page.getByRole('button', { name: '进入房间' }).click()
  await expect(page.getByRole('textbox', { name: '房间密码' })).toHaveClass(/input-error/)
})
