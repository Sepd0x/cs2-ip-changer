// The logo's shape (tools/logo.mjs makes the files). A disc cut by one jump that lands on a point at the edge (the new IP). The cut and the clear ring
// around the point split the disc in two: the ground the connection leaves from and the sky it crosses.
// Built as real filled paths (paper.js boolean subtraction in the browser), no masks: build(page) -> {ground, sky, dot}.
import { existsSync, readFileSync } from 'node:fs';
import { execSync } from 'node:child_process';

// paper.js 0.12.18 (npm i paper@0.12.18): PAPER=<path to paper-core.min.js>, or found in the repo or the global npm
function paperPath() {
  const tries = [process.env.PAPER];
  for (const base of [process.cwd(), process.env.PLAYWRIGHT].filter(Boolean)) tries.push(`${base}/node_modules/paper/dist/paper-core.min.js`);
  try { tries.push(`${execSync('npm root -g').toString().trim()}/paper/dist/paper-core.min.js`); } catch {}
  const hit = tries.filter(Boolean).find((p) => existsSync(p));
  if (!hit) throw new Error('paper.js not found: npm i paper@0.12.18, or set PAPER=<path to paper-core.min.js>');
  return hit;
}

// 32 grid. The jump is a cubic from outside the disc (low left) to the point; the cut is 4 wide (2 px at 16).
export const G = {
  disc: [15, 15.5, 13.5], // top edge on y 2
  jump: [[2.5, 30.7], [5.5, 19.5], [10.5, 10], [17, 10], [23.5, 10], [25.8, 15], [26, 21.8]], // top of the cut on y 8..12
  width: 4,
  dot: [26, 24, 3.8], // outer radius; the yellow core sits inside a ring of the sky colour
  core: 2.3,
  gap: 2, // clear ring around the point: 1 px at 16
  blunt: 2.2, // no tip thinner than this
};
// hand-fitted small sizes: ring and core on whole pixels
export const FIT = {
  16: { dot: [26, 24, 4], core: 2 }, // 1 px ring, 2 px core
  24: { dot: [26.667, 24, 4], core: 2.667 }, // 1 px ring, 4 px core
};

// offset a cubic by d along its normals (Tiller-Hanson: offset each control leg), good for gentle curves
function offsetCubic([p0, p1, p2, p3], d) {
  const n = (a, b) => { const dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy) || 1; return [-dy / l * d, dx / l * d]; };
  const a = n(p0, p1), b = n(p1, p2), c = n(p2, p3);
  const sh = (p, v) => [p[0] + v[0], p[1] + v[1]];
  // the middle leg moves by its own normal; ends keep tangents
  const q0 = sh(p0, a), q3 = sh(p3, c);
  const l1 = [sh(p1, a), sh(p2, b)], l2 = [sh(p1, b), sh(p2, c)];
  const hit = (pA, vA, pB, vB) => { // intersection of two lines
    const den = vA[0] * vB[1] - vA[1] * vB[0];
    if (Math.abs(den) < 1e-9) return pB;
    const t = ((pB[0] - pA[0]) * vB[1] - (pB[1] - pA[1]) * vB[0]) / den;
    return [pA[0] + vA[0] * t, pA[1] + vA[1] * t];
  };
  const q1 = hit(q0, [p1[0] - p0[0], p1[1] - p0[1]], l1[0], [p2[0] - p1[0], p2[1] - p1[1]]);
  const q2 = hit(q3, [p2[0] - p3[0], p2[1] - p3[1]], l2[1], [p2[0] - p1[0], p2[1] - p1[1]]);
  return [q0, q1, q2, q3];
}

export function cutPath(g = G) {
  const j = g.jump, segs = [[j[0], j[1], j[2], j[3]], [j[3], j[4], j[5], j[6]]];
  const up = segs.map((s) => offsetCubic(s, -g.width / 2)), lo = segs.map((s) => offsetCubic(s, g.width / 2));
  const f = (p) => `${+p[0].toFixed(3)} ${+p[1].toFixed(3)}`;
  const fwd = (cs) => cs.map((c) => `C${f(c[1])} ${f(c[2])} ${f(c[3])}`).join('');
  const back = (cs) => cs.slice().reverse().map((c) => `C${f(c[2])} ${f(c[1])} ${f(c[0])}`).join('');
  return `M${f(up[0][0])}${fwd(up)}L${f(lo[1][3])}${back(lo)}Z`;
}

export async function build(page, g = G) {
  if (!(await page.evaluate(() => !!window.paper))) await page.addScriptTag({ content: readFileSync(paperPath(), 'utf8') });
  return page.evaluate(([g, cut]) => {
    const { paper } = window;
    paper.setup(new paper.Size(64, 64));
    const disc = new paper.Path.Circle(new paper.Point(g.disc[0], g.disc[1]), g.disc[2]);
    const band = new paper.CompoundPath(cut);
    const ring = new paper.Path.Circle(new paper.Point(g.dot[0], g.dot[1]), g.dot[2] + g.gap);
    const rest = disc.subtract(band).subtract(ring);
    // cut every sharp tip straight across where it is `blunt` wide
    const blunt = (path) => {
      let out = path;
      for (const sg of path.segments) {
        const P = sg.point;
        const prev = sg.previous ? sg.previous.point : path.lastSegment.point, next = sg.next ? sg.next.point : path.firstSegment.point;
        const a = (sg.handleIn.length > 1e-6 ? sg.handleIn : prev.subtract(P)).normalize();
        const b = (sg.handleOut.length > 1e-6 ? sg.handleOut : next.subtract(P)).normalize();
        const th = Math.acos(Math.max(-1, Math.min(1, a.dot(b))));
        if (th > (75 * Math.PI) / 180) continue;
        let u = a.add(b).normalize();
        const snap = Math.round(Math.atan2(u.y, u.x) / (Math.PI / 12)) * (Math.PI / 12); // cut lines on 15 degree steps
        u = new paper.Point(Math.cos(snap), Math.sin(snap));
        const v = new paper.Point(-u.y, u.x);
        const t = (g.blunt / 2) / Math.tan(th / 2);
        const c = P.add(u.multiply(t));
        const cap = new paper.Path([c.add(v.multiply(20)), c.add(v.multiply(20)).subtract(u.multiply(30)), c.subtract(v.multiply(20)).subtract(u.multiply(30)), c.subtract(v.multiply(20))]);
        cap.closed = true;
        out = out.subtract(cap);
      }
      return out;
    };
    const parts = (rest.children ? rest.children.slice() : [rest]).map((p) => blunt(p)).map((p) => ({ d: p.pathData, y: p.bounds.center.y, a: Math.abs(p.area) }));
    parts.sort((a, b) => a.y - b.y);
    const r3 = (d) => d.replace(/-?\d+\.\d+/g, (n) => String(+(+n).toFixed(3)));
    return { sky: r3(parts[0].d), ground: r3(parts[parts.length - 1].d), parts: parts.length, dot: g.dot, core: g.core };
  }, [g, cutPath(g)]);
}

// fills: [ground, sky, point, ring round the point (defaults to the sky)]
export const svg = (s, [cg, cs, cd, cr], { title, bg, scale = 1 } = {}) => {
  const [x, y, r] = s.dot, ring = cr || cs;
  const point = ring === cd ? `<circle cx="${x}" cy="${y}" r="${r}" fill="${cd}"/>`
    : `<circle cx="${x}" cy="${y}" r="${r}" fill="${ring}"/><circle cx="${x}" cy="${y}" r="${s.core}" fill="${cd}"/>`;
  const inner = `<path d="${s.sky}" fill="${cs}"/><path d="${s.ground}" fill="${cg}"/>${point}`;
  const g = scale === 1 ? inner : `<g transform="translate(16 16) scale(${scale}) translate(-16 -16)">${inner}</g>`;
  return `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" width="32" height="32">${title ? `<title>${title}</title>` : ''}${bg ? `<rect width="32" height="32" fill="${bg}"/>` : ''}${g}</svg>\n`;
};
