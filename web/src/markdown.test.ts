import { describe, expect, it } from 'vitest'
import { renderAnnouncement } from './markdown'

describe('announcement', () => {
  it('keeps safe GitHub style content and removes active HTML', () => {
    const html = renderAnnouncement('| A | B |\n|---|---|\n|1|2|\n\n- [x] Done\n\n<strong>OK</strong><script>alert(1)</script><a href="javascript:alert(1)" onclick="alert(1)">bad</a>')
    expect(html).toContain('<table>')
    expect(html).toContain('type="checkbox"')
    expect(html).toContain('disabled')
    expect(html).toContain('<strong>OK</strong>')
    expect(html).not.toContain('<script')
    expect(html).not.toContain('javascript:')
    expect(html).not.toContain('onclick=')
  })
})
