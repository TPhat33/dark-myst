<script setup lang="ts">
import { ref } from 'vue'
import { bootstrapAdmin } from '../api'

const emit = defineEmits<{ 'logged-in': [token: string] }>()

const pastedToken = ref('')
const bootstrapName = ref('dev-admin')
const busy = ref(false)
const error = ref<string | null>(null)

function usePastedToken() {
  if (pastedToken.value.trim().length > 0) {
    emit('logged-in', pastedToken.value.trim())
  }
}

async function bootstrap() {
  busy.value = true
  error.value = null
  try {
    const admin = await bootstrapAdmin(bootstrapName.value || 'dev-admin')
    emit('logged-in', admin.accessToken)
  } catch (e) {
    error.value =
      'Could not bootstrap a dev admin — this only works when the API has ' +
      'Admin:AllowBootstrap enabled (Development by default). Paste an existing admin token instead.'
    console.error(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="login-card card">
    <h2>Sign in</h2>
    <p class="muted">
      Real admin SSO is deferred to phase E, same as player auth (docs/11-admin-spec.md). Paste an
      admin token you already have, or — on a dev/local API — create one.
    </p>

    <label>
      Admin token
      <input
        v-model="pastedToken"
        type="password"
        placeholder="paste an X-Admin-Token value"
        data-testid="token-input"
        @keyup.enter="usePastedToken"
      />
    </label>
    <button class="primary" data-testid="use-token" @click="usePastedToken">Use this token</button>

    <hr />

    <label>
      Name for a new dev admin
      <input v-model="bootstrapName" type="text" data-testid="bootstrap-name" />
    </label>
    <button data-testid="bootstrap-button" :disabled="busy" @click="bootstrap">
      {{ busy ? 'Creating…' : 'Create dev admin (Development only)' }}
    </button>

    <p v-if="error" class="error" data-testid="login-error">{{ error }}</p>
  </div>
</template>

<style scoped>
.login-card {
  max-width: 420px;
  margin: 2rem auto;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  font-size: 0.9rem;
}

.muted {
  color: var(--muted);
  font-size: 0.9rem;
}

.error {
  color: var(--danger);
}

hr {
  border: none;
  border-top: 1px solid var(--panel-border);
  margin: 0.25rem 0;
}
</style>
