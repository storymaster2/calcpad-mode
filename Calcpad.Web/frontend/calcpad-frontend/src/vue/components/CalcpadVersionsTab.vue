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
    <div v-else-if="displayRows.length === 0" class="versions-status">No commits found for this calculation.</div>
    <ul v-else class="versions-list">
      <li
        v-for="row in displayRows"
        :key="row.key"
        class="version-row"
        :class="{
          selected: row.key === selectedKey,
          tip: row.kind === 'commit' && row.isTip,
          canonical: row.kind === 'commit' && row.isCanonical,
          unsaved: row.kind === 'draft',
        }"
        @click="onRowClick(row)"
      >
        <div class="version-main">
          <span class="version-date">{{ row.kind === 'draft' ? 'Unsaved changes' : formatDate(row.date) }}</span>
          <span v-if="row.kind === 'draft'" class="unsaved-badge">unsaved</span>
          <template v-else>
            <span v-if="row.isCanonical" class="canonical-badge">canonical</span>
            <span v-if="row.isTip" class="tip-badge">tip</span>
          </template>
        </div>
        <div
          v-if="row.kind === 'commit'"
          class="version-message"
          :title="row.message"
        >{{ row.message || '(no message)' }}</div>
        <div v-else class="version-message">Local edits — not saved to library</div>
        <div class="version-sha">{{ row.kind === 'draft' ? shortSha(row.parentSha) : shortSha(row.sha) }}</div>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { postMessage } from '../services/messaging'

export interface VersionCommit {
  sha: string
  message: string
  date: string
  isTip: boolean
  isCanonical?: boolean
}

type DisplayRow =
  | { kind: 'draft'; key: string; parentSha: string }
  | { kind: 'commit'; key: string; sha: string; message: string; date: string; isTip: boolean; isCanonical: boolean }

const DRAFT_PREFIX = '__draft__:'

const commits = ref<VersionCommit[]>([])
const draftParentShas = ref<string[]>([])
const activeDraftParentSha = ref<string | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const selectedKey = ref<string | null>(null)

const displayRows = computed((): DisplayRow[] => {
  const draftSet = new Set(draftParentShas.value)
  const rows: DisplayRow[] = []
  const placed = new Set<string>()

  for (const commit of commits.value) {
    if (draftSet.has(commit.sha)) {
      rows.push({
        kind: 'draft',
        key: DRAFT_PREFIX + commit.sha,
        parentSha: commit.sha,
      })
      placed.add(commit.sha)
    }
    rows.push({
      kind: 'commit',
      key: commit.sha,
      sha: commit.sha,
      message: commit.message,
      date: commit.date,
      isTip: commit.isTip,
      isCanonical: !!commit.isCanonical,
    })
  }

  for (const parentSha of draftParentShas.value) {
    if (!placed.has(parentSha)) {
      rows.unshift({
        kind: 'draft',
        key: DRAFT_PREFIX + parentSha,
        parentSha,
      })
    }
  }

  return rows
})

const requestHistory = () => {
  loading.value = true
  error.value = null
  postMessage({ type: 'getHistory' })
}

const loadVersion = (sha: string) => {
  postMessage({ type: 'loadVersion', sha })
}

const loadDraft = (parentSha: string) => {
  postMessage({ type: 'loadDraft', parentSha })
}

const onRowClick = (row: DisplayRow) => {
  if (row.kind === 'draft') loadDraft(row.parentSha)
  else loadVersion(row.sha)
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

const applyDraftState = (message: { drafts?: { parentSha: string }[]; activeParentSha?: string | null }) => {
  const list = Array.isArray(message.drafts) ? message.drafts : []
  draftParentShas.value = list
    .map(d => d.parentSha)
    .filter((sha): sha is string => typeof sha === 'string' && !!sha)
  activeDraftParentSha.value = typeof message.activeParentSha === 'string'
    ? message.activeParentSha
    : null
  if (activeDraftParentSha.value) {
    selectedKey.value = DRAFT_PREFIX + activeDraftParentSha.value
  }
}

const onHostMessage = (event: MessageEvent) => {
  const message = event.data
  if (!message || typeof message !== 'object') return

  if (message.type === 'historyResponse') {
    loading.value = false
    const list = Array.isArray(message.commits) ? message.commits : []
    const canonicalSha = typeof message.canonicalCommitSha === 'string'
      ? message.canonicalCommitSha
      : ''
    commits.value = list.map((c: VersionCommit) => ({
      ...c,
      isCanonical: c.isCanonical === true || (!!canonicalSha && c.sha === canonicalSha),
    }))
    error.value = typeof message.error === 'string' ? message.error : null
    if (!selectedKey.value) {
      const tip = commits.value.find(c => c.isTip)
      if (tip) selectedKey.value = tip.sha
    }
  }

  if (message.type === 'draftState') {
    applyDraftState(message)
  }

  if (message.type === 'versionLoaded' && typeof message.sha === 'string') {
    selectedKey.value = message.sha
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

.version-row.canonical {
  border-color: var(--vscode-charts-orange, #ce9178);
  background: color-mix(in srgb, var(--vscode-charts-orange, #ce9178) 14%, transparent);
}

.version-row.canonical.selected {
  border-color: var(--vscode-charts-orange, #ce9178);
  background: color-mix(in srgb, var(--vscode-charts-orange, #ce9178) 22%, var(--vscode-list-activeSelectionBackground, rgba(0, 122, 204, 0.2)));
}

.version-row.unsaved {
  border-style: dashed;
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

.tip-badge,
.unsaved-badge,
.canonical-badge {
  font-size: 10px;
  text-transform: uppercase;
  padding: 1px 5px;
  border-radius: 2px;
  background: var(--vscode-badge-background, #4d4d4d);
  color: var(--vscode-badge-foreground, #fff);
}

.canonical-badge {
  background: var(--vscode-charts-orange, #ce9178);
  color: var(--vscode-editor-background, #1e1e1e);
}

.unsaved-badge {
  background: var(--vscode-editorWarning-foreground, #cca700);
  color: var(--vscode-editor-background, #1e1e1e);
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
