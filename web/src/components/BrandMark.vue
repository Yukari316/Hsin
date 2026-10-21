<script setup lang="ts">
import { Tv } from '@lucide/vue'

withDefaults(defineProps<{ logoUrl: string | null; size?: 'md' | 'lg' }>(), { size: 'md' })
</script>

<template>
  <img v-if="logoUrl" :src="logoUrl" alt="" :class="['object-contain', size === 'lg' ? 'size-12' : 'size-9']" />
  <div
    v-else
    :class="['grid shrink-0 place-items-center bg-base-content text-base-100', size === 'lg' ? 'size-12 rounded-2xl' : 'size-9 rounded-xl']"
    aria-hidden="true"
  >
    <Tv :size="size === 'lg' ? 24 : 18" />
  </div>
</template>
