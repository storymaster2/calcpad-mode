import { spawn } from 'child_process';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';

const dir = dirname(fileURLToPath(import.meta.url));
const distDir = join(dir, '../dist/calcpad-desktop');
const exe = join(distDir, 'calcpad-desktop-win_x64.exe');

const proc = spawn(exe, process.argv.slice(2), { cwd: distDir, stdio: 'inherit', windowsHide: false });
proc.on('exit', (code) => process.exit(code ?? 0));
