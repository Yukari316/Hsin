<script setup lang="ts">
import { Pause, Play, Volume2, VolumeX, Expand, Maximize, Minimize, Activity } from '@lucide/vue'

defineProps<{ paused: boolean; muted: boolean; volume: number; live: boolean; pageFullscreen: boolean; fullscreen: boolean; debugOpen: boolean }>()
const emit = defineEmits<{
  pause: []
  mute: []
  volume: [value: number]
  pageFullscreen: []
  fullscreen: []
  debug: []
}>()
</script>

<template>
  <div class="player-controls flex min-h-12 items-center gap-1 bg-base-300 px-2 py-1 text-base-content" aria-label="播放控制">
    <div class="tooltip tooltip-top" :data-tip="paused ? '继续直播' : '暂停'">
      <button class="btn btn-ghost btn-square btn-sm" :aria-label="paused ? '继续直播' : '暂停'" :disabled="!live" @click="emit('pause')">
        <Play v-if="paused" :size="19" /><Pause v-else :size="19" />
      </button>
    </div>
    <div class="tooltip tooltip-top" :data-tip="muted ? '开启声音' : '静音'">
      <button class="btn btn-ghost btn-square btn-sm" :aria-label="muted ? '开启声音' : '静音'" @click="emit('mute')">
        <VolumeX v-if="muted || volume === 0" :size="18" /><Volume2 v-else :size="18" />
      </button>
    </div>
    <input class="range range-xs w-20 sm:w-28" type="range" min="0" max="1" step="0.01" :value="volume" aria-label="音量" @input="emit('volume', Number(($event.target as HTMLInputElement).value))" />
    <div class="flex-1" />
    <div class="tooltip tooltip-top" data-tip="调试数据">
      <button class="btn btn-ghost btn-square btn-sm" :class="{ 'btn-active': debugOpen }" aria-label="调试数据" :aria-pressed="debugOpen" @click="emit('debug')"><Activity :size="18" /></button>
    </div>
    <div class="tooltip tooltip-top" :data-tip="pageFullscreen ? '退出网页全屏' : '网页全屏'">
      <button class="btn btn-ghost btn-square btn-sm" :aria-label="pageFullscreen ? '退出网页全屏' : '网页全屏'" :aria-pressed="pageFullscreen" @click="emit('pageFullscreen')">
        <Minimize v-if="pageFullscreen" :size="18" /><Expand v-else :size="18" />
      </button>
    </div>
    <div class="tooltip tooltip-top tooltip-left" :data-tip="fullscreen ? '退出全屏' : '全屏'">
      <button class="btn btn-ghost btn-square btn-sm" :aria-label="fullscreen ? '退出全屏' : '全屏'" :aria-pressed="fullscreen" @click="emit('fullscreen')">
        <Minimize v-if="fullscreen" :size="18" /><Maximize v-else :size="18" />
      </button>
    </div>
  </div>
</template>
