<template>
  <div class="app-layout">
    <!-- Use v-show (not v-if) so #vue-sidebar stays in the DOM and the
         Vue app mounted to it in main.ts isn't orphaned across collapses. -->
    <div
      v-show="sidebarVisible"
      class="sidebar-pane"
      :style="{ width: sidebarWidth + 'px' }"
    >
      <div id="vue-sidebar"></div>
    </div>
    <!-- Doubles as a drag-to-resize handle (when sidebar is open) and a
         click-to-toggle collapse/expand button. -->
    <div
      class="resize-handle"
      :class="{ collapsed: !sidebarVisible, dragging: isResizing }"
      @mousedown="onSidebarHandleMouseDown"
      @dblclick="toggleSidebar"
      :title="sidebarVisible ? 'Drag to resize · double-click to collapse (Ctrl+Shift+B)' : 'Click to expand sidebar (Ctrl+Shift+B)'"
      role="separator"
      :aria-orientation="'vertical'"
      :aria-expanded="sidebarVisible"
    ></div>
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
      <!-- Tab strip (VS Code-style). Hidden until at least one tab is registered. -->
      <div v-if="tabs.length > 0" class="tab-strip" role="tablist">
        <div
          v-for="tab in tabs"
          :key="tab.id"
          class="tab"
          :class="{ active: tab.isActive, dirty: tab.dirty }"
          role="tab"
          :aria-selected="tab.isActive"
          :title="tab.filePath || tab.title"
          @mousedown.left="onTabClick(tab.id)"
          @mousedown.middle.prevent="onTabClose(tab.id)"
        >
          <span class="tab-title">{{ tab.title }}</span>
          <button
            class="tab-close"
            :title="tab.dirty ? 'Close (unsaved changes)' : 'Close'"
            @mousedown.stop
            @click.stop="onTabClose(tab.id)"
          >
            <span v-if="tab.dirty" class="tab-dirty-dot">●</span>
            <span v-else>✕</span>
          </button>
        </div>
        <button class="tab-new" title="New tab (Ctrl+T)" @click="onNewTab">+</button>
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
          <select
            v-if="activeBottomTab === 'output'"
            class="output-channel-select"
            v-model="activeOutputChannel"
            title="Output channel"
          >
            <option v-for="ch in (['app', 'preview', 'server'] as OutputChannel[])" :key="ch" :value="ch">
              {{ OUTPUT_CHANNEL_LABELS[ch] }}
            </option>
          </select>
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
          <div v-for="(line, i) in filteredOutputLines" :key="i" class="output-row" :class="line.level">
            <span class="output-timestamp">{{ line.time }}</span>
            <span class="output-level">{{ line.label }}</span>
            <span class="output-message">{{ line.message }}</span>
          </div>
          <div v-if="filteredOutputLines.length === 0" class="problems-empty">No output on this channel yet.</div>
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
      <!-- allow-scripts is required so the injected console-interception
           script (and any user #HTML script) actually runs in the iframe.
           Without it the preview is silent — matches the VS Code webview
           behaviour where "hello!" / `console.log(...)` reach the Output
           panel via the previewConsole bridge in injectPreviewConsole. -->
      <iframe ref="previewFrame" class="preview-frame" sandbox="allow-same-origin allow-scripts"></iframe>
    </div>

    <!-- Confirm dialog (used in place of Neutralino's GTK dialog, which has
         a button-mapping bug where YES_NO_CANCEL → "No" returns "CANCEL"). -->
    <div v-if="confirmState" class="modal-backdrop" @click.self="resolveConfirm('cancel')">
      <div class="modal-card" role="dialog" aria-modal="true">
        <div class="modal-title">{{ confirmState.title }}</div>
        <div class="modal-message">{{ confirmState.message }}</div>
        <div class="modal-actions">
          <button class="modal-btn primary" @click="resolveConfirm('yes')">{{ confirmState.yesLabel }}</button>
          <button class="modal-btn" @click="resolveConfirm('no')">{{ confirmState.noLabel }}</button>
          <button class="modal-btn" @click="resolveConfirm('cancel')">Cancel</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, nextTick, onMounted } from 'vue'

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

// ---- Tab strip ----
export interface TabUiState {
  id: string
  title: string
  filePath: string | null
  dirty: boolean
  isActive: boolean
}

const tabs = ref<TabUiState[]>([])
const onTabActivate = ref<((id: string) => void) | null>(null)
const onTabCloseRequest = ref<((id: string) => void) | null>(null)
const onNewTabRequest = ref<(() => void) | null>(null)

function setTabs(next: TabUiState[]): void {
  tabs.value = next
}

function onTabClick(id: string): void {
  onTabActivate.value?.(id)
}

function onTabClose(id: string): void {
  onTabCloseRequest.value?.(id)
}

function onNewTab(): void {
  onNewTabRequest.value?.()
}

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

export type OutputChannel = 'app' | 'preview' | 'server'

export interface OutputLine {
  time: string
  level: string
  label: string
  message: string
  channel: OutputChannel
}

const OUTPUT_CHANNEL_LABELS: Record<OutputChannel, string> = {
  app: 'CalcPad',
  preview: 'Preview Console',
  server: 'Server',
}

const outputLines = ref<OutputLine[]>([])
const outputList = ref<HTMLElement | null>(null)
const activeOutputChannel = ref<OutputChannel>('app')

const filteredOutputLines = computed(() =>
  outputLines.value.filter(l => l.channel === activeOutputChannel.value)
)

function openBottomTab(tab: 'problems' | 'output'): void {
  if (bottomPanelOpen.value && activeBottomTab.value === tab) {
    bottomPanelOpen.value = false
  } else {
    activeBottomTab.value = tab
    bottomPanelOpen.value = true
  }
}

function appendOutput(
  level: 'info' | 'warn' | 'error' | 'debug',
  message: string,
  channel: OutputChannel = 'app',
): void {
  const now = new Date()
  const time = now.toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })
  const labels: Record<string, string> = { info: 'INFO', warn: 'WARN', error: 'ERROR', debug: 'DEBUG' }
  outputLines.value.push({ time, level, label: labels[level] ?? level, message, channel })
  // Cap at 1000 lines (across all channels combined)
  if (outputLines.value.length > 1000) {
    outputLines.value.splice(0, outputLines.value.length - 1000)
  }
  // Auto-scroll only when the active channel matches
  if (channel === activeOutputChannel.value) {
    nextTick(() => {
      const el = outputList.value
      if (el) el.scrollTop = el.scrollHeight
    })
  }
}

function clearOutput(): void {
  // Only clear the currently visible channel — match VS Code's per-channel clear.
  outputLines.value = outputLines.value.filter(l => l.channel !== activeOutputChannel.value)
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

// ---- Sidebar drag-to-resize ----
const SIDEBAR_MIN = 180
const SIDEBAR_MAX = 600
const SIDEBAR_DEFAULT = 320
const sidebarWidth = ref<number>(loadSidebarWidth())
const isResizing = ref(false)

function loadSidebarWidth(): number {
  const raw = parseInt(localStorage.getItem('calcpad.sidebarWidth') ?? '', 10)
  if (!Number.isFinite(raw)) return SIDEBAR_DEFAULT
  return Math.min(SIDEBAR_MAX, Math.max(SIDEBAR_MIN, raw))
}

function onSidebarHandleMouseDown(e: MouseEvent): void {
  // If the sidebar is currently collapsed, a single click opens it instead
  // of starting a resize drag — there's nothing to resize yet.
  if (!sidebarVisible.value) {
    e.preventDefault()
    sidebarVisible.value = true
    return
  }
  e.preventDefault()
  isResizing.value = true
  const startX = e.clientX
  const startWidth = sidebarWidth.value
  let moved = false

  const onMove = (ev: MouseEvent) => {
    const dx = ev.clientX - startX
    if (!moved && Math.abs(dx) > 2) moved = true
    const next = Math.min(SIDEBAR_MAX, Math.max(SIDEBAR_MIN, startWidth + dx))
    sidebarWidth.value = next
  }
  const onUp = () => {
    isResizing.value = false
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
    if (moved) {
      localStorage.setItem('calcpad.sidebarWidth', String(sidebarWidth.value))
    }
  }
  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
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
  doc.write(injectPreviewConsole(html))
  doc.close()
}

// Forward iframe console.* + uncaught errors to the parent window via
// postMessage. The desktop's main.ts listens for these and routes them
// into the Output panel.
function injectPreviewConsole(html: string): string {
  // Body of the script tag. Tags themselves are assembled at runtime to avoid
  // the Vue SFC parser interpreting them as the closing tag of <script setup>.
  const body = [
    '(function() {',
    '  if (window.__calcpadConsolePatched) return;',
    '  window.__calcpadConsolePatched = true;',
    '  var post = function(level, args) {',
    '    var msg = Array.from(args).map(function(a) {',
    '      if (a instanceof Error) return a.stack || a.message;',
    "      if (typeof a === 'object') { try { return JSON.stringify(a); } catch (e) { return String(a); } }",
    '      return String(a);',
    "    }).join(' ');",
    "    try { window.parent.postMessage({ type: 'previewConsole', level: level, message: msg }, '*'); } catch (e) {}",
    '  };',
    "  ['log','info','debug','warn','error'].forEach(function(level) {",
    '    var orig = console[level];',
    '    console[level] = function() { try { orig.apply(console, arguments); } catch (e) {} post(level, arguments); };',
    '  });',
    "  window.addEventListener('error', function(e) {",
    "    post('error', ['[Uncaught] ' + (e.message || '') + ' (' + (e.filename || '') + ':' + (e.lineno || 0) + ':' + (e.colno || 0) + ')']);",
    '  });',
    "  window.addEventListener('unhandledrejection', function(e) {",
    '    var r = e.reason; var d = r && (r.stack || r.message) || String(r);',
    "    post('error', ['[Unhandled Rejection] ' + d]);",
    '  });',
    // Heartbeat — mirrors the VS Code webview\'s "console interception
    // initialized" announcement so the Preview Console channel always
    // shows at least one line per render. Goes through console.log so the
    // patched console forwards it via the same postMessage path as
    // anything the user logs.
    "  console.log('CalcPad preview console interception initialized');",
    '})();',
  ].join('\n')
  const open = '<' + 'script>'
  const close = '</' + 'script>'
  const script = open + body + close
  const headIdx = html.indexOf('<head>')
  if (headIdx >= 0) {
    return html.slice(0, headIdx + 6) + script + html.slice(headIdx + 6)
  }
  return script + html
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

// ---- In-app confirm dialog ----
export type ConfirmChoice = 'yes' | 'no' | 'cancel'

interface ConfirmState {
  title: string
  message: string
  yesLabel: string
  noLabel: string
  resolve: (c: ConfirmChoice) => void
}

const confirmState = ref<ConfirmState | null>(null)

function showConfirm(opts: {
  title: string
  message: string
  yesLabel?: string
  noLabel?: string
}): Promise<ConfirmChoice> {
  // If a previous prompt is still up, treat its answer as cancel.
  confirmState.value?.resolve('cancel')
  return new Promise(resolve => {
    confirmState.value = {
      title: opts.title,
      message: opts.message,
      yesLabel: opts.yesLabel ?? 'Yes',
      noLabel: opts.noLabel ?? 'No',
      resolve,
    }
  })
}

function resolveConfirm(choice: ConfirmChoice): void {
  const state = confirmState.value
  if (!state) return
  confirmState.value = null
  state.resolve(choice)
}

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
  showConfirm,
  setTabs,
  onTabActivate,
  onTabCloseRequest,
  onNewTabRequest,
})
</script>
