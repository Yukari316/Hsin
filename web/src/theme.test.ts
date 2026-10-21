import { beforeEach, describe, expect, it, vi } from 'vitest'

describe('theme', () => {
  beforeEach(() => { localStorage.clear(); vi.resetModules() })

  it('starts from OS preference and remembers user choice', async () => {
    vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: true }))
    const { theme, toggleTheme } = await import('./theme')
    expect(theme.value).toBe('dark')
    expect(document.documentElement.dataset.theme).toBe('dracula')
    toggleTheme()
    expect(theme.value).toBe('light')
    expect(document.documentElement.dataset.theme).toBe('valentine')
    expect(localStorage.getItem('hsin-theme')).toBe('light')
  })
})
