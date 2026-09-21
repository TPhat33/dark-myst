<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import {
  ApiError,
  diffVersions as apiDiffVersions,
  getCurrent,
  listVersions,
  newIdempotencyKey,
  publishCharacters,
  rollbackTo,
  validateCharacters
} from '../api'
import { diffCharacterList } from '../lib/diff'
import type { AdminValidateResult, AdminVersionEntry, CharacterData, ContentDiff } from '../types'
import CharacterEditForm from './CharacterEditForm.vue'

const props = defineProps<{ adminToken: string }>()
const emit = defineEmits<{ unauthorized: [] }>()

const loading = ref(true)
const loadError = ref<string | null>(null)

const currentVersion = ref<string>('')
const originalCharacters = ref<CharacterData[]>([])
const editable = reactive<{ characters: CharacterData[] }>({ characters: [] })

const selectedId = ref<string | null>(null)
const selected = computed(() => editable.characters.find((c) => c.id === selectedId.value) ?? null)

const validation = ref<AdminValidateResult | null>(null)
const validating = ref(false)

const publishNotes = ref('')
const publishing = ref(false)
const publishMessage = ref<string | null>(null)
const publishProblems = ref<string[] | null>(null)

const versions = ref<AdminVersionEntry[]>([])
const versionsLoading = ref(false)
const rollingBackVersion = ref<string | null>(null)

const compareFrom = ref('')
const compareTo = ref('')
const compareResult = ref<ContentDiff | null>(null)
const compareError = ref<string | null>(null)

const pendingDiff = computed(() => diffCharacterList(originalCharacters.value, editable.characters))
const hasPendingChanges = computed(
  () => pendingDiff.value.changed.length > 0 || pendingDiff.value.added.length > 0 || pendingDiff.value.removed.length > 0
)

function cloneAll(characters: CharacterData[]): CharacterData[] {
  return characters.map((c) => JSON.parse(JSON.stringify(c)) as CharacterData)
}

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

async function loadCurrent() {
  loading.value = true
  loadError.value = null
  const result = await handleUnauthorized(() => getCurrent(props.adminToken))
  if (result) {
    currentVersion.value = result.version
    originalCharacters.value = cloneAll(result.characters)
    editable.characters = cloneAll(result.characters)
    if (!selectedId.value && editable.characters.length > 0) {
      selectedId.value = editable.characters[0].id
    }
    validation.value = null
  }
  loading.value = false
}

async function loadVersions() {
  versionsLoading.value = true
  const result = await handleUnauthorized(() => listVersions(props.adminToken))
  if (result) {
    versions.value = result
  }
  versionsLoading.value = false
}

function resetLocalEdits() {
  editable.characters = cloneAll(originalCharacters.value)
  validation.value = null
}

async function runValidate() {
  validating.value = true
  validation.value = null
  try {
    const result = await handleUnauthorized(() => validateCharacters(props.adminToken, editable.characters))
    if (result) {
      validation.value = result
    }
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : String(e)
  } finally {
    validating.value = false
  }
}

async function runPublish() {
  publishing.value = true
  publishMessage.value = null
  publishProblems.value = null
  try {
    const result = await handleUnauthorized(() =>
      publishCharacters(props.adminToken, editable.characters, publishNotes.value, newIdempotencyKey())
    )
    if (result) {
      publishMessage.value = `Published ${result.version} (was ${result.previousVersion}).`
      publishNotes.value = ''
      await loadCurrent()
      await loadVersions()
    }
  } catch (e) {
    if (e instanceof ApiError && e.problem.error === 'content_invalid') {
      publishProblems.value = e.problem.problems ?? [e.message]
    } else {
      loadError.value = e instanceof Error ? e.message : String(e)
    }
  } finally {
    publishing.value = false
  }
}

async function runRollback(targetVersion: string) {
  rollingBackVersion.value = targetVersion
  publishMessage.value = null
  try {
    const result = await handleUnauthorized(() =>
      rollbackTo(props.adminToken, targetVersion, `rollback to ${targetVersion}`, newIdempotencyKey())
    )
    if (result) {
      publishMessage.value = `Rolled back to ${result.version} (was ${result.previousVersion}).`
      await loadCurrent()
      await loadVersions()
    }
  } catch (e) {
    loadError.value = e instanceof Error ? e.message : String(e)
  } finally {
    rollingBackVersion.value = null
  }
}

async function runCompare() {
  compareError.value = null
  compareResult.value = null
  if (!compareFrom.value || !compareTo.value) {
    compareError.value = 'Pick both a "from" and a "to" version.'
    return
  }
  try {
    const result = await handleUnauthorized(() => apiDiffVersions(props.adminToken, compareFrom.value, compareTo.value))
    if (result) {
      compareResult.value = result
    }
  } catch (e) {
    compareError.value = e instanceof Error ? e.message : String(e)
  }
}

onMounted(async () => {
  await loadCurrent()
  await loadVersions()
  loading.value = false
})
</script>

<template>
  <div v-if="loading" class="muted">Loading content…</div>
  <div v-else class="workspace">
    <p v-if="loadError" class="error" data-testid="load-error">{{ loadError }}</p>

    <div class="version-banner card">
      <strong>Published version:</strong> <span data-testid="current-version">{{ currentVersion }}</span>
      <span v-if="hasPendingChanges" class="pending-badge" data-testid="pending-badge">unsaved edits</span>
    </div>

    <div class="columns">
      <section class="card character-list">
        <h2>Characters</h2>
        <ul>
          <li v-for="c in editable.characters" :key="c.id">
            <button
              class="char-button"
              :class="{ active: c.id === selectedId }"
              :data-testid="`select-${c.id}`"
              @click="selectedId = c.id"
            >
              {{ c.name }}
              <span v-if="pendingDiff.changed.some((ch) => ch.characterId === c.id)" class="dot" />
            </button>
          </li>
        </ul>
      </section>

      <section class="card editor-panel">
        <h2>Editor</h2>
        <CharacterEditForm v-if="selected" :character="selected" />
        <p v-else class="muted">Select a character to edit.</p>
      </section>

      <section class="card actions-panel">
        <h2>Validate &amp; publish</h2>

        <div class="pending-diff" data-testid="pending-diff">
          <h3>Pending changes</h3>
          <p v-if="!hasPendingChanges" class="muted">No unpublished edits.</p>
          <ul v-else>
            <li v-for="c in pendingDiff.changed" :key="c.characterId">
              <strong>{{ c.characterId }}</strong>
              <ul>
                <li v-for="f in c.fields" :key="f.path">
                  {{ f.path }}: {{ f.before }} → {{ f.after }}
                </li>
              </ul>
            </li>
          </ul>
          <button v-if="hasPendingChanges" class="link-button" @click="resetLocalEdits">Discard local edits</button>
        </div>

        <button data-testid="validate-button" :disabled="validating" @click="runValidate">
          {{ validating ? 'Validating…' : 'Validate' }}
        </button>

        <p v-if="validation?.valid" class="ok" data-testid="validate-ok">Valid — safe to publish.</p>
        <ul v-else-if="validation && !validation.valid" class="error" data-testid="validate-errors">
          <li v-for="err in validation.errors" :key="err">{{ err }}</li>
        </ul>

        <label>
          Publish notes
          <textarea v-model="publishNotes" rows="2" data-testid="publish-notes" />
        </label>
        <button class="primary" data-testid="publish-button" :disabled="publishing" @click="runPublish">
          {{ publishing ? 'Publishing…' : 'Publish new version' }}
        </button>

        <p v-if="publishMessage" class="ok" data-testid="publish-message">{{ publishMessage }}</p>
        <ul v-if="publishProblems" class="error" data-testid="publish-problems">
          <li v-for="p in publishProblems" :key="p">{{ p }}</li>
        </ul>

        <h3>Version history</h3>
        <p v-if="versionsLoading" class="muted">Loading…</p>
        <ul v-else class="version-list" data-testid="version-list">
          <li v-for="v in versions" :key="v.version + v.publishedAt">
            <span class="version-tag">{{ v.version }}</span>
            <span class="version-kind">{{ v.kind }}</span>
            <span v-if="v.notes" class="muted"> — {{ v.notes }}</span>
            <button
              v-if="v.version !== currentVersion"
              :disabled="rollingBackVersion === v.version"
              :data-testid="`rollback-${v.version}`"
              @click="runRollback(v.version)"
            >
              {{ rollingBackVersion === v.version ? 'Rolling back…' : 'Roll back to this' }}
            </button>
          </li>
          <li v-if="versions.length === 0" class="muted">No publishes yet.</li>
        </ul>

        <h3>Compare two versions</h3>
        <div class="compare-row">
          <input v-model="compareFrom" placeholder="from (e.g. 0.3.0)" data-testid="compare-from" />
          <input v-model="compareTo" placeholder="to (e.g. 0.3.1)" data-testid="compare-to" />
          <button data-testid="compare-button" @click="runCompare">Diff</button>
        </div>
        <p v-if="compareError" class="error">{{ compareError }}</p>
        <div v-if="compareResult" data-testid="compare-result">
          <p v-if="compareResult.addedCharacterIds.length">Added: {{ compareResult.addedCharacterIds.join(', ') }}</p>
          <p v-if="compareResult.removedCharacterIds.length">Removed: {{ compareResult.removedCharacterIds.join(', ') }}</p>
          <ul>
            <li v-for="c in compareResult.changedCharacters" :key="c.characterId">
              <strong>{{ c.characterId }}</strong>
              <ul>
                <li v-for="f in c.fields" :key="f.path">{{ f.path }}: {{ f.before }} → {{ f.after }}</li>
              </ul>
            </li>
          </ul>
          <p v-if="!compareResult.changedCharacters.length && !compareResult.addedCharacterIds.length && !compareResult.removedCharacterIds.length" class="muted">
            No differences.
          </p>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.workspace {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.version-banner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.pending-badge {
  background: #493a1a;
  color: #f2c94c;
  border-radius: 999px;
  padding: 0.1rem 0.6rem;
  font-size: 0.8rem;
}

.columns {
  display: grid;
  grid-template-columns: 220px 1fr 340px;
  gap: 1rem;
  align-items: start;
}

@media (max-width: 900px) {
  .columns {
    grid-template-columns: 1fr;
  }
}

.character-list ul {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.char-button {
  width: 100%;
  text-align: left;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.char-button.active {
  border-color: var(--accent);
  background: #1c2740;
}

.dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #f2c94c;
  display: inline-block;
}

.actions-panel {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.actions-panel label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.85rem;
}

.error {
  color: var(--danger);
}

.ok {
  color: var(--ok);
}

.version-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.version-list li {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.version-tag {
  font-family: monospace;
  background: #0d0f14;
  padding: 0.1rem 0.4rem;
  border-radius: 4px;
}

.version-kind {
  font-size: 0.75rem;
  color: var(--muted);
  text-transform: uppercase;
}

.compare-row {
  display: flex;
  gap: 0.4rem;
}

.compare-row input {
  width: 120px;
}
</style>
