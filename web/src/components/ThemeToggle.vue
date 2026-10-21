<script setup lang="ts">
import { Moon, Sun } from '@lucide/vue'
import { theme, toggleTheme } from '../theme'
</script>

<template>
  <div class="tooltip tooltip-left" :data-tip="theme === 'dark' ? '浅色主题' : '深色主题'">
    <button class="btn btn-circle btn-ghost" :aria-label="theme === 'dark' ? '切换浅色主题' : '切换深色主题'" @click="toggleTheme">
      <Sun v-if="theme === 'dark'" :size="20" />
      <Moon v-else :size="20" />
    </button>
  </div>
</template>
