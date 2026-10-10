<script setup lang="ts">
import MetricSparkline from './MetricSparkline.vue'
import type { DebugHistoryPoint } from '../composables/useDebugHistory'
import type { PlaybackStats, TrackStats } from '../composables/usePlaybackStats'
import { formatRateMbps } from '../formatters'

const props = defineProps<{ stats: PlaybackStats | null; receiveToDisplayMs: number | null; history: DebugHistoryPoint[] }>()
const unavailable = '不可用'
const numeric = (value: number | null | undefined, suffix: string) => value == null ? unavailable : `${value.toFixed(2)} ${suffix}`
const bitrate = (track: TrackStats | null | undefined) => formatRateMbps(track?.bitrate == null ? null : track.bitrate / 1_000_000)
const rate = (value: number | null | undefined) => formatRateMbps(value == null ? null : value / 1_000_000)
const series = (key: keyof DebugHistoryPoint) => props.history.map(point => point[key])
</script>

<template>
  <aside class="debug-card card rounded-box border text-base-content shadow-sm" aria-label="调试信息">
    <div class="card-body gap-3 p-3 text-xs">
      <h2 class="card-title text-sm">调试信息</h2>
      <div class="debug-metric-grid">
        <MetricSparkline label="帧率" unit=" fps" :values="series('fps')" />
        <MetricSparkline label="接收至显示估算" unit=" ms" :values="series('receiveToDisplayMs')" />
        <MetricSparkline label="视频码率" rate unit="" :values="series('videoBitrateMbps')" />
        <MetricSparkline label="音频码率" rate unit="" :values="series('audioBitrateMbps')" />
        <MetricSparkline label="抖动" unit=" ms" :values="series('jitterMs')" />
        <MetricSparkline label="RTT" unit=" ms" :values="series('rttMs')" />
        <MetricSparkline label="接收速率" rate unit="" :values="series('receiveRateMbps')" />
        <MetricSparkline label="发送速率" rate unit="" :values="series('sendRateMbps')" />
      </div>
      <div class="debug-data-grid">
        <div>视频编码：{{ stats?.video?.codec ?? unavailable }}</div>
        <div>音频编码：{{ stats?.audio?.codec ?? unavailable }}</div>
        <div>解码器：{{ stats?.video?.decoder ?? unavailable }}</div>
        <div>节能解码：{{ stats?.video?.powerEfficient == null ? unavailable : (stats.video.powerEfficient ? '是' : '否') }}</div>
        <div>分辨率：{{ stats?.video?.resolution ?? unavailable }}</div>
        <div>帧率：{{ numeric(stats?.video?.fps, 'fps') }}</div>
        <div>视频码率：{{ bitrate(stats?.video) }}</div>
        <div>音频码率：{{ bitrate(stats?.audio) }}</div>
        <div>网络 RTT：{{ numeric(stats?.rttMs, 'ms') }}</div>
        <div>抖动：{{ numeric(stats?.video?.jitterMs, 'ms') }}</div>
        <div>缓冲平均驻留：{{ numeric(stats?.video?.bufferMs, 'ms') }}</div>
        <div>平均解码耗时：{{ numeric(stats?.video?.decodeMs, 'ms') }}</div>
        <div>丢包：{{ stats?.video?.packetsLost ?? unavailable }}</div>
        <div>接收：{{ numeric(stats?.received == null ? null : stats.received / 1_000_000, 'MB') }}</div>
        <div>发送：{{ numeric(stats?.sent == null ? null : stats.sent / 1_000_000, 'MB') }}</div>
        <div>接收速率：{{ rate(stats?.receiveRate) }}</div>
        <div>发送速率：{{ rate(stats?.sendRate) }}</div>
        <div>ICE：{{ stats?.candidates ?? unavailable }}</div>
        <div>传输：{{ stats?.protocol ?? unavailable }}</div>
        <div>接收至显示估算：{{ numeric(receiveToDisplayMs, 'ms') }}</div>
      </div>
    </div>
  </aside>
</template>
