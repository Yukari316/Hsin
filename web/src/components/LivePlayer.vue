<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { LoaderCircle, Pause, Radio, SignalZero } from '@lucide/vue'
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
// 画面信息卡常驻显示码率和延迟估算，因此播放期间始终每秒采集一次统计。
const playing = computed(() => !!player.stream.value && !paused.value)
const { stats, restart: restartStats } = usePlaybackStats(playing, player.getStats)
let frameCallbackId: number | null = null
// 两次统计采样之间逐帧累计的接收至显示估算，随统计按秒取平均后清零。
let latencySum = 0
let latencyCount = 0

watch(stats, value => {
  emit('latency', latencyCount > 0 ? latencySum / latencyCount : null)
  latencySum = 0
  latencyCount = 0
  emit('stats', value)
})

function stopFrameTiming() {
  if (frameCallbackId !== null && video.value?.cancelVideoFrameCallback) {
    video.value.cancelVideoFrameCallback(frameCallbackId)
  }
  frameCallbackId = null
  latencySum = 0
  latencyCount = 0
  emit('latency', null)
}

function scheduleFrameTiming() {
  stopFrameTiming()
  if (!playing.value || !video.value?.requestVideoFrameCallback) return
  const currentVideo = video.value
  function sampleFrame(_now: number, metadata: VideoFrameCallbackMetadata & { receiveTime?: number }) {
    if (!playing.value || video.value !== currentVideo) return
    const estimate = estimateReceiveToDisplay(metadata)
    if (estimate !== null) {
      latencySum += estimate
      latencyCount++
    }
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
  <section ref="shell" :class="['player-shell tile', { 'is-idle': room?.status === 'idle', 'is-page-fullscreen': pageFullscreen }]" aria-label="直播画面">
    <div class="player-stage">
      <div v-if="room?.status === 'idle'" class="flex flex-col items-center gap-3 px-6 text-center">
        <img v-if="room.idleIconUrl" :src="room.idleIconUrl" alt="" class="size-16 object-contain" />
        <Radio v-else :size="40" class="opacity-50" aria-hidden="true" />
        <p class="font-medium sm:text-lg">{{ room.idleMessage }}</p>
      </div>
      <template v-else>
        <video v-show="!!player.stream.value" ref="video" playsinline :muted="muted || paused" />
        <div v-if="!player.stream.value || paused" class="absolute inset-0 flex flex-col items-center justify-center gap-3 px-6 text-center text-sm">
          <template v-if="room?.status === 'unavailable'"><SignalZero :size="30" aria-hidden="true" /><p>媒体服务暂时不可用，正在重试</p></template>
          <template v-else-if="paused"><Pause :size="34" aria-hidden="true" /><p>已暂停</p></template>
          <template v-else-if="player.error.value"><SignalZero :size="30" aria-hidden="true" /><p>直播连接失败，正在重试</p></template>
          <template v-else><LoaderCircle :size="30" class="animate-spin" aria-hidden="true" /><p>{{ room?.status === 'live' ? '正在连接直播' : '正在连接媒体服务' }}</p></template>
        </div>
      </template>
      <p v-if="feedback" class="alert alert-warning absolute bottom-3 left-3 right-3 z-10 text-sm" role="status">{{ feedback }}</p>
    </div>
    <PlayerControls
      :paused="paused || autoplayBlocked" :muted="muted" :volume="volume" :live="room?.status === 'live'"
      :page-fullscreen="pageFullscreen" :fullscreen="fullscreen" :debug-open="debugOpen"
      @pause="togglePause" @mute="toggleMute" @volume="setVolume"
      @page-fullscreen="togglePageFullscreen" @fullscreen="toggleFullscreen" @debug="emit('update:debugOpen', !debugOpen)"
    />
  </section>
</template>
