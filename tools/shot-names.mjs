// The app's screenshots on the website under names that say what they show, for image search and the AIs that take their
// pictures from it (27 Nov. 2026: Bing Images had none of ours; ChatGPT put another app's picture under our name).
// docs/screenshots/<page>.png stays the one file tools/shots.mjs makes. The Pages workflow publishes it under both names:
// img/<page>.png (the app tour and old links use it) and img/<named>.png, each with its WebP. The pages use the named one.
//
//   node tools/shot-names.mjs <folder>   copies <folder>/<page>.png to <folder>/<named>.png for every screenshot
import { copyFileSync, existsSync } from 'node:fs';
import { join, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

export const SHOT_NAMES = {
  connection: 'pickroute-cs2-mobile-data-hide-ip',
  servers: 'pickroute-cs2-community-servers',
  accounts: 'pickroute-steam-account-switcher',
  regions: 'pickroute-cs2-server-picker-regions',
  configs: 'pickroute-copy-cs2-config',
  profiles: 'pickroute-steam-profile-editor',
};

// What each screenshot is, in the words people search with. It opens the screenshot's alt on every page (the home page and
// the guides), so the name and the topic come first; the translations are in tools/i18n/<lang>.json like any other text.
export const SHOT_TOPIC = {
  connection: "Pickroute, CS2 over your phone's mobile data.",
  servers: 'Pickroute, your CS2 community servers.',
  accounts: 'Pickroute, the Steam account switcher.',
  regions: 'Pickroute, the CS2 server picker.',
  configs: 'Pickroute, copy CS2 settings between accounts.',
  profiles: 'Pickroute, the Steam profile editor.',
};
export const shotAlt = (page, alt) => `${SHOT_TOPIC[page]} ${alt}`;

// The name a page uses for a screenshot of the app.
export const shotFile = (page) => {
  const n = SHOT_NAMES[page];
  if (!n) throw new Error(`shot-names: no published name for the ${page} screenshot`);
  return n;
};

// The file in docs/screenshots behind a published image name (the local test servers use it): "pickroute-...-regions.webp"
// gives "regions.png"; any other name is itself, with .webp read as .png.
export const shotSource = (name) => {
  const b = basename(name).replace(/\.webp$/, '.png');
  const page = Object.keys(SHOT_NAMES).find((k) => `${SHOT_NAMES[k]}.png` === b);
  return page ? `${page}.png` : b;
};

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const dir = process.argv[2];
  if (!dir) { console.error('usage: node tools/shot-names.mjs <folder>'); process.exit(2); }
  for (const [page, named] of Object.entries(SHOT_NAMES)) {
    const from = join(dir, `${page}.png`);
    if (!existsSync(from)) { console.error(`shot-names: ${from} is missing`); process.exit(1); }
    copyFileSync(from, join(dir, `${named}.png`));
  }
  console.log(`shot-names: ${Object.keys(SHOT_NAMES).length} screenshots copied under their names in ${dir}`);
}
