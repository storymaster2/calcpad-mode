<template>
  <div class="pdf-tab">
    <div class="pdf-container p-3">
      <div class="pdf-actions">
        <button class="generate-pdf-btn" @click="generatePdf">
          Generate PDF
        </button>
        <button class="reset-btn" @click="resetPdfSettings">
          Reset Settings
        </button>
      </div>

      <div class="pdf-settings">
        <!-- Header Settings -->
        <div class="settings-section">
          <div class="section-header" @click="toggleSection('header')">
            <span>Header</span>
            <span class="expand-icon" :class="{ collapsed: collapsedSections.header }">&#x25BC;</span>
          </div>
          <div class="section-content" :class="{ collapsed: collapsedSections.header }">
            <div class="setting-item">
              <label>Document Title:</label>
              <input
                type="text"
                v-model="localSettings.documentTitle"
                @input="updateSettings"
                placeholder="Document title (defaults to file name)"
              >
            </div>
            <div class="setting-item">
              <label>Timestamp Format:</label>
              <input
                type="text"
                v-model="localSettings.dateTimeFormat"
                @input="updateSettings"
                placeholder="M/d/yyyy h:mm tt"
              >
              <span class="setting-hint">e.g. M/d/yyyy h:mm tt, yyyy-MM-dd HH:mm</span>
            </div>
          </div>
        </div>

        <!-- Page Layout -->
        <div class="settings-section">
          <div class="section-header" @click="toggleSection('layout')">
            <span>Page Layout</span>
            <span class="expand-icon" :class="{ collapsed: collapsedSections.layout }">&#x25BC;</span>
          </div>
          <div class="section-content" :class="{ collapsed: collapsedSections.layout }">
            <div class="setting-item">
              <label>Page Size:</label>
              <select v-model="localSettings.format" @change="updateSettings">
                <option value="Letter">Letter (8.5 x 11 in)</option>
              </select>
            </div>
            <div class="margins-grid">
              <div class="setting-item">
                <label>Top Margin:</label>
                <input
                  type="text"
                  v-model="localSettings.marginTop"
                  @input="updateSettings"
                  placeholder="0.75in"
                >
              </div>
              <div class="setting-item">
                <label>Bottom Margin:</label>
                <input
                  type="text"
                  v-model="localSettings.marginBottom"
                  @input="updateSettings"
                  placeholder="0.75in"
                >
              </div>
              <div class="setting-item">
                <label>Left Margin:</label>
                <input
                  type="text"
                  v-model="localSettings.marginLeft"
                  @input="updateSettings"
                  placeholder="0.5in"
                >
              </div>
              <div class="setting-item">
                <label>Right Margin:</label>
                <input
                  type="text"
                  v-model="localSettings.marginRight"
                  @input="updateSettings"
                  placeholder="0.5in"
                >
              </div>
            </div>
          </div>
        </div>

      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, watch } from 'vue'
import type { PdfSettings } from '../types'
import { DEFAULT_PDF_SETTINGS } from '../types'

// Props
interface Props {
  pdfSettings?: PdfSettings
}

const props = withDefaults(defineProps<Props>(), {
  pdfSettings: () => ({ ...DEFAULT_PDF_SETTINGS })
})

// Emits
const emit = defineEmits<{
  updatePdfSettings: [settings: PdfSettings]
  resetPdfSettings: []
  generatePdf: []
}>()

// State
const localSettings = reactive<PdfSettings>({ ...props.pdfSettings })

const collapsedSections = ref({
  header: false,
  layout: false
})

// Methods
const toggleSection = (section: keyof typeof collapsedSections.value) => {
  collapsedSections.value[section] = !collapsedSections.value[section]
}

const updateSettings = () => {
  emit('updatePdfSettings', { ...localSettings })
}

const resetPdfSettings = () => {
  emit('resetPdfSettings')
}

const generatePdf = () => {
  emit('generatePdf')
}

// Watch for external changes to pdfSettings
watch(() => props.pdfSettings, (newSettings) => {
  if (newSettings) {
    Object.assign(localSettings, newSettings)
  }
}, { deep: true, immediate: true })
</script>

<style scoped>
.pdf-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.pdf-container {
  overflow-y: auto;
  height: 100%;
}

.pdf-actions {
  display: flex;
  gap: 12px;
  margin-bottom: 20px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--vscode-widget-border);
}

.generate-pdf-btn {
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  padding: 8px 16px;
  border-radius: 2px;
  cursor: pointer;
  font-size: 13px;
  font-weight: 500;
}

.generate-pdf-btn:hover {
  background: var(--vscode-button-hoverBackground);
}

.reset-btn {
  background: var(--vscode-button-secondaryBackground);
  color: var(--vscode-button-secondaryForeground);
  border: 1px solid var(--vscode-button-border);
  padding: 8px 16px;
  border-radius: 2px;
  cursor: pointer;
  font-size: 13px;
}

.reset-btn:hover {
  background: var(--vscode-button-secondaryHoverBackground);
}

.pdf-settings {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.settings-section {
  border: 1px solid var(--vscode-panel-border);
  border-radius: 4px;
}

.section-header {
  background: var(--vscode-sideBar-background);
  padding: 12px;
  cursor: pointer;
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-weight: 600;
  transition: background-color 0.2s ease;
  border-radius: 4px 4px 0 0;
}

.section-header:hover {
  background: var(--vscode-list-hoverBackground);
}

.expand-icon {
  transition: transform 0.2s;
  font-size: 12px;
}

.expand-icon.collapsed {
  transform: rotate(-90deg);
}

.section-content {
  padding: 16px;
  transition: max-height 0.3s ease;
}

.section-content.collapsed {
  display: none;
}

.setting-item {
  margin-bottom: 12px;
}

.setting-item:last-child {
  margin-bottom: 0;
}

.setting-item label {
  display: block;
  margin-bottom: 4px;
  font-size: 12px;
  font-weight: 500;
  color: var(--vscode-foreground);
}

.setting-item input[type="text"],
.setting-item input[type="number"],
.setting-item select {
  width: 100%;
  padding: 6px 8px;
  border: 1px solid var(--vscode-input-border);
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border-radius: 2px;
  font-size: 13px;
}

.setting-item input[type="text"]:focus,
.setting-item input[type="number"]:focus,
.setting-item select:focus {
  outline: none;
  border-color: var(--vscode-focusBorder);
}

.setting-hint {
  display: block;
  margin-top: 4px;
  font-size: 11px;
  color: var(--vscode-descriptionForeground);
}

.margins-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.margins-grid .setting-item {
  margin-bottom: 0;
}
</style>
