<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue'
import { getAnnouncement, getAppearanceAsset, getBootstrap, getRoom, UnauthorizedError } from './api'
import type { Bootstrap, Room } from './types'
import PasswordPage from './components/PasswordPage.vue'
import RoomPage from './components/RoomPage.vue'
import ThemeToggle from './components/ThemeToggle.vue'

const bootstrap = ref<Bootstrap | null>(null)
const room = ref<Room | null>(null)
const announcement = ref('')
const showcaseUrl = ref<string | null>(null)
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

function releaseShowcase() {
  if (showcaseUrl.value) URL.revokeObjectURL(showcaseUrl.value)
  showcaseUrl.value = null
}

function stopPolling() {
  clearTimeout(pollTimer)
  requestController?.abort()
  requestController = undefined
}

function lostAccess() {
  stopPolling()
  releaseIcon()
  releaseShowcase()
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
  releaseShowcase()
  token.value = value
  room.value = null
  announcement.value = ''
  pollFailures = 0
  const controller = new AbortController()
  requestController = controller
  const showcasePath = bootstrap.value?.showcaseImageUrl
  // 公告和展示图片各自失败时房间照常可用，只是不显示对应卡片；任一返回 401 则回到密码页。
  // 展示图片需要认证，因此以 Blob 读取后交给 <img>。
  const [text, showcase] = await Promise.allSettled([
    getAnnouncement(value, controller.signal),
    showcasePath ? getAppearanceAsset(showcasePath, value, controller.signal) : Promise.resolve(null),
  ])
  if (controller.signal.aborted) return
  if ([text, showcase].some(result => result.status === 'rejected' && result.reason instanceof UnauthorizedError)) { lostAccess(); return }
  if (text.status === 'fulfilled') announcement.value = text.value
  if (showcase.status === 'fulfilled' && showcase.value) showcaseUrl.value = URL.createObjectURL(showcase.value)
  void poll()
}

onMounted(async () => {
  try {
    bootstrap.value = await getBootstrap()
    if (!bootstrap.value.requiresToken) void enter(null)
  } catch { error.value = '页面暂时不可用，请刷新重试' }
})
onUnmounted(() => { stopPolling(); releaseIcon(); releaseShowcase() })
watch(() => bootstrap.value?.title, title => { if (title) document.title = title })
</script>

<template>
  <div class="app-root min-h-dvh">
    <template v-if="bootstrap">
      <template v-if="bootstrap.requiresToken && token === null">
        <div class="absolute right-4 top-3 z-10"><ThemeToggle /></div>
        <PasswordPage :title="bootstrap.title" :logo-url="bootstrap.logoUrl" :prompt="bootstrap.passwordPrompt" @authenticated="enter" />
      </template>
      <RoomPage v-else :bootstrap="bootstrap" :room="room" :announcement="announcement" :token="token" :showcase-url="showcaseUrl" />
    </template>
    <div v-else class="grid min-h-dvh place-items-center p-4">
      <p v-if="error" class="alert alert-error max-w-md" role="alert">{{ error }}</p>
      <span v-else class="loading loading-spinner loading-lg" aria-label="正在加载" />
    </div>
  </div>
</template>
