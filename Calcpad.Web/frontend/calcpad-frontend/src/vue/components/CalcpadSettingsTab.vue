<template>
  <div class="settings-tab">
    <div class="settings-container">
      <h3>Math Settings</h3>
      <div class="setting-group">
        <label for="decimals">Decimals:</label>
        <input
          id="decimals"
          v-model.number="localSettings.math.decimals"
          type="number"
          min="0"
          max="15"
          @input="updateSettings"
        />
      </div>

      <div class="setting-group">
        <label for="degrees">Angle Units:</label>
        <select
          id="degrees"
          v-model.number="localSettings.math.degrees"
          @change="updateSettings"
        >
          <option :value="0">Radians</option>
          <option :value="1">Degrees</option>
          <option :value="2">Gradians</option>
        </select>
      </div>

      <div class="setting-group">
        <label>
          <input
            v-model="localSettings.math.isComplex"
            type="checkbox"
            @change="updateSettings"
          />
          Complex Numbers
        </label>
      </div>

      <div class="setting-group">
        <label>
          <input
            v-model="localSettings.math.substitute"
            type="checkbox"
            @change="updateSettings"
          />
          Substitute Variables
        </label>
      </div>

      <div class="setting-group">
        <label>
          <input
            v-model="localSettings.math.formatEquations"
            type="checkbox"
            @change="updateSettings"
          />
          Format Equations
        </label>
      </div>

      <h3>Plot Settings</h3>
      <div class="setting-group">
        <label>
          <input
            v-model="localSettings.plot.isAdaptive"
            type="checkbox"
            @change="updateSettings"
          />
          Adaptive Plotting
        </label>
      </div>

      <div class="setting-group">
        <label for="screenScaleFactor">Screen Scale Factor:</label>
        <input
          id="screenScaleFactor"
          v-model.number="localSettings.plot.screenScaleFactor"
          type="number"
          min="0.1"
          max="5"
          step="0.1"
          @input="updateSettings"
        />
      </div>

      <div class="setting-group">
        <label for="imagePath">Image Path:</label>
        <input
          id="imagePath"
          v-model="localSettings.plot.imagePath"
          type="text"
          @input="updateSettings"
        />
      </div>

      <div class="setting-group">
        <label>
          <input
            v-model="localSettings.plot.vectorGraphics"
            type="checkbox"
            @change="updateSettings"
          />
          Vector Graphics
        </label>
      </div>

      <div class="setting-group">
        <label for="colorScale">Color Scale:</label>
        <select
          id="colorScale"
          v-model="localSettings.plot.colorScale"
          @change="updateSettings"
        >
          <option value="Rainbow">Rainbow</option>
          <option value="Grayscale">Grayscale</option>
          <option value="Hot">Hot</option>
          <option value="Cool">Cool</option>
          <option value="Jet">Jet</option>
          <option value="Parula">Parula</option>
        </select>
      </div>

      <div class="setting-group">
        <label>
          <input
            v-model="localSettings.plot.smoothScale"
            type="checkbox"
            @change="updateSettings"
          />
          Smooth Scale
        </label>
      </div>

      <div class="setting-group">
        <label>
          <input
            v-model="localSettings.plot.shadows"
            type="checkbox"
            @change="updateSettings"
          />
          Shadows
        </label>
      </div>

      <div class="setting-group">
        <label for="lightDirection">Light Direction:</label>
        <select
          id="lightDirection"
          v-model="localSettings.plot.lightDirection"
          @change="updateSettings"
        >
          <option value="NorthWest">NorthWest</option>
          <option value="North">North</option>
          <option value="NorthEast">NorthEast</option>
          <option value="West">West</option>
          <option value="East">East</option>
          <option value="SouthWest">SouthWest</option>
          <option value="South">South</option>
          <option value="SouthEast">SouthEast</option>
        </select>
      </div>

      <h3>Units</h3>
      <div class="setting-group">
        <label for="units">Units System:</label>
        <select
          id="units"
          v-model="localSettings.units"
          @change="updateSettings"
        >
          <option value="SI">SI (International System)</option>
          <option value="Imperial">Imperial</option>
          <option value="US">US Customary</option>
        </select>
      </div>

      <h3>Server Settings</h3>
      <div class="setting-group">
        <label for="serverUrl">Remote Server URL:</label>
        <input
          id="serverUrl"
          v-model="localSettings.server.url"
          type="text"
          @input="updateSettings"
        />
      </div>

      <h3>Preview Theme</h3>
      <div class="setting-group">
        <label for="previewTheme">Theme:</label>
        <select
          id="previewTheme"
          v-model="previewTheme"
          @change="updatePreviewTheme"
        >
          <option value="system">System</option>
          <option value="light">Light</option>
          <option value="dark">Dark</option>
        </select>
      </div>

      <div class="setting-group">
        <label for="darkBackground">Dark Mode Background:</label>
        <div class="color-input-row">
          <input
            id="darkBackground"
            v-model="darkBackground"
            type="text"
            placeholder="#1e1e1e"
            @input="updateDarkBackground"
          />
          <button
            class="reset-inline-btn"
            title="Reset to default (#1e1e1e)"
            @click="resetDarkBackground"
          >
            Reset
          </button>
        </div>
      </div>

      <h3>Color Theme</h3>
      <div class="setting-group">
        <label for="colorTheme">Color Theme:</label>
        <select
          id="colorTheme"
          v-model="colorTheme"
          @change="updateColorTheme"
        >
          <option
            v-if="colorTheme && !knownThemeLabels.has(colorTheme) && colorTheme !== 'System'"
            :value="colorTheme"
          >{{ colorTheme }}</option>
          <option value="System">System</option>
          <optgroup v-if="darkThemes.length" label="Dark">
            <option
              v-for="t in darkThemes"
              :key="t.label"
              :value="t.label"
            >{{ t.label }}</option>
          </optgroup>
          <optgroup v-if="lightThemes.length" label="Light">
            <option
              v-for="t in lightThemes"
              :key="t.label"
              :value="t.label"
            >{{ t.label }}</option>
          </optgroup>
        </select>
      </div>

      <h3>Editor Features</h3>
      <div class="setting-group">
        <label>
          <input
            v-model="enableQuickTyping"
            type="checkbox"
            @change="updateQuickTyping"
          />
          Enable Quick Typing
          <span class="setting-info" title="Type shortcuts like ~a → α, ~' → ′">ⓘ</span>
        </label>
      </div>

      <div class="setting-group">
        <label for="commentFormat">Comment Format:</label>
        <select
          id="commentFormat"
          v-model="commentFormat"
          @change="updateCommentFormat"
        >
          <option value="auto">Auto (detect #md on/off)</option>
          <option value="html">HTML</option>
          <option value="markdown">Markdown</option>
        </select>
      </div>

      <div class="setting-group">
        <label>
          <input
            v-model="enableFormattingHotkeys"
            type="checkbox"
            @change="updateFormattingHotkeys"
          />
          Enable Formatting Hotkeys
          <span class="setting-info" title="Ctrl+B for bold, Ctrl+I for italic, etc.">ⓘ</span>
        </label>
      </div>

      <div class="setting-group">
        <label>
          <input
            v-model="enablePreviewCursorSync"
            type="checkbox"
            @change="updatePreviewCursorSync"
          />
          Sync Preview to Cursor Line
          <span class="setting-info" title="Scroll the preview to follow the line the cursor is on in the editor.">ⓘ</span>
        </label>
      </div>

      <h3>Library</h3>
      <div class="setting-group">
        <label for="libraryPath">
          Library Path:
          <span class="setting-info" title="Shared .cpd/.txt files for #include autocomplete. Supports %ENV% variables.">ⓘ</span>
        </label>
        <input
          id="libraryPath"
          v-model="libraryPath"
          type="text"
          placeholder="%USERPROFILE%\Documents\CalcpadLibrary"
          @input="updateLibraryPath"
        />
      </div>

      <h3>Linter</h3>
      <div class="setting-group">
        <label for="linterMinSeverity">Minimum Severity:</label>
        <select
          id="linterMinSeverity"
          v-model="linterMinSeverity"
          @change="updateLinterMinSeverity"
        >
          <option value="error">Error</option>
          <option value="warning">Warning</option>
          <option value="information">Information (all)</option>
        </select>
      </div>

      <h3>Diagnostics</h3>
      <div class="setting-group">
        <button
          class="diagnostics-button"
          title="Opens the folder containing server logs and the most recent crash dump."
          @click="openLogsFolder"
        >
          Open Logs Folder
        </button>
      </div>

      <div v-if="versionConfig.isWebOrDesktop" class="setting-group">
        <label for="maxOutputLines">
          Max Output Lines (per channel):
          <span class="setting-info" title="Lines retained in each Output channel before older lines are dropped. Lower values reduce memory use and improve responsiveness when logs are noisy.">ⓘ</span>
        </label>
        <input
          id="maxOutputLines"
          v-model.number="maxOutputLines"
          type="number"
          min="10"
          max="100000"
          step="100"
          @change="updateMaxOutputLines"
        />
      </div>

      <h3>Configuration</h3>
      <div class="setting-group">
        <label for="activeConfig">Active Config:</label>
        <select
          id="activeConfig"
          :value="activeConfig"
          @change="switchConfig(($event.target as HTMLSelectElement).value)"
        >
          <option
            v-for="name in availableConfigs"
            :key="name"
            :value="name"
          >{{ name }}</option>
        </select>
      </div>

      <div class="setting-group">
        <label for="configName">Save current settings as:</label>
        <div class="color-input-row">
          <input
            id="configName"
            v-model="newConfigName"
            type="text"
            placeholder="e.g. my-config"
            @keyup.enter="saveNamedConfig"
          />
          <button
            class="reset-inline-btn"
            :disabled="!newConfigName.trim()"
            @click="saveNamedConfig"
          >
            Save
          </button>
        </div>
        <span v-if="saveError" class="setting-error">{{ saveError }}</span>
      </div>

      <div class="settings-actions">
        <button @click="openSettingsFolder" class="reset-button">
          Open Settings Folder
        </button>
        <button @click="resetSettings" class="reset-button">
          Reset to Default
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, computed } from 'vue'
import type { Settings, ThemeInfo, VersionConfig } from '../types'
import { DEFAULT_VERSION_CONFIG } from '../types'

// Props
interface Props {
  settings?: Settings
  initialPreviewTheme?: string
  initialColorTheme?: string
  initialAvailableThemes?: ThemeInfo[]
  initialEnableQuickTyping?: boolean
  initialCommentFormat?: string
  initialEnableFormattingHotkeys?: boolean
  initialEnablePreviewCursorSync?: boolean
  initialDarkBackground?: string
  initialLinterMinSeverity?: string
  initialMaxOutputLines?: number
  versionConfig?: VersionConfig
  initialLibraryPath?: string
  initialActiveConfig?: string
  initialAvailableConfigs?: string[]
}

const props = withDefaults(defineProps<Props>(), {
  settings: () => ({
    math: {
      decimals: 2,
      degrees: 0,
      isComplex: false,
      substitute: true,
      formatEquations: true,
      zeroSmallMatrixElements: true,
      maxOutputCount: 20,
      formatString: ''
    },
    plot: {
      isAdaptive: true,
      screenScaleFactor: 2.0,
      imagePath: '',
      imageUri: '',
      vectorGraphics: false,
      colorScale: 'Rainbow',
      smoothScale: false,
      shadows: true,
      lightDirection: 'NorthWest'
    },
    server: {
      url: ''
    },
    units: 'm'
  }),
  initialPreviewTheme: 'system',
  initialColorTheme: '',
  initialAvailableThemes: () => [],
  initialEnableQuickTyping: true,
  initialCommentFormat: 'auto',
  initialEnableFormattingHotkeys: true,
  initialEnablePreviewCursorSync: false,
  initialDarkBackground: '#1a1a2e',
  initialLinterMinSeverity: 'information',
  initialMaxOutputLines: 1000,
  versionConfig: () => ({ ...DEFAULT_VERSION_CONFIG }),
  initialLibraryPath: '',
  initialActiveConfig: 'default',
  initialAvailableConfigs: () => ['default']
})

// Emits
const emit = defineEmits<{
  updateSettings: [settings: Settings]
  updatePreviewTheme: [theme: string]
  updateColorTheme: [theme: string]
  updateQuickTyping: [enabled: boolean]
  updateCommentFormat: [format: string]
  updateFormattingHotkeys: [enabled: boolean]
  updatePreviewCursorSync: [enabled: boolean]
  updateDarkBackground: [color: string]
  updateLinterMinSeverity: [severity: string]
  updateMaxOutputLines: [value: number]
  updateLibraryPath: [path: string]
  resetSettings: []
  saveNamedConfig: [name: string]
  switchConfig: [name: string]
  openSettingsFolder: []
  openLogsFolder: []
}>()

// State
const localSettings = ref<Settings>({ ...props.settings })
const previewTheme = ref(props.initialPreviewTheme)
const colorTheme = ref(props.initialColorTheme)
const availableThemes = ref<ThemeInfo[]>(props.initialAvailableThemes)

const darkThemes = computed(() => availableThemes.value.filter(t => t.kind === 'dark'))
const lightThemes = computed(() => availableThemes.value.filter(t => t.kind === 'light'))
const knownThemeLabels = computed(() => new Set(availableThemes.value.map(t => t.label)))
const enableQuickTyping = ref(props.initialEnableQuickTyping)
const commentFormat = ref(props.initialCommentFormat)
const enableFormattingHotkeys = ref(props.initialEnableFormattingHotkeys)
const enablePreviewCursorSync = ref(props.initialEnablePreviewCursorSync)
const darkBackground = ref(props.initialDarkBackground)
const linterMinSeverity = ref(props.initialLinterMinSeverity)
const maxOutputLines = ref(props.initialMaxOutputLines)
const libraryPath = ref(props.initialLibraryPath)
const activeConfig = ref(props.initialActiveConfig)
const availableConfigs = ref<string[]>(props.initialAvailableConfigs)
const newConfigName = ref('')
const saveError = ref('')

// Methods
const updateSettings = () => {
  emit('updateSettings', localSettings.value)
}

const updatePreviewTheme = () => {
  emit('updatePreviewTheme', previewTheme.value)
}

const updateColorTheme = () => {
  emit('updateColorTheme', colorTheme.value)
}

const updateQuickTyping = () => {
  emit('updateQuickTyping', enableQuickTyping.value)
}

const updateCommentFormat = () => {
  emit('updateCommentFormat', commentFormat.value)
}

const updateFormattingHotkeys = () => {
  emit('updateFormattingHotkeys', enableFormattingHotkeys.value)
}

const updatePreviewCursorSync = () => {
  emit('updatePreviewCursorSync', enablePreviewCursorSync.value)
}

const updateDarkBackground = () => {
  emit('updateDarkBackground', darkBackground.value)
}

const resetDarkBackground = () => {
  darkBackground.value = '#1e1e1e'
  updateDarkBackground()
}

const updateLinterMinSeverity = () => {
  emit('updateLinterMinSeverity', linterMinSeverity.value)
}

const updateMaxOutputLines = () => {
  const n = Number(maxOutputLines.value)
  if (!Number.isFinite(n) || n < 10) return
  emit('updateMaxOutputLines', Math.floor(n))
}

const updateLibraryPath = () => {
  emit('updateLibraryPath', libraryPath.value)
}

const resetSettings = () => {
  emit('resetSettings')
}

const saveNamedConfig = () => {
  const name = newConfigName.value.trim()
  if (!name) return
  if (name.toLowerCase() === 'default') {
    saveError.value = 'The "default" config is protected and cannot be overridden.'
    return
  }
  saveError.value = ''
  emit('saveNamedConfig', name)
  newConfigName.value = ''
}

const switchConfig = (name: string) => {
  if (!name) return
  emit('switchConfig', name)
}

const openSettingsFolder = () => {
  emit('openSettingsFolder')
}

const openLogsFolder = () => {
  emit('openLogsFolder')
}

// Watch for prop changes
watch(
  () => props.settings,
  (newSettings) => {
    if (newSettings) {
      localSettings.value = { ...newSettings }
    }
  },
  { deep: true }
)

watch(
  () => props.initialPreviewTheme,
  (newTheme) => {
    previewTheme.value = newTheme
  }
)

watch(
  () => props.initialColorTheme,
  (newValue) => {
    colorTheme.value = newValue
  }
)

watch(
  () => props.initialAvailableThemes,
  (newValue) => {
    availableThemes.value = newValue
  }
)

watch(
  () => props.initialEnableQuickTyping,
  (newValue) => {
    enableQuickTyping.value = newValue
  }
)

watch(
  () => props.initialCommentFormat,
  (newValue) => {
    commentFormat.value = newValue
  }
)

watch(
  () => props.initialEnableFormattingHotkeys,
  (newValue) => {
    enableFormattingHotkeys.value = newValue
  }
)

watch(
  () => props.initialEnablePreviewCursorSync,
  (newValue) => {
    enablePreviewCursorSync.value = newValue
  }
)

watch(
  () => props.initialDarkBackground,
  (newValue) => {
    darkBackground.value = newValue
  }
)

watch(
  () => props.initialLinterMinSeverity,
  (newValue) => {
    linterMinSeverity.value = newValue
  }
)

watch(
  () => props.initialMaxOutputLines,
  (newValue) => {
    maxOutputLines.value = newValue
  }
)

watch(
  () => props.initialLibraryPath,
  (newValue) => {
    libraryPath.value = newValue
  }
)

watch(
  () => props.initialActiveConfig,
  (newValue) => {
    activeConfig.value = newValue
  }
)

watch(
  () => props.initialAvailableConfigs,
  (newValue) => {
    availableConfigs.value = newValue
  }
)

</script>

<style scoped>
.settings-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.settings-container {
  padding: 12px;
  overflow-y: auto;
  height: 100%;
}

.settings-container h3 {
  margin: 16px 0 8px 0;
  color: var(--vscode-sideBarSectionHeader-foreground);
  font-size: 13px;
  font-weight: bold;
  border-bottom: 1px solid var(--vscode-panel-border);
  padding-bottom: 4px;
}

.settings-container h3:first-child {
  margin-top: 0;
}

.setting-group {
  margin-bottom: 12px;
}

.setting-group label {
  display: block;
  margin-bottom: 4px;
  font-size: 12px;
  color: var(--vscode-input-foreground);
  font-weight: normal;
}

.setting-group input[type="number"],
.setting-group input[type="text"],
.setting-group select {
  width: 100%;
  padding: 6px 8px;
  background: var(--vscode-input-background);
  border: 1px solid var(--vscode-input-border);
  color: var(--vscode-input-foreground);
  border-radius: 3px;
  font-size: 12px;
}

.setting-group input[type="checkbox"] {
  margin-right: 8px;
  background: var(--vscode-checkbox-background);
  border: 1px solid var(--vscode-checkbox-border);
}

.setting-group label:has(input[type="checkbox"]) {
  display: flex;
  align-items: center;
  cursor: pointer;
}

.color-input-row {
  display: flex;
  gap: 6px;
  align-items: center;
}

.color-input-row input[type="text"] {
  flex: 1;
}

.reset-inline-btn {
  padding: 6px 10px;
  background: var(--vscode-button-secondaryBackground);
  border: 1px solid var(--vscode-button-border);
  color: var(--vscode-button-secondaryForeground);
  border-radius: 3px;
  cursor: pointer;
  font-size: 11px;
  white-space: nowrap;
}

.reset-inline-btn:hover {
  background: var(--vscode-button-secondaryHoverBackground);
}

.setting-info {
  margin-left: 4px;
  font-size: 11px;
  color: var(--vscode-descriptionForeground);
  cursor: help;
}

.setting-error {
  display: block;
  margin-top: 4px;
  font-size: 11px;
  color: var(--vscode-errorForeground, #f48771);
}

.reset-inline-btn[disabled] {
  opacity: 0.5;
  cursor: not-allowed;
}

.settings-actions {
  display: flex;
  gap: 8px;
  margin-top: 16px;
}

.settings-actions .reset-button {
  flex: 1;
  margin-top: 0;
}

.reset-button {
  width: 100%;
  padding: 8px;
  background: var(--vscode-button-secondaryBackground);
  border: 1px solid var(--vscode-button-border);
  color: var(--vscode-button-secondaryForeground);
  border-radius: 3px;
  cursor: pointer;
  font-size: 12px;
  margin-top: 16px;
}

.reset-button:hover {
  background: var(--vscode-button-secondaryHoverBackground);
}

.diagnostics-button {
  width: 100%;
  padding: 6px 10px;
  background: var(--vscode-button-secondaryBackground);
  border: 1px solid var(--vscode-button-border);
  color: var(--vscode-button-secondaryForeground);
  border-radius: 3px;
  cursor: pointer;
  font-size: 12px;
}

.diagnostics-button:hover {
  background: var(--vscode-button-secondaryHoverBackground);
}
</style>
