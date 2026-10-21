<script setup lang="ts">
import { computed } from 'vue'
import { renderAnnouncement } from '../markdown'

const props = defineProps<{ markdown: string }>()
const html = computed(() => renderAnnouncement(props.markdown))
</script>

<template>
  <section v-if="markdown.trim()" class="announcement tile p-6 sm:p-8" aria-label="公告">
    <div class="tile-label mb-3">公告</div>
    <div class="markdown-body max-w-3xl" v-html="html" />
  </section>
</template>
