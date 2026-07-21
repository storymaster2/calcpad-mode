import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';

const isNeutralinoBuild = process.env.NEUTRALINO_BUILD === '1';
// Cloud Run behind Detail Library uses /calcpad/; local + Neutralino stay at /.
const base = isNeutralinoBuild ? '/' : (process.env.VITE_BASE_PATH || '/');

export default defineConfig({
    base,
    plugins: [vue()],
    define: {
        'import.meta.env.VITE_PLATFORM': JSON.stringify('web'),
    },
    resolve: {
        alias: {
            'calcpad-frontend': path.resolve(__dirname, '../calcpad-frontend/src'),
        },
    },
    build: {
        outDir: isNeutralinoBuild
            ? path.resolve(__dirname, '../calcpad-desktop/resources')
            : 'dist',
        emptyOutDir: true,
    },
    server: {
        port: 5173,
        proxy: {
            '/api': {
                target: process.env.VITE_SERVER_URL || 'https://calcpad-server-914029425445.us-west1.run.app',
                changeOrigin: true,
            },
        },
    },
});
