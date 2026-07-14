import * as vscode from 'vscode';
import { CalcpadApiClient, SymbolAtPositionResponse } from 'calcpad-frontend';
import { VSCodeLogger, VSCodeFileSystem } from './adapters';
import { resolveSymbolLocation } from './calcpadLocationResolver';

/**
 * Provides "Go to Definition" functionality for CalcPad functions, macros, and variables.
 * Asks the server which symbol is at the cursor (`symbol-at-position`) and
 * jumps to its first assignment location.
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
        const sym = await this.fetchSymbol(document, position);
        if (!sym) {
            this.outputChannel.appendLine('[Definition] No symbol at cursor position');
            return null;
        }

        this.outputChannel.appendLine('[Definition] Looking for definition of: ' + sym.symbolName);

        const definition = sym.locations.find(loc => loc.isAssignment);
        if (!definition) {
            this.outputChannel.appendLine('[Definition] No definition (assignment) found for: ' + sym.symbolName);
            return null;
        }

        this.outputChannel.appendLine(
            `[Definition] Found definition at line ${definition.line}, col ${definition.column}` +
            (definition.sourceFile ? ` in ${definition.sourceFile}` : '')
        );

        return resolveSymbolLocation(document, definition, this.fileSystem, this.outputChannel, '[Definition]');
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
                '[Definition] Error resolving symbol: ' + (error instanceof Error ? error.message : 'Unknown error')
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
