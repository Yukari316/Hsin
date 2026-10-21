<script setup lang="ts">
import { computed } from 'vue'
import type { Room } from '../types'

const props = defineProps<{ room: Room | null }>()

const view = computed(() => {
  switch (props.room?.status) {
    case 'live': return { title: '直播中', detail: '换人推流时会自动接上新画面', tone: 'live' }
    case 'idle': return { title: '未开播', detail: '开播后画面会自动出现', tone: 'muted' }
    case 'unavailable': return { title: '暂不可用', detail: '媒体服务正在自动重试', tone: 'warning' }
    default: return { title: '连接中', detail: '正在获取房间状态', tone: 'muted' }
  }
})
</script>

<template>
  <section class="tile flex flex-col justify-between gap-6 p-5" aria-label="直播状态">
    <div class="tile-label">状态</div>
    <div role="status">
      <div class="flex items-center gap-3">
        <span v-if="view.tone === 'live'" class="status-dot" aria-hidden="true" />
        <span :class="['text-[28px] font-bold leading-tight tracking-tight', { 'text-base-content/45': view.tone === 'muted', 'text-warning': view.tone === 'warning' }]">{{ view.title }}</span>
      </div>
      <p class="mt-2 text-sm text-base-content/55">{{ view.detail }}</p>
    </div>
  </section>
</template>
