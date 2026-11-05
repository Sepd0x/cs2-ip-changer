// The home's 3D world (world.js, proposal A's scene taken further); it sets window.WorldScene.
// The earlier cover.js and route.js are kept in publish/site-proposta-a-2026-11-05/antigos/.
//
// Build: in any folder, npm i three@0.186.1 esbuild, then from the repo root:
//   NODE_PATH=<that folder>/node_modules npx esbuild site/assets/src/scenes.js --bundle --minify --format=iife --target=es2020 --legal-comments=none --outfile=site/assets/js/scenes.min.js
import './world.js';
