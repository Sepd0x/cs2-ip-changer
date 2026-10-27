// Generates the README's SVG pieces in docs/assets (a light and a dark version of each where it matters).
// Run with: node docs/assets/src/components.mjs
// Colors come from src/Theme.xaml so the README looks like the app.
import fs from 'node:fs';

const out = new URL('../', import.meta.url);
const FONT = `'Segoe UI Variable Text','Segoe UI',-apple-system,BlinkMacSystemFont,'Helvetica Neue',Arial,sans-serif`;
const MONO = `'Cascadia Mono',Consolas,'SFMono-Regular',Menlo,monospace`;

const themes = {
  dark: {
    bg: '#121212', panel: '#1D1D1D', node: '#242424', line: '#3A3A3A', strong: '#4A4A4A',
    text: '#E6E6E6', muted: '#9B9B9B', faint: '#6E6E6E',
    accent: '#5BA8F0', good: '#7CC95A', bad: '#F05A4F',
  },
  light: {
    bg: '#FFFFFF', panel: '#F6F8FA', node: '#FFFFFF', line: '#D0D7DE', strong: '#B6BEC6',
    text: '#1F2328', muted: '#59636E', faint: '#8C959F',
    accent: '#2B74C2', good: '#2DA44E', bad: '#CF222E',
  },
};

const esc = (s) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const write = (name, svg) => fs.writeFileSync(new URL(name, out), svg.replace(/\n\s*/g, '\n'));

// ------------------------------------------------------------------ logo (graphite tile, blue shield with a phone cut out of it)
const SHIELD = 'M60 74 L128 50 L196 74 L196 150 L128 214 L60 150 Z';
const logo = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">
<defs>
<linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2A2A2A"/><stop offset="1" stop-color="#141414"/></linearGradient>
<mask id="shield"><path d="${SHIELD}" fill="#fff"/><rect x="104" y="84" width="48" height="84" rx="9" fill="#000"/><rect x="118" y="92" width="20" height="5" rx="2.5" fill="#fff"/></mask>
</defs>
<rect x="8" y="8" width="240" height="240" rx="56" fill="url(#tile)" stroke="#3A3A3A" stroke-width="2"/>
<rect width="256" height="256" fill="#5BA8F0" mask="url(#shield)"/>
</svg>`;
write('logo.svg', logo);

// ------------------------------------------------------------------ download button (works on both themes)
write('download.svg', `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 264 46" width="264" height="46" role="img" aria-label="Download for Windows">
<rect width="264" height="46" rx="6" fill="#5BA8F0"/>
<g fill="none" stroke="#0B0B0B" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
<path d="M30 13 V27 M24.5 21.5 L30 27 L35.5 21.5"/><path d="M23 32 H37"/>
</g>
<text x="50" y="28.5" font-family="${FONT}" font-size="16" font-weight="600" fill="#0B0B0B">Download for Windows</text>
</svg>`);

// ------------------------------------------------------------------ how it works (animated route)
function route(t) {
  const node = (x, y, w, title, sub, opts = {}) => `
<g>
<rect x="${x}" y="${y}" width="${w}" height="62" rx="9" fill="${t.node}" stroke="${opts.stroke || t.line}" stroke-width="${opts.stroke ? 1.5 : 1}"/>
<text x="${x + 18}" y="${y + 27}" font-family="${FONT}" font-size="16" font-weight="600" fill="${t.text}">${esc(title)}</text>
<text x="${x + 18}" y="${y + 47}" font-family="${opts.mono ? MONO : FONT}" font-size="13" fill="${opts.subColor || t.muted}">${esc(sub)}</text>
</g>`;
  const pkt = (path, begin) => `<circle class="pkt" r="4" fill="${t.accent}"><animateMotion dur="1.4s" begin="${begin}" repeatCount="indefinite" path="${path}"/></circle>`;
  const A = 'M206 62 H352';
  const B = 'M548 62 H694';
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 900 250" width="900" height="250" role="img" aria-label="Your PC sends CS2 and Steam over the USB cable to the phone, which uses mobile data to reach the server. The server sees the carrier's IP. Your home router still carries the browser, Discord and downloads, but it is blocked for CS2 and your servers.">
<style>@media (prefers-reduced-motion: reduce) { .pkt { display: none } }</style>
<rect width="900" height="250" rx="12" fill="${t.panel}" stroke="${t.line}"/>
<path d="${A}" stroke="${t.accent}" stroke-width="2"/>
<path d="${B}" stroke="${t.accent}" stroke-width="2"/>
<text x="279" y="50" text-anchor="middle" font-family="${FONT}" font-size="12.5" fill="${t.muted}">USB or hotspot</text>
<text x="621" y="50" text-anchor="middle" font-family="${FONT}" font-size="12.5" fill="${t.muted}">4G / 5G</text>
${pkt(A, '0s')}${pkt(A, '-0.7s')}${pkt(B, '-0.35s')}${pkt(B, '-1.05s')}
${node(24, 31, 182, 'Your PC', 'CS2 and Steam')}
${node(352, 31, 196, 'Phone', 'mobile data', { stroke: t.accent })}
${node(694, 31, 182, 'CS2 server', 'sees 198.51.100.23', { mono: true, subColor: t.accent })}
<path d="M115 93 V187 H352" fill="none" stroke="${t.strong}" stroke-width="1.5" stroke-dasharray="5 5"/>
<path d="M548 187 H785 V93" fill="none" stroke="${t.bad}" stroke-width="1.5" stroke-dasharray="5 5" stroke-opacity=".7"/>
<g transform="translate(666 187)"><circle r="11" fill="${t.panel}" stroke="${t.bad}" stroke-width="1.5"/><path d="M-4.5 -4.5 L4.5 4.5 M4.5 -4.5 L-4.5 4.5" stroke="${t.bad}" stroke-width="2" stroke-linecap="round"/></g>
<text x="666" y="217" text-anchor="middle" font-family="${FONT}" font-size="12.5" fill="${t.muted}">blocked for CS2 and your servers</text>
${node(352, 156, 196, 'Home router', 'everything else')}
</svg>`;
}

for (const [name, t] of Object.entries(themes)) {
  write(`how-it-works-${name}.svg`, route(t));
}
console.log('written to', out.pathname);
