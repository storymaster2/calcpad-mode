<template>
  <div class="metadata-tab">
    <div class="metadata-container p-3">
      <h3 class="section-title">Metadata Comment</h3>

      <p v-if="!block" class="section-desc">
        Place the cursor inside a metadata comment
        (<code>'&lt;!--{ … }--&gt;</code>) above a definition, or right-click a
        line and choose <em>Edit Metadata Properties</em>.
      </p>

      <template v-else>
        <p v-if="!block.valid" class="warning">
          This comment contains invalid JSON. Applying will replace it with the
          values below.
        </p>

        <div class="field">
          <label>Description</label>
          <textarea
            v-model="model.desc"
            rows="2"
            placeholder="What this definition does"
          ></textarea>
        </div>

        <div v-if="showParams" class="field">
          <label>Parameter types</label>
          <div v-for="(_, i) in model.paramTypes" :key="'pt' + i" class="list-row">
            <select v-model="model.paramTypes[i]">
              <option value="">(none)</option>
              <optgroup label="Function">
                <option v-for="t in functionTypes" :key="t" :value="t">{{ t }}</option>
              </optgroup>
              <optgroup label="Macro (TokenType)">
                <option v-for="t in macroTypes" :key="t" :value="t">{{ t }}</option>
              </optgroup>
            </select>
            <button class="icon-button" title="Remove" @click="model.paramTypes.splice(i, 1)">✕</button>
          </div>
          <button class="add-button" @click="model.paramTypes.push('')">+ Add type</button>
        </div>

        <div v-if="showParams" class="field">
          <label>Parameter descriptions</label>
          <div v-for="(_, i) in model.paramDesc" :key="'pd' + i" class="list-row">
            <input type="text" v-model="model.paramDesc[i]" placeholder="Description" />
            <button class="icon-button" title="Remove" @click="model.paramDesc.splice(i, 1)">✕</button>
          </div>
          <button class="add-button" @click="model.paramDesc.push('')">+ Add description</button>
        </div>

        <div class="field">
          <label>Settings overrides</label>
          <div v-for="(row, i) in model.settings" :key="'s' + i" class="list-row">
            <select v-model="row.key" @change="onSettingKeyChange(row)">
              <option value="">(select)</option>
              <option v-for="s in settingKeys" :key="s.key" :value="s.key" :title="s.detail">{{ s.key }}</option>
            </select>
            <select v-if="settingType(row.key) === 'boolean'" v-model="row.value">
              <option value="true">true</option>
              <option value="false">false</option>
            </select>
            <select v-else-if="settingType(row.key) === 'enum'" v-model="row.value">
              <option v-for="opt in settingOptions(row.key)" :key="opt" :value="opt">{{ opt }}</option>
            </select>
            <input
              v-else
              :type="settingType(row.key) === 'number' ? 'number' : 'text'"
              v-model="row.value"
            />
            <button class="icon-button" title="Remove" @click="model.settings.splice(i, 1)">✕</button>
          </div>
          <button class="add-button" @click="model.settings.push({ key: '', value: '' })">+ Add setting</button>
        </div>

        <div class="field">
          <label>Lint ignore</label>

          <div class="sub-row">
            <span class="sub-label">Start region</span>
            <select v-model="model.startLintMode">
              <option value="off">Off</option>
              <option value="all">Ignore all</option>
              <option value="specific">Ignore specific…</option>
            </select>
          </div>
          <select
            v-if="model.startLintMode === 'specific'"
            class="code-multiselect"
            multiple
            size="8"
            v-model="model.startLintCodes"
          >
            <option v-for="c in lintCodes" :key="c.code" :value="c.code" :title="c.description">
              {{ c.code }} — {{ c.description }}
            </option>
          </select>

          <template v-if="showEndLint">
            <div class="sub-row">
              <span class="sub-label">End region</span>
              <select v-model="model.endLintMode">
                <option value="off">Off</option>
                <option value="all">End all</option>
                <option value="specific">End specific…</option>
              </select>
            </div>
            <select
              v-if="model.endLintMode === 'specific'"
              class="code-multiselect"
              multiple
              size="8"
              v-model="model.endLintCodes"
            >
              <option v-for="c in lintCodes" :key="c.code" :value="c.code" :title="c.description">
                {{ c.code }} — {{ c.description }}
              </option>
            </select>
          </template>
        </div>

        <div class="actions">
          <button class="primary-button" @click="onApply">Apply</button>
          <button class="secondary-button" @click="populate">Reset</button>
        </div>
      </template>
    </div>
  </div>
</template>

<script setup lang="ts">
import { reactive, computed, watch } from 'vue'
import {
  FUNCTION_PARAM_TYPES,
  MACRO_PARAM_TYPES,
  METADATA_SETTINGS_KEYS,
  LINT_CODES,
} from '../../text/metadata-comment'
import type { MetadataCommentBlock, MetadataCommentData } from '../../text/metadata-comment'

interface Props {
  block?: MetadataCommentBlock | null
}
const props = withDefaults(defineProps<Props>(), { block: null })

const emit = defineEmits<{
  'apply': [data: MetadataCommentData]
}>()

const functionTypes = FUNCTION_PARAM_TYPES
const macroTypes = MACRO_PARAM_TYPES
const settingKeys = METADATA_SETTINGS_KEYS
const lintCodes = LINT_CODES

type LintMode = 'off' | 'all' | 'specific'

const KNOWN_KEYS = new Set(['desc', 'paramTypes', 'paramDesc', 'settings', 'LintIgnore', 'EndLintIgnore'])

interface SettingRow { key: string; value: string }

const model = reactive({
  desc: '',
  paramTypes: [] as string[],
  paramDesc: [] as string[],
  settings: [] as SettingRow[],
  startLintMode: 'off' as LintMode,
  startLintCodes: [] as string[],
  endLintMode: 'off' as LintMode,
  endLintCodes: [] as string[],
  extra: {} as Record<string, unknown>,
})

// When the host provides no context (non-VS Code), show every field.
const noContext = computed(() => !props.block?.context)

const showParams = computed(() =>
  noContext.value
  || (props.block?.context?.paramCount ?? 0) > 0
  || model.paramTypes.length > 0
  || model.paramDesc.length > 0)

// The End-region control only makes sense inside an open LintIgnore region, or
// when this comment already carries an EndLintIgnore to stay editable.
const showEndLint = computed(() =>
  noContext.value || !!props.block?.context?.insideOpenLintRegion || model.endLintMode !== 'off')

function settingType(key: string): MetadataSettingKind {
  return METADATA_SETTINGS_KEYS.find(s => s.key === key)?.type ?? 'string'
}
type MetadataSettingKind = 'number' | 'boolean' | 'string' | 'enum'

function settingOptions(key: string): string[] {
  return METADATA_SETTINGS_KEYS.find(s => s.key === key)?.options ?? []
}

function onSettingKeyChange(row: SettingRow) {
  const def = METADATA_SETTINGS_KEYS.find(s => s.key === row.key)?.def
  row.value = def === undefined ? '' : String(def)
}

function populate() {
  const data = props.block?.data ?? {}
  model.desc = typeof data.desc === 'string' ? data.desc : ''
  model.paramTypes = Array.isArray(data.paramTypes) ? data.paramTypes.map(String) : []
  model.paramDesc = Array.isArray(data.paramDesc) ? data.paramDesc.map(String) : []
  model.settings = data.settings && typeof data.settings === 'object'
    ? Object.entries(data.settings).map(([key, value]) => ({ key, value: String(value) }))
    : []
  ;[model.startLintMode, model.startLintCodes] = readLintField(data.LintIgnore)
  ;[model.endLintMode, model.endLintCodes] = readLintField(data.EndLintIgnore)
  const extra: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(data)) {
    if (!KNOWN_KEYS.has(key)) extra[key] = value
  }
  model.extra = extra

  // Pre-size the parameter rows to the definition's parameter count so the
  // form matches the signature without the user adding rows by hand.
  const paramCount = props.block?.context?.paramCount ?? 0
  if (paramCount > 0) {
    while (model.paramTypes.length < paramCount) model.paramTypes.push('')
    while (model.paramDesc.length < paramCount) model.paramDesc.push('')
  }
}

// An array value maps to 'all' (empty) or 'specific' (codes); absent → 'off'.
function readLintField(value: unknown): [LintMode, string[]] {
  if (!Array.isArray(value)) return ['off', []]
  return value.length === 0 ? ['all', []] : ['specific', value.map(String)]
}

function lintFieldValue(mode: LintMode, codes: string[]): string[] | undefined {
  if (mode === 'off') return undefined
  if (mode === 'all') return []
  return codes.slice()
}

function coerceSetting(key: string, value: string): string | number | boolean {
  const type = settingType(key)
  if (type === 'boolean') return value === 'true'
  if (type === 'number' || type === 'enum') {
    const n = Number(value)
    return Number.isFinite(n) && value.trim() !== '' ? n : value
  }
  return value
}

function onApply() {
  const data: MetadataCommentData = { ...model.extra }

  if (model.desc.trim()) data.desc = model.desc.trim()

  const types = model.paramTypes.filter(t => t !== '')
  if (types.length) data.paramTypes = types

  const descs = model.paramDesc.filter(d => d.trim() !== '')
  if (descs.length) data.paramDesc = descs

  const settings: Record<string, string | number | boolean> = {}
  for (const row of model.settings) {
    if (row.key) settings[row.key] = coerceSetting(row.key, row.value)
  }
  if (Object.keys(settings).length) data.settings = settings

  const lintIgnore = lintFieldValue(model.startLintMode, model.startLintCodes)
  if (lintIgnore !== undefined) data.LintIgnore = lintIgnore

  const endLintIgnore = lintFieldValue(model.endLintMode, model.endLintCodes)
  if (endLintIgnore !== undefined) data.EndLintIgnore = endLintIgnore

  emit('apply', data)
}

watch(() => props.block, populate, { immediate: true })
</script>

<style scoped>
.metadata-tab {
  height: 100%;
  display: flex;
  flex-direction: column;
}

.metadata-container {
  overflow-y: auto;
  flex: 1;
  padding: 12px;
}

.section-title {
  margin: 0 0 8px 0;
  font-size: 13px;
  font-weight: 600;
  color: var(--vscode-foreground);
}

.section-desc {
  font-size: 12px;
  color: var(--vscode-descriptionForeground);
  margin: 0 0 16px 0;
  line-height: 1.5;
}

.section-desc code {
  background: var(--vscode-textCodeBlock-background);
  padding: 1px 4px;
  border-radius: 2px;
  font-size: 11px;
}

.warning {
  font-size: 12px;
  color: var(--vscode-editorWarning-foreground, #cca700);
  margin: 0 0 12px 0;
  line-height: 1.5;
}

.field {
  margin-bottom: 16px;
}

.field > label {
  display: block;
  font-size: 12px;
  font-weight: 600;
  color: var(--vscode-foreground);
  margin-bottom: 6px;
}

.list-row {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 6px;
}

.sub-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.sub-label {
  min-width: 80px;
  font-size: 12px;
  color: var(--vscode-foreground);
}

.sub-row select {
  flex: 1;
  min-width: 0;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.code-multiselect {
  width: 100%;
  margin-bottom: 8px;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  font-size: 11px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
  padding: 2px;
}

.code-multiselect option {
  padding: 2px 4px;
}

.field textarea,
.field input[type="text"],
.field input[type="number"],
.list-row input,
.list-row select {
  flex: 1;
  min-width: 0;
  background: var(--vscode-input-background);
  color: var(--vscode-input-foreground);
  border: 1px solid var(--vscode-input-border, transparent);
  padding: 4px 6px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  border-radius: 2px;
}

.field textarea {
  width: 100%;
  resize: vertical;
}

.icon-button {
  background: transparent;
  border: none;
  color: var(--vscode-descriptionForeground);
  cursor: pointer;
  padding: 2px 4px;
  font-size: 11px;
  border-radius: 2px;
  flex: 0 0 auto;
}

.icon-button:hover {
  background: var(--vscode-toolbar-hoverBackground);
  color: var(--vscode-foreground);
}

.add-button {
  background: transparent;
  border: 1px dashed var(--vscode-input-border, var(--vscode-widget-border));
  color: var(--vscode-foreground);
  cursor: pointer;
  padding: 3px 8px;
  font-size: 11px;
  border-radius: 2px;
}

.add-button:hover {
  background: var(--vscode-toolbar-hoverBackground);
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
  font-size: 12px;
  color: var(--vscode-foreground);
  margin-top: 8px;
}

.checkbox-label input {
  margin: 0;
}

.actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}

.primary-button {
  background: var(--vscode-button-background);
  color: var(--vscode-button-foreground);
  border: none;
  padding: 6px 14px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  cursor: pointer;
  border-radius: 2px;
}

.primary-button:hover {
  background: var(--vscode-button-hoverBackground);
}

.secondary-button {
  background: var(--vscode-button-secondaryBackground);
  color: var(--vscode-button-secondaryForeground);
  border: none;
  padding: 6px 14px;
  font-size: 12px;
  font-family: var(--vscode-font-family);
  cursor: pointer;
  border-radius: 2px;
}

.secondary-button:hover {
  background: var(--vscode-button-secondaryHoverBackground);
}
</style>
