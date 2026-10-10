<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { Radio, SignalZero } from '@lucide/vue'
import type { Room } from '../types'
import { useWebRtcPlayer } from '../composables/useWebRtcPlayer'
import { estimateReceiveToDisplay, usePlaybackStats } from '../composables/usePlaybackStats'
import type { PlaybackStats } from '../composables/usePlaybackStats'
import PlayerControls from './PlayerControls.vue'

const props = defineProps<{ room: Room | null; token: string | null; debugOpen: boolean }>()
const emit = defineEmits<{
  'update:debugOpen': [value: boolean]
  stats: [value: PlaybackStats | null]
  latency: [value: number | null]
}>()
const shell = ref<HTMLElement | null>(null)
const video = ref<HTMLVideoElement | null>(null)
const paused = ref(false)
const muted = ref(true)
const volume = ref(1)
const pageFullscreen = ref(false)
const fullscreen = ref(false)
const feedback = ref('')
const autoplayBlocked = ref(false)
const player = useWebRtcPlayer()
const { stats, restart: restartStats } = usePlaybackStats(computed(() => props.debugOpen), player.getStats)
let frameCallbackId: number | null = null

watch(stats, value => emit('stats', value))

function stopFrameTiming() {
  if (frameCallbackId !== null && video.value?.cancelVideoFrameCallback) {
    video.value.cancelVideoFrameCallback(frameCallbackId)
  }
  frameCallbackId = null
  emit('latency', null)
}

function scheduleFrameTiming() {
  stopFrameTiming()
  if (!props.debugOpen || paused.value || !player.stream.value || !video.value?.requestVideoFrameCallback) return
  const currentVideo = video.value
  function sampleFrame(_now: number, metadata: VideoFrameCallbackMetadata & { receiveTime?: number }) {
    if (!props.debugOpen || paused.value || video.value !== currentVideo) return
    emit('latency', estimateReceiveToDisplay(metadata))
    frameCallbackId = currentVideo.requestVideoFrameCallback(sampleFrame)
  }
  frameCallbackId = currentVideo.requestVideoFrameCallback(sampleFrame)
}

function clearVideo() {
  stopFrameTiming()
  if (video.value) {
    video.value.pause()
    video.value.srcObject = null
  }
}

watch([() => props.room?.status, () => props.room?.generation, () => props.room?.whepUrl, () => props.token], () => {
  clearVideo()
  player.stop()
  autoplayBlocked.value = false
  feedback.value = ''
  if (props.room?.status === 'live' && props.room.whepUrl && !paused.value) player.start(props.room.whepUrl, props.token)
  restartStats()
}, { immediate: true })

watch(player.stream, async stream => {
  if (!stream) { clearVideo(); return }
  if (paused.value) return
  await nextTick()
  if (!video.value || player.stream.value !== stream) return
  video.value.srcObject = stream
  video.value.volume = volume.value
  video.value.muted = muted.value
  try { await video.value.play(); feedback.value = ''; autoplayBlocked.value = false; scheduleFrameTiming() }
  catch {
    muted.value = true
    video.value.muted = true
    try { await video.value.play(); feedback.value = ''; autoplayBlocked.value = false; scheduleFrameTiming() }
    catch { feedback.value = '点击继续直播以播放'; autoplayBlocked.value = true }
  }
})

function togglePause() {
  if (props.room?.status !== 'live') return
  if (autoplayBlocked.value && !paused.value && video.value) {
    void video.value.play().then(() => { autoplayBlocked.value = false; feedback.value = '' }).catch(() => { feedback.value = '浏览器未允许播放，请重试' })
    return
  }
  if (!paused.value) {
    paused.value = true
    stopFrameTiming()
    if (video.value) { video.value.pause(); video.value.muted = true }
    return
  }
  paused.value = false
  clearVideo()
  if (props.room.whepUrl) player.start(props.room.whepUrl, props.token)
}

watch(() => props.debugOpen, scheduleFrameTiming, { flush: 'post' })

function toggleMute() {
  muted.value = !muted.value
  if (video.value) video.value.muted = muted.value || paused.value
}

function setVolume(value: number) {
  volume.value = value
  muted.value = value === 0
  if (video.value) video.value.volume = value
  if (video.value) video.value.muted = muted.value || paused.value
}

function togglePageFullscreen() {
  pageFullscreen.value = !pageFullscreen.value
}

async function toggleFullscreen() {
  try {
    if (document.fullscreenElement === shell.value) await document.exitFullscreen()
    else await shell.value?.requestFullscreen()
    feedback.value = ''
  } catch { feedback.value = '浏览器未允许进入全屏' }
}

function syncFullscreen() { fullscreen.value = document.fullscreenElement === shell.value }
function handleKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape' && pageFullscreen.value) pageFullscreen.value = false
}
onMounted(() => { document.addEventListener('fullscreenchange', syncFullscreen); document.addEventListener('keydown', handleKeydown) })
onUnmounted(() => { document.removeEventListener('fullscreenchange', syncFullscreen); document.removeEventListener('keydown', handleKeydown); clearVideo() })
</script>

<template>
  <section ref="shell" :class="['player-shell card rounded-box border border-base-300 bg-base-100 shadow-sm', { 'is-idle': room?.status === 'idle', 'is-page-fullscreen': pageFullscreen }]" aria-label="直播画面">
    <div class="player-card-body card-body p-0">
    <div class="player-stage">
      <template v-if="room?.status === 'idle'">
        <div class="flex flex-col items-center gap-3 px-4 text-center text-base-content">
          <img v-if="room.idleIconUrl" :src="room.idleIconUrl" alt="" class="h-12 w-12 object-contain" />
          <Radio v-else :size="42" aria-hidden="true" />
          <p class="text-sm sm:text-base">{{ room.idleMessage }}</p>
        </div>
      </template>
      <template v-else>
        <video v-show="!!player.stream.value" ref="video" playsinline :muted="muted || paused" />
        <div v-if="!player.stream.value || paused" class="absolute inset-0 flex items-center justify-center px-5 text-center text-sm text-white/80">
          <p v-if="room?.status === 'unavailable'" class="flex items-center gap-2"><SignalZero :size="18" /> 媒体服务暂时不可用</p>
          <p v-else-if="room?.status === 'initializing' || !room">正在连接媒体服务</p>
          <p v-else-if="paused">已暂停</p>
          <p v-else-if="player.error.value">直播连接失败</p>
          <p v-else>正在连接直播</p>
        </div>
      </template>
      <p v-if="feedback" class="alert alert-warning absolute bottom-2 left-2 right-2 z-10 text-sm" role="status">{{ feedback }}</p>
    </div>
    <PlayerControls
      :paused="paused || autoplayBlocked" :muted="muted" :volume="volume" :live="room?.status === 'live'"
      :page-fullscreen="pageFullscreen" :fullscreen="fullscreen" :debug-open="debugOpen"
      @pause="togglePause" @mute="toggleMute" @volume="setVolume"
      @page-fullscreen="togglePageFullscreen" @fullscreen="toggleFullscreen" @debug="emit('update:debugOpen', !debugOpen)"
    />
    </div>
  </section>
</template>
