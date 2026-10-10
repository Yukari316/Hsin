import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaMTXWebRTCReader from './reader.js'

class PeerConnection {
  close = vi.fn()
  getStats = vi.fn().mockResolvedValue(new Map())
  addTransceiver() {}
  createDataChannel() {}
  async createOffer() {
    return { sdp: 'v=0\r\na=ice-ufrag:test\r\na=ice-pwd:test\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=rtpmap:111 opus/48000/2\r\na=fmtp:111 stereo=1;sprop-stereo=1\r\na=x pcma/8000/2 multiopus/48000/6 L16/48000/2\r\n' }
  }
  async setLocalDescription() {}
  async setRemoteDescription() {}
}

describe('reader resource lifecycle', () => {
  beforeEach(() => {
    vi.stubGlobal('RTCPeerConnection', PeerConnection)
    vi.stubGlobal('RTCSessionDescription', class { constructor(public value: unknown) {} })
  })

  it('deletes a WHEP session returned after close and encodes UTF-8 credentials', async () => {
    let finishPost: (response: Response) => void = () => {}
    const fetch = vi.fn((_url: string, options: RequestInit) => {
      if (options.method === 'OPTIONS') return Promise.resolve(new Response(null, { status: 200 }))
      if (options.method === 'POST') return new Promise<Response>(resolve => { finishPost = resolve })
      return Promise.resolve(new Response(null, { status: 204 }))
    })
    vi.stubGlobal('fetch', fetch)
    const reader = new MediaMTXWebRTCReader({ url: 'http://localhost/live/whep', user: 'viewer', pass: '房间' })
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    const headers = fetch.mock.calls[1][1].headers as Record<string, string>
    const bytes = Uint8Array.from(atob(headers.Authorization.slice(6)), char => char.charCodeAt(0))
    expect(new TextDecoder().decode(bytes)).toBe('viewer:房间')
    reader.close()
    finishPost(new Response('v=0', { status: 201, headers: { location: '/live/whep/session' } }))
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledWith('http://localhost/live/whep/session', expect.objectContaining({ method: 'DELETE' })))
    expect(await reader.getStats()).toBeNull()
  })

  it('aborts an in-flight OPTIONS request when closed', async () => {
    let signal: AbortSignal | null | undefined
    vi.stubGlobal('fetch', vi.fn((_url: string, options: RequestInit) => {
      signal = options.signal
      return new Promise<Response>(() => {})
    }))
    const reader = new MediaMTXWebRTCReader({ url: 'http://localhost/live/whep' })
    await vi.waitFor(() => expect(signal).toBeDefined())
    reader.close()
    expect(signal?.aborted).toBe(true)
  })
})
