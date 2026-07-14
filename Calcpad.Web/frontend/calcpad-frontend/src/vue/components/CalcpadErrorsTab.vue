<template>
  <div class="errors-tab">
    <div class="errors-container p-3">
      <div v-if="errors.length === 0" class="no-errors">
        No errors from the last render.
      </div>
      <div v-else class="errors-list">
        <div
          v-for="(err, i) in errors"
          :key="i"
          class="error-item"
          :title="`Go to line ${err.sourceLine}`"
          @click="$emit('go-to-line', err.sourceLine)"
        >
          <span class="error-icon">✕</span>
          <span class="error-source">{{ err.source }}</span>
          <span class="error-message">{{ err.message }}</span>
          <span class="error-location">Ln {{ err.sourceLine }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { CalcpadError } from '../../types/api'

interface Props {
  errors?: CalcpadError[]
}

withDefaults(defineProps<Props>(), {
  errors: () => []
})

defineEmits<{
  'go-to-line': [line: number]
}>()
</script>

<style scoped>
.errors-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.errors-container {
  overflow-y: auto;
  flex: 1;
}

.no-errors {
  text-align: center;
  color: var(--vscode-descriptionForeground);
  padding: 20px;
  font-style: italic;
}

.errors-list {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.error-item {
  display: grid;
  grid-template-columns: auto auto 1fr auto;
  align-items: baseline;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 3px;
  cursor: pointer;
  font-size: 12px;
  color: var(--vscode-foreground);
}

.error-item:hover {
  background: var(--vscode-list-hoverBackground);
}

.error-icon {
  color: var(--vscode-errorForeground, #f14c4c);
  font-weight: 700;
}

.error-source {
  font-size: 10px;
  color: var(--vscode-badge-foreground, var(--vscode-foreground));
  background: var(--vscode-badge-background);
  padding: 1px 4px;
  border-radius: 3px;
}

.error-message {
  white-space: pre-wrap;
  word-break: break-word;
}

.error-location {
  font-size: 11px;
  color: var(--vscode-foreground);
  opacity: 0.7;
  white-space: nowrap;
}
</style>
