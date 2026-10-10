<script setup lang="ts">
import { computed } from 'vue'
import { formatRateMbps } from '../formatters'

const props = defineProps<{
  label: string
  unit: string
  rate?: boolean
  values: Array<number | null>
}>()

const validValues = computed(() => props.values.filter((value): value is number => value != null && Number.isFinite(value)))
const current = computed(() => validValues.value.at(-1) ?? null)
const minimum = computed(() => validValues.value.length ? Math.min(...validValues.value) : null)
const maximum = computed(() => validValues.value.length ? Math.max(...validValues.value) : null)
const path = computed(() => {
  const values = props.values
  if (!validValues.value.length) return ''
  const min = minimum.value ?? 0
  const max = maximum.value ?? min
  const span = max - min || 1
  const width = 140
  const height = 56
  let startedSegment = false
  return values.map((value, index) => {
    if (value == null || !Number.isFinite(value)) {
      startedSegment = false
      return ''
    }
    const x = values.length <= 1 ? 0 : index / (values.length - 1) * width
    const y = height - ((value - min) / span) * height
    const command = startedSegment ? 'L' : 'M'
    startedSegment = true
    return `${command}${x.toFixed(2)},${y.toFixed(2)}`
  }).filter(Boolean).join(' ')
})

function format(value: number | null) {
  if (props.rate) return formatRateMbps(value)
  return value == null ? '不可用' : `${value.toFixed(2)}${props.unit}`
}
</script>

<template>
  <div class="metric-sparkline" role="img" :aria-label="`${label}曲线`">
    <div class="metric-sparkline-label">{{ label }} {{ format(current) }}</div>
    <svg viewBox="0 0 148 66" role="img" aria-hidden="true">
      <path v-if="path" :d="path" fill="none" stroke="currentColor" stroke-width="1.5" vector-effect="non-scaling-stroke" transform="translate(4 5)" />
    </svg>
    <span class="metric-sparkline-max">{{ format(maximum) }}</span>
    <span class="metric-sparkline-min">{{ format(minimum) }}</span>
  </div>
</template>
