import { test, expect, type Page } from '@playwright/test'

const bootstrap = {
  requiresToken: true,
  title: 'Hsin',
  logoUrl: null,
  showcaseImageUrl: '/assets/appearance/showcase',
  passwordPrompt: '请输入房间密码',
}

const showcaseSvg = '<svg xmlns="http://www.w3.org/2000/svg" width="90" height="160"><rect width="90" height="160" fill="#8065a8"/><circle cx="45" cy="60" r="30" fill="#f9a8d4"/></svg>'
const announcement = '## 欢迎\n\n直播开始后画面会自动出现。\n\n- 推荐使用桌面浏览器\n- 卡顿时可查看播放统计'

async function box(page: Page, selector: string) {
  const value = await page.locator(selector).first().boundingBox()
  expect(value).not.toBeNull()
  return value!
}

test('password page leads to the bento room with protected showcase image', async ({ page }) => {
  let status: 'idle' | 'live' = 'idle'
  const showcaseAuthorization: Array<string | undefined> = []
  await page.route('**/api/bootstrap', route => route.fulfill({ json: bootstrap }))
  await page.route('**/assets/appearance/showcase', route => {
    showcaseAuthorization.push(route.request().headers().authorization)
    return route.fulfill({ contentType: 'image/svg+xml', body: showcaseSvg })
  })
  await page.route('**/api/access', route => route.fulfill({ status: 204 }))
  await page.route('**/api/room', route => route.fulfill({ json: { status, generation: status, whepUrl: status === 'live' ? 'http://127.0.0.1:24516/live/whep' : null, idleMessage: '直播尚未开始', idleIconUrl: null } }))
  await page.route('**/api/announcement', route => route.fulfill({ body: announcement, contentType: 'text/markdown' }))
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto('/')

  await expect(page.getByRole('textbox', { name: '房间密码' })).toBeVisible()
  await expect(page.getByRole('region', { name: '展示图片' })).toHaveCount(0)
  expect(showcaseAuthorization).toHaveLength(0)
  const initialTheme = await page.locator('html').getAttribute('data-theme')
  expect(['valentine', 'dracula']).toContain(initialTheme)
  await page.screenshot({ path: '../agent_tmp/password-desktop.png' })

  await page.getByRole('textbox', { name: '房间密码' }).fill('secret')
  await page.getByRole('button', { name: '进入房间' }).click()
  await expect(page.getByRole('region', { name: '直播画面' }).getByText('直播尚未开始')).toBeVisible()
  await expect(page.getByRole('region', { name: '直播状态' })).toContainText('未开播')
  await expect(page.getByRole('region', { name: '画面信息' })).toContainText('延迟估算')
  await expect(page.getByRole('region', { name: '展示图片' }).locator('img')).toHaveAttribute('src', /^blob:/)
  expect(showcaseAuthorization.at(-1)).toBe('Bearer secret')
  await expect(page.locator('.player-shell')).toHaveClass(/is-idle/)

  // 桌面：侧栏位于播放器右侧，展示图片填满侧栏剩余高度
  const player = await box(page, '.room-player')
  const side = await box(page, '.room-side')
  expect(side.x).toBeGreaterThanOrEqual(player.x + player.width)
  expect(Math.abs(side.y - player.y)).toBeLessThan(1)
  expect(Math.abs(side.height - player.height)).toBeLessThan(2)
  const header = await box(page, '.app-header')
  const main = await box(page, '.app-main')
  expect(Math.abs(header.width - main.width)).toBeLessThan(1)
  await page.screenshot({ path: '../agent_tmp/room-desktop.png' })

  await page.getByRole('button', { name: '播放统计' }).click()
  const debug = page.getByRole('region', { name: '播放统计' })
  await expect(debug).toBeVisible()
  await expect(debug.locator('.metric-sparkline')).toHaveCount(8)
  await expect(debug.locator('.metric-sparkline-current').first()).toHaveText('不可用')
  expect((await box(page, '.room-debug')).y).toBeGreaterThan(player.y + player.height)
  await page.screenshot({ path: '../agent_tmp/room-desktop-debug.png', fullPage: true })

  await page.setViewportSize({ width: 390, height: 844 })
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
  const mobilePlayer = await box(page, '.room-player')
  const mobileStatus = await box(page, '[aria-label="直播状态"]')
  const mobileShowcase = await box(page, '[aria-label="展示图片"]')
  const mobileDebug = await box(page, '.room-debug')
  expect(mobileStatus.y).toBeGreaterThan(mobilePlayer.y + mobilePlayer.height)
  expect(mobileShowcase.y).toBeGreaterThan(mobileStatus.y)
  expect(mobileDebug.y).toBeGreaterThan(mobileShowcase.y)
  await page.screenshot({ path: '../agent_tmp/room-mobile.png', fullPage: true })

  await expect(page.getByRole('link', { name: 'Hsin' })).toHaveAttribute('href', 'https://github.com/Yukari316/Hsin')
  await page.getByRole('button', { name: /切换.*主题/ }).click()
  const toggledTheme = initialTheme === 'dracula' ? 'valentine' : 'dracula'
  await expect(page.locator('html')).toHaveAttribute('data-theme', toggledTheme)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.screenshot({ path: `../agent_tmp/room-desktop-${toggledTheme}.png`, fullPage: true })
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('data-theme', toggledTheme)

  await page.getByRole('textbox', { name: '房间密码' }).fill('secret')
  await page.getByRole('button', { name: '进入房间' }).click()
  await expect(page.getByRole('region', { name: '直播状态' })).toContainText('未开播')
  status = 'live'
  await expect(page.getByRole('region', { name: '直播状态' })).toContainText('直播中')
  await expect(page.locator('.player-shell')).not.toHaveClass(/is-idle/)
  await expect(page.locator('.player-stage')).toHaveCSS('background-color', 'rgb(0, 0, 0)')
  await page.getByRole('button', { name: '网页全屏' }).click()
  await expect(page.locator('.player-shell')).toHaveClass(/is-page-fullscreen/)
  await page.keyboard.press('Escape')
  await expect(page.locator('.player-shell')).not.toHaveClass(/is-page-fullscreen/)
})

test('anonymous access skips password and failed entry marks the field', async ({ page }) => {
  await page.route('**/api/bootstrap', route => route.fulfill({ json: { ...bootstrap, requiresToken: false, showcaseImageUrl: null } }))
  await page.route('**/api/room', route => route.fulfill({ json: { status: 'idle', generation: 'a', whepUrl: null, idleMessage: '直播尚未开始', idleIconUrl: null } }))
  await page.route('**/api/announcement', route => route.fulfill({ body: '' }))
  await page.goto('/')
  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.getByRole('region', { name: '直播画面' }).getByText('直播尚未开始')).toBeVisible()
  await expect(page.getByRole('textbox', { name: '房间密码' })).toHaveCount(0)
  await expect(page.getByRole('region', { name: '展示图片' })).toHaveCount(0)
  await expect(page.getByRole('region', { name: '公告' })).toHaveCount(0)
  await page.route('**/api/bootstrap', route => route.fulfill({ json: bootstrap }))
  await page.route('**/api/access', route => route.fulfill({ status: 401, json: { error: 'Unauthorized' } }))
  await page.reload()
  await page.getByRole('textbox', { name: '房间密码' }).fill('wrong')
  await page.getByRole('button', { name: '进入房间' }).click()
  await expect(page.getByRole('textbox', { name: '房间密码' })).toHaveAttribute('aria-invalid', 'true')
  await expect(page.getByRole('alert')).toHaveText('密码错误，请重试')
  await page.screenshot({ path: '../agent_tmp/password-mobile-error.png' })
})
