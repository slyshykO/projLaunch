import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { viteSingleFile } from 'vite-plugin-singlefile'
import { readFileSync } from 'node:fs'
import { globSync } from 'glob'
import tailwindcss from '@tailwindcss/vite';

const host = process.env.TAURI_DEV_HOST;

export default defineConfig({
    //plugins: [react(), viteTest(), viteSingleFile(), compression()],
    plugins: [tailwindcss(), react(), viteTest(), viteSingleFile()],
    root: "./src",
    build: {
        outDir: "../dist",
    },

    //for tauri
    clearScreen: false,
    server: {
        // Tauri expects a fixed port, fail if that port is not available
        strictPort: true,
        // if the host Tauri is expecting is set, use it
        host: host || false,
        port: 5173,
    },
    // Env variables starting with the item of `envPrefix` will be exposed in tauri's source code through `import.meta.env`.
    envPrefix: ['VITE_', 'TAURI_ENV_*'],
})

function viteTest() {
    return {
        name: 'vite-test',
        apply: 'build',
        enforce: 'post',
        generateBundle(_, bundle) {
            console.log('changing favicon with base64...')
            console.log(`work dir: ${process.cwd()}`);
            const html = bundle["index.html"] /*as OutputAsset*/;
            const htmlDir = html.fileName.replace(/[^/]*$/, '');
            console.log(`html: ${html.fileName}, htmlDir: ${htmlDir}`);
            console.log(`{css,public,dist${htmlDir ? `,${htmlDir}` : ''}}/*.{png,jpeg}`);
            const imageAssets = Object.values(bundle).filter(
                asset => asset.type === 'asset' && /\.(png|jpe?g)$/i.test(asset.fileName)
            );
            imageAssets.forEach((asset) => {
                const imgR = asset.fileName.replace(/^.*[\\\/]/, '');
                console.log(`convert \`${asset.fileName}\` (${imgR}) to base64`);
                const imgData = Buffer.from(asset.source);
                html.source = html.source.replaceAll(
                    `./${imgR}`,
                    "data:image/png;base64," + imgData.toString("base64"),
                );
            });
        }
    }
}
