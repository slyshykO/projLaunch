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
            const html = bundle["index.html"] /*as OutputAsset*/;
            const htmlDir = html.fileName.replace(/[^/]*$/, '');
            const imagesAlt = globSync(`{css,public,dist,${htmlDir}}/*.{png,jpeg}`)
            imagesAlt.forEach((img) => {
                var imgR = img.replace(/^.*[\\\/]/, '');
                console.log(`convert \`${img}\` (${imgR}) to base64`)
                const imgData = readFileSync(img)
                html.source = html.source.replaceAll(
                    `./${imgR}`,
                    "data:image/png;base64," + imgData.toString("base64"),
                );
            });
        }
    }
}
