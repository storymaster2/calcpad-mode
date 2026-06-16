import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { execFile, execSync } from 'child_process';

const DOTNET_MAJOR_VERSION = '10';
const RELEASES_JSON_URL = `https://builds.dotnet.microsoft.com/dotnet/release-metadata/${DOTNET_MAJOR_VERSION}.0/releases.json`;
const DOTNET_DOWNLOAD_PAGE = `https://dotnet.microsoft.com/en-us/download/dotnet/${DOTNET_MAJOR_VERSION}.0`;
const RUNTIME_DIR_NAME = 'dotnet-runtime';

type DotnetStatus = 'ready' | 'wrong-version' | 'not-found';

interface PlatformRuntimeInfo {
    rid: string;
    archiveExt: string;
}

function getPlatformRuntimeInfo(): PlatformRuntimeInfo | null {
    const platform = process.platform;
    const arch = process.arch;

    if (platform === 'win32' && arch === 'x64') {
        return { rid: 'win-x64', archiveExt: 'zip' };
    } else if (platform === 'win32' && arch === 'arm64') {
        return { rid: 'win-arm64', archiveExt: 'zip' };
    } else if (platform === 'linux' && arch === 'x64') {
        return { rid: 'linux-x64', archiveExt: 'tar.gz' };
    } else if (platform === 'darwin' && arch === 'x64') {
        return { rid: 'osx-x64', archiveExt: 'tar.gz' };
    } else if (platform === 'darwin' && arch === 'arm64') {
        return { rid: 'osx-arm64', archiveExt: 'tar.gz' };
    }

    return null;
}

export class DotnetRuntimeManager {
    private outputChannel: vscode.OutputChannel;

    constructor(outputChannel: vscode.OutputChannel) {
        this.outputChannel = outputChannel;
    }

    /**
     * Check if the system dotnet has the required ASP.NET Core runtime.
     */
    public checkSystemDotnet(dotnetPath: string): Promise<DotnetStatus> {
        return new Promise((resolve) => {
            execFile(dotnetPath, ['--list-runtimes'], { timeout: 10000 }, (error, stdout) => {
                if (error) {
                    this.log(`System dotnet not found at '${dotnetPath}': ${error.message}`);
                    resolve('not-found');
                    return;
                }

                const lines = stdout.toString().split('\n');
                const hasAspNetCore = lines.some(line =>
                    line.startsWith(`Microsoft.AspNetCore.App ${DOTNET_MAJOR_VERSION}.`)
                );

                if (hasAspNetCore) {
                    this.log(`System dotnet has ASP.NET Core ${DOTNET_MAJOR_VERSION}.x`);
                    resolve('ready');
                } else {
                    this.log(`System dotnet found but lacks ASP.NET Core ${DOTNET_MAJOR_VERSION}.x. Installed runtimes:\n${stdout}`);
                    resolve('wrong-version');
                }
            });
        });
    }

    /**
     * Check if a locally-installed runtime exists and return the dotnet path.
     */
    public getLocalDotnetPath(storagePath: string): string | null {
        const exe = process.platform === 'win32' ? 'dotnet.exe' : 'dotnet';
        const dotnetPath = path.join(storagePath, RUNTIME_DIR_NAME, exe);

        if (fs.existsSync(dotnetPath)) {
            this.log(`Local runtime found at ${dotnetPath}`);
            return dotnetPath;
        }

        return null;
    }

    /**
     * Prompt the user to install .NET or download it manually.
     */
    public async promptUserForInstallation(status: DotnetStatus): Promise<'install-local' | 'download' | 'cancel'> {
        const message = status === 'not-found'
            ? `CalcPad requires the .NET ${DOTNET_MAJOR_VERSION}.0 runtime to run calculations locally. It was not found on your system.`
            : `CalcPad requires the ASP.NET Core ${DOTNET_MAJOR_VERSION}.0 runtime, but only older versions were found on your system.`;

        const choice = await vscode.window.showWarningMessage(
            message,
            'Install Locally',
            'Download .NET'
        );

        if (choice === 'Install Locally') {
            return 'install-local';
        } else if (choice === 'Download .NET') {
            return 'download';
        }
        return 'cancel';
    }

    /**
     * Open the .NET download page in the user's browser.
     */
    public openDownloadPage(): void {
        vscode.env.openExternal(vscode.Uri.parse(DOTNET_DOWNLOAD_PAGE));
    }

    /**
     * Download and install the ASP.NET Core runtime locally into the global storage directory.
     * Returns the path to the dotnet executable.
     */
    public async installRuntimeLocally(storagePath: string): Promise<string> {
        const platformInfo = getPlatformRuntimeInfo();
        if (!platformInfo) {
            throw new Error(`Unsupported platform: ${process.platform}-${process.arch}`);
        }

        // Ensure storage directory exists
        fs.mkdirSync(storagePath, { recursive: true });

        const runtimeDir = path.join(storagePath, RUNTIME_DIR_NAME);
        const downloadUrl = await this.resolveDownloadUrl(platformInfo);

        this.log(`Downloading ASP.NET Core runtime from ${downloadUrl}`);

        await vscode.window.withProgress({
            location: vscode.ProgressLocation.Notification,
            title: 'CalcPad: Installing .NET runtime...',
            cancellable: false
        }, async (progress) => {
            progress.report({ message: 'Downloading runtime...' });

            const response = await fetch(downloadUrl, { redirect: 'follow' });
            if (!response.ok) {
                throw new Error(`Failed to download runtime: HTTP ${response.status}`);
            }

            const arrayBuffer = await response.arrayBuffer();
            const buffer = Buffer.from(arrayBuffer);
            const sizeMB = (buffer.length / 1024 / 1024).toFixed(1);
            this.log(`Downloaded ${sizeMB} MB`);

            progress.report({ message: 'Extracting runtime...' });

            const tmpDir = path.join(storagePath, '.tmp-dotnet-download');
            fs.mkdirSync(tmpDir, { recursive: true });

            try {
                const archivePath = path.join(tmpDir, `runtime.${platformInfo.archiveExt}`);
                fs.writeFileSync(archivePath, buffer);

                // Ensure clean target directory
                if (fs.existsSync(runtimeDir)) {
                    fs.rmSync(runtimeDir, { recursive: true, force: true });
                }
                fs.mkdirSync(runtimeDir, { recursive: true });

                if (platformInfo.archiveExt === 'zip') {
                    execSync(
                        `powershell -NoProfile -Command "Expand-Archive -Path '${archivePath}' -DestinationPath '${runtimeDir}' -Force"`,
                        { timeout: 120000 }
                    );
                } else {
                    execSync(`tar xzf "${archivePath}" -C "${runtimeDir}"`, { timeout: 120000 });
                }

                // Set executable permission on Unix
                if (process.platform !== 'win32') {
                    const dotnetExe = path.join(runtimeDir, 'dotnet');
                    if (fs.existsSync(dotnetExe)) {
                        fs.chmodSync(dotnetExe, 0o755);
                    }
                }

                progress.report({ message: 'Done!' });
                this.log(`Runtime installed to ${runtimeDir}`);
            } finally {
                // Clean up temp directory
                try {
                    fs.rmSync(tmpDir, { recursive: true, force: true });
                } catch {
                    this.log('Warning: Could not clean up temp directory');
                }
            }
        });

        const exe = process.platform === 'win32' ? 'dotnet.exe' : 'dotnet';
        const dotnetPath = path.join(runtimeDir, exe);

        if (!fs.existsSync(dotnetPath)) {
            throw new Error(`Runtime installation failed: ${dotnetPath} not found after extraction`);
        }

        return dotnetPath;
    }

    /**
     * Resolve the full flow: check local, check system, prompt user, install if needed.
     * Returns the dotnet path to use, or null if server should not be started.
     */
    public async resolveDotnetPath(
        storagePath: string,
        configuredDotnetPath: string,
        serverMode: string
    ): Promise<string | null> {
        // 1. Check for existing local runtime installation
        const localPath = this.getLocalDotnetPath(storagePath);
        if (localPath) {
            return localPath;
        }

        // 2. Check system dotnet
        const status = await this.checkSystemDotnet(configuredDotnetPath);
        if (status === 'ready') {
            return configuredDotnetPath;
        }

        // 3. Prompt user for installation
        const choice = await this.promptUserForInstallation(status);

        if (choice === 'install-local') {
            try {
                return await this.installRuntimeLocally(storagePath);
            } catch (err) {
                const message = err instanceof Error ? err.message : String(err);
                this.log(`Failed to install runtime locally: ${message}`);
                vscode.window.showErrorMessage(`CalcPad: Failed to install .NET runtime: ${message}`);
                return null;
            }
        } else if (choice === 'download') {
            this.openDownloadPage();
            if (serverMode === 'local') {
                vscode.window.showInformationMessage(
                    'CalcPad: Please restart VS Code after installing the .NET runtime.'
                );
            }
            return null;
        }

        // User cancelled
        return null;
    }

    /**
     * Fetch the releases JSON and find the download URL for the current platform.
     */
    private async resolveDownloadUrl(platformInfo: PlatformRuntimeInfo): Promise<string> {
        this.log(`Fetching release metadata from ${RELEASES_JSON_URL}`);

        const response = await fetch(RELEASES_JSON_URL);
        if (!response.ok) {
            throw new Error(`Failed to fetch .NET release metadata: HTTP ${response.status}`);
        }

        const data = await response.json() as {
            releases: Array<{
                'aspnetcore-runtime': {
                    version: string;
                    files: Array<{
                        rid: string;
                        url: string;
                        name: string;
                    }>;
                };
            }>;
        };

        // First release is the latest
        const latestRelease = data.releases?.[0];
        if (!latestRelease) {
            throw new Error('No releases found in .NET release metadata');
        }

        const runtime = latestRelease['aspnetcore-runtime'];
        if (!runtime?.files) {
            throw new Error('No aspnetcore-runtime files in latest release');
        }

        // Find the archive file for our platform (not the installer .exe/.pkg)
        const expectedName = `aspnetcore-runtime-${runtime.version}-${platformInfo.rid}.${platformInfo.archiveExt}`;
        const file = runtime.files.find(f => f.name === expectedName);

        if (!file) {
            // Fallback: match by RID and extension
            const fallback = runtime.files.find(f =>
                f.rid === platformInfo.rid && f.name.endsWith(`.${platformInfo.archiveExt}`)
            );
            if (fallback) {
                return fallback.url;
            }
            throw new Error(`No ASP.NET Core runtime download found for ${platformInfo.rid} (.${platformInfo.archiveExt})`);
        }

        this.log(`Resolved download: ${file.name} (${runtime.version})`);
        return file.url;
    }

    private log(message: string): void {
        this.outputChannel.appendLine(`[DotnetRuntime] ${message}`);
    }
}
