import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import MetricSparkline from './MetricSparkline.vue'

describe('metric sparkline', () => {
  it('shows the adaptive range and leaves unavailable samples as line breaks', () => {
    const wrapper = mount(MetricSparkline, {
      props: { label: 'RTT', unit: ' ms', values: [10, 20, null, 15] },
    })

    expect(wrapper.get('.metric-sparkline-current').text()).toBe('15.00 ms')
    expect(wrapper.get('.metric-sparkline-min').text()).toBe('最低 10.00 ms')
    expect(wrapper.get('.metric-sparkline-max').text()).toBe('最高 20.00 ms')
    expect(wrapper.get('.spark-line').attributes('d')).toBe('M0.00,48.00 L66.67,0.00 M200.00,24.00')
    expect(wrapper.get('.spark-area').attributes('d')).toBe('M0.00,48 L0.00,48.00 L66.67,0.00 L66.67,48 Z')
  })

  it('formats rate values below one Mbps as Kbps', () => {
    const wrapper = mount(MetricSparkline, {
      props: { label: '视频码率', rate: true, unit: '', values: [0.8, 1.2] },
    })

    expect(wrapper.get('.metric-sparkline-current').text()).toBe('1.20 Mbps')
    expect(wrapper.get('.metric-sparkline-min').text()).toBe('最低 800.00 Kbps')
    expect(wrapper.get('.metric-sparkline-max').text()).toBe('最高 1.20 Mbps')
  })

  it('shows unavailable without drawing when no sample exists', () => {
    const wrapper = mount(MetricSparkline, {
      props: { label: '帧率', unit: ' fps', values: [null, null] },
    })

    expect(wrapper.get('.metric-sparkline-current').text()).toBe('不可用')
    expect(wrapper.find('path').exists()).toBe(false)
  })
})
