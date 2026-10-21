import { describe, expect, it, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import PasswordPage from './PasswordPage.vue'
import * as api from '../api'

vi.mock('../api', () => ({ checkAccess: vi.fn() }))

describe('password entry', () => {
  beforeEach(() => vi.mocked(api.checkAccess).mockReset())

  it('accepts plain Enter, including Unicode credentials', async () => {
    vi.mocked(api.checkAccess).mockResolvedValue(true)
    const wrapper = mount(PasswordPage, { props: { title: 'Hsin', logoUrl: null, prompt: '请输入房间密码' } })
    await wrapper.get('input').setValue('密碼')
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })
    await vi.waitFor(() => expect(wrapper.emitted('authenticated')?.[0]).toEqual(['密碼']))
  })

  it.each(['ctrlKey', 'altKey', 'shiftKey', 'metaKey'])('ignores Enter with %s', async modifier => {
    const wrapper = mount(PasswordPage, { props: { title: 'Hsin', logoUrl: null, prompt: '密码' } })
    await wrapper.get('input').trigger('keydown', { key: 'Enter', [modifier]: true })
    expect(api.checkAccess).not.toHaveBeenCalled()
  })

  it('ignores composing Enter and marks the field on rejection', async () => {
    vi.mocked(api.checkAccess).mockResolvedValue(false)
    const wrapper = mount(PasswordPage, { props: { title: 'Hsin', logoUrl: null, prompt: '密码' } })
    await wrapper.get('input').trigger('compositionstart')
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })
    expect(api.checkAccess).not.toHaveBeenCalled()
    await wrapper.get('input').trigger('compositionend')
    await wrapper.get('button').trigger('click')
    await vi.waitFor(() => expect(wrapper.get('input').attributes('aria-invalid')).toBe('true'))
  })
})
