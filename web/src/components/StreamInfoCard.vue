<script setup lang="ts">
import { computed } from 'vue'
import { Info } from '@lucide/vue'
import type { PlaybackStats } from '../composables/usePlaybackStats'
import { formatRateMbps } from '../formatters'

const props = defineProps<{ stats: PlaybackStats | null; receiveToDisplayMs: number | null }>()
const EMPTY = '—'

const items = computed(() => {
  const video = props.stats?.video
  return [
    { label: '分辨率', value: video?.height == null ? EMPTY : `${video.height}p` },
    { label: '帧率', value: video?.fps == null ? EMPTY : `${Math.round(video.fps)} fps` },
    { label: '视频码率', value: video?.bitrate == null ? EMPTY : formatRateMbps(video.bitrate / 1_000_000) },
    { label: '延迟估算', value: props.receiveToDisplayMs == null ? EMPTY : `${Math.round(props.receiveToDisplayMs)} ms`, hint: true },
  ]
})
</script>

<template>
  <section class="tile p-5" aria-label="画面信息">
    <div class="tile-label">画面</div>
    <dl class="mt-3 grid grid-cols-2 gap-x-3 gap-y-4">
      <div v-for="item in items" :key="item.label" class="flex min-w-0 flex-col-reverse">
        <dt class="flex items-center gap-1 text-xs text-base-content/55">
          {{ item.label }}
          <span v-if="item.hint" class="tooltip tooltip-left" tabindex="0" data-tip="浏览器从收到画面到显示的估算时间，不含网络和服务器转发延迟">
            <Info :size="12" aria-label="延迟估算说明" />
          </span>
        </dt>
        <dd class="num truncate text-xl font-semibold" :class="{ 'text-base-content/30': item.value === EMPTY }">{{ item.value }}</dd>
      </div>
    </dl>
  </section>
</template>
