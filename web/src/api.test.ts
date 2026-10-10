import { describe, expect, it } from 'vitest'
import { bearerAuthorization } from './api'

describe('room credentials', () => {
  it('omits authorization for anonymous access and sends a bearer token', () => {
    expect(bearerAuthorization(null)).toEqual({})
    const header = bearerAuthorization('房间') as Record<string, string>
    expect(header.Authorization).toBe('Bearer 房间')
  })
})
