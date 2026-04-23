// API response types matching the server schema
export interface SnippetParameterDto {
    name: string;
    description?: string;
}

export interface SnippetDto {
    insert: string;
    description: string;
    label?: string;
    category: string;
    quickType?: string;
    keywordType?: string;
    parameters?: SnippetParameterDto[];
}

export interface SnippetsResponse {
    count: number;
    snippets: SnippetDto[];
}

// Internal InsertItem used throughout the extension and desktop app
export interface InsertItem {
    tag: string;
    label?: string;
    description: string;
    categoryPath?: string;
    quickType?: string;
    keywordType?: string;
    parameters?: SnippetParameterDto[];
}

// Hierarchical structure for tree view
export interface InsertDataTree {
    [category: string]: InsertDataTree | InsertItem[];
}

export type SnippetsLoadedCallback = () => void;
