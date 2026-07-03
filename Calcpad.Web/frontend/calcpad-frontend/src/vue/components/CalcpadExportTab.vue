<template>
  <div class="export-tab">
    <div class="export-container p-3">
      <div class="header-actions">
        <button
          class="btn"
          @click="$emit('saveHtml')"
          :title="'Save the rendered HTML for the current document'"
        >
          Save HTML…
        </button>
        <button
          class="btn"
          @click="$emit('saveDocx')"
          :title="'Export the current document as a Word .docx file'"
        >
          Save Word…
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
defineEmits<{
  saveHtml: []
  saveDocx: []
}>()
</script>

<style scoped>
.export-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.export-container {
  overflow-y: auto;
  flex: 1;
}

.header-actions {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}

.btn {
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  padding: 4px 10px;
  border-radius: 2px;
  font-size: 11px;
  cursor: pointer;
}

.btn:hover {
  background: var(--vscode-button-hoverBackground);
}
</style>
