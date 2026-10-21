import { onUnmounted, ref, type Ref, watch } from 'vue'
import type { PlaybackStats } from './usePlaybackStats'

export interface DebugHistoryPoint {
  fps: number | null
  receiveToDisplayMs: number | null
  videoBitrateMbps: number | null
  audioBitrateMbps: number | null
  jitterMs: number | null
  rttMs: number | null
  receiveRateMbps: number | null
  sendRateMbps: number | null
}

export const DEBUG_HISTORY_LIMIT = 60

// 直播播放期间持续记录最近一分钟，打开统计时即可看到历史曲线；统计中断（null）时清空。
export function useDebugHistory(
  stats: Ref<PlaybackStats | null>,
  receiveToDisplayMs: Ref<number | null>
) {
  const history = ref<DebugHistoryPoint[]>([])

  function clear() {
    history.value = []
  }

  function append(value: PlaybackStats | null) {
    if (!value) return
    history.value = [
      ...history.value,
      {
        fps: value.video?.fps ?? null,
        receiveToDisplayMs: receiveToDisplayMs.value,
        videoBitrateMbps: value.video?.bitrate == null ? null : value.video.bitrate / 1_000_000,
        audioBitrateMbps: value.audio?.bitrate == null ? null : value.audio.bitrate / 1_000_000,
        jitterMs: value.video?.jitterMs ?? null,
        rttMs: value.rttMs,
        receiveRateMbps: value.receiveRate == null ? null : value.receiveRate / 1_000_000,
        sendRateMbps: value.sendRate == null ? null : value.sendRate / 1_000_000,
      },
    ].slice(-DEBUG_HISTORY_LIMIT)
  }

  const sample = watch(stats, value => {
    if (value === null) clear()
    else append(value)
  })

  onUnmounted(sample)

  return { history, clear }
}
