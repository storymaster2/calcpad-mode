<template>
  <div class="export-tab">
    <div class="export-container p-3">
      <div class="export-header">
        <div class="export-summary">
          <span v-if="exports.length === 0">No exports captured yet.</span>
          <span v-else>{{ exports.length }} file{{ exports.length === 1 ? '' : 's' }} from #write/#append</span>
        </div>
        <div class="header-actions">
          <button class="btn btn-secondary" @click="$emit('refresh')" :title="'Re-fetch the export list'">
            Refresh
          </button>
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
          <button
            v-if="exports.length > 1"
            class="btn"
            @click="$emit('downloadZip')"
            :title="'Download all exports as a ZIP archive'"
          >
            Download all (.zip)
          </button>
        </div>
      </div>

      <div v-if="exports.length === 0" class="empty-state">
        Run a calculation that uses
        <code>#write</code> or <code>#append</code> to populate this tab.
      </div>
      <div v-else class="export-list">
        <div
          v-for="entry in exports"
          :key="entry.filename"
          class="export-row"
        >
          <span class="file-icon">{{ iconFor(entry) }}</span>
          <div class="export-meta">
            <div class="export-name" :title="entry.filename">{{ entry.filename }}</div>
            <div class="export-sub">
              <span class="export-type">{{ shortType(entry.contentType) }}</span>
              <span class="export-size">{{ formatSize(entry.size) }}</span>
            </div>
          </div>
          <button
            class="btn btn-download"
            @click="$emit('download', entry.filename)"
            :title="'Download ' + entry.filename"
          >
            Download
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { ExportMeta } from '../../types/api'

interface Props {
  exports?: ExportMeta[]
}

const props = withDefaults(defineProps<Props>(), {
  exports: () => []
})

defineEmits<{
  download: [filename: string]
  downloadZip: []
  refresh: []
  saveHtml: []
  saveDocx: []
}>()

void props // silence unused warning when template-only access

function iconFor(entry: ExportMeta): string {
  const lower = entry.filename.toLowerCase()
  if (lower.endsWith('.csv')) return '📊'
  if (lower.endsWith('.xlsx') || lower.endsWith('.xlsm') || lower.endsWith('.xls')) return '📈'
  if (lower.endsWith('.json')) return '🧾'
  if (lower.endsWith('.xml') || lower.endsWith('.html') || lower.endsWith('.htm')) return '📄'
  return '📝'
}

function shortType(contentType: string): string {
  if (!contentType) return ''
  if (contentType === 'text/csv') return 'CSV'
  if (contentType.includes('spreadsheetml')) return 'Excel'
  if (contentType === 'text/plain') return 'Text'
  if (contentType === 'application/json') return 'JSON'
  if (contentType === 'application/xml') return 'XML'
  if (contentType === 'text/html') return 'HTML'
  return contentType
}

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

.export-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 12px;
  flex-wrap: wrap;
}

.export-summary {
  font-size: 12px;
  color: var(--vscode-descriptionForeground);
}

.header-actions {
  display: flex;
  gap: 6px;
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

.btn-secondary {
  background: var(--vscode-button-secondaryBackground);
  color: var(--vscode-button-secondaryForeground);
}

.btn-secondary:hover {
  background: var(--vscode-button-secondaryHoverBackground);
}

.btn-download {
  flex-shrink: 0;
}

.empty-state {
  text-align: center;
  color: var(--vscode-descriptionForeground);
  padding: 20px;
  font-style: italic;
}

.empty-state code {
  background: var(--vscode-textCodeBlock-background);
  padding: 1px 4px;
  border-radius: 2px;
  font-size: 11px;
  font-style: normal;
}

.export-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.export-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 8px;
  border-radius: 3px;
  background: var(--vscode-list-hoverBackground, transparent);
}

.export-row:hover {
  background: var(--vscode-list-activeSelectionBackground);
  color: var(--vscode-list-activeSelectionForeground);
}

.file-icon {
  font-size: 16px;
  flex-shrink: 0;
}

.export-meta {
  flex: 1;
  min-width: 0;
}

.export-name {
  font-size: 12px;
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.export-sub {
  display: flex;
  gap: 8px;
  font-size: 10px;
  color: var(--vscode-descriptionForeground);
}

.export-type {
  text-transform: uppercase;
  letter-spacing: 0.5px;
}
</style>
