import type { VariableDefinition } from 'calcpad-frontend';

/**
 * In-memory model for UI input field values.
 * Stores user-entered override values and provides them
 * for re-submission to the convert-ui endpoint.
 */
export interface UiInputEntry {
    variableName: string;
    currentValue: string;
    sourceLine: number;
}

export class UiInputModel {
    private entries: Map<string, UiInputEntry> = new Map();

    /** Update or add a value from a user input change */
    setValue(varName: string, value: string, sourceLine: number): void {
        this.entries.set(varName, {
            variableName: varName,
            currentValue: value,
            sourceLine
        });
    }

    /** Get all override values to send to convert-ui endpoint */
    getOverrides(): Record<string, string> {
        const overrides: Record<string, string> = {};
        for (const [varName, entry] of this.entries) {
            overrides[varName] = entry.currentValue;
        }
        return overrides;
    }

    /** Check if there are any overrides */
    hasOverrides(): boolean {
        return this.entries.size > 0;
    }

    /** Clear all entries (e.g., when source file changes) */
    clear(): void {
        this.entries.clear();
    }

    /**
     * Load overrides from a persisted HTML comment, resolving source lines
     * from variable definitions returned by the definitions API.
     * Replaces all entries (used on initial load).
     */
    loadFromPersisted(overrides: Record<string, string>, variables: VariableDefinition[]): void {
        this.entries.clear();
        const varMap = new Map(variables.map(v => [v.name, v.lineNumber]));
        for (const [varName, value] of Object.entries(overrides)) {
            this.entries.set(varName, {
                variableName: varName,
                currentValue: value,
                sourceLine: varMap.get(varName) ?? -1
            });
        }
    }

    /**
     * Merge persisted overrides without replacing existing in-memory values.
     * Only adds entries for variables that don't already have a user-set value.
     * Updates source line numbers for all known variables.
     */
    mergeFromPersisted(overrides: Record<string, string>, variables: VariableDefinition[]): void {
        const varMap = new Map(variables.map(v => [v.name, v.lineNumber]));

        // Update source lines for existing entries
        for (const [varName, entry] of this.entries) {
            const line = varMap.get(varName);
            if (line !== undefined) {
                entry.sourceLine = line;
            }
        }

        // Add persisted values only for variables not already in memory
        for (const [varName, value] of Object.entries(overrides)) {
            if (!this.entries.has(varName)) {
                this.entries.set(varName, {
                    variableName: varName,
                    currentValue: value,
                    sourceLine: varMap.get(varName) ?? -1
                });
            }
        }
    }
}
