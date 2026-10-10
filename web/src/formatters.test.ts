import { describe, expect, it } from 'vitest'
import { formatRateMbps } from './formatters'

describe('rate formatting', () => {
  it('uses decimal Kbps below one Mbps and Mbps at or above one Mbps', () => {
    expect(formatRateMbps(0.8)).toBe('800.00 Kbps')
    expect(formatRateMbps(0.999)).toBe('999.00 Kbps')
    expect(formatRateMbps(1)).toBe('1.00 Mbps')
    expect(formatRateMbps(1.2)).toBe('1.20 Mbps')
    expect(formatRateMbps(null)).toBe('不可用')
  })
})
