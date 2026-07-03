<template>
  <div class="files-tab">
    <div v-if="!openedFolder" class="files-empty">
      <p class="files-empty-message">No folder open.</p>
      <button class="files-open-btn" @click="$emit('open-folder-request')">Open Folder</button>
    </div>
    <template v-else>
      <div class="files-header">
        <span class="files-folder-name" :title="openedFolder">{{ folderBasename }}</span>
        <div class="files-header-actions">
          <button class="files-icon-btn" @click="expandAll" title="Expand All">
            <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" xmlns="http://www.w3.org/2000/svg"><path d="M9 9H4v1h5v5h1v-5h5V9h-5V4H9v5z"/></svg>
          </button>
          <button class="files-icon-btn" @click="collapseAll" title="Collapse All">
            <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" xmlns="http://www.w3.org/2000/svg"><path d="M4 9h11v1H4z"/></svg>
          </button>
          <button class="files-change-btn" @click="$emit('open-folder-request')" title="Open a different folder">
            Change Folder
          </button>
        </div>
      </div>
      <label class="files-show-all">
        <input type="checkbox" v-model="showAll" />
        <span>Show all files</span>
      </label>
      <div class="file-tree" @contextmenu.prevent>
        <FileTreeNode
          v-for="node in filteredRoots"
          :key="node.path"
          :node="node"
          :expanded-paths="expandedPaths"
          @toggle="handleToggle"
          @open-file="(p) => $emit('open-file', p)"
          @context-menu="handleContextMenu"
        />
      </div>
    </template>
    <!-- Custom right-click menu. Rendered outside .files-tab so it can be
         positioned absolutely without being clipped by the sidebar's overflow.
         @mousedown.stop keeps the document-level closer from firing before the
         button's click handler runs. -->
    <div
      v-if="contextMenu"
      class="tree-context-menu"
      :style="{ left: contextMenu.x + 'px', top: contextMenu.y + 'px' }"
      @mousedown.stop
      @click.stop
    >
      <button class="tree-context-item" @click="onOpenContainingFolder">
        Open Containing Folder
      </button>
      <button class="tree-context-item" @click="onCopyFullPath">
        Copy Full Path
      </button>
      <button class="tree-context-item" @click="onCopyRelativePath">
        Copy Relative Path
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import type { FileNode } from '../types'
import FileTreeNode from './FileTreeNode.vue'
import type { ContextMenuPayload } from './FileTreeNode.vue'

interface Props {
  openedFolder: string | null
  treeRoots: FileNode[]
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'open-folder-request': []
  'open-file': [path: string]
  'expand-folder': [path: string]
  'open-containing-folder': [path: string]
}>()

const expandedPaths = ref<string[]>([])
const showAll = ref(false)

// Extension filter: only .cpd files are surfaced by default. Folders are
// always kept so the user can drill in.
const isCalcpadFile = (name: string): boolean => {
  const dot = name.lastIndexOf('.')
  if (dot < 0) return false
  return name.substring(dot + 1).toLowerCase() === 'cpd'
}

// Structural sharing: return the original array when no children were
// filtered out and no directory needed to be re-cloned. Big win on large
// trees — Vue can skip re-rendering entire branches when their reference
// identity is unchanged.
const filterNodes = (nodes: FileNode[]): FileNode[] => {
  let out: FileNode[] | null = null
  for (let i = 0; i < nodes.length; i++) {
    const n = nodes[i]
    if (n.isDirectory) {
      const original = n.children
      const filtered = original ? filterNodes(original) : original
      if (filtered === original) {
        if (out) out.push(n)
      } else {
        if (!out) out = nodes.slice(0, i)
        out.push({ ...n, children: filtered })
      }
    } else if (isCalcpadFile(n.name)) {
      if (out) out.push(n)
    } else {
      if (!out) out = nodes.slice(0, i)
      // drop it
    }
  }
  return out ?? nodes
}

const filteredRoots = computed(() =>
  showAll.value ? props.treeRoots : filterNodes(props.treeRoots)
)

// Reset expansion state whenever the workspace root changes.
watch(() => props.openedFolder, () => {
  expandedPaths.value = []
})

const folderBasename = computed(() => {
  if (!props.openedFolder) return ''
  const normalized = props.openedFolder.replace(/\\/g, '/').replace(/\/+$/, '')
  const idx = normalized.lastIndexOf('/')
  return idx >= 0 ? normalized.substring(idx + 1) : normalized
})

const findNode = (nodes: FileNode[], targetPath: string): FileNode | null => {
  for (const n of nodes) {
    if (n.path === targetPath) return n
    if (n.children) {
      const found = findNode(n.children, targetPath)
      if (found) return found
    }
  }
  return null
}

const handleToggle = (path: string, isExpanding: boolean) => {
  if (isExpanding) {
    if (!expandedPaths.value.includes(path)) {
      expandedPaths.value = [...expandedPaths.value, path]
    }
    const node = findNode(props.treeRoots, path)
    if (node && !node.loaded) {
      emit('expand-folder', path)
    }
  } else {
    expandedPaths.value = expandedPaths.value.filter(p => p !== path)
  }
}

const collectDirectoryPaths = (nodes: FileNode[], acc: string[]): void => {
  for (const n of nodes) {
    if (n.isDirectory) {
      acc.push(n.path)
      if (n.children) collectDirectoryPaths(n.children, acc)
    }
  }
}

// Expand All has to cascade: initially only the root's children are loaded,
// so expanding once only reveals depth 1. This async loop drives the cascade
// in the background — it yields between batches so the UI stays responsive
// even when there are hundreds of nested folders to walk.
const expandingAll = ref(false)
const requestedForExpandAll = new Set<string>()

// Small batch size + short delay keeps the bridge from being flooded and
// gives the browser time to paint between reactive updates.
const EXPAND_ALL_BATCH = 4
const EXPAND_ALL_STEP_MS = 20
const EXPAND_ALL_WAIT_MS = 40

async function cascadeExpandAll(): Promise<void> {
  requestedForExpandAll.clear()

  while (expandingAll.value) {
    const dirs: string[] = []
    collectDirectoryPaths(props.treeRoots, dirs)

    // Mark every known dir as expanded so children render as they arrive.
    // Only write when the set actually changed — otherwise every tick would
    // reassign the ref and force a re-render of the whole tree.
    let changed = false
    const merged = new Set(expandedPaths.value)
    for (const p of dirs) {
      if (!merged.has(p)) { merged.add(p); changed = true }
    }
    if (changed) expandedPaths.value = Array.from(merged)

    // Anything we haven't asked for yet?
    const pending: string[] = []
    for (const p of dirs) {
      if (requestedForExpandAll.has(p)) continue
      const node = findNode(props.treeRoots, p)
      if (node && !node.loaded) pending.push(p)
    }

    if (pending.length === 0) {
      // Nothing new to request — are we waiting on outstanding responses?
      const stillLoading = dirs.some(p => {
        const node = findNode(props.treeRoots, p)
        return node && !node.loaded
      })
      if (!stillLoading) {
        expandingAll.value = false
        break
      }
      await new Promise<void>(r => setTimeout(r, EXPAND_ALL_WAIT_MS))
      continue
    }

    for (const p of pending.slice(0, EXPAND_ALL_BATCH)) {
      requestedForExpandAll.add(p)
      emit('expand-folder', p)
    }
    await new Promise<void>(r => setTimeout(r, EXPAND_ALL_STEP_MS))
  }
}

const expandAll = () => {
  if (expandingAll.value) return
  expandingAll.value = true
  void cascadeExpandAll()
}

const collapseAll = () => {
  expandingAll.value = false
  expandedPaths.value = []
}

// ---- Context menu ----

interface ContextMenuState {
  node: FileNode
  x: number
  y: number
}
const contextMenu = ref<ContextMenuState | null>(null)

const handleContextMenu = (payload: ContextMenuPayload) => {
  contextMenu.value = { node: payload.node, x: payload.x, y: payload.y }
}

const closeContextMenu = () => {
  contextMenu.value = null
}

const onDocumentInteraction = (e: MouseEvent | KeyboardEvent) => {
  if (!contextMenu.value) return
  if (e instanceof KeyboardEvent && e.key !== 'Escape') return
  closeContextMenu()
}

onMounted(() => {
  document.addEventListener('mousedown', onDocumentInteraction)
  document.addEventListener('keydown', onDocumentInteraction)
})

onBeforeUnmount(() => {
  document.removeEventListener('mousedown', onDocumentInteraction)
  document.removeEventListener('keydown', onDocumentInteraction)
})

const relativePathFor = (fullPath: string): string => {
  if (!props.openedFolder) return fullPath
  const rootNorm = props.openedFolder.replace(/[\\/]+$/, '')
  const rootWithSep = rootNorm + (rootNorm.includes('\\') ? '\\' : '/')
  if (fullPath.startsWith(rootWithSep)) {
    return fullPath.substring(rootWithSep.length)
  }
  if (fullPath === rootNorm) return ''
  return fullPath
}

const writeClipboard = async (text: string) => {
  try {
    await navigator.clipboard.writeText(text)
  } catch {
    // Fallback: legacy execCommand path. The webview should support the
    // Clipboard API in Neutralino, so this is a safety net only.
    const ta = document.createElement('textarea')
    ta.value = text
    ta.style.position = 'fixed'
    ta.style.left = '-9999px'
    document.body.appendChild(ta)
    ta.select()
    try { document.execCommand('copy') } catch { /* ignore */ }
    document.body.removeChild(ta)
  }
}

const onOpenContainingFolder = () => {
  const menu = contextMenu.value
  if (!menu) return
  emit('open-containing-folder', menu.node.path)
  closeContextMenu()
}

const onCopyFullPath = async () => {
  const menu = contextMenu.value
  if (!menu) return
  await writeClipboard(menu.node.path)
  closeContextMenu()
}

const onCopyRelativePath = async () => {
  const menu = contextMenu.value
  if (!menu) return
  await writeClipboard(relativePathFor(menu.node.path))
  closeContextMenu()
}
</script>

<style scoped>
.files-tab {
  padding: 4px 0;
}

.files-empty {
  padding: 16px;
  text-align: center;
  color: var(--vscode-descriptionForeground);
}

.files-empty-message {
  margin: 0 0 12px 0;
  font-size: 12px;
}

.files-open-btn {
  padding: 6px 14px;
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  border-radius: 2px;
  cursor: pointer;
  font-size: 12px;
}

.files-open-btn:hover {
  background: var(--vscode-button-hoverBackground);
}

.files-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 8px;
  gap: 6px;
  border-bottom: 1px solid var(--vscode-widget-border);
  font-size: 11px;
  text-transform: uppercase;
  color: var(--vscode-sideBarSectionHeader-foreground, var(--vscode-foreground));
  background: var(--vscode-sideBarSectionHeader-background, var(--vscode-editor-background));
  /* Pin header while the tree scrolls. */
  position: sticky;
  top: 0;
  z-index: 2;
}

.files-folder-name {
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
  min-width: 0;
}

.files-header-actions {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
}

.files-icon-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  background: transparent;
  color: var(--vscode-icon-foreground, var(--vscode-foreground));
  border: none;
  border-radius: 3px;
  cursor: pointer;
  opacity: 0.85;
}

.files-icon-btn:hover {
  background: var(--vscode-toolbar-hoverBackground, rgba(90, 93, 94, 0.31));
  opacity: 1;
}

.files-change-btn {
  background: transparent;
  color: var(--vscode-textLink-foreground);
  border: none;
  cursor: pointer;
  font-size: 11px;
  padding: 2px 6px;
  text-transform: none;
}

.files-change-btn:hover {
  text-decoration: underline;
}

.files-show-all {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 10px;
  font-size: 11px;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
  user-select: none;
  /* Stick right below the header — z-index just under so the header still
     covers it if the sticky offsets ever overlap. */
  position: sticky;
  top: 24px;
  z-index: 1;
  background: var(--vscode-editor-background);
  border-bottom: 1px solid var(--vscode-widget-border);
}

.files-show-all input {
  margin: 0;
  cursor: pointer;
}

.file-tree {
  padding: 4px 0;
}

.tree-context-menu {
  position: fixed;
  z-index: 1000;
  min-width: 180px;
  padding: 4px 0;
  background: var(--vscode-menu-background, var(--vscode-editor-background));
  color: var(--vscode-menu-foreground, var(--vscode-foreground));
  border: 1px solid var(--vscode-menu-border, var(--vscode-widget-border));
  border-radius: 4px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3);
}

.tree-context-item {
  display: block;
  width: 100%;
  padding: 5px 14px;
  text-align: left;
  background: transparent;
  color: inherit;
  border: none;
  cursor: pointer;
  font-size: 12px;
  font-family: inherit;
}

.tree-context-item:hover {
  background: var(--vscode-menu-selectionBackground, var(--vscode-list-hoverBackground));
  color: var(--vscode-menu-selectionForeground, inherit);
}
</style>
