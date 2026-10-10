import { describe, expect, it } from 'vitest'
import { estimateReceiveToDisplay, summarizeStats } from './usePlaybackStats'

function report(timestamp: number, bytes: number, delay: number, count: number) {
  const values = new Map<string, RTCStats>([
    ['video', { id: 'video', type: 'inbound-rtp', timestamp, kind: 'video', ssrc: 42, codecId: 'codec', bytesReceived: bytes, jitterBufferDelay: delay, jitterBufferEmittedCount: count, framesDecoded: count, totalDecodeTime: delay } as RTCStats],
    ['codec', { id: 'codec', type: 'codec', timestamp, mimeType: 'video/H264', clockRate: 90000 } as RTCStats],
  ])
  return values as RTCStatsReport
}

describe('WebRTC stats', () => {
  it('uses consecutive samples and discards counters after reset', () => {
    const baseline = new Map()
    expect(summarizeStats(report(1000, 100, 1, 10), baseline).video?.bitrate).toBeNull()
    const second = summarizeStats(report(2000, 600, 1.5, 20), baseline)
    expect(second.video?.bitrate).toBe(4000)
    expect(second.video?.bufferMs).toBe(50)
    expect(second.video?.codec).toBe('video/H264 / 90000 Hz')
    const reset = summarizeStats(report(3000, 5, 0.1, 1), baseline)
    expect(reset.video?.bitrate).toBeNull()
    expect(reset.video?.bufferMs).toBeNull()
  })

  it('tracks selected pair activity and rejects reset deltas', () => {
    const baseline = new Map()
    const makeReport = (received: number, timestamp: number) => new Map<string, RTCStats>([
      ['transport', { id: 'transport', type: 'transport', timestamp, selectedCandidatePairId: 'pair' } as RTCStats],
      ['pair', { id: 'pair', type: 'candidate-pair', timestamp, bytesReceived: received, bytesSent: 100 } as RTCStats],
    ]) as RTCStatsReport
    expect(summarizeStats(makeReport(100, 1000), baseline).receiveDelta).toBeNull()
    const second = summarizeStats(makeReport(220, 2000), baseline)
    expect(second.receiveDelta).toBe(120)
    expect(second.receiveRate).toBe(960)
    expect(summarizeStats(makeReport(10, 3000), baseline).receiveRate).toBeNull()
  })

  it('reports browser frame receive to expected display only when both timestamps are valid', () => {
    const frame = (receiveTime?: number, expectedDisplayTime = 120) =>
      ({ receiveTime, expectedDisplayTime }) as VideoFrameCallbackMetadata & { receiveTime?: number }
    expect(estimateReceiveToDisplay(frame(100))).toBe(20)
    expect(estimateReceiveToDisplay(frame())).toBeNull()
    expect(estimateReceiveToDisplay(frame(130))).toBeNull()
  })
})
