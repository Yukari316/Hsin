<script setup lang="ts">
import { X } from '@lucide/vue'
import MetricSparkline from './MetricSparkline.vue'
import type { DebugHistoryPoint } from '../composables/useDebugHistory'
import type { PlaybackStats, TrackStats } from '../composables/usePlaybackStats'
import { formatRateMbps } from '../formatters'

const props = defineProps<{ stats: PlaybackStats | null; receiveToDisplayMs: number | null; history: DebugHistoryPoint[] }>()
const emit = defineEmits<{ close: [] }>()
const unavailable = '不可用'
const numeric = (value: number | null | undefined, suffix: string) => value == null ? unavailable : `${value.toFixed(2)} ${suffix}`
const bitrate = (track: TrackStats | null | undefined) => formatRateMbps(track?.bitrate == null ? null : track.bitrate / 1_000_000)
const rate = (value: number | null | undefined) => formatRateMbps(value == null ? null : value / 1_000_000)
const series = (key: keyof DebugHistoryPoint) => props.history.map(point => point[key])
</script>

<template>
  <section class="debug-panel" aria-label="播放统计">
    <div class="mb-3 flex items-center gap-2 px-1">
      <h2 class="text-base font-semibold">播放统计</h2>
      <span class="text-xs text-base-content/50">每秒更新 · 最近 60 秒</span>
      <div class="flex-1" />
      <button class="btn btn-circle btn-ghost btn-sm" aria-label="收起播放统计" @click="emit('close')"><X :size="18" /></button>
    </div>
    <div class="grid grid-cols-2 gap-4 md:grid-cols-4">
      <MetricSparkline label="帧率" unit=" fps" :values="series('fps')" />
      <MetricSparkline label="接收至显示估算" unit=" ms" :values="series('receiveToDisplayMs')" />
      <MetricSparkline label="视频码率" rate unit="" :values="series('videoBitrateMbps')" />
      <MetricSparkline label="音频码率" rate unit="" :values="series('audioBitrateMbps')" />
      <MetricSparkline label="抖动" unit=" ms" :values="series('jitterMs')" />
      <MetricSparkline label="RTT" unit=" ms" :values="series('rttMs')" />
      <MetricSparkline label="接收速率" rate unit="" :values="series('receiveRateMbps')" />
      <MetricSparkline label="发送速率" rate unit="" :values="series('sendRateMbps')" />
    </div>
    <div class="tile mt-4 p-5">
      <div class="tile-label mb-3">连接详情</div>
      <dl class="debug-details grid grid-cols-1 gap-x-8 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
        <div><dt>视频编码</dt><dd>{{ stats?.video?.codec ?? unavailable }}</dd></div>
        <div><dt>音频编码</dt><dd>{{ stats?.audio?.codec ?? unavailable }}</dd></div>
        <div><dt>解码器</dt><dd>{{ stats?.video?.decoder ?? unavailable }}</dd></div>
        <div><dt>节能解码</dt><dd>{{ stats?.video?.powerEfficient == null ? unavailable : (stats.video.powerEfficient ? '是' : '否') }}</dd></div>
        <div><dt>分辨率</dt><dd>{{ stats?.video?.resolution ?? unavailable }}</dd></div>
        <div><dt>帧率</dt><dd>{{ numeric(stats?.video?.fps, 'fps') }}</dd></div>
        <div><dt>视频码率</dt><dd>{{ bitrate(stats?.video) }}</dd></div>
        <div><dt>音频码率</dt><dd>{{ bitrate(stats?.audio) }}</dd></div>
        <div><dt>网络 RTT</dt><dd>{{ numeric(stats?.rttMs, 'ms') }}</dd></div>
        <div><dt>抖动</dt><dd>{{ numeric(stats?.video?.jitterMs, 'ms') }}</dd></div>
        <div><dt>缓冲平均驻留</dt><dd>{{ numeric(stats?.video?.bufferMs, 'ms') }}</dd></div>
        <div><dt>平均解码耗时</dt><dd>{{ numeric(stats?.video?.decodeMs, 'ms') }}</dd></div>
        <div><dt>丢包</dt><dd>{{ stats?.video?.packetsLost ?? unavailable }}</dd></div>
        <div><dt>接收</dt><dd>{{ numeric(stats?.received == null ? null : stats.received / 1_000_000, 'MB') }}</dd></div>
        <div><dt>发送</dt><dd>{{ numeric(stats?.sent == null ? null : stats.sent / 1_000_000, 'MB') }}</dd></div>
        <div><dt>接收速率</dt><dd>{{ rate(stats?.receiveRate) }}</dd></div>
        <div><dt>发送速率</dt><dd>{{ rate(stats?.sendRate) }}</dd></div>
        <div><dt>ICE</dt><dd>{{ stats?.candidates ?? unavailable }}</dd></div>
        <div><dt>传输</dt><dd>{{ stats?.protocol ?? unavailable }}</dd></div>
        <div><dt>接收至显示估算</dt><dd>{{ numeric(receiveToDisplayMs, 'ms') }}</dd></div>
      </dl>
    </div>
  </section>
</template>
