import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import StreamInfoCard from './StreamInfoCard.vue'
import type { PlaybackStats } from '../composables/usePlaybackStats'

const video = { codec: null, decoder: null, powerEfficient: null, resolution: '1920 × 1080', height: 1080, fps: 59.6, bitrate: 6_210_000, bytes: null, packetsLost: null, jitterMs: null, bufferMs: null, decodeMs: null }
const stats: PlaybackStats = { audio: null, video, rttMs: null, protocol: null, candidates: null, received: null, sent: null, receiveDelta: null, sendDelta: null, receiveRate: null, sendRate: null }

describe('stream info card', () => {
  it('summarizes resolution, frame rate, video bitrate and receive-to-display estimate', () => {
    const wrapper = mount(StreamInfoCard, { props: { stats, receiveToDisplayMs: 37.6 } })
    expect(wrapper.findAll('dd').map(item => item.text())).toEqual(['1080p', '60 fps', '6.21 Mbps', '38 ms'])
  })

  it('shows placeholders without playback data', () => {
    const wrapper = mount(StreamInfoCard, { props: { stats: null, receiveToDisplayMs: null } })
    expect(wrapper.findAll('dd').map(item => item.text())).toEqual(['—', '—', '—', '—'])
  })
})
