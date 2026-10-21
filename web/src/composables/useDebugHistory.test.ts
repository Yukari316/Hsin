import { defineComponent, h, nextTick, ref, shallowRef } from 'vue'
import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { DEBUG_HISTORY_LIMIT, useDebugHistory } from './useDebugHistory'
import type { PlaybackStats } from './usePlaybackStats'

function stats(fps: number): PlaybackStats {
  const track = { codec: null, decoder: null, powerEfficient: null, resolution: null, height: null, fps, bitrate: null, bytes: null, packetsLost: null, jitterMs: null, bufferMs: null, decodeMs: null }
  return { audio: null, video: track, rttMs: null, protocol: null, candidates: null, received: null, sent: null, receiveDelta: null, sendDelta: null, receiveRate: null, sendRate: null }
}

describe('debug history', () => {
  it('keeps the latest minute of samples and clears when stats stop', async () => {
    const host = defineComponent({
      setup(_, { expose }) {
        const currentStats = shallowRef<PlaybackStats | null>(null)
        const latency = ref<number | null>(12)
        const { history } = useDebugHistory(currentStats, latency)
        expose({ currentStats, history })
        return () => h('div')
      },
    })
    const wrapper = mount(host)
    const vm = wrapper.vm as unknown as { currentStats: PlaybackStats | null; history: Array<{ fps: number | null }> }

    for (let fps = 1; fps <= DEBUG_HISTORY_LIMIT + 1; fps++) {
      vm.currentStats = stats(fps)
      await nextTick()
    }
    expect(vm.history).toHaveLength(DEBUG_HISTORY_LIMIT)
    expect(vm.history[0].fps).toBe(2)
    expect(vm.history.at(-1)?.fps).toBe(DEBUG_HISTORY_LIMIT + 1)

    vm.currentStats = null
    await nextTick()
    expect(vm.history).toHaveLength(0)
    wrapper.unmount()
  })
})
