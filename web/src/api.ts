import type { Bootstrap, Room } from './types'

export class UnauthorizedError extends Error {}

export function bearerAuthorization(token: string | null): HeadersInit {
  if (token === null) return {}
  return { Authorization: `Bearer ${token}` }
}

async function request(path: string, token: string | null, signal?: AbortSignal): Promise<Response> {
  const response = await fetch(path, {
    headers: bearerAuthorization(token),
    cache: 'no-store',
    signal,
  })
  if (response.status === 401) throw new UnauthorizedError('房间访问已失效')
  if (!response.ok) throw new Error(`请求失败 (${response.status})`)
  return response
}

export async function getBootstrap(): Promise<Bootstrap> {
  return (await request('/api/bootstrap', null)).json() as Promise<Bootstrap>
}

export async function checkAccess(token: string): Promise<boolean> {
  const response = await fetch('/api/access', {
    method: 'POST',
    headers: bearerAuthorization(token),
    cache: 'no-store',
  })
  if (response.status === 401) return false
  if (!response.ok) throw new Error(`验证失败 (${response.status})`)
  return true
}

export async function getRoom(token: string | null, signal?: AbortSignal): Promise<Room> {
  return (await request('/api/room', token, signal)).json() as Promise<Room>
}

export async function getAnnouncement(token: string | null, signal?: AbortSignal): Promise<string> {
  return (await request('/api/announcement', token, signal)).text()
}

export async function getAppearanceAsset(path: string, token: string | null, signal?: AbortSignal): Promise<Blob> {
  return (await request(path, token, signal)).blob()
}
