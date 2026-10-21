<script setup lang="ts">
import { ref, shallowRef } from 'vue'
import type { Bootstrap, Room } from '../types'
import type { PlaybackStats } from '../composables/usePlaybackStats'
import { useDebugHistory } from '../composables/useDebugHistory'
import LivePlayer from './LivePlayer.vue'
import DebugPanel from './DebugPanel.vue'
import Announcement from './Announcement.vue'
import BrandMark from './BrandMark.vue'
import ThemeToggle from './ThemeToggle.vue'
import RoomStatusCard from './RoomStatusCard.vue'
import StreamInfoCard from './StreamInfoCard.vue'
import ShowcaseCard from './ShowcaseCard.vue'

defineProps<{ bootstrap: Bootstrap; room: Room | null; announcement: string; token: string | null; showcaseUrl: string | null }>()
const debugOpen = ref(false)
const stats = shallowRef<PlaybackStats | null>(null)
const receiveToDisplayMs = ref<number | null>(null)
const { history } = useDebugHistory(stats, receiveToDisplayMs)
</script>

<template>
  <div class="flex min-h-dvh flex-col">
    <header class="app-header page-width flex h-16 items-center gap-3">
      <BrandMark :logo-url="bootstrap.logoUrl" />
      <h1 class="min-w-0 truncate text-lg font-semibold">{{ bootstrap.title }}</h1>
      <div class="flex-1" />
      <ThemeToggle />
    </header>

    <main class="app-main page-width flex-1 pb-8">
      <div class="room-grid">
        <LivePlayer
          v-model:debug-open="debugOpen" class="room-player" :room="room" :token="token"
          @stats="stats = $event" @latency="receiveToDisplayMs = $event"
        />
        <aside class="room-side" aria-label="房间信息">
          <RoomStatusCard :room="room" />
          <StreamInfoCard :stats="stats" :receive-to-display-ms="receiveToDisplayMs" />
          <ShowcaseCard v-if="showcaseUrl" :image-url="showcaseUrl" />
        </aside>
        <DebugPanel
          v-if="debugOpen" class="room-debug"
          :stats="stats" :receive-to-display-ms="receiveToDisplayMs" :history="history"
          @close="debugOpen = false"
        />
        <Announcement class="room-notice" :markdown="announcement" />
      </div>
    </main>

    <footer class="app-footer page-width pb-8 text-center text-xs text-base-content/50">
      Powered by <a class="link link-hover" href="https://github.com/Yukari316/Hsin" target="_blank" rel="noopener noreferrer">Hsin</a>
      · <a class="link link-hover" href="https://github.com/bluenviron/mediamtx" target="_blank" rel="noopener noreferrer">MediaMTX</a>
    </footer>
  </div>
</template>
