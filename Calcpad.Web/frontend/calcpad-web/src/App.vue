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
        <span class="spacer"></span>
        <button
          class="toolbar-btn"
          @click="onToggleSplit"
          :title="isSplit ? 'Merge editor groups' : 'Split editor down (Ctrl+\\)'"
        >
          {{ isSplit ? 'Unsplit' : 'Split ⬓' }}
        </button>
        <button class="toolbar-btn" @click="togglePreview" title="Preview HTML">
          {{ previewVisible ? 'Hide Preview' : 'Preview' }}
        </button>
      </div>

      <!-- Editor groups, stacked top/bottom. One group normally; two when split. -->
      <div class="editor-groups">
        <template v-for="(group, gi) in groups" :key="group.id">
          <div
            class="editor-group"
            :class="{ 'active-group': group.id === activeGroupId && isSplit }"
            :style="editorGroupStyle(gi)"
            @mousedown="onGroupFocus(group.id)"
          >
            <!-- Tab strip (VS Code-style). Hidden until at least one tab is registered. -->
            <div v-if="group.tabs.length > 0" class="tab-strip" role="tablist" @contextmenu.prevent>
              <div
                v-for="tab in group.tabs"
                :key="tab.id"
                class="tab"
                :class="{ active: tab.isActive, dirty: tab.dirty }"
                role="tab"
                :aria-selected="tab.isActive"
                :title="tab.filePath || tab.title"
                @mousedown.left="onTabClick(group.id, tab.id)"
                @mousedown.middle.prevent="onTabClose(group.id, tab.id)"
                @contextmenu.prevent="onTabContextMenu($event, group.id, tab.id)"
              >
                <span class="tab-title">{{ tab.title }}</span>
                <span v-if="tab.dirty" class="tab-dirty-dot" :title="'Unsaved changes'">●</span>
                <button
                  class="tab-close"
                  :title="tab.dirty ? 'Close (unsaved changes)' : 'Close'"
                  @mousedown.stop
                  @click.stop="onTabClose(group.id, tab.id)"
                >
                  ✕
                </button>
              </div>
              <button class="tab-new" title="New tab (Ctrl+T)" @click="onNewTab(group.id)">+</button>
              <span class="spacer"></span>
              <button
                v-if="isSplit"
                class="group-close"
                title="Close this editor group"
                @click="onCloseGroup(group.id)"
              >✕</button>
            </div>
            <div class="editor-container" :ref="el => setEditorRef(group.id, el)"></div>
          </div>
          <!-- Horizontal divider between the two stacked groups. -->
          <div
            v-if="gi === 0 && isSplit"
            class="group-divider"
            :class="{ dragging: draggingEditorDivider }"
            @mousedown="onEditorDividerMouseDown"
            role="separator"
            aria-orientation="horizontal"
          ></div>
        </template>
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

      <!-- Bottom panel (Problems / Output) — reflects the ACTIVE group. -->
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
          <span v-if="isSplit" class="panel-scope" :title="'Showing the active editor group'">
            {{ activeGroupLabel }}
          </span>
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
          <svg class="status-icon lintError" viewBox="0 0 16 16" aria-hidden="true">
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
      <!-- One preview iframe per editor group, stacked to mirror the editor
           split. allow-scripts is required so the injected console-interception
           script (and any user #HTML script) actually runs in the iframe. -->
      <div class="preview-frames">
        <template v-for="(group, gi) in groups" :key="'pv-' + group.id">
          <iframe
            class="preview-frame"
            :class="{ 'active-group': group.id === activeGroupId && isSplit }"
            :style="editorGroupStyle(gi)"
            :ref="el => setPreviewRef(group.id, el)"
            sandbox="allow-same-origin allow-scripts"
          ></iframe>
          <div
            v-if="gi === 0 && isSplit"
            class="group-divider"
            :class="{ dragging: draggingEditorDivider }"
            @mousedown="onEditorDividerMouseDown"
            role="separator"
            aria-orientation="horizontal"
          ></div>
        </template>
      </div>
    </div>

    <!-- Confirm dialog. HTML modal instead of a native dialog for cross-platform
         consistency between web and desktop. -->
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

    <!-- Quick-pick dialog. A single-select list modal (VS Code QuickPick
         analog) used e.g. by the image-storage prompt. -->
    <div v-if="quickPickState" class="modal-backdrop" @click.self="resolveQuickPick(null)">
      <div class="modal-card quick-pick-card" role="dialog" aria-modal="true">
        <div class="modal-title">{{ quickPickState.title }}</div>
        <div v-if="quickPickState.placeholder" class="modal-message">{{ quickPickState.placeholder }}</div>
        <div class="quick-pick-list">
          <button
            v-for="(opt, i) in quickPickState.options"
            :key="i"
            class="quick-pick-option"
            @click="resolveQuickPick(i)"
          >
            <div class="quick-pick-option-label">{{ opt.label }}</div>
            <div v-if="opt.detail" class="quick-pick-option-detail">{{ opt.detail }}</div>
          </button>
        </div>
        <div class="modal-actions">
          <button class="modal-btn" @click="resolveQuickPick(null)">Cancel</button>
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

export type PreviewMode = 'wrapped' | 'unwrapped'

// Preview mode is shared across both groups. `onGotoProblem` targets the
// active group's editor (main.ts resolves it).
const onGotoProblem = ref<((problem: ProblemItem) => void) | null>(null)
const onPreviewToggled = ref<((visible: boolean) => void) | null>(null)
const onPreviewModeChanged = ref<((mode: PreviewMode) => void) | null>(null)
const previewMode = ref<PreviewMode>('wrapped')

// ---- Tab strip / editor groups ----
export interface TabUiState {
  id: string
  title: string
  filePath: string | null
  dirty: boolean
  isActive: boolean
}

interface GroupUi {
  id: string
  tabs: TabUiState[]
  problems: ProblemItem[]
  errorCount: number
  warningCount: number
  infoCount: number
}

function emptyGroup(id: string): GroupUi {
  return { id, tabs: [], problems: [], errorCount: 0, warningCount: 0, infoCount: 0 }
}

// Seed with the primary group. main.ts adds a second on split.
const groups = ref<GroupUi[]>([emptyGroup('g0')])
const activeGroupId = ref<string>('g0')

const isSplit = computed(() => groups.value.length > 1)
const activeGroup = computed(() => groups.value.find(g => g.id === activeGroupId.value) ?? groups.value[0])
const problems = computed(() => activeGroup.value?.problems ?? [])
const errorCount = computed(() => activeGroup.value?.errorCount ?? 0)
const warningCount = computed(() => activeGroup.value?.warningCount ?? 0)
const infoCount = computed(() => activeGroup.value?.infoCount ?? 0)
const activeGroupLabel = computed(() => {
  const i = groups.value.findIndex(g => g.id === activeGroupId.value)
  return i === 0 ? 'Top' : 'Bottom'
})

// DOM element registries (function refs). main.ts reads these to create the
// Monaco editor / write preview HTML for each group.
const editorEls = new Map<string, HTMLElement>()
const previewEls = new Map<string, HTMLIFrameElement>()

function setEditorRef(id: string, el: unknown): void {
  if (el instanceof HTMLElement) editorEls.set(id, el)
  else editorEls.delete(id)
}
function setPreviewRef(id: string, el: unknown): void {
  if (el instanceof HTMLIFrameElement) previewEls.set(id, el)
  else previewEls.delete(id)
}
function getEditorContainer(id: string): HTMLElement | null {
  return editorEls.get(id) ?? null
}

// ---- Split ratio (top group's fraction of the stack height) ----
const editorSplitRatio = ref<number>(0.5)
const draggingEditorDivider = ref(false)

function editorGroupStyle(index: number): Record<string, string> {
  if (!isSplit.value) return { flex: '1 1 0', minHeight: '0' }
  if (index === 0) return { flex: `0 0 ${editorSplitRatio.value * 100}%`, minHeight: '0' }
  return { flex: '1 1 0', minHeight: '0' }
}

function onEditorDividerMouseDown(e: MouseEvent): void {
  e.preventDefault()
  draggingEditorDivider.value = true
  const container = (e.currentTarget as HTMLElement).parentElement
  if (!container) return
  const rect = container.getBoundingClientRect()
  const onMove = (ev: MouseEvent) => {
    const frac = (ev.clientY - rect.top) / rect.height
    editorSplitRatio.value = Math.min(0.85, Math.max(0.15, frac))
  }
  const onUp = () => {
    draggingEditorDivider.value = false
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
  }
  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
}

// ---- Group lifecycle (driven by main.ts) ----
const onSplitRequest = ref<(() => void) | null>(null)
const onCloseGroupRequest = ref<((groupId: string) => void) | null>(null)
const onGroupFocusRequest = ref<((groupId: string) => void) | null>(null)

function addGroup(id: string): void {
  if (groups.value.some(g => g.id === id)) return
  groups.value.push(emptyGroup(id))
}
function removeGroup(id: string): void {
  const idx = groups.value.findIndex(g => g.id === id)
  if (idx < 0) return
  groups.value.splice(idx, 1)
  editorEls.delete(id)
  previewEls.delete(id)
  if (activeGroupId.value === id) {
    activeGroupId.value = groups.value[0]?.id ?? 'g0'
  }
}
function setActiveGroup(id: string): void {
  if (groups.value.some(g => g.id === id)) activeGroupId.value = id
}
function groupIds(): string[] {
  return groups.value.map(g => g.id)
}

function onToggleSplit(): void {
  if (isSplit.value) {
    // Merge: close the non-active group (main.ts prompts for dirty tabs).
    const other = groups.value.find(g => g.id !== activeGroupId.value)
    if (other) onCloseGroupRequest.value?.(other.id)
  } else {
    onSplitRequest.value?.()
  }
}
function onCloseGroup(groupId: string): void {
  onCloseGroupRequest.value?.(groupId)
}
function onGroupFocus(groupId: string): void {
  if (activeGroupId.value !== groupId) onGroupFocusRequest.value?.(groupId)
}

// ---- Tab-strip callbacks (per group) ----
const onTabActivate = ref<((groupId: string, id: string) => void) | null>(null)
const onTabCloseRequest = ref<((groupId: string, id: string) => void) | null>(null)
const onNewTabRequest = ref<((groupId: string) => void) | null>(null)
const onTabCloseOthersRequest = ref<((groupId: string, id: string) => void) | null>(null)
const onTabCloseAllRequest = ref<((groupId: string) => void) | null>(null)
const onTabOpenContainingFolderRequest = ref<((groupId: string, id: string) => void) | null>(null)
const onTabCopyFullPathRequest = ref<((groupId: string, id: string) => void) | null>(null)
const onTabCopyRelativePathRequest = ref<((groupId: string, id: string) => void) | null>(null)

interface TabContextMenuState {
  x: number
  y: number
  groupId: string
  tabId: string
  filePath: string | null
}
const tabContextMenu = ref<TabContextMenuState | null>(null)

function setTabs(groupId: string, next: TabUiState[]): void {
  const g = groups.value.find(g => g.id === groupId)
  if (g) g.tabs = next
}

function onTabClick(groupId: string, id: string): void {
  onTabActivate.value?.(groupId, id)
}

function onTabClose(groupId: string, id: string): void {
  onTabCloseRequest.value?.(groupId, id)
}

function onNewTab(groupId: string): void {
  onNewTabRequest.value?.(groupId)
}

function onTabContextMenu(e: MouseEvent, groupId: string, tabId: string): void {
  const tab = groups.value.find(g => g.id === groupId)?.tabs.find(t => t.id === tabId)
  tabContextMenu.value = {
    x: e.clientX,
    y: e.clientY,
    groupId,
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
  onTabCloseRequest.value?.(m.groupId, m.tabId)
  closeTabContextMenu()
}

function onContextCloseOthers(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCloseOthersRequest.value?.(m.groupId, m.tabId)
  closeTabContextMenu()
}

function onContextCloseAll(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCloseAllRequest.value?.(m.groupId)
  closeTabContextMenu()
}

function onContextOpenContainingFolder(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabOpenContainingFolderRequest.value?.(m.groupId, m.tabId)
  closeTabContextMenu()
}

function onContextCopyFullPath(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCopyFullPathRequest.value?.(m.groupId, m.tabId)
  closeTabContextMenu()
}

function onContextCopyRelativePath(): void {
  const m = tabContextMenu.value
  if (!m) return
  onTabCopyRelativePathRequest.value?.(m.groupId, m.tabId)
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

const serverConnected = ref(false)
const sidebarVisible = ref(true)
const previewVisible = ref(false)
const bottomPanelOpen = ref(false)
const activeBottomTab = ref<'problems' | 'output'>('problems')

export type OutputChannel = 'app' | 'preview' | 'server'

export interface OutputLine {
  time: string
  level: string
  label: string
  message: string
  channel: OutputChannel
  /** For the per-group 'preview' channel: which group's preview emitted it. */
  groupId?: string
}

const OUTPUT_CHANNEL_LABELS: Record<OutputChannel, string> = {
  app: 'CalcPad',
  preview: 'Preview Console',
  server: 'Server',
}

const outputLines = ref<OutputLine[]>([])
const outputList = ref<HTMLElement | null>(null)
const activeOutputChannel = ref<OutputChannel>('app')

// The 'preview' channel is per-group (each split preview has its own console),
// so filter it by the active group. 'app' / 'server' are global.
const filteredOutputLines = computed(() =>
  outputLines.value.filter(l =>
    l.channel === activeOutputChannel.value &&
    (l.channel !== 'preview' || !l.groupId || l.groupId === activeGroupId.value)
  )
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
  groupId?: string,
): void {
  const now = new Date()
  const time = now.toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })
  const labels: Record<string, string> = { info: 'INFO', warn: 'WARN', error: 'ERROR', debug: 'DEBUG' }
  // Sample scroll position BEFORE mutating outputLines so the new line's
  // height doesn't inflate scrollHeight and mask a user-initiated scroll-up.
  const el = outputList.value
  const wasAtBottom = el
    ? (el.scrollHeight - el.scrollTop - el.clientHeight) <= 4
    : true
  outputLines.value.push({ time, level, label: labels[level] ?? level, message, channel, groupId })
  // Cap at 1000 lines (across all channels combined)
  if (outputLines.value.length > 1000) {
    outputLines.value.splice(0, outputLines.value.length - 1000)
  }
  const visible = channel === activeOutputChannel.value &&
    (channel !== 'preview' || !groupId || groupId === activeGroupId.value)
  if (wasAtBottom && visible) {
    nextTick(() => {
      const target = outputList.value
      if (target) target.scrollTop = target.scrollHeight
    })
  }
}

function clearOutput(): void {
  // Only clear the currently visible channel — match VS Code's per-channel clear.
  outputLines.value = outputLines.value.filter(l => l.channel !== activeOutputChannel.value)
}

function showOutput(channel: OutputChannel = 'app'): void {
  activeOutputChannel.value = channel
  activeBottomTab.value = 'output'
  bottomPanelOpen.value = true
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

function setPreviewHtml(groupId: string, html: string, scrollToLine?: number): void {
  const frame = previewEls.get(groupId)
  if (!frame) return
  const doc = frame.contentDocument
  if (!doc) return
  doc.open()
  doc.write(injectPreviewConsole(injectLineLinks(injectScrollbarStyles(html), scrollToLine, groupId), groupId))
  doc.close()
}

// Scroll a group's preview to a source line (editor -> preview sync). Posts to
// the listener injected by injectLineLinks; no-op if that preview isn't shown.
function scrollPreviewToSourceLine(groupId: string, line: number): void {
  const frame = previewEls.get(groupId)
  frame?.contentWindow?.postMessage({ type: 'scrollPreviewToLine', line }, '*')
}

// Give the preview iframe a clearly visible vertical scrollbar. The default
// scrollbar is nearly invisible; reserving the gutter with `overflow-y: scroll`
// keeps the layout from shifting. The iframe has no VS Code theme variables, so
// use plain rgba values (mirrors vscode-calcpad's getScrollbarStyleScript).
// The .code (unwrapped view) rule mirrors this on the code container so the
// Tauri webview shows the same scrollbar for long code output.
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
    '.line { position: relative; }',
    // Brief flash when the preview is focused to the editor's cursor line.
    '.cpd-line-focus { background-color: rgba(120,170,255,0.28) !important; transition: background-color 0.3s ease !important; }',
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

// Inject the line-link behaviour ported from vscode-calcpad. Posted messages
// carry `groupId` so main.ts routes navigation to the group that owns this
// preview (see App.vue's per-group iframes).
function injectLineLinks(html: string, scrollToLine: number | undefined, groupId: string): string {
  const scrollTarget = typeof scrollToLine === 'number' ? String(scrollToLine) : 'null'
  const gid = JSON.stringify(groupId)
  const body = [
    "document.addEventListener('DOMContentLoaded', function() {",
    "  var GROUP_ID = " + gid + ";",
    "  var post = function(line, lineType) {",
    "    try { window.parent.postMessage({ type: 'navigateToLine', line: line, lineType: lineType, groupId: GROUP_ID }, '*'); } catch (e) {}",
    "  };",
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
    "  function hideAllLineLinks() {",
    "    document.querySelectorAll('.lineLink').forEach(function(l) { l.style.display = 'none'; });",
    "  }",
    "  document.querySelectorAll('.line').forEach(function(el) {",
    "    var id = el.id || '';",
    "    var n = id.indexOf('line-') === 0 ? id.slice(5) : '';",
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
    "  document.querySelectorAll('.roundBox').forEach(function(box) {",
    "    box.addEventListener('click', function() {",
    "      var errId = box.getAttribute('data-error');",
    "      var target = errId ? document.getElementById(errId) : null;",
    "      if (!target) {",
    "        var line = box.getAttribute('data-line');",
    "        target = line ? document.getElementById('line-' + line) : null;",
    "      }",
    "      if (target) target.scrollIntoView({ block: 'start' });",
    "    });",
    "  });",
    "  var scrollToLine = " + scrollTarget + ";",
    "  if (scrollToLine !== null) {",
    "    var target = document.getElementById('line-' + scrollToLine);",
    "    if (target) target.scrollIntoView({ block: 'center' });",
    "  }",
    "  var focusTimer = null;",
    "  function focusPreviewLine(line) {",
    "    if (typeof line !== 'number' || isNaN(line)) return;",
    "    var target = document.querySelector('[data-source-line=\"' + line + '\"]');",
    "    if (!target) {",
    "      var anchor = document.querySelector('a.line-num[data-text=\"' + line + '\"]');",
    "      if (anchor) target = anchor.closest('.line-text') || anchor;",
    "    }",
    "    if (!target) {",
    "      var best = null, bestSrc = -1;",
    "      document.querySelectorAll('[data-source-line]').forEach(function(el) {",
    "        var s = parseInt(el.getAttribute('data-source-line'), 10);",
    "        if (!isNaN(s) && s <= line && s > bestSrc) { bestSrc = s; best = el; }",
    "      });",
    "      target = best;",
    "    }",
    "    if (!target) return;",
    "    target.scrollIntoView({ block: 'center' });",
    "    document.querySelectorAll('.cpd-line-focus').forEach(function(el) { el.classList.remove('cpd-line-focus'); });",
    "    target.classList.add('cpd-line-focus');",
    "    if (focusTimer) clearTimeout(focusTimer);",
    "    focusTimer = setTimeout(function() { target.classList.remove('cpd-line-focus'); }, 1200);",
    "  }",
    "  window.addEventListener('message', function(e) {",
    "    var d = e.data;",
    "    if (d && d.type === 'scrollPreviewToLine') focusPreviewLine(d.line);",
    "  });",
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
// postMessage, tagged with groupId so the Output panel's "Preview Console"
// channel can be split by the active editor group.
function injectPreviewConsole(html: string, groupId: string): string {
  const gid = JSON.stringify(groupId)
  const body = [
    '(function() {',
    '  if (window.__calcpadConsolePatched) return;',
    '  window.__calcpadConsolePatched = true;',
    '  var GROUP_ID = ' + gid + ';',
    '  var post = function(level, args) {',
    '    var msg = Array.from(args).map(function(a) {',
    '      if (a instanceof Error) return a.stack || a.message;',
    "      if (typeof a === 'object') { try { return JSON.stringify(a); } catch (e) { return String(a); } }",
    '      return String(a);',
    "    }).join(' ');",
    "    try { window.parent.postMessage({ type: 'previewConsole', level: level, message: msg, groupId: GROUP_ID }, '*'); } catch (e) {}",
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

function setProblems(groupId: string, markers: ProblemItem[]): void {
  const g = groups.value.find(g => g.id === groupId)
  if (!g) return
  g.problems = markers
  g.errorCount = markers.filter(m => m.severity === 8).length
  g.warningCount = markers.filter(m => m.severity === 4).length
  g.infoCount = markers.filter(m => m.severity === 2).length
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

// ---- In-app quick-pick dialog ----
interface QuickPickOptionUi {
  label: string
  detail?: string
}

interface QuickPickState {
  title: string
  placeholder?: string
  options: QuickPickOptionUi[]
  resolve: (index: number | null) => void
}

const quickPickState = ref<QuickPickState | null>(null)

/** Show a single-select list; resolves with the chosen option index, or null if dismissed. */
function showQuickPick(opts: {
  title: string
  placeholder?: string
  options: QuickPickOptionUi[]
}): Promise<number | null> {
  // If a previous prompt is still up, treat it as dismissed.
  quickPickState.value?.resolve(null)
  return new Promise(resolve => {
    quickPickState.value = {
      title: opts.title,
      placeholder: opts.placeholder,
      options: opts.options,
      resolve,
    }
  })
}

function resolveQuickPick(index: number | null): void {
  const state = quickPickState.value
  if (!state) return
  quickPickState.value = null
  state.resolve(index)
}

defineExpose({
  // group lifecycle
  addGroup,
  removeGroup,
  setActiveGroup,
  groupIds,
  getEditorContainer,
  onSplitRequest,
  onCloseGroupRequest,
  onGroupFocusRequest,
  // panels / preview
  toggleSidebar,
  togglePreview,
  isPreviewVisible,
  setPreviewHtml,
  scrollPreviewToSourceLine,
  setProblems,
  onGotoProblem,
  onPreviewToggled,
  onPreviewModeChanged,
  setPreviewMode,
  getPreviewMode,
  appendOutput,
  clearOutput,
  showOutput,
  showConfirm,
  showQuickPick,
  // tabs
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
