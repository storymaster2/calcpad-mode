import * as vscode from 'vscode';
import * as path from 'path';
import {
    CalcpadApiClient,
    FindReferencesResponse,
    SymbolLocation,
    buildClientFileCacheFromContent,
} from 'calcpad-frontend';
import { VSCodeLogger, VSCodeFileSystem } from './adapters';

/**
 * Provides "Rename Symbol" (F2) functionality for CalcPad variables and custom functions.
 * Uses the server's find-references endpoint to locate all occurrences,
 * then builds a WorkspaceEdit to replace them all at once.
 */
export class CalcpadRenameProvider implements vscode.RenameProvider {
    private apiClient: CalcpadApiClient;
    private outputChannel: vscode.OutputChannel;
    private logger: VSCodeLogger;
    private fileSystem: VSCodeFileSystem;

    constructor(apiClient: CalcpadApiClient, outputChannel: vscode.OutputChannel) {
        this.apiClient = apiClient;
        this.outputChannel = outputChannel;
        this.logger = new VSCodeLogger(outputChannel);
        this.fileSystem = new VSCodeFileSystem();
    }

    async prepareRename(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken
    ): Promise<vscode.Range | { range: vscode.Range; placeholder: string }> {
        const refs = await this.fetchReferences(document);
        if (!refs) {
            throw new Error('Unable to resolve symbol references (is the CalcPad server running?)');
        }

        const hit = findSymbolAtPosition(position, refs);
        if (!hit) {
            throw new Error('No renameable symbol found at cursor position');
        }

        // Check if any occurrence is local (we can only rename local symbols)
        const localLocations = hit.locations.filter(loc => loc.source === 'local');
        if (localLocations.length === 0) {
            throw new Error(`'${hit.name}' is defined in an include file and cannot be renamed here`);
        }

        const loc = hit.hitLocation;
        const range = new vscode.Range(
            new vscode.Position(loc.line, loc.column),
            new vscode.Position(loc.line, loc.column + loc.length)
        );

        return { range, placeholder: hit.name };
    }

    async provideRenameEdits(
        document: vscode.TextDocument,
        position: vscode.Position,
        newName: string,
        token: vscode.CancellationToken
    ): Promise<vscode.WorkspaceEdit | null> {
        const refs = await this.fetchReferences(document);
        if (!refs) {
            this.outputChannel.appendLine('[Rename] Failed to fetch references');
            return null;
        }

        const hit = findSymbolAtPosition(position, refs);
        if (!hit) {
            this.outputChannel.appendLine('[Rename] No symbol found at cursor position');
            return null;
        }

        if (hit.name === newName) {
            return null;
        }

        this.outputChannel.appendLine(`[Rename] Renaming '${hit.name}' to '${newName}'`);

        // Only rename local occurrences (current file)
        const localLocations = hit.locations.filter(loc => loc.source === 'local');
        this.outputChannel.appendLine(`[Rename] Found ${localLocations.length} local occurrence(s) (${hit.locations.length} total)`);

        const edit = new vscode.WorkspaceEdit();
        for (const loc of localLocations) {
            const range = new vscode.Range(
                new vscode.Position(loc.line, loc.column),
                new vscode.Position(loc.line, loc.column + loc.length)
            );
            edit.replace(document.uri, range, newName);
        }

        return edit;
    }

    private async fetchReferences(document: vscode.TextDocument): Promise<FindReferencesResponse | null> {
        const content = document.getText();
        try {
            const sourceDir = path.dirname(document.uri.fsPath);
            const clientFileCache = await buildClientFileCacheFromContent(
                content, sourceDir, this.fileSystem, this.logger
            );
            return await this.apiClient.findReferences(content, clientFileCache);
        } catch (error) {
            this.outputChannel.appendLine(
                '[Rename] Error fetching references: ' + (error instanceof Error ? error.message : 'Unknown error')
            );
            return null;
        }
    }

    static register(
        apiClient: CalcpadApiClient,
        outputChannel: vscode.OutputChannel
    ): vscode.Disposable {
        const provider = new CalcpadRenameProvider(apiClient, outputChannel);
        return vscode.languages.registerRenameProvider(
            { language: 'calcpad' },
            provider
        );
    }
}

interface SymbolHit {
    name: string;
    locations: SymbolLocation[];
    hitLocation: SymbolLocation;
}

/**
 * Find which symbol the cursor is on by checking actual token positions
 * from the server response, rather than guessing with regex.
 */
function findSymbolAtPosition(position: vscode.Position, refs: FindReferencesResponse): SymbolHit | null {
    const line = position.line;
    const col = position.character;

    for (const [name, locations] of Object.entries(refs.variables)) {
        for (const loc of locations) {
            if (loc.source === 'local' && loc.line === line && col >= loc.column && col < loc.column + loc.length) {
                return { name, locations, hitLocation: loc };
            }
        }
    }

    for (const [name, locations] of Object.entries(refs.functions)) {
        for (const loc of locations) {
            if (loc.source === 'local' && loc.line === line && col >= loc.column && col < loc.column + loc.length) {
                return { name, locations, hitLocation: loc };
            }
        }
    }

    for (const [name, locations] of Object.entries(refs.macros)) {
        for (const loc of locations) {
            if (loc.source === 'local' && loc.line === line && col >= loc.column && col < loc.column + loc.length) {
                return { name, locations, hitLocation: loc };
            }
        }
    }

    return null;
}
