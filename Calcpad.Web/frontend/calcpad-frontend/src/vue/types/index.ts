// Type definitions for CalcpadVuePanel

export interface SnippetParameter {
  name: string
  description?: string
}

export interface InsertItem {
  label?: string
  tag: string
  description?: string
  categoryPath?: string
  category?: string
  quickType?: string
  parameters?: SnippetParameter[]
}

export interface InsertCategory {
  direct?: InsertItem[]
  [key: string]: InsertItem[] | InsertCategory | undefined
}

export interface InsertData {
  [key: string]: InsertCategory
}

export interface Settings {
  math: {
    decimals: number
    degrees: number
    isComplex: boolean
    substitute: boolean
    formatEquations: boolean
    zeroSmallMatrixElements: boolean
    maxOutputCount: number
    formatString: string
  }
  plot: {
    isAdaptive: boolean
    screenScaleFactor: number
    imagePath: string
    imageUri: string
    vectorGraphics: boolean
    colorScale: string
    smoothScale: boolean
    shadows: boolean
    lightDirection: string
  }
  server: {
    url: string
  }
  units: string
}

export interface VariableItem {
  name: string
  definition?: string
  content?: string
  source?: string
  sourceFile?: string
  params?: string
  description?: string
  paramTypes?: string[]
  paramDescriptions?: string[]
  defaults?: (string | null)[]
}

export interface VariablesData {
  macros: VariableItem[]
  variables: VariableItem[]
  functions: VariableItem[]
  customUnits: VariableItem[]
}

export interface Tab {
  id: string
  label: string
  icon?: string
}

export type ThemeKind = 'dark' | 'light'

export interface ThemeInfo {
  label: string
  id: string
  kind: ThemeKind
}

// VS Code message types
export interface VscodeMessage {
  type: string
  [key: string]: any
}

export type { PdfSettings } from '../../types/pdf-settings'
export { DEFAULT_PDF_SETTINGS } from '../../types/pdf-settings'

export interface TocHeading {
  level: number   // 1-6
  text: string    // Heading text content (stripped of markup)
  line: number    // 1-indexed line number in the source document
}

export interface FileNode {
  name: string
  path: string
  isDirectory: boolean
  children?: FileNode[]
  loaded?: boolean
}