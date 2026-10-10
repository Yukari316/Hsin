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

export function useDebugHistory(
  enabled: Ref<boolean>,
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

  const stop = watch(enabled, value => {
    if (!value) clear()
  })
  const sample = watch(stats, value => {
    if (!enabled.value) return
    if (value === null) clear()
    else append(value)
  })

  onUnmounted(() => {
    stop()
    sample()
  })

  return { history, clear }
}
