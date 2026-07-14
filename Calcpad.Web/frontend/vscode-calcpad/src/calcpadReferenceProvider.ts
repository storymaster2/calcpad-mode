import * as vscode from 'vscode';
import { CalcpadApiClient, SymbolAtPositionResponse } from 'calcpad-frontend';
import { VSCodeLogger, VSCodeFileSystem } from './adapters';
import { resolveSymbolLocation } from './calcpadLocationResolver';

/**
 * Provides "Find All References" (Shift+F12) functionality for CalcPad
 * variables, custom functions, and macros. Server resolves the cursor to a
 * symbol and returns every occurrence in one round-trip.
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
        const sym = await this.fetchSymbol(document, position);
        if (!sym) {
            this.outputChannel.appendLine('[References] No symbol at cursor position');
            return null;
        }

        this.outputChannel.appendLine('[References] Finding references for: ' + sym.symbolName);

        const filtered = context.includeDeclaration
            ? sym.locations
            : sym.locations.filter(loc => !loc.isAssignment);

        this.outputChannel.appendLine(`[References] Found ${filtered.length} reference(s) (${sym.locations.length} total)`);

        const results: vscode.Location[] = [];
        for (const loc of filtered) {
            const vsLoc = await resolveSymbolLocation(document, loc, this.fileSystem, this.outputChannel, '[References]');
            if (vsLoc) results.push(vsLoc);
        }

        return results;
    }

    private async fetchSymbol(document: vscode.TextDocument, position: vscode.Position): Promise<SymbolAtPositionResponse | null> {
        try {
            return await this.apiClient.symbolAtPosition(
                document.getText(),
                position.line,
                position.character,
                document.uri.fsPath,
            );
        } catch (error) {
            this.outputChannel.appendLine(
                '[References] Error resolving symbol: ' + (error instanceof Error ? error.message : 'Unknown error')
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
