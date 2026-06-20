<template>
  <div class="calcpad-vue-ui">
    <div class="tab-container">
      <!-- TODO: Remove v-show condition after Files feature is fully developed -->
      <button
        v-for="tab in tabs"
        :key="tab.id"
        v-show="tab.id !== 'files'"
        :class="['tab', { active: activeTab === tab.id }]"
        @click="switchTab(tab.id)"
      >
        {{ tab.label }}
      </button>
    </div>

    <div class="tab-content">
      <CalcpadInsertTab
        v-if="activeTab === 'insert'"
        :insert-items="insertItems"
        @insert-text="handleInsertText"
        @insert-image="handleInsertImage"
      />
      <CalcpadTocTab
        v-else-if="activeTab === 'toc'"
        :headings="tocHeadings"
        :loading="tocLoading"
        @go-to-line="handleGoToLine"
      />
      <CalcpadSettingsTab
        v-else-if="activeTab === 'settings'"
        :settings="settings"
        :initial-preview-theme="previewTheme"
        :initial-color-theme="colorTheme"
        :initial-available-themes="availableThemes"
        :initial-enable-quick-typing="enableQuickTyping"
        :initial-comment-format="commentFormat"
        :initial-enable-formatting-hotkeys="enableFormattingHotkeys"
        :initial-dark-background="darkBackground"
        :initial-linter-min-severity="linterMinSeverity"
        :initial-library-path="libraryPath"
        @update-settings="handleUpdateSettings"
        @update-preview-theme="handleUpdatePreviewTheme"
        @update-color-theme="handleUpdateColorTheme"
        @update-quick-typing="handleUpdateQuickTyping"
        @update-comment-format="handleUpdateCommentFormat"
        @update-formatting-hotkeys="handleUpdateFormattingHotkeys"
        @update-dark-background="handleUpdateDarkBackground"
        @update-linter-min-severity="handleUpdateLinterMinSeverity"
        @update-library-path="handleUpdateLibraryPath"
        @reset-settings="handleResetSettings"
        @open-logs-folder="handleOpenLogsFolder"
      />
      <CalcpadVariablesTab
        v-else-if="activeTab === 'variables'"
        :variables-data="variablesData"
        :loading="variablesLoading"
        @insert-text="handleInsertText"
      />
      <CalcpadFilesTab
        v-else-if="activeTab === 'files'"
      />
      <CalcpadPdfTab
        v-else-if="activeTab === 'pdf'"
        :pdf-settings="pdfSettings"
        @update-pdf-settings="handleUpdatePdfSettings"
        @reset-pdf-settings="handleResetPdfSettings"
        @generate-pdf="handleGeneratePdf"
      />
      <CalcpadFormattingTab
        v-else-if="activeTab === 'formatting'"
        :indent-style="prettifyIndentStyle"
        :indent-size="prettifyIndentSize"
        :trim-trailing-whitespace="prettifyTrimTrailing"
        @prettify="handlePrettify"
        @update-indent-style="handleUpdatePrettifyIndentStyle"
        @update-indent-size="handleUpdatePrettifyIndentSize"
        @update-trim-trailing="handleUpdatePrettifyTrim"
      />
      <CalcpadExportTab
        v-else-if="activeTab === 'export'"
        :exports="exportEntries"
        @download="handleDownloadExport"
        @download-zip="handleDownloadExportZip"
        @refresh="requestExports"
        @save-html="handleSaveSourceHtml"
        @save-docx="handleSaveDocx"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import CalcpadInsertTab from './CalcpadInsertTab.vue'
import CalcpadTocTab from './CalcpadTocTab.vue'
import CalcpadSettingsTab from './CalcpadSettingsTab.vue'
import CalcpadVariablesTab from './CalcpadVariablesTab.vue'
import CalcpadFilesTab from './CalcpadFilesTab.vue'
import CalcpadPdfTab from './CalcpadPdfTab.vue'
import CalcpadFormattingTab from './CalcpadFormattingTab.vue'
import CalcpadExportTab from './CalcpadExportTab.vue'
import { postMessage } from '../services/messaging'
import type { Tab, InsertItem, Settings, VariablesData, PdfSettings, TocHeading, ThemeInfo } from '../types'
import type { ExportMeta } from '../../types/api'
import { DEFAULT_PDF_SETTINGS } from '../types'

// State
const activeTab = ref('insert')
const insertItems = ref<InsertItem[]>([])
const settings = ref<Settings>()
const previewTheme = ref('system')
const colorTheme = ref('')
const availableThemes = ref<ThemeInfo[]>([])
const enableQuickTyping = ref(true)
const commentFormat = ref('auto')
const enableFormattingHotkeys = ref(true)
const darkBackground = ref('#1e1e1e')
const linterMinSeverity = ref('information')
const libraryPath = ref('')
const variablesData = ref<VariablesData>({
  macros: [],
  variables: [],
  functions: [],
  customUnits: []
})
const variablesLoading = ref(false)
const tocHeadings = ref<TocHeading[]>([])
const tocLoading = ref(false)
const pdfSettings = ref<PdfSettings>({ ...DEFAULT_PDF_SETTINGS })
const prettifyIndentStyle = ref<'tab' | 'space'>('tab')
const prettifyIndentSize = ref(4)
const prettifyTrimTrailing = ref(true)

const tabs: Tab[] = [
  { id: 'insert', label: 'Insert' },
  { id: 'toc', label: 'TOC' },
  { id: 'settings', label: 'Settings' },
  { id: 'variables', label: 'Variables' },
  { id: 'files', label: 'Files' },
  { id: 'pdf', label: 'PDF' },
  { id: 'formatting', label: 'Formatting' },
  { id: 'export', label: 'Export' }
]

const exportEntries = ref<ExportMeta[]>([])

// Methods
const switchTab = (tabId: string) => {
  activeTab.value = tabId

  // Request fresh data when switching to variables tab
  if (tabId === 'variables') {
    variablesLoading.value = true
    postMessage({ type: 'getVariables' })
  }

  // Request headings when switching to TOC tab
  if (tabId === 'toc') {
    tocLoading.value = true
    postMessage({ type: 'getHeadings' })
  }

  // Request prettify settings when switching to Formatting tab
  if (tabId === 'formatting') {
    postMessage({ type: 'getPrettifySettings' })
  }

  // Request export list when switching to Export tab
  if (tabId === 'export') {
    requestExports()
  }
}

const requestExports = () => {
  postMessage({ type: 'getExports' })
}

const handleDownloadExport = (filename: string) => {
  postMessage({ type: 'downloadExport', filename })
}

const handleDownloadExportZip = () => {
  postMessage({ type: 'downloadExportZip' })
}

const handleSaveSourceHtml = () => {
  postMessage({ type: 'saveSourceHtml' })
}

const handleSaveDocx = () => {
  postMessage({ type: 'saveDocx' })
}

const handleInsertText = (text: string) => {
  postMessage({
    type: 'insertText',
    text
  })
}

const handleInsertImage = () => {
  postMessage({ type: 'insertImage' })
}

const handleUpdateSettings = (newSettings: Settings) => {
  settings.value = { ...newSettings }
  postMessage({
    type: 'updateSettings',
    settings: newSettings
  })
}

const handleUpdatePreviewTheme = (theme: string) => {
  previewTheme.value = theme
  postMessage({ type: 'updatePreviewTheme', theme })
}

const handleUpdateColorTheme = (theme: string) => {
  colorTheme.value = theme
  postMessage({ type: 'updateColorTheme', theme })
}

const handleUpdateQuickTyping = (enabled: boolean) => {
  enableQuickTyping.value = enabled
  postMessage({ type: 'updateQuickTyping', enabled })
}

const handleUpdateCommentFormat = (format: string) => {
  commentFormat.value = format
  postMessage({ type: 'updateCommentFormat', format })
}

const handleUpdateFormattingHotkeys = (enabled: boolean) => {
  enableFormattingHotkeys.value = enabled
  postMessage({ type: 'updateFormattingHotkeys', enabled })
}

const handleUpdateDarkBackground = (color: string) => {
  darkBackground.value = color
  postMessage({ type: 'updateDarkBackground', color })
}

const handleUpdateLinterMinSeverity = (severity: string) => {
  linterMinSeverity.value = severity
  postMessage({ type: 'updateLinterMinSeverity', severity })
}

const handleUpdateLibraryPath = (path: string) => {
  libraryPath.value = path
  postMessage({ type: 'updateLibraryPath', path })
}

const handleResetSettings = () => {
  postMessage({
    type: 'resetSettings'
  })
}

const handleOpenLogsFolder = () => {
  postMessage({
    type: 'openLogsFolder'
  })
}

const handleUpdatePdfSettings = (settings: PdfSettings) => {
  postMessage({
    type: 'updatePdfSettings',
    settings
  })
}

const handleResetPdfSettings = () => {
  postMessage({
    type: 'resetPdfSettings'
  })
}

const handleGeneratePdf = () => {
  postMessage({
    type: 'generatePdf'
  })
}

const handleGoToLine = (line: number) => {
  postMessage({
    type: 'goToLine',
    line
  })
}

const handlePrettify = () => {
  postMessage({ type: 'prettifyDocument' })
}

const handleUpdatePrettifyIndentStyle = (style: 'tab' | 'space') => {
  prettifyIndentStyle.value = style
  postMessage({ type: 'updatePrettifyIndentStyle', value: style })
}

const handleUpdatePrettifyIndentSize = (size: number) => {
  prettifyIndentSize.value = size
  postMessage({ type: 'updatePrettifyIndentSize', value: size })
}

const handleUpdatePrettifyTrim = (enabled: boolean) => {
  prettifyTrimTrailing.value = enabled
  postMessage({ type: 'updatePrettifyTrim', value: enabled })
}

// Message handler
const handleMessage = (event: MessageEvent) => {
  const message = event.data

  switch (message.type) {
    case 'insertDataResponse':
      insertItems.value = message.items || []
      break
    case 'settingsResponse':
      settings.value = message.settings
      previewTheme.value = message.previewTheme || 'system'
      colorTheme.value = message.colorTheme || ''
      availableThemes.value = message.availableThemes || []
      commentFormat.value = message.commentFormat || 'auto'
      enableFormattingHotkeys.value = message.enableFormattingHotkeys !== false
      darkBackground.value = message.darkBackground || '#1e1e1e'
      linterMinSeverity.value = message.linterMinSeverity || 'information'
      libraryPath.value = message.libraryPath || ''
      break
    case 'settingsReset':
      settings.value = message.settings
      break
    case 'updateVariables':
      variablesData.value = message.data
      variablesLoading.value = false
      break
    case 'updateHeadings':
      tocHeadings.value = message.headings || []
      tocLoading.value = false
      break
    case 'pdfSettingsResponse':
      pdfSettings.value = message.settings
      break
    case 'pdfSettingsReset':
      pdfSettings.value = message.settings
      break
    case 'prettifySettingsResponse':
      prettifyIndentStyle.value = message.indentStyle === 'space' ? 'space' : 'tab'
      prettifyIndentSize.value = typeof message.indentSize === 'number' ? message.indentSize : 4
      prettifyTrimTrailing.value = message.trimTrailingWhitespace !== false
      break
    case 'exportsResponse':
      exportEntries.value = message.exports || []
      break
  }
}

defineExpose({ switchTab })

// Initialize
onMounted(() => {
  // Listen for messages from host
  window.addEventListener('message', handleMessage)

  // Request initial data
  postMessage({ type: 'getInsertData' })
  postMessage({ type: 'getSettings' })
  postMessage({ type: 'getPdfSettings' })

  // Debug message
  postMessage({
    type: 'debug',
    message: 'CalcpadVueApp mounted successfully'
  })
})
</script>

<style scoped>
.calcpad-vue-ui {
  /* Natural document flow inside the parent (#vue-sidebar in calcpad-desktop,
   * <body> in the VS Code webview). Avoids flex-column edge cases where a
   * wrapped .tab-container can render on top of .tab-content because the
   * column's fixed height was computed before the wrap layout pass. */
  height: 100%;
  display: block;
  font-family: var(--vscode-font-family);
  font-size: var(--vscode-font-size);
  color: var(--vscode-foreground);
  background: var(--vscode-editor-background);
}

.tab-container {
  display: flex;
  flex-wrap: wrap;
  /* Sticky so the tab strip stays visible while .tab-content scrolls. */
  position: sticky;
  top: 0;
  z-index: 1;
  border-bottom: 1px solid var(--vscode-widget-border);
  background: var(--vscode-editor-background);
}

.tab {
  padding: 8px 12px;
  border: none;
  background: transparent;
  color: var(--vscode-tab-inactiveForeground);
  cursor: pointer;
  font-size: 11px;
  font-weight: normal;
  border-radius: 0;
  transition: all 0.2s ease;
}

.tab:hover {
  background: var(--vscode-tab-hoverBackground);
  color: var(--vscode-tab-activeForeground);
}

.tab.active {
  background: var(--vscode-tab-activeBackground);
  color: var(--vscode-tab-activeForeground);
  border-bottom: 2px solid var(--vscode-tab-activeBorder);
}

.tab-content {
  /* Natural-flow content area — grows with its content. The parent
   * (#vue-sidebar) handles overflow scrolling for the whole panel. */
  padding: 0;
}

.tab-placeholder {
  padding: 20px;
  text-align: center;
  color: var(--vscode-descriptionForeground);
  font-style: italic;
}
</style>
