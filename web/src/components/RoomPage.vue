<script setup lang="ts">
import { ref, shallowRef } from 'vue'
import type { Bootstrap, Room } from '../types'
import type { PlaybackStats } from '../composables/usePlaybackStats'
import { useDebugHistory } from '../composables/useDebugHistory'
import { useDebugPanelPosition } from '../composables/useDebugPanelPosition'
import LivePlayer from './LivePlayer.vue'
import DebugPanel from './DebugPanel.vue'
import Announcement from './Announcement.vue'

defineProps<{ bootstrap: Bootstrap; room: Room | null; announcement: string; token: string | null }>()
const debugOpen = ref(false)
const debugStats = shallowRef<PlaybackStats | null>(null)
const receiveToDisplayMs = ref<number | null>(null)
const { history: debugHistory } = useDebugHistory(debugOpen, debugStats, receiveToDisplayMs)
const main = ref<HTMLElement | null>(null)
const debugPanelPosition = useDebugPanelPosition(debugOpen, main)
</script>

<template>
  <div class="app-shell">
    <header class="app-header app-content-width navbar min-h-14">
      <div class="flex items-center gap-2">
        <img v-if="bootstrap.logoUrl" :src="bootstrap.logoUrl" alt="" class="h-8 w-8 object-contain" />
        <h1 class="text-lg font-semibold">{{ bootstrap.title }}</h1>
      </div>
    </header>
    <div class="room-layout-shell">
      <main ref="main" class="app-content-width app-main">
        <div class="player-layout">
          <LivePlayer
            v-model:debug-open="debugOpen" :room="room" :token="token"
            @stats="debugStats = $event" @latency="receiveToDisplayMs = $event"
          />
        </div>
        <Announcement :markdown="announcement" />
      </main>
      <div v-if="debugOpen" class="debug-panel-position app-content-width" :style="debugPanelPosition ?? undefined">
        <DebugPanel :stats="debugStats" :receive-to-display-ms="receiveToDisplayMs" :history="debugHistory" />
      </div>
    </div>
    <footer class="app-content-width app-footer">
      Powered by <a class="link" href="https://github.com/Yukari316/Hsin" target="_blank" rel="noopener noreferrer">Hsin</a>
      | <a class="link" href="https://github.com/bluenviron/mediamtx" target="_blank" rel="noopener noreferrer">MediaMTX</a>
    </footer>
  </div>
</template>
