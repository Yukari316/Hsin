export interface Bootstrap {
  requiresToken: boolean
  title: string
  logoUrl: string | null
  backgroundLeftUrl: string | null
  backgroundRightUrl: string | null
  mobileBackgroundUrl: string | null
  passwordPrompt: string
}

export type RoomStatus = 'initializing' | 'idle' | 'live' | 'unavailable'

export interface Room {
  status: RoomStatus
  generation: string
  whepUrl: string | null
  idleMessage: string
  idleIconUrl: string | null
}
