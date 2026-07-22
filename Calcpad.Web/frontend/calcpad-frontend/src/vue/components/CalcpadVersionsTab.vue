<template>
  <div class="versions-tab">
    <div class="versions-header">
      <span class="versions-title">Version history</span>
      <button class="btn-refresh" type="button" :disabled="loading" @click="requestHistory" title="Refresh">
        Refresh
      </button>
    </div>

    <div v-if="loading" class="versions-status">Loading history…</div>
    <div v-else-if="error" class="versions-status versions-error">{{ error }}</div>
    <div v-else-if="commits.length === 0" class="versions-status">No commits found for this calculation.</div>
    <ul v-else class="versions-list">
      <li
        v-for="commit in commits"
        :key="commit.sha"
        class="version-row"
        :class="{ selected: commit.sha === selectedSha, tip: commit.isTip }"
        @click="loadVersion(commit.sha)"
      >
        <div class="version-main">
          <span class="version-date">{{ formatDate(commit.date) }}</span>
          <span v-if="commit.isTip" class="tip-badge">tip</span>
        </div>
        <div class="version-message" :title="commit.message">{{ commit.message || '(no message)' }}</div>
        <div class="version-sha">{{ shortSha(commit.sha) }}</div>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import { postMessage } from '../services/messaging'

export interface VersionCommit {
  sha: string
  message: string
  date: string
  isTip: boolean
}

const commits = ref<VersionCommit[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const selectedSha = ref<string | null>(null)

const requestHistory = () => {
  loading.value = true
  error.value = null
  postMessage({ type: 'getHistory' })
}

const loadVersion = (sha: string) => {
  postMessage({ type: 'loadVersion', sha })
}

const formatDate = (iso: string): string => {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

const shortSha = (sha: string): string => (sha ? sha.slice(0, 7) : '')

const onHostMessage = (event: MessageEvent) => {
  const message = event.data
  if (!message || typeof message !== 'object') return

  if (message.type === 'historyResponse') {
    loading.value = false
    commits.value = Array.isArray(message.commits) ? message.commits : []
    error.value = typeof message.error === 'string' ? message.error : null
    const tip = commits.value.find(c => c.isTip)
    if (tip && !selectedSha.value) selectedSha.value = tip.sha
  }

  if (message.type === 'versionLoaded' && typeof message.sha === 'string') {
    selectedSha.value = message.sha
    error.value = null
  }

  if (message.type === 'versionLoadError') {
    error.value = typeof message.error === 'string' ? message.error : 'Failed to load version.'
  }
}

onMounted(() => {
  window.addEventListener('message', onHostMessage)
  requestHistory()
})

onUnmounted(() => {
  window.removeEventListener('message', onHostMessage)
})

defineExpose({ requestHistory })
</script>

<style scoped>
.versions-tab {
  padding: 12px;
  font-size: 12px;
}

.versions-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 10px;
}

.versions-title {
  font-weight: 600;
  color: var(--vscode-foreground);
}

.btn-refresh {
  border: 1px solid var(--vscode-button-border, transparent);
  background: var(--vscode-button-secondaryBackground, #3a3d41);
  color: var(--vscode-button-secondaryForeground, #ccc);
  padding: 3px 8px;
  font-size: 11px;
  cursor: pointer;
  border-radius: 2px;
}

.btn-refresh:disabled {
  opacity: 0.5;
  cursor: default;
}

.versions-status {
  color: var(--vscode-descriptionForeground);
  font-style: italic;
  padding: 8px 0;
}

.versions-error {
  color: var(--vscode-errorForeground, #f48771);
  font-style: normal;
}

.versions-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.version-row {
  padding: 8px;
  border: 1px solid var(--vscode-widget-border, #333);
  border-radius: 3px;
  cursor: pointer;
  background: transparent;
}

.version-row:hover {
  background: var(--vscode-list-hoverBackground, rgba(255, 255, 255, 0.06));
}

.version-row.selected {
  border-color: var(--vscode-focusBorder, #007acc);
  background: var(--vscode-list-activeSelectionBackground, rgba(0, 122, 204, 0.2));
}

.version-main {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 2px;
}

.version-date {
  font-weight: 600;
  color: var(--vscode-foreground);
}

.tip-badge {
  font-size: 10px;
  text-transform: uppercase;
  padding: 1px 5px;
  border-radius: 2px;
  background: var(--vscode-badge-background, #4d4d4d);
  color: var(--vscode-badge-foreground, #fff);
}

.version-message {
  color: var(--vscode-foreground);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.version-sha {
  margin-top: 2px;
  font-family: var(--vscode-editor-font-family, monospace);
  font-size: 10px;
  color: var(--vscode-descriptionForeground);
}
</style>
