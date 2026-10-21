import { onUnmounted, ref, watch, type Ref } from 'vue'

type Sample = RTCStats & Record<string, number | string | boolean | undefined>
export interface TrackStats {
  codec: string | null
  decoder: string | null
  powerEfficient: boolean | null
  resolution: string | null
  height: number | null
  fps: number | null
  bitrate: number | null
  bytes: number | null
  packetsLost: number | null
  jitterMs: number | null
  bufferMs: number | null
  decodeMs: number | null
}
export interface PlaybackStats {
  audio: TrackStats | null
  video: TrackStats | null
  rttMs: number | null
  protocol: string | null
  candidates: string | null
  received: number | null
  sent: number | null
  receiveDelta: number | null
  sendDelta: number | null
  receiveRate: number | null
  sendRate: number | null
}

function number(sample: Sample, field: string): number | null {
  return typeof sample[field] === 'number' ? sample[field] as number : null
}

function delta(current: Sample, previous: Sample | undefined, field: string): number | null {
  const value = number(current, field)
  const old = previous ? number(previous, field) : null
  return value !== null && old !== null && value >= old ? value - old : null
}

function average(current: Sample, previous: Sample | undefined, total: string, count: string): number | null {
  const time = delta(current, previous, total)
  const samples = delta(current, previous, count)
  return time !== null && samples !== null && samples > 0 ? 1000 * time / samples : null
}

export function estimateReceiveToDisplay(metadata: VideoFrameCallbackMetadata & { receiveTime?: number }): number | null {
  const receiveTime = metadata.receiveTime
  const estimate = receiveTime === undefined ? null : metadata.expectedDisplayTime - receiveTime
  return estimate !== null && Number.isFinite(estimate) && estimate >= 0 && estimate <= 60_000 ? estimate : null
}

export function summarizeStats(report: RTCStatsReport, baseline: Map<string, Sample>): PlaybackStats {
  const result: PlaybackStats = { audio: null, video: null, rttMs: null, protocol: null, candidates: null, received: null, sent: null, receiveDelta: null, sendDelta: null, receiveRate: null, sendRate: null }
  const next = new Map<string, Sample>()
  report.forEach(value => {
    const sample = value as Sample
    if (sample.type === 'inbound-rtp' && (sample.kind === 'video' || sample.kind === 'audio')) {
      const key = `${sample.id}:${sample.ssrc}`
      const previous = baseline.get(key)
      const elapsed = previous ? (sample.timestamp - previous.timestamp) / 1000 : 0
      const bytes = delta(sample, previous, 'bytesReceived')
      const codec = report.get(sample.codecId as string) as Sample | undefined
      const jitter = number(sample, 'jitter')
      result[sample.kind] = {
        codec: codec && codec.mimeType ? `${codec.mimeType}${codec.clockRate ? ` / ${codec.clockRate} Hz` : ''}` : null,
        decoder: typeof sample.decoderImplementation === 'string' ? sample.decoderImplementation : null,
        powerEfficient: typeof sample.powerEfficientDecoder === 'boolean' ? sample.powerEfficientDecoder : null,
        resolution: typeof sample.frameWidth === 'number' && typeof sample.frameHeight === 'number' ? `${sample.frameWidth} × ${sample.frameHeight}` : null,
        height: number(sample, 'frameHeight'),
        fps: number(sample, 'framesPerSecond'),
        bitrate: bytes !== null && elapsed > 0 ? 8 * bytes / elapsed : null,
        bytes: number(sample, 'bytesReceived'),
        packetsLost: number(sample, 'packetsLost'),
        jitterMs: jitter !== null ? jitter * 1000 : null,
        bufferMs: average(sample, previous, 'jitterBufferDelay', 'jitterBufferEmittedCount'),
        decodeMs: average(sample, previous, 'totalDecodeTime', 'framesDecoded'),
      }
      next.set(key, sample)
    }
    if (sample.type === 'transport' && sample.selectedCandidatePairId) {
      const pair = report.get(sample.selectedCandidatePairId as string) as Sample | undefined
      if (!pair) return
      const local = report.get(pair.localCandidateId as string) as Sample | undefined
      const remote = report.get(pair.remoteCandidateId as string) as Sample | undefined
      const rtt = number(pair, 'currentRoundTripTime')
      result.rttMs = rtt !== null ? rtt * 1000 : null
      result.protocol = local && typeof local.protocol === 'string' ? local.protocol : null
      result.candidates = local?.candidateType && remote?.candidateType ? `${local.candidateType} → ${remote.candidateType}` : null
      result.received = number(pair, 'bytesReceived')
      result.sent = number(pair, 'bytesSent')
      result.receiveDelta = delta(pair, baseline.get(pair.id), 'bytesReceived')
      result.sendDelta = delta(pair, baseline.get(pair.id), 'bytesSent')
      const elapsed = baseline.has(pair.id) ? (pair.timestamp - baseline.get(pair.id)!.timestamp) / 1000 : 0
      result.receiveRate = result.receiveDelta !== null && elapsed > 0 ? 8 * result.receiveDelta / elapsed : null
      result.sendRate = result.sendDelta !== null && elapsed > 0 ? 8 * result.sendDelta / elapsed : null
      next.set(pair.id, pair)
    }
  })
  baseline.clear()
  next.forEach((sample, key) => baseline.set(key, sample))
  return result
}

export function usePlaybackStats(enabled: Ref<boolean>, getStats: () => Promise<RTCStatsReport | null>) {
  const stats = ref<PlaybackStats | null>(null)
  const baseline = new Map<string, Sample>()
  let timer: ReturnType<typeof setTimeout> | undefined
  let epoch = 0
  async function sample(current: number) {
    try {
      const report = await getStats()
      if (current !== epoch) return
      stats.value = report ? summarizeStats(report, baseline) : null
      if (!report) baseline.clear()
    } catch {
      if (current !== epoch) return
      stats.value = null
      baseline.clear()
    }
    if (current === epoch && enabled.value) timer = setTimeout(() => sample(current), 1000)
  }
  const stop = () => { epoch++; clearTimeout(timer); baseline.clear(); stats.value = null }
  const restart = () => { stop(); if (enabled.value) void sample(epoch) }
  watch(enabled, restart)
  onUnmounted(stop)
  return { stats, restart }
}
