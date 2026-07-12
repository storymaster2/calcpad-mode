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

      <div class="plots-section">
        <div class="plots-header">
          <h3>Plots</h3>
          <div class="plots-actions">
            <button
              class="btn"
              :disabled="loading"
              @click="$emit('refreshPlots')"
              title="Re-run the current document and list its plots"
            >
              {{ loading ? 'Loading…' : 'Refresh' }}
            </button>
            <button
              class="btn"
              :disabled="loading || plots.length === 0"
              @click="$emit('savePlotsZip')"
              title="Download every plot as a single ZIP archive"
            >
              Download all (ZIP)
            </button>
          </div>
        </div>

        <p v-if="!loading && plots.length === 0" class="empty">
          No plots in the current document.
        </p>

        <ul v-if="plots.length > 0" class="plots-list">
          <li v-for="p in plots" :key="p.index" class="plot-item">
            <img class="thumb" :src="p.dataUri" :alt="`Plot ${p.index + 1}`" />
            <div class="plot-meta">
              <div class="plot-name">Plot {{ p.index + 1 }}.{{ p.ext }}</div>
              <div class="plot-size">{{ formatSize(p.sizeBytes) }}</div>
            </div>
            <button
              class="btn"
              @click="$emit('savePlot', p.index)"
              title="Download this plot as an image file"
            >
              Save…
            </button>
          </li>
        </ul>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
export interface PlotSummary {
  index: number
  ext: 'png' | 'svg'
  dataUri: string
  sizeBytes: number
}

defineProps<{
  plots: PlotSummary[]
  loading: boolean
}>()

defineEmits<{
  saveHtml: []
  saveDocx: []
  refreshPlots: []
  savePlot: [index: number]
  savePlotsZip: []
}>()

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`
}
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

.plots-section {
  margin-top: 16px;
}

.plots-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.plots-header h3 {
  margin: 0;
  font-size: 12px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  opacity: 0.8;
}

.plots-actions {
  display: flex;
  gap: 6px;
}

.empty {
  margin: 4px 0;
  font-size: 11px;
  opacity: 0.7;
}

.plots-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.plot-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px;
  border: 1px solid var(--vscode-panel-border, rgba(128, 128, 128, 0.3));
  border-radius: 2px;
}

.thumb {
  width: 48px;
  height: 48px;
  object-fit: contain;
  background: var(--vscode-editor-background, #fff);
  border: 1px solid var(--vscode-panel-border, rgba(128, 128, 128, 0.2));
}

.plot-meta {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}

.plot-name {
  font-size: 11px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.plot-size {
  font-size: 10px;
  opacity: 0.7;
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

.btn:hover:not(:disabled) {
  background: var(--vscode-button-hoverBackground);
}

.btn:disabled {
  opacity: 0.5;
  cursor: default;
}
</style>
