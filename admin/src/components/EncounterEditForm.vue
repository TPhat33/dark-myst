<script setup lang="ts">
import type { EncounterData } from '../types'

const props = defineProps<{ encounter: EncounterData }>()

function addUnit() {
  const usedSlots = new Set(props.encounter.units.map((u) => u.slot))
  let slot = 0
  while (usedSlots.has(slot) && slot < 5) {
    slot++
  }
  props.encounter.units.push({ enemyId: '', slot, statScalePerMille: 1000 })
}

function removeUnit(index: number) {
  props.encounter.units.splice(index, 1)
}
</script>

<template>
  <form class="encounter-form" @submit.prevent>
    <div class="row">
      <label>
        Name
        <input v-model="encounter.name" data-testid="encounter-field-name" />
      </label>
      <label>
        Leader slot
        <input v-model.number="encounter.leaderSlot" type="number" min="0" max="4" data-testid="encounter-field-leader-slot" />
      </label>
    </div>

    <fieldset>
      <legend>Units</legend>
      <table class="unit-table">
        <thead>
          <tr>
            <th>Enemy id</th>
            <th>Slot</th>
            <th>Stat scale ‰</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(unit, index) in encounter.units" :key="index">
            <td>
              <input v-model="unit.enemyId" :data-testid="`encounter-unit-${index}-enemy-id`" />
            </td>
            <td>
              <input v-model.number="unit.slot" type="number" min="0" max="4" :data-testid="`encounter-unit-${index}-slot`" />
            </td>
            <td>
              <input
                v-model.number="unit.statScalePerMille"
                type="number"
                min="0"
                :data-testid="`encounter-unit-${index}-scale`"
              />
            </td>
            <td>
              <button type="button" class="danger" :data-testid="`encounter-unit-${index}-remove`" @click="removeUnit(index)">
                Remove
              </button>
            </td>
          </tr>
        </tbody>
      </table>
      <button type="button" data-testid="encounter-add-unit" @click="addUnit">Add unit</button>
    </fieldset>
  </form>
</template>

<style scoped>
.encounter-form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.row {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.row label {
  flex: 1;
  min-width: 100px;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.85rem;
}

fieldset {
  border: 1px solid var(--panel-border);
  border-radius: 8px;
}

.unit-table {
  width: 100%;
  border-collapse: collapse;
}

.unit-table th {
  text-align: left;
  font-size: 0.75rem;
  color: var(--muted);
  padding: 0.2rem 0.4rem;
}

.unit-table td {
  padding: 0.2rem 0.4rem;
}

.unit-table input {
  width: 100%;
}
</style>
