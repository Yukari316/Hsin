import { describe, expect, it, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import LivePlayer from './LivePlayer.vue'
import type { Room } from '../types'

const start = vi.fn()
const stop = vi.fn()
vi.mock('../composables/useWebRtcPlayer', () => ({
  useWebRtcPlayer: () => ({ stream: { value: null }, connecting: { value: false }, error: { value: '' }, start, stop, getStats: () => Promise.resolve(null) }),
}))

const live: Room = { status: 'live', generation: '1', whepUrl: 'http://localhost/live/whep', idleMessage: '', idleIconUrl: null }

describe('live player', () => {
  vi.stubGlobal('HTMLMediaElement', HTMLMediaElement)
  vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => {})
  it('keeps one connection across identical polls and reconnects for a new source', async () => {
    start.mockClear()
    stop.mockClear()
    const wrapper = mount(LivePlayer, { props: { room: live, token: null, debugOpen: false } })
    expect(start).toHaveBeenCalledTimes(1)
    await wrapper.setProps({ room: { ...live } })
    expect(start).toHaveBeenCalledTimes(1)
    await wrapper.setProps({ room: { ...live, generation: '2' } })
    expect(start).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('clears the connection when idle and uses the translucent surface', async () => {
    const wrapper = mount(LivePlayer, { props: { room: live, token: null, debugOpen: false } })
    await wrapper.setProps({ room: { ...live, status: 'idle', whepUrl: null } })
    await nextTick()
    expect(wrapper.get('.player-shell').classes()).toContain('is-idle')
    expect(wrapper.find('video').exists()).toBe(false)
    expect(stop).toHaveBeenCalled()
    wrapper.unmount()
  })
})
