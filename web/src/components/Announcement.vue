<script setup lang="ts">
import { computed } from 'vue'
import { renderAnnouncement } from '../markdown'

const props = defineProps<{ markdown: string }>()
const html = computed(() => renderAnnouncement(props.markdown))
</script>

<template>
  <section v-if="markdown.trim()" class="announcement card mt-5 rounded-box border border-base-300 bg-base-100 shadow-sm" aria-label="公告">
    <div class="markdown-body" v-html="html" />
  </section>
</template>
