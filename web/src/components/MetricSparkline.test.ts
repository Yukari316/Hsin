import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import MetricSparkline from './MetricSparkline.vue'

describe('metric sparkline', () => {
  it('shows the adaptive range and leaves unavailable samples as line breaks', () => {
    const wrapper = mount(MetricSparkline, {
      props: { label: 'RTT', unit: ' ms', values: [10, 20, null, 15] },
    })

    expect(wrapper.text()).toContain('20.00 ms')
    expect(wrapper.text()).toContain('10.00 ms')
    expect(wrapper.get('.metric-sparkline-label').text()).toBe('RTT 15.00 ms')
    expect(wrapper.get('path').attributes('d')).toBe('M0.00,56.00 L46.67,0.00 M140.00,28.00')
    expect(wrapper.get('path').attributes('stroke')).toBe('currentColor')
  })

  it('formats rate values below one Mbps as Kbps', () => {
    const wrapper = mount(MetricSparkline, {
      props: { label: '视频码率', rate: true, unit: '', values: [0.8, 1.2] },
    })

    expect(wrapper.get('.metric-sparkline-label').text()).toBe('视频码率 1.20 Mbps')
    expect(wrapper.get('.metric-sparkline-min').text()).toBe('800.00 Kbps')
    expect(wrapper.get('.metric-sparkline-max').text()).toBe('1.20 Mbps')
  })
})
