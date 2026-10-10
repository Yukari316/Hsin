<script setup lang="ts">
import { ref } from 'vue'
import { ArrowRight } from '@lucide/vue'
import { checkAccess } from '../api'

const props = defineProps<{ prompt: string }>()
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
    else error.value = '密码错误'
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
  <main class="password-wrap">
    <form class="password-form" @submit.prevent @keydown="onKeydown">
      <p class="mb-4 text-center text-base font-medium">{{ props.prompt }}</p>
      <div class="join w-full">
        <input
          v-model="token"
          type="password"
          autocomplete="off"
          :aria-invalid="!!error"
          :aria-describedby="error ? 'password-error' : undefined"
          :class="['input join-item min-w-0 flex-1', { 'input-error': error }]"
          aria-label="房间密码"
          :disabled="pending"
          @compositionstart="compositionStart"
          @compositionend="compositionEnd"
          @input="error = ''"
        />
        <button class="btn btn-primary join-item" type="button" aria-label="进入房间" title="进入房间" :disabled="pending" @click="submit">
          <span v-if="pending" class="loading loading-spinner loading-sm" />
          <ArrowRight v-else :size="19" />
        </button>
      </div>
      <p v-if="error" id="password-error" class="text-error mt-2 text-sm" role="alert">{{ error }}</p>
    </form>
  </main>
</template>
