<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { getAnnouncement, getAppearanceAsset, getBootstrap, getRoom, UnauthorizedError } from './api'
import type { Bootstrap, Room } from './types'
import PasswordPage from './components/PasswordPage.vue'
import RoomPage from './components/RoomPage.vue'
import { Sun, Moon } from '@lucide/vue'
import { theme, toggleTheme } from './theme'

const bootstrap = ref<Bootstrap | null>(null)
const room = ref<Room | null>(null)
const announcement = ref('')
const token = ref<string | null>(null)
const error = ref('')
let pollTimer: ReturnType<typeof setTimeout> | undefined
let requestController: AbortController | undefined
let pollFailures = 0
let iconSource: string | null = null
let iconObjectUrl: string | null = null

function releaseIcon() {
  if (iconObjectUrl) URL.revokeObjectURL(iconObjectUrl)
  iconObjectUrl = null
  iconSource = null
}

function stopPolling() {
  clearTimeout(pollTimer)
  requestController?.abort()
  requestController = undefined
}

function lostAccess() {
  stopPolling()
  releaseIcon()
  token.value = null
  room.value = null
  announcement.value = ''
}

async function poll() {
  requestController?.abort()
  const controller = new AbortController()
  requestController = controller
  try {
    const snapshot = await getRoom(token.value, controller.signal)
    if (snapshot.status === 'idle' && snapshot.idleIconUrl) {
      if (iconSource !== snapshot.idleIconUrl) {
        releaseIcon()
        const blob = await getAppearanceAsset(snapshot.idleIconUrl, token.value, controller.signal)
        if (controller.signal.aborted) return
        iconSource = snapshot.idleIconUrl
        iconObjectUrl = URL.createObjectURL(blob)
      }
      snapshot.idleIconUrl = iconObjectUrl
    }
    room.value = snapshot
    pollFailures = 0
  } catch (cause) {
    if (controller.signal.aborted) return
    if (cause instanceof UnauthorizedError) { lostAccess(); return }
    room.value = { status: 'unavailable', generation: '', whepUrl: null, idleMessage: '', idleIconUrl: null }
    pollFailures++
  }
  if (!controller.signal.aborted) pollTimer = setTimeout(poll, Math.min(1000 * 2 ** pollFailures, 8000))
}

async function enter(value: string | null) {
  stopPolling()
  releaseIcon()
  token.value = value
  room.value = null
  announcement.value = ''
  pollFailures = 0
  const controller = new AbortController()
  requestController = controller
  try {
    announcement.value = await getAnnouncement(value, controller.signal)
  } catch (cause) {
    if (controller.signal.aborted) return
    if (cause instanceof UnauthorizedError) { lostAccess(); return }
    // The room remains usable if the announcement request fails.
  }
  if (!controller.signal.aborted) void poll()
}

onMounted(async () => {
  try {
    bootstrap.value = await getBootstrap()
    if (!bootstrap.value.requiresToken) void enter(null)
  } catch { error.value = '页面暂时不可用，请刷新重试' }
})
onUnmounted(() => { stopPolling(); releaseIcon() })
watch(() => bootstrap.value?.title, title => { if (title) document.title = title })
</script>

<template>
  <div class="app-root min-h-screen">
    <div class="theme-switch-position app-content-width">
      <div class="tooltip tooltip-left" :data-tip="theme === 'dark' ? '浅色主题' : '深色主题'">
        <button class="btn btn-circle btn-ghost" :aria-label="theme === 'dark' ? '切换浅色主题' : '切换深色主题'" @click="toggleTheme">
          <Sun v-if="theme === 'dark'" :size="20" />
          <Moon v-else :size="20" />
        </button>
      </div>
    </div>
    <template v-if="bootstrap">
    <div
      v-if="(!bootstrap.requiresToken || token !== null) && bootstrap.backgroundLeftUrl"
      class="app-background-image app-background-desktop app-background-left"
      :style="{ backgroundImage: `url(${JSON.stringify(bootstrap.backgroundLeftUrl)})` }"
    />
    <div
      v-if="(!bootstrap.requiresToken || token !== null) && bootstrap.backgroundRightUrl"
      class="app-background-image app-background-desktop app-background-right"
      :style="{ backgroundImage: `url(${JSON.stringify(bootstrap.backgroundRightUrl)})` }"
    />
    <div
      v-if="(!bootstrap.requiresToken || token !== null) && bootstrap.mobileBackgroundUrl"
      class="app-background-image app-background-mobile"
      :style="{ backgroundImage: `url(${JSON.stringify(bootstrap.mobileBackgroundUrl)})` }"
    />
    <PasswordPage v-if="bootstrap.requiresToken && token === null" :prompt="bootstrap.passwordPrompt" @authenticated="enter" />
    <RoomPage v-else :bootstrap="bootstrap" :room="room" :announcement="announcement" :token="token" />
    </template>
    <div v-else class="app-shell items-center justify-center p-4">
      <p v-if="error" class="alert alert-error max-w-md" role="alert">{{ error }}</p>
      <span v-else class="loading loading-spinner loading-lg" aria-label="正在加载" />
    </div>
  </div>
</template>
