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
 * Provides "Go to Definition" functionality for CalcPad functions, macros, and variables.
 * Uses the find-references endpoint to locate the definition (isAssignment === true).
 */
export class CalcpadDefinitionProvider implements vscode.DefinitionProvider {
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

    async provideDefinition(
        document: vscode.TextDocument,
        position: vscode.Position,
        token: vscode.CancellationToken
    ): Promise<vscode.Definition | null> {
        const refs = await this.fetchReferences(document);
        if (!refs) {
            this.outputChannel.appendLine('[Definition] Failed to fetch references');
            return null;
        }

        const hit = findSymbolAtPosition(position, refs);
        if (!hit) {
            this.outputChannel.appendLine('[Definition] No symbol found at cursor position');
            return null;
        }

        this.outputChannel.appendLine('[Definition] Looking for definition of: ' + hit.name);

        // Find the definition (first assignment location)
        const definition = hit.locations.find(loc => loc.isAssignment);
        if (!definition) {
            this.outputChannel.appendLine('[Definition] No definition (assignment) found for: ' + hit.name);
            return null;
        }

        this.outputChannel.appendLine(
            `[Definition] Found definition at line ${definition.line}, col ${definition.column}` +
            (definition.sourceFile ? ` in ${definition.sourceFile}` : '')
        );

        return this.createLocation(document, definition);
    }

    private async createLocation(
        document: vscode.TextDocument,
        loc: SymbolLocation
    ): Promise<vscode.Location> {
        const line = Math.max(0, loc.line);

        if (loc.sourceFile && loc.source !== 'local') {
            const pattern = '**/' + loc.sourceFile;
            const foundFiles = await vscode.workspace.findFiles(pattern, '**/node_modules/**', 1);

            if (foundFiles.length > 0) {
                const targetUri = foundFiles[0];
                this.outputChannel.appendLine('[Definition] Found source file: ' + targetUri.fsPath);
                return new vscode.Location(targetUri, new vscode.Position(line, loc.column));
            } else {
                this.outputChannel.appendLine('[Definition] Source file not found in workspace: ' + loc.sourceFile);
            }
        }

        return new vscode.Location(
            document.uri,
            new vscode.Position(line, loc.column)
        );
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
                '[Definition] Error fetching references: ' + (error instanceof Error ? error.message : 'Unknown error')
            );
            return null;
        }
    }

    static register(
        apiClient: CalcpadApiClient,
        outputChannel: vscode.OutputChannel
    ): vscode.Disposable {
        const provider = new CalcpadDefinitionProvider(apiClient, outputChannel);
        return vscode.languages.registerDefinitionProvider(
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
