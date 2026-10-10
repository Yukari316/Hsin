import { nextTick, onMounted, onUnmounted, ref, watch, type Ref } from 'vue'
import { theme } from '../theme'

export function useDebugPanelPosition(enabled: Ref<boolean>, main: Ref<HTMLElement | null>) {
  const position = ref<{ left: string; width: string } | null>(null)
  let observer: ResizeObserver | null = null

  function updatePosition() {
    const mainElement = main.value
    const root = mainElement?.closest<HTMLElement>('.app-root')
    if (!enabled.value || !mainElement || !root || root.getBoundingClientRect().width < 1360) {
      position.value = null
      return
    }

    const background = root.querySelector<HTMLElement>('.app-background-right')
    const rootBounds = root.getBoundingClientRect()
    const mainBounds = mainElement.getBoundingClientRect()
    const backgroundBounds = background && getComputedStyle(background).display !== 'none'
      ? background.getBoundingClientRect()
      : null
    const left = backgroundBounds?.left ?? mainBounds.right
    const width = backgroundBounds?.width ?? rootBounds.right - mainBounds.right
    const panelWidth = Math.max(0, width - 16)
    position.value = {
      left: `${left + (width - panelWidth) / 2}px`,
      width: `${panelWidth}px`,
    }
  }

  watch([enabled, theme], async () => {
    await nextTick()
    updatePosition()
  }, { flush: 'post' })

  onMounted(() => {
    observer = new ResizeObserver(updatePosition)
    const root = main.value?.closest<HTMLElement>('.app-root')
    if (root) observer.observe(root)
    if (main.value) observer.observe(main.value)
    window.addEventListener('resize', updatePosition)
    window.addEventListener('scroll', updatePosition, true)
    updatePosition()
  })

  onUnmounted(() => {
    observer?.disconnect()
    window.removeEventListener('resize', updatePosition)
    window.removeEventListener('scroll', updatePosition, true)
  })

  return position
}
