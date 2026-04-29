<template>
  <div class="app-layout">
    <div v-if="sidebarVisible" class="sidebar-pane">
      <div id="vue-sidebar"></div>
    </div>
    <div v-if="sidebarVisible" class="resize-handle"></div>
    <div class="editor-pane">
      <div class="editor-toolbar">
        <template v-if="isNeutralino">
          <span class="file-name">{{ fileName || 'Untitled' }}</span>
          <span v-if="isDirty" class="dirty-indicator">*</span>
        </template>
        <span v-else>CalcPad Web</span>
        <span class="spacer"></span>
        <button class="toolbar-btn" @click="togglePreview" title="Preview HTML">
          {{ previewVisible ? 'Hide Preview' : 'Preview' }}
        </button>
        <span
          class="server-status"
          :class="{ connected: serverConnected, disconnected: !serverConnected }"
        >
          {{ serverConnected ? 'Connected' : 'Disconnected' }}
        </span>
      </div>
      <div ref="editorContainer" class="editor-container"></div>
      <!-- Bottom panel (Problems / Output) -->
      <div v-if="bottomPanelOpen" class="bottom-panel">
        <div class="bottom-panel-header">
          <button
            class="panel-tab"
            :class="{ active: activeBottomTab === 'problems' }"
            @click="activeBottomTab = 'problems'"
          >
            Problems
            <span v-if="errorCount + warningCount + infoCount > 0" class="problems-badge" :class="errorCount > 0 ? 'error' : warningCount > 0 ? 'warning' : 'info'">
              {{ errorCount + warningCount + infoCount }}
            </span>
          </button>
          <button
            class="panel-tab"
            :class="{ active: activeBottomTab === 'output' }"
            @click="activeBottomTab = 'output'"
          >
            Output
          </button>
          <span class="spacer"></span>
          <button v-if="activeBottomTab === 'output'" class="toolbar-btn" @click="clearOutput" title="Clear Output">⌫</button>
          <button class="toolbar-btn" @click="bottomPanelOpen = false">✕</button>
        </div>
        <!-- Problems tab -->
        <div v-show="activeBottomTab === 'problems'" class="problems-list" ref="problemsList">
          <div
            v-for="(problem, i) in problems"
            :key="i"
            class="problem-row"
            @click="gotoProblem(problem)"
          >
            <span class="problem-icon" :class="problem.severityClass">{{ problem.icon }}</span>
            <span class="problem-message">{{ problem.message }}</span>
            <span v-if="problem.code" class="problem-code">{{ problem.code }}</span>
            <span class="problem-location">[Ln {{ problem.startLineNumber }}, Col {{ problem.startColumn }}]</span>
          </div>
          <div v-if="problems.length === 0" class="problems-empty">No problems detected.</div>
        </div>
        <!-- Output tab -->
        <div v-show="activeBottomTab === 'output'" class="output-list" ref="outputList">
          <div v-for="(line, i) in outputLines" :key="i" class="output-row" :class="line.level">
            <span class="output-timestamp">{{ line.time }}</span>
            <span class="output-level">{{ line.label }}</span>
            <span class="output-message">{{ line.message }}</span>
          </div>
          <div v-if="outputLines.length === 0" class="problems-empty">No output yet.</div>
        </div>
      </div>
      <!-- Status bar -->
      <div class="status-bar">
        <span class="status-problems" @click="openBottomTab('problems')">
          <span class="status-icon error">✕</span> {{ errorCount }}
          <span class="status-icon warning">⚠</span> {{ warningCount }}
          <span class="status-icon info">ℹ</span> {{ infoCount }}
        </span>
        <span class="status-output" @click="openBottomTab('output')">Output</span>
        <span class="spacer"></span>
        <span class="status-text">CalcPad</span>
      </div>
    </div>
    <div v-if="previewVisible" class="preview-pane">
      <div class="preview-toolbar">
        <span>Preview</span>
        <div class="preview-mode-group">
          <button
            class="toolbar-btn"
            :class="{ active: previewMode === 'wrapped' }"
            @click="setPreviewMode('wrapped')"
            title="Full HTML document"
          >Wrapped</button>
          <button
            class="toolbar-btn"
            :class="{ active: previewMode === 'unwrapped' }"
            @click="setPreviewMode('unwrapped')"
            title="Body markup only"
          >Unwrapped</button>
          <button
            class="toolbar-btn"
            :class="{ active: previewMode === 'ui' }"
            @click="setPreviewMode('ui')"
            title="Interactive UI inputs"
          >Interactive</button>
        </div>
        <span class="spacer"></span>
        <button class="toolbar-btn" @click="togglePreview">✕</button>
      </div>
      <iframe ref="previewFrame" class="preview-frame" sandbox="allow-same-origin"></iframe>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, nextTick, onMounted } from 'vue'

export interface ProblemItem {
  severity: number
  severityClass: string
  icon: string
  message: string
  code: string
  startLineNumber: number
  startColumn: number
  endLineNumber: number
  endColumn: number
}

defineProps<{
  isNeutralino?: boolean
}>()

export type PreviewMode = 'wrapped' | 'unwrapped' | 'ui'

const onGotoProblem = ref<((problem: ProblemItem) => void) | null>(null)
const onPreviewToggled = ref<((visible: boolean) => void) | null>(null)
const onPreviewModeChanged = ref<((mode: PreviewMode) => void) | null>(null)
const previewMode = ref<PreviewMode>('wrapped')

function gotoProblem(problem: ProblemItem): void {
  onGotoProblem.value?.(problem)
}

const editorContainer = ref<HTMLElement | null>(null)
const previewFrame = ref<HTMLIFrameElement | null>(null)
const serverConnected = ref(false)
const fileName = ref('')
const isDirty = ref(false)
const sidebarVisible = ref(true)
const previewVisible = ref(false)
const bottomPanelOpen = ref(false)
const activeBottomTab = ref<'problems' | 'output'>('problems')
const problems = ref<ProblemItem[]>([])
const errorCount = ref(0)
const warningCount = ref(0)
const infoCount = ref(0)

export interface OutputLine {
  time: string
  level: string
  label: string
  message: string
}

const outputLines = ref<OutputLine[]>([])
const outputList = ref<HTMLElement | null>(null)

function openBottomTab(tab: 'problems' | 'output'): void {
  if (bottomPanelOpen.value && activeBottomTab.value === tab) {
    bottomPanelOpen.value = false
  } else {
    activeBottomTab.value = tab
    bottomPanelOpen.value = true
  }
}

function appendOutput(level: 'info' | 'warn' | 'error' | 'debug', message: string): void {
  const now = new Date()
  const time = now.toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })
  const labels: Record<string, string> = { info: 'INFO', warn: 'WARN', error: 'ERROR', debug: 'DEBUG' }
  outputLines.value.push({ time, level, label: labels[level] ?? level, message })
  // Cap at 1000 lines
  if (outputLines.value.length > 1000) {
    outputLines.value.splice(0, outputLines.value.length - 1000)
  }
  // Auto-scroll
  nextTick(() => {
    const el = outputList.value
    if (el) el.scrollTop = el.scrollHeight
  })
}

function clearOutput(): void {
  outputLines.value = []
}

function setFileName(name: string): void {
  fileName.value = name
}

function setDirty(dirty: boolean): void {
  isDirty.value = dirty
}

function getIsDirty(): boolean {
  return isDirty.value
}

function toggleSidebar(): void {
  sidebarVisible.value = !sidebarVisible.value
}

function togglePreview(): void {
  previewVisible.value = !previewVisible.value
  onPreviewToggled.value?.(previewVisible.value)
}

function isPreviewVisible(): boolean {
  return previewVisible.value
}

function setPreviewMode(mode: PreviewMode): void {
  if (previewMode.value === mode) return
  previewMode.value = mode
  onPreviewModeChanged.value?.(mode)
}

function getPreviewMode(): PreviewMode {
  return previewMode.value
}

function setPreviewHtml(html: string): void {
  const frame = previewFrame.value
  if (!frame) return
  const doc = frame.contentDocument
  if (!doc) return
  doc.open()
  doc.write(html)
  doc.close()
}

function setProblems(markers: ProblemItem[]): void {
  problems.value = markers
  errorCount.value = markers.filter(m => m.severity === 8).length
  warningCount.value = markers.filter(m => m.severity === 4).length
  infoCount.value = markers.filter(m => m.severity === 2).length
}

onMounted(async () => {
  const checkHealth = async () => {
    try {
      const bridge = (window as any).calcpadBridge
      if (bridge) {
        serverConnected.value = await bridge.api.checkHealth()
      }
    } catch {
      serverConnected.value = false
    }
  }

  setTimeout(checkHealth, 1000)
  setInterval(checkHealth, 30000)
})

defineExpose({
  editorContainer,
  setFileName,
  setDirty,
  isDirty: getIsDirty,
  toggleSidebar,
  togglePreview,
  isPreviewVisible,
  setPreviewHtml,
  setProblems,
  onGotoProblem,
  onPreviewToggled,
  onPreviewModeChanged,
  setPreviewMode,
  getPreviewMode,
  appendOutput,
  clearOutput,
})
</script>
