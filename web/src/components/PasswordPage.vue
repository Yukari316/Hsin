<script setup lang="ts">
import { ref } from 'vue'
import { ArrowRight, LockKeyhole } from '@lucide/vue'
import { checkAccess } from '../api'
import BrandMark from './BrandMark.vue'

const props = defineProps<{ title: string; logoUrl: string | null; prompt: string }>()
const emit = defineEmits<{ authenticated: [token: string] }>()
const token = ref('')
const pending = ref(false)
const error = ref('')
const composing = ref(false)
function compositionStart() { composing.value = true }
function compositionEnd() { composing.value = false }

async function submit() {
  if (pending.value) return
  pending.value = true
  error.value = ''
  try {
    if (await checkAccess(token.value)) emit('authenticated', token.value)
    else error.value = '密码错误，请重试'
  } catch {
    error.value = '验证暂时不可用，请重试'
  } finally {
    pending.value = false
  }
}

function onKeydown(event: KeyboardEvent) {
  if (event.key !== 'Enter') return
  event.preventDefault()
  if (event.ctrlKey || event.altKey || event.shiftKey || event.metaKey || event.isComposing || composing.value) return
  void submit()
}
</script>

<template>
  <main class="password-page relative grid min-h-dvh place-items-center p-6">
    <form class="tile w-full max-w-sm p-8" @submit.prevent @keydown="onKeydown">
      <BrandMark :logo-url="props.logoUrl" size="lg" />
      <h1 class="mt-6 text-2xl font-semibold tracking-tight">{{ props.title }}</h1>
      <p class="mt-1 text-sm text-base-content/60">{{ props.prompt }}</p>
      <label :class="['input mt-6 w-full bg-base-200', error ? 'input-error' : 'border-transparent']">
        <LockKeyhole :size="18" class="opacity-45" aria-hidden="true" />
        <input
          v-model="token"
          type="password"
          autocomplete="current-password"
          placeholder="房间密码"
          aria-label="房间密码"
          :aria-invalid="!!error"
          :aria-describedby="error ? 'password-error' : undefined"
          :disabled="pending"
          @compositionstart="compositionStart"
          @compositionend="compositionEnd"
          @input="error = ''"
        />
      </label>
      <p v-if="error" id="password-error" class="mt-2 text-sm text-error" role="alert">{{ error }}</p>
      <button class="btn btn-primary mt-4 w-full" type="button" :disabled="pending" @click="submit">
        <span v-if="pending" class="loading loading-spinner loading-sm" aria-hidden="true" />
        进入房间
        <ArrowRight v-if="!pending" :size="18" aria-hidden="true" />
      </button>
    </form>
  </main>
</template>
