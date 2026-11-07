// Construit le processus principal, le pont (preload) et l'interface dans dist/.
import { build } from "esbuild";
import { copyFileSync, mkdirSync } from "node:fs";

const prod = process.argv.includes("--prod");
const common = { bundle: true, sourcemap: !prod, minify: prod, logLevel: "warning" };

await Promise.all([
  build({ ...common, entryPoints: { app: "src/main/app.ts", preload: "src/main/preload.ts", "sync-cli": "src/main/sync-cli.ts" },
    outdir: "dist", outExtension: { ".js": ".cjs" }, platform: "node", format: "cjs", external: ["electron", "koffi"] }),
  build({ ...common, entryPoints: { app: "src/renderer/main.tsx", widget: "src/renderer/widget.tsx" }, outdir: "dist/renderer", platform: "browser", format: "iife",
    jsx: "automatic", define: { "process.env.NODE_ENV": JSON.stringify(prod ? "production" : "development") } }),
]);
mkdirSync("dist/renderer", { recursive: true });
copyFileSync("src/renderer/index.html", "dist/renderer/index.html");
copyFileSync("src/renderer/styles.css", "dist/renderer/styles.css");
copyFileSync("src/renderer/widget.html", "dist/renderer/widget.html");
copyFileSync("src/renderer/widget.css", "dist/renderer/widget.css");
console.log("build ok");
