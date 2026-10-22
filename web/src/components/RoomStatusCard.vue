<script setup lang="ts">
import { computed } from 'vue'
import type { Room } from '../types'

const props = defineProps<{ room: Room | null }>()

const view = computed(() => {
  switch (props.room?.status) {
    case 'live': return { title: '直播中', tone: 'live' }
    case 'idle': return { title: '未开播', tone: 'muted' }
    case 'unavailable': return { title: '暂不可用', tone: 'warning' }
    default: return { title: '连接中', tone: 'muted' }
  }
})
</script>

<template>
  <section class="tile flex flex-col justify-between gap-6 p-5" aria-label="直播状态">
    <div class="tile-label">状态</div>
    <div class="flex items-center gap-3" role="status">
      <span v-if="view.tone === 'live'" class="status-dot" aria-hidden="true" />
      <span :class="['text-[28px] font-bold leading-tight tracking-tight', { 'text-base-content/45': view.tone === 'muted', 'text-warning': view.tone === 'warning' }]">{{ view.title }}</span>
    </div>
  </section>
</template>
