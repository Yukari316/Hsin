export interface ReaderConfiguration {
  url: string
  user?: string
  pass?: string
  token?: string
  onError?: (error: string) => void
  onTrack?: (event: RTCTrackEvent) => void
  onDataChannel?: (event: RTCDataChannelEvent) => void
}

export default class MediaMTXWebRTCReader {
  constructor(configuration: ReaderConfiguration)
  close(): void
  getStats(): Promise<RTCStatsReport | null>
}
