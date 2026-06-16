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
 * Provides "Find All References" (Shift+F12) functionality for CalcPad
 * variables, custom functions, and macros.
 * Uses the server's find-references endpoint to locate all occurrences.
 */
export class CalcpadReferenceProvider implements vscode.ReferenceProvider {
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

    async provideReferences(
        document: vscode.TextDocument,
        position: vscode.Position,
        context: vscode.ReferenceContext,
        token: vscode.CancellationToken
    ): Promise<vscode.Location[] | null> {
        const refs = await this.fetchReferences(document);
        if (!refs) {
            this.outputChannel.appendLine('[References] Failed to fetch references');
            return null;
        }

        const hit = findSymbolAtPosition(position, refs);
        if (!hit) {
            this.outputChannel.appendLine('[References] No symbol found at cursor position');
            return null;
        }

        this.outputChannel.appendLine('[References] Finding references for: ' + hit.name);

        // Filter based on context.includeDeclaration
        const filtered = context.includeDeclaration
            ? hit.locations
            : hit.locations.filter(loc => !loc.isAssignment);

        this.outputChannel.appendLine(`[References] Found ${filtered.length} reference(s) (${hit.locations.length} total)`);

        const results: vscode.Location[] = [];
        for (const loc of filtered) {
            const vsLoc = await this.createLocation(document, loc);
            results.push(vsLoc);
        }

        return results;
    }

    private async createLocation(
        document: vscode.TextDocument,
        loc: SymbolLocation
    ): Promise<vscode.Location> {
        const line = Math.max(0, loc.line);
        const range = new vscode.Range(
            new vscode.Position(line, loc.column),
            new vscode.Position(line, loc.column + loc.length)
        );

        if (loc.sourceFile && loc.source !== 'local') {
            const pattern = '**/' + loc.sourceFile;
            const foundFiles = await vscode.workspace.findFiles(pattern, '**/node_modules/**', 1);

            if (foundFiles.length > 0) {
                return new vscode.Location(foundFiles[0], range);
            }
        }

        return new vscode.Location(document.uri, range);
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
                '[References] Error fetching references: ' + (error instanceof Error ? error.message : 'Unknown error')
            );
            return null;
        }
    }

    static register(
        apiClient: CalcpadApiClient,
        outputChannel: vscode.OutputChannel
    ): vscode.Disposable {
        const provider = new CalcpadReferenceProvider(apiClient, outputChannel);
        return vscode.languages.registerReferenceProvider(
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
 * from the server response.
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
