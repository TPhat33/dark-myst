<script setup lang="ts">
import { ref, onMounted } from 'vue'
import LoginPanel from './components/LoginPanel.vue'
import CharactersWorkspace from './components/CharactersWorkspace.vue'
import SkillsWorkspace from './components/SkillsWorkspace.vue'
import EnemiesWorkspace from './components/EnemiesWorkspace.vue'
import EncountersWorkspace from './components/EncountersWorkspace.vue'
import SweepPanel from './components/SweepPanel.vue'

const STORAGE_KEY = 'darkmyst-admin-token'

type Tab = 'characters' | 'skills' | 'enemies' | 'encounters' | 'sweep'

const tabs: { id: Tab; label: string }[] = [
  { id: 'characters', label: 'Characters' },
  { id: 'skills', label: 'Skills' },
  { id: 'enemies', label: 'Enemies' },
  { id: 'encounters', label: 'Encounters' },
  { id: 'sweep', label: 'Sweep' }
]

const activeTab = ref<Tab>('characters')

const adminToken = ref<string | null>(null)

onMounted(() => {
  try {
    adminToken.value = localStorage.getItem(STORAGE_KEY)
  } catch {
    adminToken.value = null
  }
})

function onLoggedIn(token: string) {
  adminToken.value = token
  try {
    localStorage.setItem(STORAGE_KEY, token)
  } catch {
    // Per-viewer convenience only — losing it just means logging in again next visit.
  }
}

function onLogout() {
  adminToken.value = null
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    // Nothing to do — see onLoggedIn.
  }
}
</script>

<template>
  <div class="app-shell">
    <header class="app-header">
      <h1>Dark Myst — Content Admin</h1>
      <button v-if="adminToken" class="link-button" @click="onLogout">Log out</button>
    </header>
    <main>
      <LoginPanel v-if="!adminToken" @logged-in="onLoggedIn" />
      <template v-else>
        <nav class="tab-bar">
          <button
            v-for="tab in tabs"
            :key="tab.id"
            class="tab-button"
            :class="{ active: activeTab === tab.id }"
            :data-testid="`tab-${tab.id}`"
            @click="activeTab = tab.id"
          >
            {{ tab.label }}
          </button>
        </nav>

        <CharactersWorkspace v-if="activeTab === 'characters'" :admin-token="adminToken" @unauthorized="onLogout" />
        <SkillsWorkspace v-else-if="activeTab === 'skills'" :admin-token="adminToken" @unauthorized="onLogout" />
        <EnemiesWorkspace v-else-if="activeTab === 'enemies'" :admin-token="adminToken" @unauthorized="onLogout" />
        <EncountersWorkspace v-else-if="activeTab === 'encounters'" :admin-token="adminToken" @unauthorized="onLogout" />
        <SweepPanel v-else-if="activeTab === 'sweep'" :admin-token="adminToken" @unauthorized="onLogout" />
      </template>
    </main>
  </div>
</template>

<style>
:root {
  color-scheme: light dark;
  --bg: #0f1116;
  --panel: #171a22;
  --panel-border: #2a2f3a;
  --text: #e7e9ee;
  --muted: #9aa1b0;
  --accent: #5b8cff;
  --danger: #ef5b6b;
  --ok: #4fbf6b;
  font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  background: var(--bg);
  color: var(--text);
}

.app-shell {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.app-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.75rem 1.25rem;
  border-bottom: 1px solid var(--panel-border);
}

.app-header h1 {
  font-size: 1.1rem;
  margin: 0;
}

main {
  flex: 1;
  padding: 1.25rem;
}

button {
  font: inherit;
  cursor: pointer;
  border-radius: 6px;
  border: 1px solid var(--panel-border);
  background: var(--panel);
  color: var(--text);
  padding: 0.45rem 0.9rem;
}

button:hover:not(:disabled) {
  border-color: var(--accent);
}

button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

button.primary {
  background: var(--accent);
  border-color: var(--accent);
  color: #08101f;
  font-weight: 600;
}

button.danger {
  background: var(--danger);
  border-color: var(--danger);
  color: #200;
}

.link-button {
  background: none;
  border: none;
  color: var(--muted);
  text-decoration: underline;
}

.tab-bar {
  display: flex;
  gap: 0.4rem;
  margin-bottom: 1rem;
  flex-wrap: wrap;
}

.tab-button.active {
  border-color: var(--accent);
  background: #1c2740;
}

input,
textarea {
  font: inherit;
  background: #0d0f14;
  border: 1px solid var(--panel-border);
  color: var(--text);
  border-radius: 6px;
  padding: 0.4rem 0.55rem;
}

.card {
  background: var(--panel);
  border: 1px solid var(--panel-border);
  border-radius: 10px;
  padding: 1rem;
}
</style>
