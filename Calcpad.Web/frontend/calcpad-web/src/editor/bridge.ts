import type { CalcpadApiClient } from 'calcpad-frontend/api/client';
import type { CalcpadDefinitionsService } from 'calcpad-frontend/services/definitions';
import type { CalcpadSnippetService } from 'calcpad-frontend/services/snippets';
import type { CalcpadSettings } from 'calcpad-frontend/types/settings';

/**
 * Shape both `MessageBridge` (web) and `NeutralinoMessageBridge` (desktop) must satisfy
 * so editor providers can be written once and consume either.
 */
export interface EditorBridge {
    api: CalcpadApiClient;
    snippets: CalcpadSnippetService;
    definitions: CalcpadDefinitionsService;
    getSettings(): CalcpadSettings;
    getExtraSetting(key: string): string | undefined;
    setExtraSetting(key: string, value: string): void;
}

/** Stable key used by editor providers when caching/looking up definitions for the active document. */
export const EDITOR_DOCUMENT_KEY = 'calcpad-editor';
