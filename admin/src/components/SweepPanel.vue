<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ApiError, getCurrent, runSweep } from '../api'
import type { EncounterData, SweepResult } from '../types'

const props = defineProps<{ adminToken: string }>()
const emit = defineEmits<{ unauthorized: [] }>()

const loading = ref(true)
const loadError = ref<string | null>(null)
const encounters = ref<EncounterData[]>([])
const defaultRoster = ref<string[]>([])

const encounterId = ref('')
const rosterText = ref('')
const level = ref(20)
const seed = ref(1)
const repeat = ref(200)

const running = ref(false)
const result = ref<SweepResult | null>(null)
const error = ref<string | null>(null)
// "sweep_repeat_too_large" / "sweep_in_progress" / "content_invalid" / other. Kept apart from the
// plain message so the template can react to the specific bound that fired.
const errorKind = ref<string | null>(null)

const roster = computed(() =>
  rosterText.value
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
)

async function handleUnauthorized<T>(run: () => Promise<T>): Promise<T | null> {
  try {
    return await run()
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) {
      emit('unauthorized')
      return null
    }
    throw e
  }
}

onMounted(async () => {
  loading.value = true
  try {
    const current = await handleUnauthorized(() => getCurrent(props.adminToken))
    if (current) {
      encounters.value = current.encounters
      if (encounters.value.length > 0) {
        encounterId.value = encounters.value[0].id
      }
      // A reasonable default roster so the panel is immediately usable: the first five characters
      // this content pack has, sorted the same way the "current" response already sorts them.
      defaultRoster.value = current.characters.slice(0, 5).map((c) => c.id)
      rosterText.value = defaultRoster.value.join(', ')
    }
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
})

async function runTheSweep() {
  running.value = true
  result.value = null
  error.value = null
  errorKind.value = null
  try {
    const outcome = await handleUnauthorized(() =>
      runSweep(props.adminToken, encounterId.value, roster.value, level.value, seed.value, repeat.value)
    )
    if (outcome) {
      result.value = outcome
      // Clamp the input back to whatever the server just told us its cap is, so a designer who
      // hits the cap once cannot keep re-triggering the same refusal.
      if (repeat.value > outcome.maxRepeat) {
        repeat.value = outcome.maxRepeat
      }
    }
  } catch (e) {
    if (e instanceof ApiError) {
      errorKind.value = e.problem.error ?? null
      error.value = e.problem.message ?? e.message
      if (e.problem.error === 'sweep_repeat_too_large' && typeof e.problem.max === 'number') {
        repeat.value = e.problem.max
      }
    } else {
      error.value = e instanceof Error ? e.message : String(e)
    }
  } finally {
    running.value = false
  }
}
</script>

<template>
  <div v-if="loading" class="muted">Loading content…</div>
  <div v-else class="sweep-panel">
    <p v-if="loadError" class="error" data-testid="sweep-load-error">{{ loadError }}</p>

    <section class="card sweep-form">
      <h2>Sweep</h2>
      <p class="muted">
        Runs many seeds of an attacker roster against one encounter and reports win rate, average
        length and survival by character — the same loop <code>simrunner sweep</code> runs from a
        terminal, without leaving the browser (docs/05-content-pipeline.md).
      </p>

      <div class="row">
        <label>
          Encounter
          <select v-model="encounterId" data-testid="sweep-field-encounter">
            <option v-for="e in encounters" :key="e.id" :value="e.id">{{ e.name }} ({{ e.id }})</option>
          </select>
        </label>
      </div>

      <label>
        Roster (comma-separated character ids)
        <input v-model="rosterText" data-testid="sweep-field-roster" />
      </label>

      <div class="row">
        <label>
          Level
          <input v-model.number="level" type="number" min="1" data-testid="sweep-field-level" />
        </label>
        <label>
          Seed
          <input v-model.number="seed" type="number" min="0" data-testid="sweep-field-seed" />
        </label>
        <label>
          Repeat (battles)
          <input v-model.number="repeat" type="number" min="1" data-testid="sweep-field-repeat" />
        </label>
      </div>

      <button class="primary" data-testid="sweep-run-button" :disabled="running || roster.length === 0" @click="runTheSweep">
        {{ running ? 'Running sweep…' : 'Run sweep' }}
      </button>

      <p v-if="error" class="error" data-testid="sweep-error" :data-error-kind="errorKind ?? ''">{{ error }}</p>

      <div v-if="result" class="sweep-result" data-testid="sweep-result">
        <h3>Result</h3>
        <p>
          <strong>{{ result.encounterId }}</strong> — {{ result.battles }} battles (max
          {{ result.maxRepeat }} per sweep)
        </p>
        <p data-testid="sweep-result-summary">
          Win rate {{ ((result.wins / result.battles) * 100).toFixed(1) }}% · draws
          {{ ((result.draws / result.battles) * 100).toFixed(1) }}% · avg
          {{ result.averageRounds.toFixed(1) }} rounds
        </p>
        <h4>Survival by character</h4>
        <ul class="survivor-list" data-testid="sweep-survivors">
          <li v-for="s in result.survivors" :key="s.characterId">
            {{ s.characterId }} — {{ ((s.survived / result.battles) * 100).toFixed(1) }}%
          </li>
        </ul>
      </div>
    </section>
  </div>
</template>

<style scoped>
.sweep-panel {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.sweep-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: 640px;
}

.row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.row label {
  flex: 1;
  min-width: 120px;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.85rem;
}

select {
  font: inherit;
  background: #0d0f14;
  border: 1px solid var(--panel-border);
  color: var(--text);
  border-radius: 6px;
  padding: 0.4rem 0.55rem;
}

.error {
  color: var(--danger);
}

.sweep-result {
  border-top: 1px solid var(--panel-border);
  padding-top: 0.75rem;
}

.survivor-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  font-family: ui-monospace, Menlo, Consolas, monospace;
  font-size: 0.85rem;
}
</style>
