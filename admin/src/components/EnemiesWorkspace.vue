<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ApiError, getCurrent, listVersions, newIdempotencyKey, publishEnemies, rollbackTo, validateEnemies } from '../api'
import { diffList } from '../lib/diff'
import type { AdminValidateResult, AdminVersionEntry, EnemyData } from '../types'
import EnemyEditForm from './EnemyEditForm.vue'

const props = defineProps<{ adminToken: string }>()
const emit = defineEmits<{ unauthorized: [] }>()

const loading = ref(true)
const loadError = ref<string | null>(null)

const currentVersion = ref('')
const rollbackableVersions = ref<string[]>([])
const originalEnemies = ref<EnemyData[]>([])
const editable = reactive<{ enemies: EnemyData[] }>({ enemies: [] })

const selectedId = ref<string | null>(null)
const selected = computed(() => editable.enemies.find((s) => s.id === selectedId.value) ?? null)

const validation = ref<AdminValidateResult | null>(null)
const validating = ref(false)

const publishNotes = ref('')
const publishing = ref(false)
const publishMessage = ref<string | null>(null)
const publishProblems = ref<string[] | null>(null)

const versions = ref<AdminVersionEntry[]>([])
const versionsLoading = ref(false)
const rollingBackVersion = ref<string | null>(null)

const pendingDiff = computed(() => diffList(originalEnemies.value, editable.enemies))
const hasPendingChanges = computed(
  () => pendingDiff.value.changed.length > 0 || pendingDiff.value.added.length > 0 || pendingDiff.value.removed.length > 0
)

function cloneAll(enemies: EnemyData[]): EnemyData[] {
  return enemies.map((s) => JSON.parse(JSON.stringify(s)) as EnemyData)
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
    rollbackableVersions.value = result.rollbackableVersions
    originalEnemies.value = cloneAll(result.enemies)
    editable.enemies = cloneAll(result.enemies)
    if (!selectedId.value && editable.enemies.length > 0) {
      selectedId.value = editable.enemies[0].id
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
  editable.enemies = cloneAll(originalEnemies.value)
  validation.value = null
}

async function runValidate() {
  validating.value = true
  validation.value = null
  try {
    const result = await handleUnauthorized(() => validateEnemies(props.adminToken, editable.enemies))
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
      publishEnemies(props.adminToken, editable.enemies, publishNotes.value, newIdempotencyKey())
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

onMounted(async () => {
  await loadCurrent()
  await loadVersions()
  loading.value = false
})
</script>

<template>
  <div v-if="loading" class="muted">Loading content…</div>
  <div v-else class="workspace">
    <p v-if="loadError" class="error" data-testid="enemy-load-error">{{ loadError }}</p>

    <div class="version-banner card">
      <strong>Published version:</strong> <span data-testid="enemy-current-version">{{ currentVersion }}</span>
      <span v-if="hasPendingChanges" class="pending-badge" data-testid="enemy-pending-badge">unsaved edits</span>
    </div>

    <div class="columns">
      <section class="card item-list">
        <h2>Enemies</h2>
        <ul>
          <li v-for="s in editable.enemies" :key="s.id">
            <button
              class="item-button"
              :class="{ active: s.id === selectedId }"
              :data-testid="`enemy-select-${s.id}`"
              @click="selectedId = s.id"
            >
              {{ s.name }}
              <span v-if="pendingDiff.changed.some((ch) => ch.id === s.id)" class="dot" />
            </button>
          </li>
        </ul>
      </section>

      <section class="card editor-panel">
        <h2>Editor</h2>
        <EnemyEditForm v-if="selected" :enemy="selected" />
        <p v-else class="muted">Select an enemy to edit.</p>
      </section>

      <section class="card actions-panel">
        <h2>Validate &amp; publish</h2>

        <div class="pending-diff" data-testid="enemy-pending-diff">
          <h3>Pending changes</h3>
          <p v-if="!hasPendingChanges" class="muted">No unpublished edits.</p>
          <ul v-else>
            <li v-for="c in pendingDiff.changed" :key="c.id">
              <strong>{{ c.id }}</strong>
              <ul>
                <li v-for="f in c.fields" :key="f.path">{{ f.path }}: {{ f.before }} → {{ f.after }}</li>
              </ul>
            </li>
          </ul>
          <button v-if="hasPendingChanges" class="link-button" @click="resetLocalEdits">Discard local edits</button>
        </div>

        <button data-testid="enemy-validate-button" :disabled="validating" @click="runValidate">
          {{ validating ? 'Validating…' : 'Validate' }}
        </button>

        <p v-if="validation?.valid" class="ok" data-testid="enemy-validate-ok">Valid — safe to publish.</p>
        <ul v-else-if="validation && !validation.valid" class="error" data-testid="enemy-validate-errors">
          <li v-for="err in validation.errors" :key="err">{{ err }}</li>
        </ul>

        <label>
          Publish notes
          <textarea v-model="publishNotes" rows="2" data-testid="enemy-publish-notes" />
        </label>
        <button class="primary" data-testid="enemy-publish-button" :disabled="publishing" @click="runPublish">
          {{ publishing ? 'Publishing…' : 'Publish new version' }}
        </button>

        <p v-if="publishMessage" class="ok" data-testid="enemy-publish-message">{{ publishMessage }}</p>
        <ul v-if="publishProblems" class="error" data-testid="enemy-publish-problems">
          <li v-for="p in publishProblems" :key="p">{{ p }}</li>
        </ul>

        <h3>Roll back to</h3>
        <ul class="version-list" data-testid="enemy-rollback-list">
          <li v-for="v in rollbackableVersions" :key="v">
            <span class="version-tag">{{ v }}</span>
            <button :disabled="rollingBackVersion === v" :data-testid="`enemy-rollback-${v}`" @click="runRollback(v)">
              {{ rollingBackVersion === v ? 'Rolling back…' : 'Roll back to this' }}
            </button>
          </li>
          <li v-if="rollbackableVersions.length === 0" class="muted">Nothing to roll back to yet.</li>
        </ul>

        <h3>Publish/rollback log</h3>
        <p v-if="versionsLoading" class="muted">Loading…</p>
        <ul v-else class="version-list" data-testid="enemy-version-list">
          <li v-for="v in versions" :key="v.version + v.publishedAt">
            <span class="version-tag">{{ v.version }}</span>
            <span class="version-kind">{{ v.kind }}</span>
            <span v-if="v.notes" class="muted"> — {{ v.notes }}</span>
          </li>
          <li v-if="versions.length === 0" class="muted">No publishes yet.</li>
        </ul>
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

.item-list ul {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.item-button {
  width: 100%;
  text-align: left;
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.item-button.active {
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
</style>
