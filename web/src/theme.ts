import { ref } from 'vue'

export type Theme = 'light' | 'dark'

export const themeDataNames: Record<Theme, 'valentine' | 'dracula'> = {
  light: 'valentine',
  dark: 'dracula',
}

function initialTheme(): Theme {
  const saved = localStorage.getItem('hsin-theme')
  if (saved === 'light' || saved === 'dark') return saved
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export const theme = ref<Theme>(initialTheme())
document.documentElement.dataset.theme = themeDataNames[theme.value]

export function toggleTheme() {
  theme.value = theme.value === 'light' ? 'dark' : 'light'
  localStorage.setItem('hsin-theme', theme.value)
  document.documentElement.dataset.theme = themeDataNames[theme.value]
}
