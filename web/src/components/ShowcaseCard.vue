<script setup lang="ts">
defineProps<{ imageUrl: string }>()
</script>

<template>
  <section class="showcase-card tile" aria-label="展示图片">
    <div class="showcase-backdrop" :style="{ backgroundImage: `url(${JSON.stringify(imageUrl)})` }" aria-hidden="true" />
    <img :src="imageUrl" alt="" />
  </section>
</template>
