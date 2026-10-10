import { onUnmounted, ref, shallowRef } from 'vue'
import MediaMTXWebRTCReader from '../vendor/reader.js'

export function useWebRtcPlayer() {
  const stream = shallowRef<MediaStream | null>(null)
  const connecting = ref(false)
  const error = ref('')
  let reader: MediaMTXWebRTCReader | null = null
  let epoch = 0

  function stop() {
    epoch++
    reader?.close()
    reader = null
    stream.value = null
    connecting.value = false
    error.value = ''
  }

  function start(url: string, token: string | null) {
    stop()
    const current = epoch
    connecting.value = true
    reader = new MediaMTXWebRTCReader({
      url,
      ...(token === null ? {} : { user: 'viewer', pass: token }),
      onTrack: event => {
        if (current !== epoch) return
        const target = stream.value ?? new MediaStream()
        if (!target.getTracks().some(track => track.id === event.track.id)) target.addTrack(event.track)
        stream.value = target
        connecting.value = false
        error.value = ''
      },
      onError: message => {
        if (current !== epoch) return
        connecting.value = true
        error.value = message
        stream.value = null
      },
    })
  }

  const getStats = () => reader?.getStats() ?? Promise.resolve(null)
  onUnmounted(stop)
  return { stream, connecting, error, start, stop, getStats }
}
