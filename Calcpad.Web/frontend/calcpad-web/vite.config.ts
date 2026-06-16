import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import path from 'path';

const isNeutralinoBuild = process.env.NEUTRALINO_BUILD === '1';

export default defineConfig({
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
                target: process.env.VITE_SERVER_URL || 'http://localhost:9420',
                changeOrigin: true,
            },
        },
    },
});
