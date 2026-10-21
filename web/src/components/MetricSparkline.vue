<script setup lang="ts">
import { computed } from 'vue'
import { formatRateMbps } from '../formatters'

const props = defineProps<{
  label: string
  unit: string
  rate?: boolean
  values: Array<number | null>
}>()

const WIDTH = 200
const HEIGHT = 48

const validValues = computed(() => props.values.filter((value): value is number => value != null && Number.isFinite(value)))
const current = computed(() => validValues.value.at(-1) ?? null)
const minimum = computed(() => validValues.value.length ? Math.min(...validValues.value) : null)
const maximum = computed(() => validValues.value.length ? Math.max(...validValues.value) : null)

// 缺失值断开曲线，不补零也不跨缺口连线；每段单独生成折线和填充区域。
const segments = computed(() => {
  const values = props.values
  const min = minimum.value ?? 0
  const span = (maximum.value ?? min) - min || 1
  const result: Array<Array<[number, number]>> = []
  let segment: Array<[number, number]> = []
  values.forEach((value, index) => {
    if (value == null || !Number.isFinite(value)) {
      if (segment.length) result.push(segment)
      segment = []
      return
    }
    const x = values.length <= 1 ? 0 : index / (values.length - 1) * WIDTH
    segment.push([x, HEIGHT - (value - min) / span * HEIGHT])
  })
  if (segment.length) result.push(segment)
  return result
})
const line = computed(() => segments.value
  .map(points => points.map(([x, y], index) => `${index ? 'L' : 'M'}${x.toFixed(2)},${y.toFixed(2)}`).join(' '))
  .join(' '))
const area = computed(() => segments.value
  .filter(points => points.length > 1)
  .map(points => `M${points[0][0].toFixed(2)},${HEIGHT} ${points.map(([x, y]) => `L${x.toFixed(2)},${y.toFixed(2)}`).join(' ')} L${points[points.length - 1][0].toFixed(2)},${HEIGHT} Z`)
  .join(' '))

function format(value: number | null) {
  if (props.rate) return formatRateMbps(value)
  return value == null ? '不可用' : `${value.toFixed(2)}${props.unit}`
}
</script>

<template>
  <div class="metric-sparkline tile px-4 pb-3 pt-3.5" role="img" :aria-label="`${label}：当前 ${format(current)}，最低 ${format(minimum)}，最高 ${format(maximum)}`">
    <div class="tile-label">{{ label }}</div>
    <div class="metric-sparkline-current num mt-1 text-xl font-semibold">{{ format(current) }}</div>
    <svg :viewBox="`0 -2 ${WIDTH} ${HEIGHT + 4}`" preserveAspectRatio="none" aria-hidden="true">
      <path v-if="area" class="spark-area" :d="area" />
      <path v-if="line" class="spark-line" :d="line" vector-effect="non-scaling-stroke" />
    </svg>
    <div class="num mt-1 flex justify-between gap-2 text-[11px] text-base-content/50">
      <span class="metric-sparkline-min">最低 {{ format(minimum) }}</span>
      <span class="metric-sparkline-max">最高 {{ format(maximum) }}</span>
    </div>
  </div>
</template>
