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
      </div>
      <!-- Tab strip (VS Code-style). Hidden until at least one tab is registered. -->
      <div v-if="tabs.length > 0" class="tab-strip" role="tablist" @contextmenu.prevent>
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
          @contextmenu.prevent="onTabContextMenu($event, tab.id)"
        >
          <span class="tab-title">{{ tab.title }}</span>
          <span v-if="tab.dirty" class="tab-dirty-dot" :title="'Unsaved changes'">●</span>
          <button
            class="tab-close"
            :title="tab.dirty ? 'Close (unsaved changes)' : 'Close'"
            @mousedown.stop
            @click.stop="onTabClose(tab.id)"
          >
            ✕
          </button>
        </div>
        <button class="tab-new" title="New tab (Ctrl+T)" @click="onNewTab">+</button>
      </div>
      <!-- Right-click context menu for tabs. Rendered outside .tab-strip so it
           can be positioned absolutely without being clipped. @mousedown.stop
           keeps the document-level closer from firing before the button's
           click handler runs. -->
      <div
        v-if="tabContextMenu"
        class="tab-context-menu"
        :style="{ left: tabContextMenu.x + 'px', top: tabContextMenu.y + 'px' }"
        @mousedown.stop
        @click.stop
      >
        <button class="tab-context-item" @click="onContextClose">Close</button>
        <button
          class="tab-context-item"
          :disabled="tabs.length <= 1"
          @click="onContextCloseOthers"
        >Close Others</button>
        <button class="tab-context-item" @click="onContextCloseAll">Close All</button>
        <template v-if="tabContextMenu.filePath">
          <div class="tab-context-sep" role="separator"></div>
          <button class="tab-context-item" @click="onContextOpenContainingFolder">
            Open Containing Folder
          </button>
          <button class="tab-context-item" @click="onContextCopyFullPath">
            Copy Full Path
          </button>
          <button class="tab-context-item" @click="onContextCopyRelativePath">
            Copy Relative Path
          </button>
        </template>
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
          <svg class="status-icon error" viewBox="0 0 16 16" aria-hidden="true">
            <path d="M16 8A8 8 0 1 1 0 8a8 8 0 0 1 16 0zM5.354 4.646a.5.5 0 1 0-.708.708L7.293 8l-2.647 2.646a.5.5 0 0 0 .708.708L8 8.707l2.646 2.647a.5.5 0 0 0 .708-.708L8.707 8l2.647-2.646a.5.5 0 0 0-.708-.708L8 7.293z"/>
          </svg> {{ errorCount }}
          <svg class="status-icon warning" viewBox="0 0 16 16" aria-hidden="true">
            <path d="M8.982 1.566a1.13 1.13 0 0 0-1.964 0L.165 13.233c-.457.778.091 1.767.982 1.767h13.706c.891 0 1.44-.99.982-1.767zM8 5c.535 0 .954.462.9.995l-.35 3.507a.552.552 0 0 1-1.1 0L7.1 5.995A.905.905 0 0 1 8 5m.002 6a1 1 0 1 1 0 2 1 1 0 0 1 0-2"/>
          </svg> {{ warningCount }}
          <svg class="status-icon info" viewBox="0 0 16 16" aria-hidden="true">
            <path d="M8 16A8 8 0 1 0 8 0a8 8 0 0 0 0 16m.93-9.412-1 4.705c-.07.34.029.533.304.533.194 0 .487-.07.686-.246l-.088.416c-.287.346-.92.598-1.465.598-.703 0-1.002-.422-.808-1.319l.738-3.468c.064-.293.006-.399-.287-.47l-.451-.081.082-.381 2.29-.287zM8 5.5a1 1 0 1 1 0-2 1 1 0 0 1 0 2"/>
          </svg> {{ infoCount }}
        </span>
        <span class="status-output" @click="openBottomTab('output')">Output</span>
        <span class="spacer"></span>
        <span
          class="status-server"
          :class="{ connected: serverConnected, disconnected: !serverConnected }"
          :title="serverConnected ? 'Server connected' : 'Server disconnected'"
        >
          ● {{ serverConnected ? 'Connected' : 'Disconnected' }}
        </span>
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
import { ref, computed, nextTick, onMounted, onBeforeUnmount } from 'vue'

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

export type PreviewMode = 'wrapped' | 'unwrapped'

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
const onTabCloseOthersRequest = ref<((id: string) => void) | null>(null)
const onTabCloseAllRequest = ref<(() => void) | null>(null)
const onTabOpenContainingFolderRequest = ref<((id: string) => void) | null>(null)
const onTabCopyFullPathRequest = ref<((id: string) => void) | null>(null)
const onTabCopyRelativePathRequest = ref<((id: string) => void) | null>(null)

interface TabContextMenuState {
  x: number
  y: number
  tabId: string
  filePath: string | null
}
const tabContextMenu = ref<TabContextMenuState | null>(null)

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

function onTabContextMenu(e: MouseEvent, tabId: string): void {
  const tab = tabs.value.find(t => t.id === tabId)
  tabContextMenu.value = {
    x: e.clientX,
    y: e.clientY,
    tabId,
    filePath: tab?.filePath ?? null,
  }
}

function closeTabContextMenu(): void {
  tabContextMenu.value = null
}

function onContextClose(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCloseRequest.value?.(m.tabId)
  closeTabContextMenu()
}

function onContextCloseOthers(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCloseOthersRequest.value?.(m.tabId)
  closeTabContextMenu()
}

function onContextCloseAll(): void {
  if (!tabContextMenu.value) return
  onTabCloseAllRequest.value?.()
  closeTabContextMenu()
}

function onContextOpenContainingFolder(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabOpenContainingFolderRequest.value?.(m.tabId)
  closeTabContextMenu()
}

function onContextCopyFullPath(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCopyFullPathRequest.value?.(m.tabId)
  closeTabContextMenu()
}

function onContextCopyRelativePath(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCopyRelativePathRequest.value?.(m.tabId)
  closeTabContextMenu()
}

function onDocumentInteractionForTabMenu(e: MouseEvent | KeyboardEvent): void {
  if (!tabContextMenu.value) return
  if (e instanceof KeyboardEvent && e.key !== 'Escape') return
  closeTabContextMenu()
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

function setPreviewHtml(html: string, scrollToLine?: number): void {
  const frame = previewFrame.value
  if (!frame) return
  const doc = frame.contentDocument
  if (!doc) return
  doc.open()
  doc.write(injectPreviewConsole(injectLineLinks(injectScrollbarStyles(html), scrollToLine)))
  doc.close()
}

// Give the preview iframe a clearly visible vertical scrollbar. The default
// scrollbar is nearly invisible; reserving the gutter with `overflow-y: scroll`
// keeps the layout from shifting. The iframe has no VS Code theme variables, so
// use plain rgba values (mirrors vscode-calcpad's getScrollbarStyleScript).
// The .code (unwrapped view) rule mirrors this on the code container so the
// Neutralino webview shows the same scrollbar for long code output.
// The .lineLink override enlarges the hover arrow so it's easier to click.
function injectScrollbarStyles(html: string): string {
  const style = [
    '<' + 'style>',
    'html { overflow-y: scroll; }',
    'body { min-height: 100vh; }',
    '.code { overflow-y: auto; }',
    '::-webkit-scrollbar { width: 12px; height: 12px; }',
    '::-webkit-scrollbar-track { background: transparent; }',
    '::-webkit-scrollbar-thumb { background: rgba(121,121,121,0.4); border-radius: 6px; }',
    '::-webkit-scrollbar-thumb:hover { background: rgba(100,100,100,0.7); }',
    '::-webkit-scrollbar-thumb:active { background: rgba(85,85,85,0.9); }',
    '::-webkit-scrollbar-corner { background: transparent; }',
    // Pin the arrow to its own line (position: relative on .line) and extend it
    // across the body's left margin so the whole gutter is a hover+click target.
    // Each arrow is always in the DOM at opacity 0 so pointing at the margin
    // reveals it directly — no need to hover the line text first. The JS still
    // toggles `display` for scroll-hides, but `!important` keeps the anchor
    // interactive whenever visible.
    '.line { position: relative; }',
    '.lineLink { left: -3em !important; top: 0 !important; bottom: 0 !important; width: 3em !important; height: auto !important; font-size: 16pt !important; padding-right: 4pt !important; box-sizing: border-box !important; display: flex !important; align-items: center !important; justify-content: flex-end !important; opacity: 0 !important; transition: opacity 0.15s !important; }',
    '.lineLink:hover { opacity: 1 !important; }',
    '</' + 'style>',
  ].join('\n')
  const headIdx = html.indexOf('<head>')
  if (headIdx >= 0) {
    return html.slice(0, headIdx + 6) + style + html.slice(headIdx + 6)
  }
  return style + html
}

// Inject the line-link behaviour ported from vscode-calcpad
// (getErrorNavigationScript's a[data-text] binding + getLineLinkScript):
//  - a[data-text] anchors (error links + .line-num code anchors) post
//    'navigateToLine'. isCodeView (any .line-num present) or a .line-num class
//    marks the target as a 'source' line; otherwise it's an expanded 'output'
//    line — the discriminator main.ts uses for the macro two-step.
//  - each wrapped-view .line gets a hover '←' link posting an 'output' line.
//  - .roundBox error chips scroll the preview to their output line.
//  - an optional baked-in scrollToLine target is scrolled into view on load,
//    avoiding a postMessage race with the iframe reload.
function injectLineLinks(html: string, scrollToLine?: number): string {
  const scrollTarget = typeof scrollToLine === 'number' ? String(scrollToLine) : 'null'
  const body = [
    "document.addEventListener('DOMContentLoaded', function() {",
    "  var post = function(line, lineType) {",
    "    try { window.parent.postMessage({ type: 'navigateToLine', line: line, lineType: lineType }, '*'); } catch (e) {}",
    "  };",
    // Error links + line-num anchors: the code view (unwrapped output, or the
    // wrapped view's error fallback) renders .line-num anchors whose data-text
    // is already a source line. Tag each click so main.ts only does the
    // output->unwrapped two-step for real output lines.
    "  var isCodeView = !!document.querySelector('.line-num');",
    "  document.querySelectorAll('a[data-text]').forEach(function(link) {",
    "    link.addEventListener('click', function(e) {",
    "      e.preventDefault();",
    "      var n = link.getAttribute('data-text');",
    "      if (!n) return;",
    "      var lineType = (link.classList.contains('line-num') || isCodeView) ? 'source' : 'output';",
    "      post(parseInt(n, 10), lineType);",
    "    });",
    "  });",
    // Hover '←' links on each wrapped-view line. Prefer data-source-line
    // (set by Calcpad.Core when the line came from a macro/include expansion)
    // so the arrow navigates the editor straight to the source line and skips
    // the wrapped->unwrapped two-step. Error links keep the 'output' path.
    "  function hideAllLineLinks() {",
    "    document.querySelectorAll('.lineLink').forEach(function(l) { l.style.display = 'none'; });",
    "  }",
    "  document.querySelectorAll('.line').forEach(function(el) {",
    "    var id = el.id || '';",
    "    var n = id.indexOf('line-') === 0 ? id.slice(5) : '';",
    // Loop iterations past the first drop the id (it's the scroll anchor and must
    // stay unique) but keep data-source-line, so key off the source line here.
    "    var src = el.getAttribute('data-source-line') || n;",
    "    if (!src) return;",
    "    var link = document.createElement('a');",
    "    link.className = 'lineLink';",
    "    link.href = '#0';",
    "    link.setAttribute('data-text', src);",
    "    link.title = 'Source line ' + src;",
    "    link.textContent = '\\u2190';",
    "    link.style.display = 'none';",
    "    link.addEventListener('click', function(e) {",
    "      e.preventDefault();",
    "      post(parseInt(src, 10), 'source');",
    "    });",
    "    el.appendChild(link);",
    "    el.addEventListener('mouseenter', function() {",
    "      hideAllLineLinks();",
    "      link.style.display = 'inline-block';",
    "    });",
    "  });",
    "  window.addEventListener('scroll', hideAllLineLinks);",
    // Error-summary chips: scroll the preview to the referenced output line.
    "  document.querySelectorAll('.roundBox').forEach(function(box) {",
    "    box.addEventListener('click', function() {",
    "      var line = box.getAttribute('data-line');",
    "      var target = line && document.getElementById('line-' + line);",
    "      if (target) target.scrollIntoView({ block: 'start' });",
    "    });",
    "  });",
    "  var scrollToLine = " + scrollTarget + ";",
    "  if (scrollToLine !== null) {",
    "    var target = document.getElementById('line-' + scrollToLine);",
    "    if (target) target.scrollIntoView({ block: 'center' });",
    "  }",
    "});",
  ].join('\n')
  const script = '<' + 'script>' + body + '</' + 'script>'
  const bodyCloseIdx = html.lastIndexOf('</body>')
  if (bodyCloseIdx >= 0) {
    return html.slice(0, bodyCloseIdx) + script + html.slice(bodyCloseIdx)
  }
  return html + script
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

  document.addEventListener('mousedown', onDocumentInteractionForTabMenu)
  document.addEventListener('keydown', onDocumentInteractionForTabMenu)
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onDocumentInteractionForTabMenu)
  document.removeEventListener('keydown', onDocumentInteractionForTabMenu)
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
  onTabCloseOthersRequest,
  onTabCloseAllRequest,
  onTabOpenContainingFolderRequest,
  onTabCopyFullPathRequest,
  onTabCopyRelativePathRequest,
})
</script>
