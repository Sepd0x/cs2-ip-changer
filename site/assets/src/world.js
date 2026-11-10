// CS2 IP Changer home: proposal A's one world, told by the scroll.
// The app's top line (blue, magenta, yellow) is the route the game takes: out of the PC, along the USB cable, into the phone,
// up through the air to the carrier's mast and over to the CS2 server, which only ever sees the carrier's IP.
// The home route stays grey. The kill switch is Windows Firewall on the PC.
//
// window.WorldScene.mount(canvas, opts) -> api or null without WebGL
//   opts.still: reduced motion (no ambient loops, draws only when something changes)
//   opts.frame(W, H, shot, layout): the free part of the canvas for a shot, { box:[l,t,r,b] } in css px
// api.set({ p, sub, cta }): p = story position (0..6, one unit per shot), sub = progress inside the ban beat (0..1)
// The canvas fires 'worldready' after its first frame. Labels live in div.wd-labels right after the canvas (aria-hidden).
// Colours only come from the CSS tokens.
import {
  WebGLRenderer, WebGLRenderTarget, HalfFloatType, Scene, PerspectiveCamera, OrthographicCamera, Color, Vector2, Vector3, Group, Mesh, Points,
  BoxGeometry, PlaneGeometry, SphereGeometry, CylinderGeometry, TorusGeometry, RingGeometry, TubeGeometry, CircleGeometry, BufferGeometry, BufferAttribute,
  CatmullRomCurve3, QuadraticBezierCurve3, CubicBezierCurve3, CurvePath, InstancedMesh, Object3D,
  MeshStandardMaterial, MeshPhysicalMaterial, MeshBasicMaterial, MeshLambertMaterial, ShaderMaterial, Material,
  CanvasTexture, HemisphereLight, DirectionalLight, PointLight, RectAreaLight, FogExp2, PMREMGenerator, VSMShadowMap,
  SRGBColorSpace, AdditiveBlending, DoubleSide, BackSide, ColorManagement, SRGBTransfer, RepeatWrapping, LinearFilter,
  ShaderChunk, Shape, ShapeGeometry, Path, ExtrudeGeometry, Matrix4, Quaternion, Euler, Vector4, LinearMipmapLinearFilter,
} from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { Reflector } from 'three/addons/objects/Reflector.js';
import { HorizontalBlurShader } from 'three/addons/shaders/HorizontalBlurShader.js';
import { VerticalBlurShader } from 'three/addons/shaders/VerticalBlurShader.js';
import { FullScreenQuad } from 'three/addons/postprocessing/Pass.js';
import { RectAreaLightUniformsLib } from 'three/addons/lights/RectAreaLightUniformsLib.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/addons/postprocessing/OutputPass.js';
import { ShaderPass } from 'three/addons/postprocessing/ShaderPass.js';

const V3 = (x, y, z) => new Vector3(x, y, z);
const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
const smooth = (a, b, v) => { const t = clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); };
const lerp = (a, b, t) => a + (b - a) * t;
const yieldTask = () => new Promise(r => setTimeout(r, 0));
const IP_OLD = '198.51.100.23', IP_NEW = '192.0.2.140';

// labels: proposal A's callouts; prefixed so they never meet the page's own classes
const CSS = `
.wd-labels{position:absolute;inset:0;pointer-events:none;overflow:hidden;contain:layout paint}
.wd-lab{position:absolute;left:0;top:0;opacity:var(--o,0);transition:opacity .25s;transform:translate3d(var(--x,-999px),var(--y,-999px),0);will-change:transform,opacity}
.wd-lab i{position:absolute;left:-4px;top:-4px;width:8px;height:8px;border-radius:50%;background:var(--t-text);box-shadow:0 0 0 4px color-mix(in srgb,var(--t-text) 14%,transparent)}
.wd-lab::before{content:"";position:absolute;left:-.5px;bottom:8px;width:1px;height:var(--stem,44px);background:linear-gradient(0deg,color-mix(in srgb,var(--t-text) 55%,transparent),transparent)}
.wd-lab>div{position:absolute;left:-10px;bottom:calc(var(--stem,44px) + 12px);white-space:nowrap;padding:.5rem .7rem;border-radius:7px;background:var(--t-bg);box-shadow:inset 0 0 0 1px color-mix(in srgb,var(--t-text) 13%,transparent),0 10px 28px -12px #000}
.wd-lab b{display:block;font:600 .9rem/1.2 "Funnel Sans",system-ui,sans-serif;color:var(--t-text)}
.wd-lab span,.wd-lab small{display:block;font:400 .78rem/1.35 "Fragment Mono",ui-monospace,Consolas,monospace;color:var(--t-muted);margin-top:.15rem}
.wd-lab small{font-size:.75rem}
.wd-lab em{color:var(--t-accent-light);font-style:normal}
.wd-lab .bad{color:var(--t-bad)}.wd-lab .good{color:var(--t-good)}
.wd-lab.wd-r>div{transform:translateX(calc(-100% + 20px))}
.wd-lab::before{transform-origin:0 0}
.wd-lab.wd-dn::before{transform:translateY(calc(var(--stem,44px) + 16px)) scaleY(.5);background:linear-gradient(180deg,color-mix(in srgb,var(--t-text) 55%,transparent),transparent)}
.wd-lab.wd-dn>div{transform:translateY(calc(100% + var(--stem,44px) + 44px))}
.wd-lab.wd-r.wd-dn>div{transform:translate(calc(-100% + 20px),calc(100% + var(--stem,44px) + 44px))}
.wd-lab.wd-hot i{background:var(--t-bad);box-shadow:0 0 0 4px color-mix(in srgb,var(--t-bad) 22%,transparent)}
.wd-lab[data-lab=server]>div{min-height:4.3rem}
.wd-tall .wd-lab[data-lab=server]>div{min-height:5.4rem}
.wd-tall .wd-lab>div{padding:.4rem .55rem;width:max-content;max-width:min(13rem,46vw);white-space:normal}
.wd-tall .wd-lab b{font-size:.82rem}.wd-tall .wd-lab span,.wd-tall .wd-lab small{font-size:.75rem}
`;
let cssOn = false;
function addCSS() {
  if (cssOn) return; cssOn = true;
  const s = document.createElement('style'); s.dataset.wd = ''; s.textContent = CSS; document.head.appendChild(s);
}

function* build(canvas, opts = {}) {
  const still = !!opts.still;
  const root = document.documentElement;
  const css = getComputedStyle(root);
  const cssv = n => css.getPropertyValue(n).trim();
  const tok = n => new Color(cssv(n));
  const C = {
    bg: tok('--t-bg'), side: tok('--t-side'), edge: tok('--t-edge'), surface: tok('--t-surface'), surface2: tok('--t-surface2'), surface3: tok('--t-surface3'),
    line: tok('--t-line'), lineStrong: tok('--t-line-strong'), faint: tok('--t-faint'), muted: tok('--t-muted'), text: tok('--t-text'),
    accent: tok('--t-accent'), accentDeep: tok('--t-accent-deep'), top1: tok('--t-top1'), top2: tok('--t-top2'), top3: tok('--t-top3'), good: tok('--t-good'), bad: tok('--t-bad'), warn: tok('--t-warn'),
  };
  const mul = (c, k) => c.clone().multiplyScalar(k);

  // ---------- the air: the horizon takes the route's colours in the route's order (blue on the home side, magenta behind the
  // mast and round the servers; 6b took the yellow out of the air), so far things melt into the sky behind them. (A ground
  // haze lifted the blacks: out) ----------
  const v3 = c => `vec3(${c.r.toFixed(5)},${c.g.toFixed(5)},${c.b.toFixed(5)})`;
  const HZ = `
  vec3 hzTint(vec3 d){ vec2 h = d.xz / max(length(d.xz), 1e-4); float s = dot(h, vec2(.94, -.34));
    return s < 0. ? mix(${v3(C.top2)}, ${v3(C.top1)}, -s) : ${v3(C.top2)}; } // 6b: no yellow in the air any more (it made the darks olive round the servers); the route keeps its yellow
  // 6c: a thinner, lower glow (OLED: the owner saw a violet mist over the whole sky); the wide term was what lifted it
  float hzGlow(vec3 d){ return (exp(-abs(d.y)*30.)*.01 + exp(-abs(d.y)*12.)*.005) * (.35 + .65*smoothstep(.3, -.9, d.z)); }
  // 6b: the sky's own band, much brighter than the haze on things (in the haze it turned the whole floor magenta).
  // 6c: a narrow glow just over the skyline, as in the generated pictures, and night black above it
  float hzSky(vec3 d){ return (exp(-abs(d.y)*42.)*.065 + exp(-abs(d.y)*14.)*.005) * (.3 + .7*smoothstep(.3, -.9, d.z)); }`;
  ShaderChunk.fog_pars_vertex = `#ifdef USE_FOG\nvarying float vFogDepth; varying vec3 vFogV;\n#endif`;
  ShaderChunk.fog_vertex = `#ifdef USE_FOG\nvFogDepth = - mvPosition.z; vFogV = mvPosition.xyz;\n#endif`;
  ShaderChunk.fog_pars_fragment = `#ifdef USE_FOG
  uniform vec3 fogColor; varying float vFogDepth; varying vec3 vFogV;
  #ifdef FOG_EXP2
  uniform float fogDensity;
  #else
  uniform float fogNear; uniform float fogFar;
  #endif
  ${HZ}
  #endif`;
  ShaderChunk.fog_fragment = `#ifdef USE_FOG
  vec3 fgD = (vec4(vFogV, 0.) * viewMatrix).xyz; float fgL = length(fgD);
  #ifdef FOG_EXP2
  float fogFactor = 1.0 - exp(-fogDensity*fogDensity*vFogDepth*vFogDepth);
  #else
  float fogFactor = smoothstep(fogNear, fogFar, vFogDepth);
  #endif
  vec3 fgN = fgD / max(fgL, 1e-4);
  gl_FragColor.rgb = mix(gl_FragColor.rgb, fogColor + hzTint(fgN)*hzGlow(fgN), fogFactor);
  #endif`;

  // ---------- renderer: MSAA into a half float buffer, so edges are clean and the bloom has room ----------
  let renderer;
  try { renderer = new WebGLRenderer({ canvas, antialias: false, powerPreference: 'high-performance' }); }
  catch (e) { return null; }
  if (!renderer.capabilities.isWebGL2) { renderer.dispose(); return null; }
  renderer.debug.checkShaderErrors = false;
  yield;
  const small = () => (canvas.clientWidth || innerWidth) < 761;
  const dpr = () => Math.min(devicePixelRatio, small() ? 2 : 1.75);
  renderer.setPixelRatio(dpr());
  renderer.setClearColor(C.bg, 1);
  renderer.shadowMap.enabled = true; renderer.shadowMap.type = VSMShadowMap; renderer.shadowMap.autoUpdate = false;
  RectAreaLightUniformsLib.init();
  const scene = new Scene();
  // 9 Nov. (session 6b): thinner air. At 0.018 the racks, 53 m from the cover's camera, were 60% haze and read as grey blocks
  scene.fog = new FogExp2(mul(C.bg, 0.45), 0.0105); // 6c: a darker air (OLED): at the floor's far end it was a grey plain under the town
  const camera = new PerspectiveCamera(30, 1, 0.1, 320);

  yield;
  // ---------- a studio for reflections, from the tokens: a dark room, the key's soft box, two tall strips for rims, the route's
  // colours low on the horizon where each runs in the world. Brighter than white, so metal gets a real highlight. Until 7 Nov.
  // it was only built when asked (opts.env), so no material had any reflection and every bevel was invisible ----------
  const ENV_K = 1; let envRT = null;
  const envScene = new Scene();
  {
    const room = new Mesh(new BoxGeometry(44, 24, 44), new MeshBasicMaterial({ color: C.surface, side: BackSide })); room.position.y = 8; envScene.add(room);
    const ground = new Mesh(new PlaneGeometry(44, 44), new MeshBasicMaterial({ color: C.edge })); ground.rotation.x = -Math.PI / 2; ground.position.y = -3.9; envScene.add(ground);
    const box = (w, h, col, k, x, y, z) => { const m = new Mesh(new PlaneGeometry(w, h), new MeshBasicMaterial({ color: mul(col, k), side: DoubleSide })); m.position.set(x, y, z); m.lookAt(0, 0, 0); envScene.add(m); };
    box(16, 7, C.text, 3, -8, 17, 9);        // the key's soft box, on the key light's side
    box(1.4, 14, C.text, 2.2, 9, 5, -20);    // a tall strip behind, on the rim side: long highlights down the mast and the rack edges
    box(1.4, 12, C.muted, 1.4, -20, 5, -5);  // a second strip, left
    box(12, 1.6, C.top1, 1.6, -18, 1, 9);    // blue on the desk's side
    box(12, 1.6, C.top2, 1.6, 3, 1, -20);    // magenta towards the mast
    box(12, 1.6, C.top3, 1.6, 20, 1, -7);    // yellow towards the server
  }

  yield;
  // ---------- light: a dim world; each part of the route lights what is near it, the screen lights the desk ----------
  const hemi = new HemisphereLight(C.surface3, C.bg, 0.42); scene.add(hemi); // 6b: more fill from above (0.24), metals read in the shade
  const key = new DirectionalLight(mul(C.muted, 1), 2.3); // 6b: brighter key and rim, the metals read as metal key.position.set(-14, 26, 16); key.target.position.set(2, 0, -3);
  key.castShadow = true; key.shadow.mapSize.set(2048, 2048); key.shadow.bias = -0.0015; key.shadow.normalBias = 0.05; key.shadow.radius = 12; key.shadow.blurSamples = 16;
  Object.assign(key.shadow.camera, { left: -22, right: 24, top: 18, bottom: -16, near: 4, far: 70 });
  scene.add(key, key.target);
  const rim = new DirectionalLight(mul(C.text, 1), 1.9); rim.position.set(8, 1.6, -22); scene.add(rim); // separates the silhouettes from the dark
  const SX = 17, SZ = -9.5;
  const L1 = new PointLight(C.top1, 14, 14, 1.6); L1.position.set(-3.5, 1.4, 3.6); scene.add(L1);
  const L2 = new PointLight(C.top2, 26, 18, 1.6); L2.position.set(5.5, 8.7, -5.5); scene.add(L2);
  const L3 = new PointLight(C.top3, 22, 16, 1.6); L3.position.set(SX - 1, 6, SZ + 2.5); scene.add(L3);
  const Lrack = new PointLight(C.muted, 7, 9, 1.6); Lrack.position.set(SX, 2.8, SZ + 3.6); scene.add(Lrack);
  const Lsrv = new PointLight(C.bad, 0, 7, 1.6); Lsrv.position.set(SX, 2.6, SZ + 2.4); scene.add(Lsrv);
  const Lbad = new PointLight(C.bad, 0, 6, 1.8); Lbad.position.set(-8.4, 1.4, -1.6); scene.add(Lbad);

  // sky: darker overhead; low down the same haze the fog uses, seen from the camera, so far things melt into it. A hair of dither
  const sky = new Mesh(new SphereGeometry(150, 48, 24), new ShaderMaterial({
    side: BackSide, depthWrite: false, fog: false, uniforms: { uBg: { value: C.bg }, uNight: { value: C.accentDeep.clone() } },
    vertexShader: `varying vec3 vP; void main(){ vec4 w = modelMatrix*vec4(position,1.); vP = w.xyz - cameraPosition; gl_Position = projectionMatrix*viewMatrix*w; }`,
    // 9 Nov. (session 6b): a night sky, blue going violet low down, under it the band in the route's colours (it was black)
    fragmentShader: `uniform vec3 uBg,uNight; varying vec3 vP; ${HZ}
    // 6c: night black overhead (OLED), a hair of blue night low down; the dither moved to the last pass (3D-011: here it was a
    // sine hash, which mobile GPUs turn into a grid, and in the darkest sky it lit and unlit single pixels)
    void main(){ vec3 d = normalize(vP); vec3 c = uBg*(.62 - .5*smoothstep(.04, .5, d.y)) + uNight*.022*smoothstep(-.04, .05, d.y)*(1. - smoothstep(.06, .3, d.y)) + mix(hzTint(d), ${v3(C.top2)}, .45)*hzSky(d);
      gl_FragColor = vec4(c, 1.); }`,
  })); scene.add(sky);
  yield;
  // the far town: 130 m out, where the floor ends, all round; taller blocks towards the servers. 6c (3D-012): it stands on the
  // floor's edge, in front of the ridge (the ridge used to hide it and a dark ring of low hills lay between the floor and the sky,
  // the "hole to the end" the owner saw). A few dim windows, never smaller than a pixel (a lit window under a pixel sparkles).
  // Hashes without sine (3D-011: fract(sin) turns into a grid on mobile GPUs)
  const skyline = new Mesh(new CylinderGeometry(130, 130, 30, 192, 1, true), new ShaderMaterial({
    side: BackSide, fog: false, alphaToCoverage: true, uniforms: { uSil: { value: mul(C.bg, 0.3) }, uBg: { value: C.bg }, uWin: { value: C.warn.clone().lerp(C.top2, 0.35) } },
    vertexShader: `varying vec3 vW; void main(){ vec4 w = modelMatrix*vec4(position,1.); vW = w.xyz; gl_Position = projectionMatrix*viewMatrix*w; }`,
    fragmentShader: `uniform vec3 uSil,uBg,uWin; varying vec3 vW; ${HZ}
    float hs(float n){ n = fract(n*.1031); n *= n + 33.33; n *= n + n; return fract(n); }
    float hs2(vec2 p){ vec3 q = fract(vec3(p.xyx)*.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y)*q.z); }
    float lot(float x, float sd, float lo, float hi, float pw){
      float id = floor(x), f = fract(x), r = hs(id + sd), h = mix(lo, hi, pow(r, pw));
      h += step(.65, hs(id*5.7 + sd))*step(f, .45)*h*.22;
      h += step(.84, hs(id*3.1 + sd))*step(abs(f - .3 - .4*hs(id + 9.)), .03)*(1.5 + 2.5*r);
      return h*step(.05, f)*step(f, .95); }
    void main(){
      float az = atan(vW.z, vW.x), a = az/6.2831853 + .5, town = smoothstep(-.1, .7, cos(az + .9));
      // a mountain ridge far behind, lower where the town stands (6b: high enough to stand against the sky's band)
      float ridge = (6.5 + 3.5*sin(az*2. + .7) + 2.6*sin(az*5. + 1.9) + 1.4*sin(az*11. + .3) + .6*sin(az*29. + 2.2) + .25*sin(az*67.))*(1. - .3*town);
      float blds = max(lot(a*260., 3., 1.2, 3.4, 2.)*(.6 + .4*town), lot(a*110., 11., 2., 5.2, 3.)*town);
      float y = vW.y, h = max(ridge, blds), d = h - y, cov = clamp(d/max(fwidth(d), 1e-3) + .5, 0., 1.); if (cov < .01) discard;
      float db = blds - y, inB = clamp(db/max(fwidth(db), 1e-3) + .5, 0., 1.);
      vec3 v = normalize(vW - cameraPosition), haze = uBg*.45 + hzTint(v)*hzGlow(v);
      // the ridge is farther: more of the night's haze on it; the blocks are black against it, their feet in the floor's haze
      vec3 ridgeC = mix(uSil, haze, .55) + hzTint(v)*.025*exp(-max(ridge - y, 0.)*1.2);
      vec3 bldC = mix(uSil*.6, haze, .25 + .55*(1. - smoothstep(0., 5., y)));
      // windows: 0.9 m storeys, 0.8 m bays, one in twelve lit, faded out once a window is under a pixel
      vec2 cell = vec2(a*130.*6.2831853/.8, y/.9), wf = fract(cell), wi = floor(cell);
      float win = step(.92, hs2(wi + 17.))*step(.3, wf.x)*step(wf.x, .7)*step(.35, wf.y)*step(wf.y, .65)*step(1., y)*step(y, blds - .5);
      win *= 1. - smoothstep(.35, .7, max(fwidth(cell.x), fwidth(cell.y)));
      bldC += uWin*win*(.025 + .03*hs2(wi))*(1. - smoothstep(-.9, .9, -town*2. + 1.)*.5);
      gl_FragColor = vec4(mix(ridgeC, bldC, inB), cov); }`,
  }));
  skyline.position.y = 11; scene.add(skyline);
  yield;
  // other carriers' masts among the far roofs, as dark silhouettes (no lights: red means blocked here). 6c: they stood on a ring
  // of low hills, which left an unlit band between the floor and the horizon (3D-012); the floor now runs out to the town
  const FAR = [[-2.7, 127, 6], [-2.25, 126, 7], [-1.9, 127, 5.5], [-1.55, 125, 7.5], [-1.2, 127, 6], [-0.85, 126, 7], [-0.45, 127, 5]]; // azimuth, metres out, height
  const farMasts = (() => {
    // a thin pole, and at its top a ring of three panels (the head every cell mast has), in one unit-high shape scaled per mast
    const pole = new CylinderGeometry(0.008, 0.022, 1, 6, 1).translate(0, 0.5, 0);
    const heads = [0, 1, 2].map(k => new BoxGeometry(0.025, 0.09, 0.012).translate(0.045, 0.93, 0).rotateY(k * Math.PI * 2 / 3));
    const g = mergeGeometries([pole, ...heads]);
    const im = new InstancedMesh(g, new MeshBasicMaterial({ color: mul(C.bg, 0.5), fog: false }), FAR.length), o = new Object3D();
    FAR.forEach(([az, r, h], i) => {
      const x = Math.cos(az) * r, z = Math.sin(az) * r;
      o.position.set(x, 0, z); o.scale.setScalar(h); o.rotation.y = az; o.updateMatrix(); im.setMatrixAt(i, o.matrix);
    });
    scene.add(im); return im;
  })();

  yield;
  // roughness as grey data (not colour): soft blotches, fine grain, optional streaks along x; seeded, so every load is the same
  function roughMap(size, base, blot, grain, streak = 0, seed = 7) {
    let s = seed; const rnd = () => (s = (s * 16807) % 2147483647) / 2147483647;
    const t = cvTex(size, size, (g, w, h) => {
      const grey = (v, a) => `rgba(${v | 0},${v | 0},${v | 0},${a})`;
      g.fillStyle = grey(base, 1); g.fillRect(0, 0, w, h);
      for (let i = 0; i < 70; i++) {
        const x = rnd() * w, y = rnd() * h, r = (0.04 + rnd() * 0.16) * w, v = clamp(base + (rnd() - 0.5) * 2 * blot, 0, 255);
        for (const dx of [-w, 0, w]) for (const dy of [-h, 0, h]) { const gr = g.createRadialGradient(x + dx, y + dy, 0, x + dx, y + dy, r); gr.addColorStop(0, grey(v, 0.55)); gr.addColorStop(1, grey(v, 0)); g.fillStyle = gr; g.fillRect(x + dx - r, y + dy - r, 2 * r, 2 * r); }
      }
      const im = g.getImageData(0, 0, w, h), d = im.data, row = new Float32Array(h);
      for (let y = 0; y < h; y++) row[y] = (rnd() - 0.5) * 2 * streak;
      for (let i = 0, p = 0; i < d.length; i += 4, p++) { const v = d[i] + (rnd() - 0.5) * 2 * grain + row[(p / w) | 0]; d[i] = d[i + 1] = d[i + 2] = v; }
      g.putImageData(im, 0, 0);
    }, false);
    t.wrapS = t.wrapT = RepeatWrapping; return t;
  }
  const R = { concrete: roughMap(256, 215, 35, 18), spangle: roughMap(128, 110, 70, 10, 0, 11), powder: roughMap(128, 165, 12, 26, 0, 3), brushed: roughMap(256, 120, 20, 8, 22, 5) };

  yield;
  // ---------- materials: what each thing is made of; the studio's reflections give them their edges (no emissive lift) ----------
  const M = {
    metal: new MeshStandardMaterial({ color: mul(C.muted, 0.42), metalness: 1, roughness: 0.8, roughnessMap: R.brushed }),      // anodised aluminium
    metalDark: new MeshStandardMaterial({ color: mul(C.muted, 0.2), metalness: 1, roughness: 0.95, roughnessMap: R.brushed }),  // dark anodised
    plastic: new MeshStandardMaterial({ color: C.surface2, metalness: 0, roughness: 0.62 }),                                     // black ABS
    rubber: new MeshStandardMaterial({ color: C.edge, metalness: 0, roughness: 0.92 }),
    mat: new MeshPhysicalMaterial({ color: mul(C.surface, 0.3), roughness: 1, sheen: 0.25, sheenRoughness: 0.8, sheenColor: C.surface3 }),  // cloth, a dark gaming mat
    galv: new MeshStandardMaterial({ color: mul(C.muted, 1), metalness: 0.5, roughness: 0.6, roughnessMap: R.spangle }),    // galvanised steel, light (6b: it was a dark grey)
    radome: new MeshStandardMaterial({ color: mul(C.muted, 0.62), metalness: 0, roughness: 0.55 }),
    powder: new MeshStandardMaterial({ color: C.surface2, metalness: 0, roughness: 1, roughnessMap: R.powder }),                 // powder-coated steel
    concrete: new MeshStandardMaterial({ color: mul(C.surface3, 0.7), metalness: 0, roughness: 1, roughnessMap: R.concrete }),
    glass: new MeshStandardMaterial({ color: C.edge, metalness: 0, roughness: 0.05, transparent: true, opacity: 0.45 }),        // the tint
    glassSpec: new MeshStandardMaterial({ color: C.edge, metalness: 0, roughness: 0.04, transparent: true, blending: AdditiveBlending, depthWrite: false }), // its reflection, added
    // bead-blasted aluminium: the tower and the phone stand catch the light without a hot spot
    matte: new MeshStandardMaterial({ color: mul(C.muted, 0.36), metalness: 1, roughness: 0.58 }),
    matteDark: new MeshStandardMaterial({ color: mul(C.muted, 0.16), metalness: 1, roughness: 0.62 }),
    // 6b: the tower's case, silver brushed aluminium with bright edges (it was the dark anodised one, lost in the night)
    alu: new MeshStandardMaterial({ color: mul(C.muted, 1), metalness: 0.6, roughness: 0.62, roughnessMap: R.brushed }),
    keycap: new MeshStandardMaterial({ color: mul(C.surface3, 1.5), metalness: 0, roughness: 0.45 }),
    chrome: new MeshStandardMaterial({ color: mul(C.muted, 1.3), metalness: 1, roughness: 0.16 }),
    // the phone's screen glass: the studio's reflections only. The desk's blue lamp (L1), a point a metre in front of it, came
    // back off it as a hot ball that bloomed in front of the screen in close-ups (3D-002, 9 Nov.; gone with L1 off, and a
    // rougher glass only made it a bigger blob): the lamps' highlights are taken out of this one material
    phoneGlass: new MeshStandardMaterial({ color: C.edge, metalness: 0, roughness: 0.04, transparent: true, blending: AdditiveBlending, depthWrite: false }),
  };
  M.phoneGlass.onBeforeCompile = sh => { sh.fragmentShader = sh.fragmentShader.replace('#include <lights_fragment_end>', '#include <lights_fragment_end>\n reflectedLight.directSpecular = vec3(0.);'); };
  M.phoneGlass.customProgramCacheKey = () => 'glass-env-only';
  const shadowy = o => { o.traverse(m => { if (m.isMesh) { m.castShadow = true; m.receiveShadow = true; } }); return o; };
  // one geometry per size: the racks, the props and the cards repeat the same boxes, and building each again was a third of
  // the start's longest task on a slow phone (6 Nov.). Nothing changes a box's geometry after it is made
  const RB = new Map();
  const rgeo = (w, h, d, r, seg = 3) => { const rr = Math.min(r, w / 2, h / 2, d / 2), k = w + ',' + h + ',' + d + ',' + rr + ',' + seg; let g = RB.get(k); if (!g) RB.set(k, g = new RoundedBoxGeometry(w, h, d, seg, rr)); return g; };
  const rbox = (w, h, d, r, mat, seg = 3) => new Mesh(rgeo(w, h, d, r, seg), mat);
  // many small parts in one draw: [x, y, z, rx, ry, rz, sx, sy, sz]
  const inst = (geo, mat, list) => { const m = new InstancedMesh(geo, mat, list.length), o = new Object3D(); list.forEach(([x, y, z, rx = 0, ry = 0, rz = 0, sx = 1, sy = 1, sz = 1], i) => { o.position.set(x, y, z); o.rotation.set(rx, ry, rz); o.scale.set(sx, sy, sz); o.updateMatrix(); m.setMatrixAt(i, o.matrix); }); return m; };
  const unitBox = new BoxGeometry(1, 1, 1);
  const rrect = (p, x, y, w, h, r) => { p.moveTo(x + r, y); p.lineTo(x + w - r, y); p.quadraticCurveTo(x + w, y, x + w, y + r); p.lineTo(x + w, y + h - r); p.quadraticCurveTo(x + w, y + h, x + w - r, y + h); p.lineTo(x + r, y + h); p.quadraticCurveTo(x, y + h, x, y + h - r); p.lineTo(x, y + r); p.quadraticCurveTo(x, y, x + r, y); return p; };

  // small canvas textures, drawn with the app's tokens
  function cvTex(w, h, draw, srgb = true) {
    const cv = document.createElement('canvas'); cv.width = w; cv.height = h; draw(cv.getContext('2d'), w, h);
    const t = new CanvasTexture(cv); if (srgb) t.colorSpace = SRGBColorSpace; t.anisotropy = 8; return t;
  }

  yield;
  // ---------- ground: dark polished tiles; the route lights it and is mirrored in it ----------
  // 6b: no specular of its own (at a grazing angle the key and the studio made the far floor a grey sheet); its shine is the
  // mirror below and the sky's band in the dots' shader
  const floor = new Mesh(new PlaneGeometry(260, 260), new MeshLambertMaterial({ color: mul(C.bg, 0.3) }));
  floor.rotation.x = -Math.PI / 2; floor.receiveShadow = true; scene.add(floor);
  // 6c (3D-011): no dot lattice any more (a 1 m grid of grey dots: on an OLED the floor read as a pattern of points, and far off
  // the dots fell under a pixel and shimmered); the name stays. Slow wear like a poured floor, and the light the route's radio leg and arc throw on the ground under them
  // (18 small lamps along them, filled in once the route is built; they go out with the line)
  const dots = new Mesh(new PlaneGeometry(260, 260), new ShaderMaterial({
    transparent: true, depthWrite: false, blending: AdditiveBlending,
    uniforms: { uSurf: { value: C.surface3 }, uNight: { value: C.accentDeep.clone().lerp(C.top2, 0.2) }, uCam: { value: camera.position }, c1: { value: C.top1 }, c2: { value: C.top2 }, c3: { value: C.top3 },
      uRL: { value: Array.from({ length: 18 }, () => new Vector4()) }, uLeg: { value: V3(0, 0, 0) }, uDraw: { value: 1 }, uReveal: { value: 0 }, uK: { value: 0.35 } },
    vertexShader: `varying vec3 vW; void main(){ vec4 w = modelMatrix*vec4(position,1.); vW = w.xyz; gl_Position = projectionMatrix*viewMatrix*w; }`,
    fragmentShader: `uniform vec3 uSurf,uCam,c1,c2,c3,uLeg,uNight; uniform vec4 uRL[18]; uniform float uDraw,uReveal,uK; varying vec3 vW; ${HZ}
    float h2(vec2 p){ vec3 q = fract(vec3(p.xyx)*.1031); q += dot(q, q.yzx + 33.33); return fract((q.x + q.y)*q.z); } // no sine (3D-011)
    float vn(vec2 p){ vec2 i = floor(p), f = fract(p); f = f*f*(3. - 2.*f);
      return mix(mix(h2(i), h2(i + vec2(1., 0.)), f.x), mix(h2(i + vec2(0., 1.)), h2(i + 1.), f.x), f.y); }
    void main(){
      vec2 p = vW.xz; float dist = length(vW - uCam);
      float tone = vn(p*.045)*.55 + vn(p*.15 + 7.)*.3 + vn(p*.6 + 3.)*.15;
      vec3 c = uSurf*smoothstep(.45, .9, tone)*.035*exp(-dist*.014);
      // 6b: big polished tiles, 3 m, their joints catching the light, each tile a shade of its own. 6c: darker, and the joints fade
      // out where they get thinner than a pixel (there they turned into a grid of dashes)
      vec2 tf = fract((p - 1.5)/3.), tid = floor((p - 1.5)/3.);
      float jl = min(min(tf.x, 1. - tf.x), min(tf.y, 1. - tf.y))*3., jaa = fwidth(jl);
      float joint = 1. - smoothstep(.012, .012 + jaa*1.5, jl);
      c += uSurf*(joint*.1*(1. - smoothstep(.03, .1, jaa)) + h2(tid)*.03)*exp(-dist*.016);
      // the far floor takes the sky's band at a grazing angle, as polished stone does (6b)
      vec3 vd = normalize(uCam - vW); float fr = pow(1. - clamp(vd.y, 0., 1.), 6.);
      c += mix(hzTint(-vd*vec3(1., -1., 1.)), uNight, .5)*fr*.035*(.6 + .4*h2(tid));
      vec3 lt = vec3(0.);
      if (p.x > -12. && p.x < 30. && p.y > -22. && p.y < 12.) for (int i = 0; i < 18; i++) {
        vec4 q = uRL[i]; float u = q.w;
        if (u > uReveal || (u > .58 && (u - .58)/.42 > uDraw)) continue;
        vec3 dv = q.xyz - vW; vec3 col = u < .5 ? mix(c1, c2, u*2.) : mix(c2, c3, u*2. - 1.);
        lt += col*(u < .58 ? uLeg.y*.45 : uLeg.z)*q.y/pow(dot(dv, dv) + .3, 1.5);
      }
      gl_FragColor = vec4(c + min(lt*uK, vec3(.008)), 1.);
    }`,
  }));
  dots.rotation.x = -Math.PI / 2; dots.position.y = 0.003; scene.add(dots);
  // the mirror: only what glows (layer REFL) is drawn into it, at half size, blurred through its mips by the floor's roughness
  const REFL = 1;
  const mirror = new Reflector(new PlaneGeometry(260, 260), { textureWidth: 512, textureHeight: 512, multisample: 0, clipBias: 0.003, color: C.text, shader: {
    name: 'RouteMirror',
    uniforms: { color: { value: null }, tDiffuse: { value: null }, textureMatrix: { value: null }, tRough: { value: null }, uK: { value: 7 }, uBg: { value: C.bg } },
    vertexShader: `uniform mat4 textureMatrix; varying vec4 vUv; varying vec3 vW;
      void main(){ vUv = textureMatrix*vec4(position,1.); vec4 w = modelMatrix*vec4(position,1.); vW = w.xyz; gl_Position = projectionMatrix*viewMatrix*w; }`,
    fragmentShader: `uniform sampler2D tDiffuse,tRough; uniform float uK; uniform vec3 uBg; varying vec4 vUv; varying vec3 vW;
      void main(){
        float r = texture2D(tRough, vW.xz*.1).r*.6 + texture2D(tRough, vW.xz*.023).r*.4;
        vec2 uv = vUv.xy/vUv.w;
        vec3 v = cameraPosition - vW; float dist = length(v);
        // 6b: a wet look, everywhere but the cloth: the reflection is pulled down the screen (taps along the reflection's
        // vertical), as polished stone smears a light into a streak. Until 6b it showed only round the mast and the racks
        float lod = 1.3 + r*.9 + dist*.008, sy = .0034 + .002*r; // 6c: the taps overlap (3D-011: further apart than their blur, a light came back as a column of dots)
        vec3 c = vec3(0.);
        for (int j = -3; j <= 3; j++) { float fj = float(j); c += max(textureLod(tDiffuse, uv + vec2(0., fj*sy), lod + abs(fj)*.2).rgb - uBg, 0.)*exp(-fj*fj/5.); }
        c /= 3.9;
        // the texture's edges are clamped: fade there, or the last row smears into a bar (6b)
        c *= smoothstep(0., .05, uv.x)*smoothstep(1., .95, uv.x)*smoothstep(0., .05, uv.y)*smoothstep(1., .95, uv.y);
        float f = .04 + .96*pow(1. - clamp(normalize(v).y,0.,1.), 5.);
        vec2 dq = abs(vW.xz - vec2(-4.1, 1.75)) - vec2(5.8, 3.4); float desk = smoothstep(.2, 1.2, max(dq.x, dq.y));
        gl_FragColor = vec4(c*uK*(.35+.65*f)*(1.15-.5*r)*exp(-dist*.016)*desk, 1.);
      }`,
  } });
  mirror.getReflectionCamera(camera).layers.set(REFL);
  { const t = mirror.getRenderTarget().texture; t.generateMipmaps = true; t.minFilter = LinearMipmapLinearFilter; }
  Object.assign(mirror.material, { transparent: true, depthWrite: false, blending: AdditiveBlending });
  mirror.material.uniforms.tRough.value = R.concrete;
  mirror.rotation.x = -Math.PI / 2; mirror.position.y = 0.002; scene.add(mirror);
  const mirrorDraw = mirror.onBeforeRender; let mirrorAt = -1, frameNo = 0, inBloom = false;
  // once a frame, in the real render (6b): drawn in the bloom's render it had the screens black and no key light
  mirror.onBeforeRender = function (...a) { if (inBloom || mirrorAt === frameNo) return; mirrorAt = frameNo; const lk = leds.uniforms.uK.value; leds.uniforms.uK.value = lk * 0.35; mirrorDraw.apply(this, a); leds.uniforms.uK.value = lk; }; // 6c: the racks' lights softer in the wet floor (it was three solid green columns) // once a frame, not once per render
  const glows = o => { o.layers.enable(REFL); return o; };
  // contact shadows, baked once from the real shapes: everything that casts a shadow is drawn from below in an orthographic
  // view, darker the nearer the ground, then blurred; dark pools on the floor, the desk mat, the mast's pad and the raised
  // floor take it. Until 7 Nov. these were round blobs, and the desk's lay under the mat where nobody saw them.
  // A part that moves or hides (the phone, the plug, the firewall, a guide's prop) is marked userData.noBake
  const blobTex = cvTex(128, 128, (g) => { const r = g.createRadialGradient(64, 64, 0, 64, 64, 64); r.addColorStop(0, 'rgba(0,0,0,.85)'); r.addColorStop(.55, 'rgba(0,0,0,.35)'); r.addColorStop(1, 'rgba(0,0,0,0)'); g.fillStyle = r; g.fillRect(0, 0, 128, 128); }, false);
  const blobMat = new MeshBasicMaterial({ map: blobTex, transparent: true, depthWrite: false, opacity: 1 });
  function blobAt(x, y, z, w, d, ry = 0) { const m = new Mesh(new PlaneGeometry(w, d), blobMat); m.rotation.set(-Math.PI / 2, 0, ry); m.position.set(x, y, z); scene.add(m); return m; }
  function blob() {}
  const AO = { x0: -14, x1: 21, z0: -12.5, z1: 6.5 }, AO_H = 1.2, aoA = new WebGLRenderTarget(1024, 512), aoB = new WebGLRenderTarget(1024, 512);
  const aoCam = new OrthographicCamera(AO.x0, AO.x1, AO.z1, AO.z0, 0, AO_H); aoCam.up.set(0, 0, 1); aoCam.lookAt(0, 1, 0); aoCam.updateMatrixWorld();
  const aoMat = new ShaderMaterial({
    side: DoubleSide, uniforms: { uH: { value: AO_H } },
    vertexShader: `varying float vH; void main(){
      #ifdef USE_INSTANCING
      vec4 w = modelMatrix*instanceMatrix*vec4(position,1.);
      #else
      vec4 w = modelMatrix*vec4(position,1.);
      #endif
      vH = w.y; gl_Position = projectionMatrix*viewMatrix*w; }`,
    fragmentShader: `uniform float uH; varying float vH; void main(){ float k = 1.-clamp(vH/uH,0.,1.); gl_FragColor = vec4(0.,0.,0.,k*k); }`,
  });
  const aoShow = new ShaderMaterial({
    transparent: true, depthWrite: false,
    uniforms: { tAO: { value: aoB.texture }, uM: { value: new Matrix4().multiplyMatrices(aoCam.projectionMatrix, aoCam.matrixWorldInverse) }, uK: { value: 0.9 } },
    vertexShader: `varying vec3 vW; void main(){ vec4 w = modelMatrix*vec4(position,1.); vW = w.xyz; gl_Position = projectionMatrix*viewMatrix*w; }`,
    fragmentShader: `uniform sampler2D tAO; uniform mat4 uM; uniform float uK; varying vec3 vW;
      void main(){ vec2 uv = (uM*vec4(vW.x,0.,vW.z,1.)).xy*.5+.5; if(any(lessThan(uv,vec2(0.)))||any(greaterThan(uv,vec2(1.)))) discard;
        gl_FragColor = vec4(0.,0.,0.,texture2D(tAO,uv).a*uK); }`,
  });
  const aoRecv = []; // the mat, the pad, the raised floor: they take the shadows, they are not baked into them
  function aoPlane(w, d, x, y, z) { const m = new Mesh(new PlaneGeometry(w, d), aoShow); m.rotation.x = -Math.PI / 2; m.position.set(x, y, z); scene.add(m); return m; }
  aoPlane(AO.x1 - AO.x0, AO.z1 - AO.z0, (AO.x0 + AO.x1) / 2, 0.006, (AO.z0 + AO.z1) / 2);
  function bakeContact() {
    const hid = [];
    scene.traverse(o => {
      let skip = false; for (let q = o; q; q = q.parent) if (q.userData.noBake) skip = true;
      if ((o.isMesh || o.isPoints) && o.visible && (skip || !o.castShadow || o.material.isShaderMaterial || aoRecv.includes(o))) { o.visible = false; hid.push(o); }
    });
    scene.overrideMaterial = aoMat; renderer.setClearColor(C.bg, 0);
    renderer.setRenderTarget(aoA); renderer.clear(); renderer.render(scene, aoCam);
    scene.overrideMaterial = null; renderer.setClearColor(C.bg, 1); hid.forEach(o => (o.visible = true));
    const h = new FullScreenQuad(new ShaderMaterial(HorizontalBlurShader)), v = new FullScreenQuad(new ShaderMaterial(VerticalBlurShader));
    for (const k of [2.5, 1]) {
      h.material.uniforms.tDiffuse.value = aoA.texture; h.material.uniforms.h.value = k / 1024; renderer.setRenderTarget(aoB); h.render(renderer);
      v.material.uniforms.tDiffuse.value = aoB.texture; v.material.uniforms.v.value = k / 512; renderer.setRenderTarget(aoA); v.render(renderer);
    }
    h.material.uniforms.tDiffuse.value = aoA.texture; renderer.setRenderTarget(aoB); h.render(renderer); // the last pass ends in aoB
    renderer.setRenderTarget(null); h.dispose(); v.dispose(); h.material.dispose(); v.material.dispose(); aoA.dispose();
  }

  let stitchRef = null;
  yield;
  // ---------- your desk: a mat that groups PC, screen, keyboard and phone ----------
  {
    // a cloth mat (9 Nov., session 6: a 5 cm box with square corners read as a concrete slab): round corners, a soft rolled
    // edge, a woven top and a stitched border a little in from the edge, as gaming mats have. Its top stays at 0.05 (MAT_Y)
    const MW = 11.6, MD = 6.8, MR = 0.42, BT = 0.012;
    const matGeo = new ExtrudeGeometry(rrect(new Shape(), -MW / 2 + BT, -MD / 2 + BT, MW - 2 * BT, MD - 2 * BT, MR), { depth: 0.05 - BT, bevelEnabled: true, bevelThickness: BT, bevelSize: BT, bevelSegments: 3, curveSegments: 10 }).rotateX(-Math.PI / 2);
    const deskMat = new Mesh(matGeo, M.mat); deskMat.position.set(-4.1, 0, 1.75); deskMat.receiveShadow = true; scene.add(deskMat); aoRecv.push(deskMat);
    aoPlane(MW - 0.06, MD - 0.06, -4.1, 0.052, 1.75);
    // the stitching: a thin band 0.1 in from the edge, its corners following the mat's
    const sIn = 0.1, sW = 0.022, band = rrect(new Shape(), -MW / 2 + sIn, -MD / 2 + sIn, MW - 2 * sIn, MD - 2 * sIn, MR - sIn);
    band.holes.push(rrect(new Path(), -MW / 2 + sIn + sW, -MD / 2 + sIn + sW, MW - 2 * (sIn + sW), MD - 2 * (sIn + sW), MR - sIn - sW));
    const stitch = new Mesh(new ShapeGeometry(band, 10), new MeshStandardMaterial({ color: mul(C.muted, 0.5), roughness: 0.9, metalness: 0 })); stitchRef = stitch; // 6b: a thread you can see
    stitch.rotation.x = -Math.PI / 2; stitch.position.set(-4.1, 0.0512, 1.75); stitch.receiveShadow = true; scene.add(stitch);
  }

  yield;
  // ---------- the PC tower: brushed side, a glass side panel with the parts inside, a vented front with the app's top line ----------
  const pc = new Group(); scene.add(pc);
  const TW = 1.15, TH = 2.5, TD = 2.5, TX = -7.6, TZ = 0.6;
  {
    const t = new Group(); t.position.set(TX, 0, TZ); pc.add(t);
    // a shell, not a block: a solid body filled the case, so nothing behind the glass ever showed (7 Nov.)
    const Y0 = 0.06, YC = TH / 2 + Y0;
    for (const [w, h, d, x, y, z] of [[0.05, TH, TD, TW / 2 - 0.025, YC, 0], [TW, 0.08, TD, 0, TH + Y0 - 0.04, 0], [TW, 0.1, TD, 0, Y0 + 0.05, 0], [TW, TH, 0.07, 0, YC, TD / 2 - 0.035], [TW, TH, 0.05, 0, YC, -TD / 2 + 0.025]]) { const p = rbox(w, h, d, 0.03, M.alu); p.position.set(x, y, z); t.add(p); }
    for (const [h, d, y, z] of [[0.1, TD - 0.1, TH + Y0 - 0.09, 0], [0.1, TD - 0.1, Y0 + 0.1, 0], [TH, 0.1, YC, TD / 2 - 0.08], [TH, 0.1, YC, -TD / 2 + 0.08]]) { const b = rbox(0.035, h, d, 0.012, M.alu); b.position.set(-TW / 2 + 0.018, y, z); t.add(b); }
    const meshTex = cvTex(256, 560, (g, w, h) => { g.fillStyle = cssv('--t-side'); g.fillRect(0, 0, w, h); g.fillStyle = cssv('--t-edge'); for (let y = 12; y < h - 12; y += 8) for (let x = 12 + ((y / 8) % 2) * 4; x < w - 12; x += 8) { g.beginPath(); g.arc(x, y, 2.6, 0, 7); g.fill(); } });
    const topMesh = new Mesh(new PlaneGeometry(TW - 0.22, TD - 0.62), new MeshStandardMaterial({ map: meshTex, roughness: 0.8, metalness: 0.3 })); topMesh.rotation.x = -Math.PI / 2; topMesh.position.set(0, TH + Y0 + 0.002, -0.2); t.add(topMesh);
    for (const [w, d, x] of [[0.1, 0.035, -0.28], [0.1, 0.035, -0.14], [0.06, 0.022, -0.02]]) { const s = rbox(w, 0.012, d, 0.006, M.rubber); s.position.set(x, TH + Y0, TD / 2 - 0.2); t.add(s); } // USB-A, USB-A, USB-C
    // feet
    // feet: they stand on the mat (top 0.05) and reach into the case's floor (from 0.06); until 9 Nov. they went from 0 to
    // 0.06, all but 1 cm of them inside the mat (3D-003 in lab/BUGS-DEFERRED.md)
    for (const [x, z] of [[-0.4, -1], [0.4, -1], [-0.4, 1], [0.4, 1]]) { const f = new Mesh(new CylinderGeometry(0.08, 0.09, 0.04, 16), M.rubber); f.position.set(x, 0.07, z); t.add(f); }
    // front panel (+z): fine vents and the top line, vertical
    const frontTex = cvTex(256, 560, (g, w, h) => {
      g.fillStyle = cssv('--t-side'); g.fillRect(0, 0, w, h);
      g.fillStyle = cssv('--t-edge');
      for (let y = 40; y < h - 40; y += 9) for (let x = 30 + ((y / 9) % 2) * 4; x < w - 60; x += 9) { g.beginPath(); g.arc(x, y, 2.4, 0, 7); g.fill(); }
      g.fillStyle = cssv('--t-surface3'); g.fillRect(w - 40, 40, 2, h - 80);
    });
    const front = new Mesh(new PlaneGeometry(TW - 0.12, TH - 0.12), new MeshStandardMaterial({ map: frontTex, roughness: 0.7, metalness: 0 }));
    front.position.set(0, TH / 2 + 0.06, TD / 2 + 0.002); t.add(front);
    const lineGeo = new PlaneGeometry(0.035, TH - 0.5);
    const topLine = new Mesh(lineGeo, new ShaderMaterial({
      uniforms: { c1: { value: C.top1 }, c2: { value: C.top2 }, c3: { value: C.top3 } },
      vertexShader: `varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }`,
      fragmentShader: `uniform vec3 c1,c2,c3; varying vec2 vUv; void main(){ float u = 1.-vUv.y; vec3 c = u < .5 ? mix(c1,c2,u*2.) : mix(c2,c3,u*2.-1.); gl_FragColor = vec4(c*1.15,1.); }`,
    }));
    topLine.position.set(TW / 2 - 0.2, TH / 2 + 0.06, TD / 2 + 0.004); t.add(glows(topLine));
    // power button
    const pb = new Mesh(new CylinderGeometry(0.06, 0.06, 0.02, 24), M.chrome); pb.rotation.x = Math.PI / 2; pb.position.set(-0.28, TH - 0.18, TD / 2 + 0.01); t.add(pb);
    const pl = new Mesh(new RingGeometry(0.062, 0.075, 32), new MeshBasicMaterial({ color: mul(C.top1, 1.2) })); pl.position.set(-0.28, TH - 0.18, TD / 2 + 0.021); t.add(pl);
    // glass side (-x, towards the camera) and what is inside
    const glass = new Mesh(new PlaneGeometry(TD - 0.24, TH - 0.24), M.glass); glass.rotation.y = -Math.PI / 2; glass.position.set(-TW / 2 - 0.003, TH / 2 + 0.06, 0); t.add(glass);
    const gs = glass.clone(); gs.material = M.glassSpec; gs.position.x -= 0.001; t.add(gs);
    // the parts, seen through the glass: a vertical GPU with its fans to the glass, a tower cooler, RAM, sleeved cables
    const pcbMat = new MeshStandardMaterial({ color: C.edge, roughness: 0.7, metalness: 0.15 });
    const part = (w, h, d, r, m, x, y, z) => { const p = rbox(w, h, d, r, m); p.position.set(x, y, z); t.add(p); return p; };
    part(0.03, 1.4, 1.4, 0.01, pcbMat, 0.36, 1.72, -0.45);                    // the board
    part(TW - 0.09, 0.42, TD - 0.1, 0.02, M.matteDark, -0.02, 0.37, 0);        // PSU shroud
    part(0.05, 0.26, 0.26, 0.01, M.metal, 0.32, 1.98, -0.55);                  // cooler's cold plate
    part(0.09, 0.12, 0.55, 0.02, M.metal, 0.3, 2.33, -0.6);                    // VRM heatsinks
    part(0.12, 0.45, 0.14, 0.02, M.metalDark, 0.28, 2.15, -1.05);              // rear I/O cover
    t.add(inst(unitBox, M.galv, Array.from({ length: 24 }, (_, i) => [-0.08, 1.72 + i * 0.023, -0.55, 0, 0, 0, 0.64, 0.006, 0.24]))); // fin stack
    t.add(inst(new CylinderGeometry(0.016, 0.016, 0.07, 8), M.galv, [-0.32, -0.18, -0.04, 0.1].flatMap(x => [[x, 2.28, -0.6], [x, 2.28, -0.5]])));
    t.add(inst(rgeo(0.19, 0.7, 0.035, 0.008), M.metalDark, [0, 1, 2, 3].map(i => [0.25, 1.95, -0.2 + i * 0.06])));        // RAM
    t.add(inst(rgeo(0.03, 0.7, 0.038, 0.008), M.galv, [0, 1, 2, 3].map(i => [0.145, 1.95, -0.2 + i * 0.06])));
    part(0.24, 0.6, 1.58, 0.03, M.metalDark, -0.32, 0.97, -0.35);              // the GPU
    part(0.015, 0.6, 1.58, 0.006, M.metal, -0.195, 0.97, -0.35);
    part(0.3, 0.1, 0.14, 0.02, M.matteDark, -0.3, 0.62, -1.0);                 // its vertical mount
    const shroudGeo = (() => { const s = rrect(new Shape(), -0.79, -0.3, 1.58, 0.6, 0.05); for (const z of [-0.52, 0, 0.52]) { const h = new Path(); h.absarc(z, 0, 0.21, 0, Math.PI * 2, true); s.holes.push(h); } return new ExtrudeGeometry(s, { depth: 0.03, bevelEnabled: true, bevelThickness: 0.008, bevelSize: 0.008, bevelSegments: 1, curveSegments: 24 }).rotateY(-Math.PI / 2); })();
    const shroud = new Mesh(shroudGeo, M.metal); shroud.position.set(-0.44, 0.97, -0.35); t.add(shroud);
    const bar = new Mesh(new PlaneGeometry(1.3, 0.014), new MeshBasicMaterial({ color: mul(C.top1, 0.8) })); bar.rotation.y = -Math.PI / 2; bar.position.set(-0.482, 1.235, -0.35); t.add(bar);
    // fans: [centre, facing, size, frame ring]
    const fans = [...[-0.87, -0.35, 0.17].map(z => [V3(-0.455, 0.97, z), new Euler(0, -Math.PI / 2, 0), 1, 0]), [V3(-0.08, 1.985, -0.4), new Euler(), 1.15, 1], [V3(-0.05, 2.0, -1.15), new Euler(), 1, 1], ...[0.95, 1.42, 1.89].map(y => [V3(-0.05, y, 1.15), new Euler(), 1, 1])];
    const blades = new InstancedMesh(new BoxGeometry(0.155, 0.07, 0.006).translate(0.1225, 0, 0), M.plastic, fans.length * 7);
    const hubs = new InstancedMesh(new CylinderGeometry(0.06, 0.06, 0.03, 16).rotateX(Math.PI / 2), M.metalDark, fans.length);
    const frames = new InstancedMesh(new TorusGeometry(0.215, 0.02, 6, 32), M.metalDark, fans.length);
    const FM = new Matrix4(), BM = new Matrix4(), TM = new Matrix4(), pitch = new Matrix4().makeRotationX(0.5), fq = new Quaternion(), fs = V3(1, 1, 1);
    let nb = 0, nr = 0;
    fans.forEach(([p, e, sc, ring], i) => { FM.compose(p, fq.setFromEuler(e), fs.setScalar(sc)); hubs.setMatrixAt(i, FM); if (ring) frames.setMatrixAt(nr++, FM); for (let k = 0; k < 7; k++) blades.setMatrixAt(nb++, TM.multiplyMatrices(FM, BM.makeRotationZ(k * Math.PI * 2 / 7 + i).multiply(pitch))); });
    frames.count = nr; t.add(blades, hubs, frames);
    // 6b: the GPU's three fans lit blue behind the glass, the case's interior reads from across the room
    t.add(glows(inst(new RingGeometry(0.17, 0.205, 40), new MeshBasicMaterial({ color: mul(C.top1, 1.3), side: DoubleSide }), [-0.87, -0.35, 0.17].map(z => [-0.472, 0.97, z, 0, -Math.PI / 2, 0]))));
    t.add(new Mesh(mergeGeometries([
      [V3(0.5, 1.5, 0.4), V3(0.36, 1.62, 0.42), V3(0.33, 1.8, 0.33), V3(0.33, 1.86, 0.27)],                 // 24-pin from the grommet
      [V3(0.5, 2.42, -1.02), V3(0.4, 2.44, -0.95), V3(0.34, 2.4, -0.9)],                                     // CPU power
      [V3(0.5, 0.75, 0.55), V3(0.1, 0.62, 0.62), V3(-0.25, 1.0, 0.52), V3(-0.32, 1.27, 0.35)],               // GPU power
    ].map(p => new TubeGeometry(new CatmullRomCurve3(p), 20, 0.028, 6))), M.plastic));
    const glow = new PointLight(C.top1, 4.5, 2.4, 2); glow.position.set(-0.25, 1.6, 0.2); t.add(glow); // the parts read through the glass (6c: 9 lit the top of the case white-hot behind the glass)
    shadowy(t); glass.castShadow = false; front.castShadow = false;
    blob(TX, TZ, 2.2, 3.4);
  }
  // the ports the two cables leave from
  const USB_PC = V3(TX + TW / 2, 0.42, TZ + 1.08);   // the USB-C port, on the face of the tower's side (9 Nov.: it was 0.03 off it)
  const LAN_PC = V3(TX - 0.15, 0.32, TZ - TD / 2 - 0.02);

  yield;
  // ---------- the monitor: thin panel, real app on screen, its light falls on the desk ----------
  const mon = new Group(); mon.position.set(-4.5, 0.05, 0.1); mon.rotation.y = -0.28; pc.add(mon);
  const SW = 3.42, SH = 2.21, SY = 2.5;
  {
    // thin bezels and a chin, a back shell with a VESA boss, a hinge, and a neck that reaches the foot (it stopped 0.24 above it)
    const panel = rbox(SW + 0.06, SH + 0.15, 0.06, 0.025, M.plastic); panel.position.set(0, SY - 0.045, 0); mon.add(panel);
    const shell = rbox(SW - 0.24, SH - 0.2, 0.09, 0.045, M.metalDark); shell.position.set(0, SY - 0.05, -0.07); mon.add(shell);
    const boss = rbox(0.8, 0.8, 0.14, 0.07, M.metalDark); boss.position.set(0, SY - 0.2, -0.16); mon.add(boss);
    const chin = new Mesh(new PlaneGeometry(SW + 0.02, 0.02), new MeshBasicMaterial({ color: C.lineStrong })); chin.position.set(0, SY - SH / 2 - 0.035, 0.031); mon.add(chin);
    const mh = new Mesh(new CylinderGeometry(0.06, 0.06, 0.36, 20), M.metal); mh.rotation.z = Math.PI / 2; mh.position.set(0, SY - 0.25, -0.26); mon.add(mh);
    const ny0 = 0.04, nz0 = -0.4, ny1 = SY - 0.25, nz1 = -0.3, NL = Math.hypot(ny1 - ny0, nz1 - nz0);
    const neckSh = rrect(new Shape(), -0.17, -NL / 2, 0.34, NL, 0.06); neckSh.holes.push(rrect(new Path(), -0.08, -NL / 2 + 0.35, 0.16, 0.42, 0.06));
    const neck = new Mesh(new ExtrudeGeometry(neckSh, { depth: 0.07, bevelEnabled: true, bevelThickness: 0.01, bevelSize: 0.01, bevelSegments: 2, curveSegments: 6 }).translate(0, 0, -0.035), M.matteDark);
    neck.position.set(0, (ny0 + ny1) / 2, (nz0 + nz1) / 2); neck.rotation.x = Math.atan2(nz1 - nz0, ny1 - ny0); mon.add(neck);
    const foot = rbox(1.36, 0.035, 0.78, 0.017, M.matteDark); foot.position.set(0, 0.0175, -0.25); mon.add(foot);
    const footBoss = rbox(0.44, 0.05, 0.24, 0.02, M.matteDark); footBoss.position.set(0, 0.04, -0.4); mon.add(footBoss);
    shadowy(mon);
    blob(-4.5, 0, 2.2, 1.6, 0.28);
  }
  // the screen: the real Connection page, with the IP the server sees patched in as the story changes it
  const scrCv = document.createElement('canvas'); scrCv.width = 1240; scrCv.height = 800;
  const scrG = scrCv.getContext('2d');
  scrG.fillStyle = cssv('--t-surface'); scrG.fillRect(0, 0, 1240, 800);
  const scrTex = new CanvasTexture(scrCv); scrTex.colorSpace = SRGBColorSpace; scrTex.anisotropy = 8; scrTex.minFilter = LinearFilter; scrTex.generateMipmaps = false;
  const screenMat = new MeshBasicMaterial({ map: scrTex, toneMapped: false, fog: false }); screenMat.color.setScalar(0.78);
  const screen = new Mesh(new PlaneGeometry(SW, SH), screenMat); screen.position.set(0, SY, 0.037); mon.add(glows(screen)); // 6b: lit screens show in the floor too
  const screenLight = new RectAreaLight(mul(C.text, 1), 3.6, SW, SH); screenLight.position.set(0, SY, 0.06); mon.add(screenLight); // faces +z by default
  let scrImg = null, scrIP = '';
  function drawScreen(ip) {
    if (!scrImg) return;
    scrIP = ip;
    scrG.drawImage(scrImg, 0, 0, 1240, 800);
    // the two places the app shows the IP servers see (as on the page's own window): the status bar and the Status card
    const patch = (x, y, w, h, size, col) => {
      scrG.fillStyle = col; scrG.fillRect(x, y, w, h);
      scrG.fillStyle = cssv('--t-text'); scrG.font = `600 ${size}px "Fragment Mono", Consolas, monospace`; scrG.textBaseline = 'middle';
      scrG.fillText(ip, x + 2, y + h / 2 + 1);
    };
    if (!opts.plainScreen) patch(1240 * 0.708, 800 * 0.022, 1240 * 0.076, 800 * 0.023, 11.5, cssv('--t-bg'));
    if (!opts.plainScreen) patch(1240 * 0.09, 800 * 0.201, 1240 * 0.15, 800 * 0.039, 19, cssv('--t-surface'));
    scrTex.needsUpdate = true;
  }

  // keyboard and mouse
  {
    const kb = new Group(); kb.position.set(-4.25, 0.05, 1.75); kb.rotation.y = -0.28; pc.add(kb);
    // a 75% layout on a deck tilted 3 degrees: a function row with its gaps, the wide keys at their real widths
    const deck = new Group(); deck.rotation.x = 0.05; kb.add(deck);
    const base = rbox(2.85, 0.07, 1.05, 0.03, M.metalDark); base.position.y = 0.035; deck.add(base);
    const ROWS = [[1, -1, 1, 1, 1, 1, -0.5, 1, 1, 1, 1, -0.5, 1, 1, 1, 1], [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2], [1.5, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1.5], [1.75, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.25], [2.25, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2.75], [1.25, 1.25, 1.25, 6.25, 1.25, 1.25, 1.25, 1.25]];
    const P = 0.165, keyGeo = rgeo(0.15, 0.05, 0.135, 0.02, 2), keys = new InstancedMesh(keyGeo, M.keycap, 80), o = new Object3D(); let n = 0;
    ROWS.forEach((row, r) => { let x = -15 * P / 2; for (const w of row) { if (w < 0) { x -= w * P; continue; } o.position.set(x + w * P / 2, 0.09, -0.42 + r * 0.15 + (r ? 0.04 : 0)); o.scale.set((w * P - 0.015) / 0.15, 1, 1); o.updateMatrix(); keys.setMatrixAt(n++, o.matrix); x += w * P; } });
    keys.count = n;
    deck.add(keys);
    // 6b: a blue backlight between the keys, so the keyboard reads on the dark mat (keys and mat were the same black)
    const under = new Mesh(new PlaneGeometry(2.5, 0.88), new MeshBasicMaterial({ color: mul(C.top1, 0.6) })); under.rotation.x = -Math.PI / 2; under.position.set(0, 0.0712, -0.025); deck.add(under);
    const mouse = new Mesh(new SphereGeometry(0.15, 24, 16), M.keycap); mouse.scale.set(0.75, 0.35, 1.15); mouse.position.set(1.9, 0.06, 0.1); kb.add(mouse);
    const wheel = new Mesh(new CylinderGeometry(0.022, 0.022, 0.014, 12), M.rubber); wheel.rotation.z = Math.PI / 2; wheel.position.set(1.9, 0.108, 0.01); kb.add(wheel);
    shadowy(kb); blob(-4.25, 1.75, 3.2, 1.3, 0.28);
  }

  yield;
  // ---------- the phone on a stand, its screen shows its real state ----------
  const phone = new Group(); phone.position.set(-0.55, 0.05, 2.85); phone.rotation.y = -0.6; scene.add(phone);
  const PW = 0.9, PH = 1.86, PD = 0.085, TILT = -0.36;
  // LIFT (9 Nov., session 5): the phone sits 0.1 higher on its stand, so the USB-C plug in its bottom edge and the plug's
  // slide out of the port clear the stand's plate (at 0.16 the plug could only go in at a slant, through the plate)
  const LIFT = 0.1, PLATE_TOP = 0.09, phParts = { hooks: [] };
  const tilt = new Group(); tilt.position.set(0, 0.16 + LIFT, 0.12); tilt.rotation.x = TILT; phone.add(tilt);
  {
    const stand = new Group(); phone.add(stand);
    const plate = rbox(1.0, 0.04, 0.95, 0.02, M.matte); plate.position.set(0, 0.02, -0.08); stand.add(plate);
    // the phone rests on the stand in its own tilt (it floated 0.2 in front of the old back), two hooks hold its bottom edge
    // with a slot between them for the USB-C plug, a kickstand on a hinge holds it up
    // the back rest runs down to the plate and its foot meets the plate's top
    const restZ = -PD / 2 - 0.022, restB = (PLATE_TOP - phone.position.y - 0.002 - (0.16 + LIFT) + restZ * Math.sin(TILT)) / Math.cos(TILT), restT = 1.125;
    const rest = rbox(0.62, restT - restB, 0.04, 0.02, M.matte); rest.position.set(0, (restT + restB) / 2, restZ); tilt.add(rest);
    // the phone's USB-C port, a dark slot in the middle of its bottom edge: the plug's metal goes in here
    const port = new Mesh(new PlaneGeometry(0.104, 0.036), new MeshBasicMaterial({ color: C.edge })); port.rotation.x = Math.PI / 2; port.position.set(0, -0.0006, 0); tilt.add(port);
    Object.assign(phParts, { plate, rest });
    for (const x of [-0.3, 0.3]) { const h = rbox(0.24, 0.035, 0.17, 0.012, M.matte); h.position.set(x, -0.018, 0); tilt.add(h); phParts.hooks.push(h); const l = rbox(0.24, 0.08, 0.03, 0.012, M.matte); l.position.set(x, 0.005, PD / 2 + 0.02); tilt.add(l); phParts.hooks.push(l); }
    const tl = (y, z) => [0.16 + LIFT + y * Math.cos(TILT) - z * Math.sin(TILT), 0.12 + y * Math.sin(TILT) + z * Math.cos(TILT)];
    const [hy, hz] = tl(0.78, -PD / 2 - 0.06), fy = 0.04, fz = -0.48;
    const leg = rbox(0.48, Math.hypot(hy - fy, hz - fz), 0.035, 0.015, M.matte); leg.position.set(0, (hy + fy) / 2, (hz + fz) / 2); leg.rotation.x = Math.atan2(hz - fz, hy - fy); stand.add(leg);
    const hinge = new Mesh(new CylinderGeometry(0.03, 0.03, 0.54, 16), M.matte); hinge.rotation.z = Math.PI / 2; hinge.position.set(0, hy, hz); stand.add(hinge);
    const btn = (x, y, h) => { const b = rbox(0.03, h, 0.04, 0.012, M.chrome); b.position.set(x, y, 0); tilt.add(b); };
    if (opts.ios) { btn(-PW / 2 - 0.008, PH - 0.42, 0.1); btn(-PW / 2 - 0.008, PH - 0.62, 0.17); btn(-PW / 2 - 0.008, PH - 0.84, 0.17); btn(PW / 2 + 0.008, PH - 0.7, 0.26); }
    else { btn(PW / 2 + 0.008, PH - 0.48, 0.13); btn(PW / 2 + 0.008, PH - 0.78, 0.28); }
    const body = rbox(PW, PH, PD, 0.1, M.metalDark, 4); body.position.set(0, PH / 2, 0); tilt.add(body); phParts.body = body;
    if (opts.ios) { const cam = rbox(0.36, 0.36, 0.035, 0.08, M.metalDark); cam.position.set(-0.2, PH - 0.3, -PD / 2 - 0.014); tilt.add(cam); for (const [lx, ly] of [[-0.08, 0.08], [-0.08, -0.08], [0.08, 0]]) { const l = new Mesh(new CylinderGeometry(0.055, 0.055, 0.03, 20), M.chrome); l.rotation.x = Math.PI / 2; l.position.set(-0.2 + lx, PH - 0.3 + ly, -PD / 2 - 0.035); tilt.add(l); } }
    else { const cam = rbox(0.7, 0.18, 0.035, 0.08, M.plastic); cam.position.set(0, PH - 0.25, -PD / 2 - 0.014); tilt.add(cam); }
    shadowy(phone); phone.userData.noBake = true; blobAt(-0.55, 0.053, 2.85, 1.3, 1.3, 0.6); // it hides in some shots: a soft blob of its own
  }
  const phCv = document.createElement('canvas'); phCv.width = 512; phCv.height = 1060;
  const phTex = new CanvasTexture(phCv); phTex.colorSpace = SRGBColorSpace; phTex.anisotropy = 8;
  const phScreen = new Mesh(new PlaneGeometry(PW - 0.07, PH - 0.08), new MeshBasicMaterial({ map: phTex, toneMapped: false, fog: false }));
  phScreen.material.color.setScalar(0.85);
  phScreen.position.set(0, PH / 2, PD / 2 + 0.002); tilt.add(glows(phScreen));
  { const pg = new Mesh(phScreen.geometry, M.phoneGlass); pg.position.copy(phScreen.position); pg.position.z += 0.001; tilt.add(pg); } // its glass
  let phState = '';
  function drawPhone(data, tether) {
    const k = data + '|' + tether; if (k === phState) return; phState = k;
    const g = phCv.getContext('2d'), W = 512, H = 1060;
    g.fillStyle = cssv('--t-edge'); g.fillRect(0, 0, W, H);
    g.font = '600 30px "Funnel Sans", sans-serif'; g.fillStyle = cssv('--t-text'); g.textBaseline = 'alphabetic';
    g.fillText('21:14', opts.ios ? 56 : 40, 64);
    if (!opts.ios) { g.fillStyle = cssv('--t-edge'); g.beginPath(); g.arc(256, 48, 12, 0, 7); g.fill(); g.strokeStyle = cssv('--t-line-strong'); g.lineWidth = 2; g.stroke(); g.fillStyle = cssv('--t-text'); }
    if (opts.ios) { g.strokeStyle = cssv('--t-line-strong'); g.lineWidth = 3; g.fillStyle = cssv('--t-edge'); g.beginPath(); if (g.roundRect) g.roundRect(196, 30, 120, 40, 20); else g.rect(196, 30, 120, 40); g.fill(); g.stroke(); g.fillStyle = cssv('--t-text'); }
    g.fillStyle = data ? cssv('--t-text') : cssv('--t-faint'); g.font = '600 26px "Funnel Sans", sans-serif'; if (data && !opts.ios) g.fillText('5G', 330, 64);
    for (let i = 0; i < 4; i++) { g.fillStyle = data ? cssv('--t-text') : cssv('--t-line-strong'); g.fillRect(380 + i * 14, 66 - (i + 1) * 8, 9, (i + 1) * 8); }
    g.strokeStyle = cssv('--t-muted'); g.lineWidth = 3; g.strokeRect(446, 42, 34, 22); g.fillStyle = cssv('--t-muted'); g.fillRect(450, 46, 22, 14);
    const tile = (y, label, sub, on) => {
      g.fillStyle = on ? cssv('--t-accent-deep') : cssv('--t-surface2');
      g.beginPath(); if (g.roundRect) g.roundRect(32, y, 448, 170, 34); else g.rect(32, y, 448, 170); g.fill();
      g.fillStyle = cssv('--t-text'); let fs = 40; g.font = '600 40px "Funnel Sans", sans-serif'; while (g.measureText(label).width > 268 && fs > 26) { fs -= 2; g.font = '600 ' + fs + 'px "Funnel Sans", sans-serif'; } g.fillText(label, 70, y + 74);
      g.fillStyle = on ? cssv('--t-text') : cssv('--t-faint'); g.font = '400 32px "Funnel Sans", sans-serif'; g.fillText(sub, 70, y + 126);
      // switch
      g.fillStyle = on ? cssv('--t-accent') : cssv('--t-line-strong');
      g.beginPath(); if (g.roundRect) g.roundRect(352, y + 64, 92, 48, 24); else g.rect(352, y + 64, 92, 48); g.fill();
      g.fillStyle = cssv('--t-text'); g.beginPath(); g.arc(on ? 420 : 376, y + 88, 18, 0, 7); g.fill();
    };
    if (opts.ios) { tile(170, 'Airplane Mode', data ? 'Off' : 'On', !data); tile(370, 'Personal Hotspot', tether && data ? 'On' : 'Off', tether && data); }
    else { tile(170, 'Mobile data', data ? 'On' : 'Off', data); tile(370, 'USB tethering', tether ? 'On' : 'Off', tether); }
    g.fillStyle = cssv('--t-faint'); g.font = '400 28px "Funnel Sans", sans-serif';
    g.fillText(!tether ? 'Not connected to a PC' : data ? 'Sharing mobile data with the PC' : 'No mobile data to share', 40, 620);
    phTex.needsUpdate = true;
  }
  phone.updateMatrixWorld(true);
  const tw = (x, y, z) => tilt.localToWorld(V3(x, y, z));
  const PORT = tw(0, 0.0, 0);             // the phone's USB-C port, bottom edge
  const PHONE_TOP = tw(0, PH + 0.05, 0);   // where the radio link leaves
  const PHONE_FRONT = tw(0, 0, 1).sub(tw(0, 0, 0)).normalize();

  yield;
  // ---------- the carrier's cell site: a monopole with three sector antennas, a dish, a cabinet and a fence ----------
  const mastBase = V3(5.5, 0, -6.5), mastH = 8.2;
  const mastTop = mastBase.clone().add(V3(0, mastH + 0.15, 0));
  {
    const s = new Group(); s.position.copy(mastBase); scene.add(s);
    const pole = new Mesh(new CylinderGeometry(0.16, 0.34, mastH, 28, 1), M.galv); pole.position.y = mastH / 2; s.add(glows(pole)); // 6b: the lit mast streaks down the wet floor
    for (const y of [2.6, 5.2]) { const j = new Mesh(new CylinderGeometry(0.3 - y * 0.022, 0.3 - y * 0.022, 0.12, 28), M.galv); j.position.y = y; s.add(j); } // the flanged joints
    const tray = rbox(0.08, mastH - 0.6, 0.05, 0.015, M.metalDark); tray.position.set(0.25, (mastH - 0.6) / 2, 0.12); tray.rotation.z = -0.017; s.add(tray);
    const ring = new Mesh(new TorusGeometry(0.62, 0.035, 8, 48), M.galv); ring.rotation.x = Math.PI / 2; ring.position.y = mastH - 0.35; s.add(ring);
    const ring2 = ring.clone(); ring2.position.y = mastH - 1.55; s.add(ring2);
    // each sector: a panel on its own pipe with a 4 degree downtilt, its radio strapped to the pole, jumpers with a drip loop
    const brk = [], feeds = [], pipeGeo = new CylinderGeometry(0.025, 0.025, 1.9, 8);
    for (let k = 0; k < 3; k++) {
      const a = (k / 3) * Math.PI * 2 + 0.4, ca = Math.cos(a), sa = Math.sin(a), ry = -a + Math.PI / 2;
      const at = (r, dy, sd = 0) => V3(ca * r - sa * sd, mastH + dy, sa * r + ca * sd);
      const arm = rbox(0.62, 0.05, 0.05, 0.012, M.galv); arm.position.copy(at(0.31, -0.95)); arm.rotation.y = -a; s.add(arm);
      const ant = rbox(0.3, 1.45, 0.12, 0.05, M.radome); ant.rotation.order = 'YXZ'; ant.rotation.set(0.07, ry, 0); ant.position.copy(at(0.64, -0.95)); s.add(ant);
      const pipe = new Mesh(pipeGeo, M.galv); pipe.position.copy(at(0.48, -0.95)); s.add(pipe);
      for (const dy of [0.55, -0.55]) { const back = 0.58 + Math.sin(0.07) * dy; brk.push([...at((0.505 + back) / 2, -0.95 + dy).toArray(), 0, ry, 0, 0.035, 0.04, back - 0.505]); }
      const rru = rbox(0.2, 0.32, 0.12, 0.02, M.metalDark); rru.position.copy(at(0.42, -1.9)); rru.rotation.y = ry; s.add(rru);
      for (const dy of [0.09, -0.09]) brk.push([...at(0.29, -1.9 + dy).toArray(), 0, ry, 0, 0.05, 0.035, 0.16]);
      for (const sd of [-0.05, 0.05]) feeds.push([at(0.42, -1.74, sd), at(0.47, -1.64, sd), at(0.56, -1.86, sd), at(0.6, -1.69, sd)]);
      let d = a - 0.45; d = Math.atan2(Math.sin(d), Math.cos(d));
      feeds.push([0, 0.4, 0.8].map(f => V3(Math.cos(0.45 + d * f) * 0.3, mastH - 2.6 + f * 0.4, Math.sin(0.45 + d * f) * 0.3)).concat([at(0.42, -2.07)]));
    }
    s.add(inst(unitBox, M.galv, brk));
    const pegs = []; // step bolts, alternating, from 2.4 m up (real ones start out of reach)
    for (let y = 2.4, i = 0; y < mastH - 3.0; y += 0.19, i++) { if (Math.abs(y - 2.6) < 0.1 || Math.abs(y - 5.2) < 0.1) continue; const th = 1.95 + (i % 2 ? 0.2 : -0.2), r = 0.34 - 0.18 * y / mastH + 0.06; pegs.push([Math.cos(th) * r, y, Math.sin(th) * r, 0, Math.PI - th, Math.PI / 2]); }
    s.add(inst(new CylinderGeometry(0.011, 0.011, 0.16, 6), M.galv, pegs));
    const dishG = new Group(); dishG.position.set(-0.4, mastH - 2.7, 0.26); dishG.rotation.y = -0.9; s.add(dishG);
    const dish = new Mesh(new SphereGeometry(0.34, 28, 10, 0, Math.PI * 2, 0, 0.55), new MeshStandardMaterial({ color: mul(C.muted, 0.62), roughness: 0.6, side: DoubleSide })); dish.rotation.x = -Math.PI / 2; dish.position.z = 0.29; dishG.add(dish);
    const dishRim = new Mesh(new TorusGeometry(0.175, 0.012, 6, 32), M.galv); dishRim.position.z = 0.13; dishG.add(dishRim);
    const feed = new Mesh(new CylinderGeometry(0.012, 0.012, 0.25, 6), M.galv); feed.rotation.x = Math.PI / 2; feed.position.z = 0.24; dishG.add(feed);
    const mount = rbox(0.05, 0.05, 0.3, 0.012, M.galv); mount.position.z = -0.12; dishG.add(mount);
    const cab = rbox(1.1, 1.5, 0.75, 0.04, M.powder); cab.position.set(1.3, 0.75, 0.5); s.add(cab);
    const door = new Mesh(new PlaneGeometry(0.004, 1.3), new MeshBasicMaterial({ color: C.surface })); door.position.set(1.3, 0.75, 0.876); s.add(door);
    // the cabinet's roof and handles, and the ice bridge that carries the feeders from the pole to it
    const roofC = rbox(1.22, 0.05, 0.86, 0.02, M.metal); roofC.position.set(1.3, 1.53, 0.5); s.add(roofC);
    for (const x of [1.22, 1.38]) { const h = rbox(0.03, 0.18, 0.04, 0.01, M.chrome); h.position.set(x, 0.85, 0.89); s.add(h); }
    for (const z of [0.26, 0.44]) { const r = rbox(0.95, 0.03, 0.03, 0.008, M.galv); r.position.set(0.75, 1.95, z); s.add(r); }
    s.add(inst(unitBox, M.galv, [0.35, 0.51, 0.67, 0.83, 0.99, 1.15].map(x => [x, 1.95, 0.35, 0, 0, 0, 0.03, 0.02, 0.18])));
    const post = new Mesh(new CylinderGeometry(0.03, 0.03, 1.83, 8), M.galv); post.position.set(0.5, 1.035, 0.35); s.add(post);
    feeds.push([V3(0.25, 2.4, 0.16), V3(0.32, 2.0, 0.34), V3(1.0, 1.99, 0.35), V3(1.15, 1.75, 0.38), V3(1.2, 1.52, 0.4)]);
    s.add(new Mesh(mergeGeometries(feeds.map((p, i) => new TubeGeometry(new CatmullRomCurve3(p), 16, i % 3 === 2 ? 0.02 : 0.011, 5))), M.rubber));
    const pad = rbox(4.2, 0.12, 3.6, 0.03, M.concrete); pad.position.set(0.6, 0.06, 0.2); s.add(pad); aoRecv.push(pad);
    aoPlane(4.14, 3.54, mastBase.x + 0.6, 0.122, mastBase.z + 0.2);
    // a low fence round the site
    const posts = [], rail = [];
    const fx0 = -1.5, fx1 = 2.7, fz0 = -1.6, fz1 = 2.0;
    const corners = [[fx0, fz0], [fx1, fz0], [fx1, fz1], [fx0, fz1]];
    for (let i = 0; i < 4; i++) {
      const [ax, az] = corners[i], [bx, bz] = corners[(i + 1) % 4], n = Math.round(Math.hypot(bx - ax, bz - az) / 0.7);
      for (let j = 0; j < n; j++) posts.push([lerp(ax, bx, j / n), lerp(az, bz, j / n)]);
      rail.push([ax, az, bx, bz]);
    }
    const postGeo = new CylinderGeometry(0.025, 0.025, 1.1, 6);
    const pm = new InstancedMesh(postGeo, M.galv, posts.length); const o = new Object3D();
    posts.forEach(([x, z], i) => { o.position.set(x, 0.55, z); o.updateMatrix(); pm.setMatrixAt(i, o.matrix); }); s.add(pm);
    for (const [ax, az, bx, bz] of rail) for (const y of [1.05, 0.55]) {
      const len = Math.hypot(bx - ax, bz - az), r = new Mesh(new CylinderGeometry(0.015, 0.015, len, 5), M.galv);
      r.rotation.z = Math.PI / 2; r.rotation.y = -Math.atan2(bz - az, bx - ax); r.position.set((ax + bx) / 2, y, (az + bz) / 2); s.add(r);
    }
    shadowy(s);
    blob(mastBase.x + 0.6, mastBase.z + 0.2, 5, 4.4);
  }
  // the aviation light: real masts carry one; it blinks slowly, as theirs do
  const beacon = new Mesh(new SphereGeometry(0.07, 12, 12), new MeshBasicMaterial({ color: C.bad }));
  beacon.position.copy(mastBase).add(V3(0, mastH + 0.05, 0)); scene.add(glows(beacon));

  yield;
  // ---------- the CS2 server: three cabinets on a raised floor; the middle one is the server you play on ----------
  const serverTop = V3(SX, 4.62, SZ);
  const RW = 1.75, RH = 4.6, RD = 1.5;
  // the servers behind the mesh doors: 1U, 2U and 4U machines with drive bays, ears and rails, a blanking panel every 7th U
  const unitTex = cvTex(512, 1024, (g, w, h) => {
    const v = cssv, U = h / 30;
    g.fillStyle = v('--t-edge'); g.fillRect(0, 0, w, h);
    g.fillStyle = v('--t-surface2'); g.fillRect(0, 0, 20, h); g.fillRect(w - 20, 0, 20, h);
    g.fillStyle = v('--t-edge'); for (let y = 0; y < h; y += U / 3) { g.fillRect(7, y + 3, 6, 6); g.fillRect(w - 13, y + 3, 6, 6); }
    const server = (y, n, alt) => {
      const hh = n * U - 3;
      g.fillStyle = alt ? v('--t-surface3') : v('--t-surface2'); g.fillRect(24, y + 1, w - 48, hh);
      g.fillStyle = v('--t-side'); g.fillRect(24, y + 1, 12, hh); g.fillRect(w - 36, y + 1, 12, hh);
      g.fillStyle = v('--t-edge');
      if (n >= 4) { for (let yy = y + 8; yy < y + hh - 6; yy += 9) for (let xx = 74 + ((yy / 9) % 2) * 5; xx < w - 60; xx += 10) { g.beginPath(); g.arc(xx, yy, 3, 0, 7); g.fill(); } return; }
      for (let k = 0; k < 4; k++) g.fillRect(70 + k * 10, y + 4, 5, hh - 8);
      for (let r = 0; r < n; r++) for (let b = 0; b < 9; b++) { const bx = 120 + b * 34, by = y + 3 + r * U; g.fillStyle = v('--t-side'); g.fillRect(bx, by, 30, U - 7); g.fillStyle = v('--t-line-strong'); g.fillRect(bx + 3, by + U - 12, 24, 2); }
    };
    let u = 0;
    for (const b of [[1, 1, 2, 2], [2, 1, 1, 1, 1], [4, 2], [1, 1, 1, 1, 2], [2]]) {
      let alt = false; for (const n of b) { server(u * U, n, alt); alt = !alt; u += n; }
      if (u < 30) { g.fillStyle = v('--t-surface'); g.fillRect(24, u * U + 1, w - 48, U - 3); g.fillStyle = v('--t-line-strong'); for (const x of [30, w - 30]) { g.beginPath(); g.arc(x, u * U + U / 2, 3, 0, 7); g.fill(); } u++; }
    }
  });
  const doorTex = cvTex(256, 512, (g, w, h) => {
    g.fillStyle = '#000'; g.fillRect(0, 0, w, h); g.fillStyle = '#fff';
    for (let y = 8; y < h - 8; y += 6) for (let x = 8 + ((y / 6) % 2) * 3; x < w - 8; x += 6) { g.beginPath(); g.arc(x, y, 1.9, 0, 7); g.fill(); }
  }, false);
  const ledPts = [];
  {
    const s = new Group(); scene.add(s);
    // a raised floor on pedestals (the void shows at its edge), perforated tiles in the cold aisle, a cable ladder overhead
    const slab = rbox(7.2, 0.045, 3.2, 0.015, M.powder); slab.position.set(SX, 0.1575, SZ); s.add(slab);
    aoPlane(7.1, 3.1, SX, 0.186, SZ);
    const ped = []; for (let i = 0; i <= 11; i++) for (let j = 0; j <= 5; j++) ped.push([SX - 3.55 + i * 7.1 / 11, 0.0675, SZ - 1.55 + j * 0.62]);
    s.add(inst(new CylinderGeometry(0.03, 0.045, 0.135, 8), M.galv, ped));
    const tileLines = new Mesh(new PlaneGeometry(7.1, 3.1), new MeshStandardMaterial({ roughness: 0.7, metalness: 0, map: cvTex(512, 224, (g, w, h) => { g.fillStyle = cssv('--t-surface2'); g.fillRect(0, 0, w, h); g.strokeStyle = cssv('--t-edge'); g.lineWidth = 3; for (let x = 0; x <= w; x += w / 11) { g.beginPath(); g.moveTo(x, 0); g.lineTo(x, h); g.stroke(); } for (let y = 0; y <= h; y += h / 5) { g.beginPath(); g.moveTo(0, y); g.lineTo(w, y); g.stroke(); } }) }));
    tileLines.receiveShadow = true; tileLines.rotation.x = -Math.PI / 2; tileLines.position.set(SX, 0.181, SZ); s.add(tileLines);
    const perfMat = new MeshStandardMaterial({ roughness: 0.6, metalness: 0.4, map: cvTex(128, 128, (g, w, h) => { g.fillStyle = cssv('--t-surface3'); g.fillRect(0, 0, w, h); g.fillStyle = cssv('--t-edge'); for (let y = 14; y < h - 10; y += 8) for (let x = 14; x < w - 10; x += 8) { g.beginPath(); g.arc(x, y, 2.6, 0, 7); g.fill(); } }) });
    const perf = inst(new PlaneGeometry(0.625, 0.6), perfMat, [2, 5, 8].map(i => [SX - 3.55 + (i + 0.5) * 7.1 / 11, 0.183, SZ + 1.24, -Math.PI / 2])); perf.receiveShadow = true; s.add(perf);
    const TY = 0.18 + RH + 0.42, TZc = SZ - 0.45;
    for (const dz of [-0.22, 0.22]) { const r = rbox(5.9, 0.07, 0.025, 0.008, M.powder); r.position.set(SX, TY, TZc + dz); s.add(r); }
    const lad = []; for (let x = -2.85; x <= 2.86; x += 0.3) lad.push([SX + x, TY - 0.02, TZc, 0, 0, 0, 0.025, 0.02, 0.44]);
    for (const x of [SX - 2.05, SX, SX + 2.05]) for (const dx of [-0.55, 0.55]) lad.push([x + dx, (TY + 4.84) / 2, TZc + 0.22, 0, 0, 0, 0.02, TY - 4.84, 0.02]);
    s.add(inst(unitBox, M.powder, lad));
    s.add(new Mesh(mergeGeometries([[V3(SX - 2.9, TY + 0.05, TZc), V3(SX + 2.9, TY + 0.05, TZc)], ...[SX - 2.05, SX, SX + 2.05].map(x => [V3(x + 0.4, TY + 0.05, TZc), V3(x + 0.4, TY + 0.04, TZc - 0.3), V3(x + 0.4, TY - 0.15, SZ - 0.62), V3(x + 0.4, 4.84, SZ - 0.6)])].map(p => new TubeGeometry(new CatmullRomCurve3(p), 16, 0.05, 6))), M.rubber));
    const doorFrameGeo = (() => { const w = RW - 0.08, h = RH - 0.12, b = 0.07, sh = rrect(new Shape(), -w / 2, -h / 2, w, h, 0.02); sh.holes.push(rrect(new Path(), -w / 2 + b, -h / 2 + b, w - 2 * b, h - 2 * b, 0.01)); return new ExtrudeGeometry(sh, { depth: 0.03, bevelEnabled: true, bevelThickness: 0.006, bevelSize: 0.006, bevelSegments: 1 }); })();
    const hinges = [];
    for (const [i, x] of [[0, SX - 2.05], [1, SX], [2, SX + 2.05]]) {
      const r = new Group(); r.position.set(x, 0.18, SZ); s.add(r);
      const frame = rbox(RW, RH, RD, 0.04, M.powder); frame.position.y = RH / 2; r.add(frame);
      const units = new Mesh(new PlaneGeometry(RW - 0.2, RH - 0.3), new MeshStandardMaterial({ map: unitTex, roughness: 0.5, metalness: 0.6 }));
      units.position.set(0, RH / 2, RD / 2 + 0.005); r.add(units);
      const door = new Mesh(new PlaneGeometry(RW - 0.12, RH - 0.16), new MeshStandardMaterial({ color: C.surface3, metalness: 0, roughness: 0.6, alphaMap: doorTex, transparent: true, side: DoubleSide }));
      door.position.set(0, RH / 2, RD / 2 + 0.05); r.add(door);
      const handle = rbox(0.04, 0.5, 0.05, 0.015, M.chrome); handle.position.set(RW / 2 - 0.16, RH / 2, RD / 2 + 0.09); r.add(handle);
      const df = new Mesh(doorFrameGeo, M.metalDark); df.position.set(0, RH / 2, RD / 2 + 0.035); r.add(df);
      const hp = rbox(0.08, 0.62, 0.02, 0.01, M.metalDark); hp.position.set(RW / 2 - 0.16, RH / 2, RD / 2 + 0.065); r.add(hp);
      for (const f of [0.15, 0.5, 0.85]) hinges.push([x - RW / 2 + 0.02, 0.18 + RH * f, SZ + RD / 2 + 0.05]);
      const top = rbox(RW, 0.06, RD, 0.02, M.metal); top.position.y = RH + 0.03; r.add(top);
      // power and activity lights on each unit: the middle cabinet's blink with the game's packets
      const U = (RH - 0.3) / 30;
      for (let u = 0; u < 30; u++) {
        if (u % 7 === 6) continue;
        const y = 0.18 + 0.15 + RH - 0.3 - (u + 0.5) * U;
        ledPts.push([x - RW / 2 + 0.24, y, SZ + RD / 2 + 0.085, i === 1 ? 1 : 0, Math.random()]);
        // 6b: a light on two drive bays too, so each cabinet is rows of green, not one dotted line
        if (u % 2 === 0) for (const b of [2, 6]) ledPts.push([x - (RW - 0.2) / 2 + (135 + b * 34) * (RW - 0.2) / 512, y - U * 0.28, SZ + RD / 2 + 0.085, i === 1 ? 1 : 0, Math.random()]);
      }
      shadowy(r); door.castShadow = false;
    }
    s.add(inst(new CylinderGeometry(0.025, 0.025, 0.16, 10), M.galv, hinges));
    // where the route comes down into the server: a cable entry on the middle cabinet's roof
    const entry = new Mesh(new CylinderGeometry(0.16, 0.2, 0.18, 20), M.metal); entry.position.set(SX, 0.18 + RH + 0.12, SZ); s.add(entry);
    blob(SX, SZ, 8.4, 4.2);
  }
  const leds = (() => {
    const n = ledPts.length, pos = new Float32Array(n * 3), kind = new Float32Array(n), seed = new Float32Array(n);
    ledPts.forEach(([x, y, z, k, s], i) => { pos.set([x, y, z], i * 3); kind[i] = k; seed[i] = s; });
    const g = new BufferGeometry(); g.setAttribute('position', new BufferAttribute(pos, 3)); g.setAttribute('aK', new BufferAttribute(kind, 1)); g.setAttribute('aSeed', new BufferAttribute(seed, 1));
    const m = new ShaderMaterial({
      transparent: true, depthWrite: false, blending: AdditiveBlending,
      uniforms: { uTime: { value: 0 }, uG: { value: C.good }, uB: { value: C.bad }, uK: { value: 1 }, uPx: { value: 1 }, uVH: { value: 900 }, uAct: { value: 1 }, uBan: { value: 0 } },
      vertexShader: `attribute float aK,aSeed; uniform float uTime,uPx,uVH,uAct,uBan; varying float vA; varying float vBan;
      void main(){ vec4 mv = modelViewMatrix*vec4(position,1.);
        float tick = floor(uTime*14. + aSeed*31.); float r = fract(sin(aSeed*91.7 + tick*1.37)*43758.5);
        float act = aK > .5 ? step(1.-uAct*.6, r)*.75 + .25*uAct : .45;   // the game server's lights follow the traffic
        vA = max(act, aK*uBan); vBan = aK*uBan; gl_PointSize = clamp(.03*projectionMatrix[1][1]*uVH*.5/-mv.z, 1.6*uPx, 8.*uPx)*(1. + .6*aK*uBan); /* 3 cm, seen through the real lens (zoom included), never under 1.6 px; 6c: up to 8 px, so close up a light is a dash, not a speck */ gl_Position = projectionMatrix*mv; }`,
      // 6b: a short horizontal dash, as a server's status light reads from across the room (a 1 px dot was only bloom)
      fragmentShader: `uniform vec3 uG,uB; uniform float uK; varying float vA; varying float vBan; void main(){ vec2 q = abs(gl_PointCoord - .5); float a = (1. - smoothstep(.36, .5, q.x))*(1. - smoothstep(.1, .24, q.y)); gl_FragColor = vec4(mix(uG,uB,vBan)*a*vA*1.8*uK, 1.); }`,
    });
    scene.add(glows(new Points(g, m))); return m;
  })();

  yield;
  // ---------- home: the router and the grey home route (browser, Discord, downloads) ----------
  const routerPos = V3(-11, 0, -4.8);
  {
    const r = new Group(); r.position.copy(routerPos); r.rotation.y = 0.35; scene.add(r);
    // a body and a lid with vent slots, feet, rear ports, four paddle antennas on hinge knuckles
    const body = rbox(1.9, 0.26, 1.2, 0.1, M.plastic); body.position.y = 0.17; r.add(body);
    const lid = rbox(1.74, 0.08, 1.04, 0.04, M.plastic); lid.position.y = 0.32; r.add(lid);
    const face = new Mesh(new PlaneGeometry(1.5, 0.05), new MeshBasicMaterial({ color: C.surface3 })); face.position.set(0, 0.2, 0.602); r.add(face);
    for (let i = 0; i < 4; i++) { const led = new Mesh(new CircleGeometry(0.025, 12), new MeshBasicMaterial({ color: mul(C.text, 0.5) })); led.position.set(-0.55 + i * 0.16, 0.26, 0.604); r.add(led); }
    r.add(inst(unitBox, M.rubber, Array.from({ length: 11 }, (_, i) => [-0.6 + i * 0.12, 0.361, -0.05, 0, 0, 0, 0.04, 0.006, 0.62]).concat([0, 1, 2, 3].map(i => [-0.45 + i * 0.2, 0.15, -0.602, 0, 0, 0, 0.13, 0.09, 0.01]))));
    r.add(inst(new CylinderGeometry(0.07, 0.08, 0.04, 12), M.rubber, [[-0.75, 0.02, -0.45], [0.75, 0.02, -0.45], [-0.75, 0.02, 0.45], [0.75, 0.02, 0.45]]));
    const knGeo = new CylinderGeometry(0.055, 0.055, 0.1, 16);
    for (const [dx, rz] of [[-0.75, 0.22], [-0.25, 0.06], [0.25, -0.06], [0.75, -0.22]]) {
      const a = new Group(); a.position.set(dx, 0.3, -0.52); a.rotation.z = rz; r.add(a);
      const kn = new Mesh(knGeo, M.plastic); kn.rotation.z = Math.PI / 2; kn.position.y = 0.06; a.add(kn);
      const pad = rbox(0.14, 0.95, 0.04, 0.035, M.plastic); pad.position.y = 0.56; a.add(pad);
    }
    shadowy(r); blob(routerPos.x, routerPos.z, 2.6, 1.8, -0.35);
  }
  const homeCable = new CatmullRomCurve3([LAN_PC, V3(-7.9, 0.04, -1.6), V3(-8.9, 0.04, -2.9), V3(-10.0, 0.04, -3.9), V3(-10.55, 0.2, -4.35)]);
  const homeOut = new CatmullRomCurve3([V3(-11.7, 0.2, -5.3), V3(-14, 0.04, -9), V3(-17, 0.04, -15), V3(-19, 0.04, -26)]);
  const homeFS = `uniform vec3 uC,uHi; uniform float uTime,uLive; varying vec2 vUv; varying vec3 vN; varying vec3 vV;
  void main(){ float core = pow(abs(dot(normalize(vN),normalize(vV))),1.2);
    float k = fract(vUv.x*3. - uTime*.12); float pulse = exp(-pow((k-.5)*10.,2.))*uLive;
    gl_FragColor = vec4(uC*(.55+.45*core) + uHi*pulse*core*.5, 1.); }`;
  const routeVS = `varying vec2 vUv; varying vec3 vN; varying vec3 vV;
  void main(){ vUv = uv; vec4 mv = modelViewMatrix*vec4(position,1.); vN = normalize(normalMatrix*normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix*mv; }`;
  const homeMat = new ShaderMaterial({ uniforms: { uC: { value: mul(C.lineStrong, 1.7) }, uHi: { value: C.muted }, uTime: { value: 0 }, uLive: { value: 0 } }, vertexShader: routeVS, fragmentShader: homeFS });
  scene.add(glows(new Mesh(new TubeGeometry(homeCable, 80, 0.04, 8), homeMat)));
  scene.add(glows(new Mesh(new TubeGeometry(homeOut, 90, 0.035, 6), homeMat)));

  yield;
  // ---------- the kill switch: Windows Firewall on the PC. CS2's way home ends at it; the rest of the PC still goes home ----------
  const gatePos = homeCable.getPointAt(0.14).setY(0), gateTan = homeCable.getTangentAt(0.14);
  const gate = new Group(); gate.position.copy(gatePos); gate.rotation.y = Math.atan2(gateTan.x, gateTan.z); scene.add(gate);
  const gateFrameMat = new MeshStandardMaterial({ color: C.surface3, metalness: 0.6, roughness: 0.4, emissive: C.bad, emissiveIntensity: 0 });
  for (const x of [-0.8, 0.8]) { const p = rbox(0.07, 1.55, 0.07, 0.02, gateFrameMat); p.position.set(x, 0.78, 0); gate.add(p); }
  { const t = rbox(1.67, 0.07, 0.07, 0.02, gateFrameMat); t.position.set(0, 1.55, 0); gate.add(t); }
  const hatch = new ShaderMaterial({
    transparent: true, depthWrite: false, side: DoubleSide, blending: AdditiveBlending,
    uniforms: { uC: { value: C.bad }, uHot: { value: 0 }, uTime: { value: 0 } },
    vertexShader: `varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }`,
    fragmentShader: `uniform vec3 uC; uniform float uHot,uTime; varying vec2 vUv; void main(){ vec2 e = min(vUv, 1.-vUv); float rim = 1.-smoothstep(.0,.025,min(e.x,e.y)); gl_FragColor = vec4(uC*(rim*.9 + .14)*uHot, 1.); }`,
  });
  { const pane = new Mesh(new PlaneGeometry(1.55, 1.45), hatch); pane.position.set(0, 0.79, 0); gate.add(glows(pane)); }
  gate.scale.setScalar(0.001);
  // CS2 trying the home connection: a red dashed run from the PC that stops at the firewall
  const homeTry = new CatmullRomCurve3([LAN_PC.clone().add(V3(0.2, 0.15, 0)), LAN_PC.clone().lerp(gatePos, 0.5).setY(0.45), gatePos.clone().add(V3(0, 0.55, 0))]);
  const tryMat = new ShaderMaterial({
    transparent: true, depthWrite: false,
    uniforms: { uTime: { value: 0 }, uBad: { value: C.bad }, uHot: { value: 0 } },
    vertexShader: `varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }`,
    fragmentShader: `uniform float uTime,uHot; uniform vec3 uBad; varying vec2 vUv;
    void main(){ if(fract(vUv.x*14. - uTime*1.2)>.55) discard; if(vUv.x > uHot*1.05) discard; gl_FragColor = vec4(uBad*1.1, uHot); }`,
  });
  scene.add(glows(new Mesh(new TubeGeometry(homeTry, 60, 0.035, 6), tryMat)));

  yield;
  // ---------- the route itself: proposal A's line, at full strength ----------
  // the USB cable and its two USB-C plugs, one set of contact points (9 Nov., session 5). Each plug is built round its port and
  // along the port's axis: the PC's straight into the tower's side, the phone's angled one (its cable leaves towards the phone's
  // front) in the bottom edge, its boot against the edge. The cable leaves each plug from the end of its strain relief, along the
  // plug's own axis, and lies on the mat (its radius above the cloth, not in it). Pulled (beat 3), the phone's plug slides out
  // along the port's axis, then drops onto the mat in front of the stand, and the cable's last stretch is rebuilt from the plug.
  // Until 9 Nov. the phone's plug went in at the cable's slant, only the plug moved (0.42, through the stand's plate) and the
  // cable's end was hidden in the shader: the cable stopped in mid air and the plug lay apart from it.
  const MAT_Y = 0.05, CR = 0.06, CR_END = 0.028, LY = MAT_Y + CR;   // the mat's top; the cable's radius, and at the plugs; on the mat
  const basis = new Matrix4();
  function plugAt(angled) {
    const g = new Group(); scene.add(g);
    const tip = rbox(0.1, 0.085, 0.032, 0.012, M.chrome); tip.position.y = 0.0425; g.add(tip);   // the metal: in the port when plugged
    const boot = angled ? rbox(0.13, 0.08, 0.235, 0.03, M.plastic) : rbox(0.13, 0.22, 0.075, 0.03, M.plastic);
    const relief = new Mesh(new CylinderGeometry(angled ? CR_END + 0.002 : 0.036, angled ? 0.036 : CR_END + 0.002, 0.06, 16), M.plastic);
    if (angled) { boot.position.set(0, -0.04, 0.0775); relief.rotation.x = Math.PI / 2; relief.position.set(0, -0.04, 0.225); }
    else { boot.position.y = -0.112; relief.position.y = -0.25; }
    g.add(boot, relief);
    return { g, parts: [tip, boot, relief], at: angled ? V3(0, -0.04, 0.25) : V3(0, -0.275, 0), dir: angled ? V3(0, 0, 1) : V3(0, -1, 0) };
  }
  function pose(pl, pos, q) { pl.g.position.copy(pos); pl.g.quaternion.copy(q); pl.g.updateMatrixWorld(true); return { at: pl.at.clone().applyMatrix4(pl.g.matrixWorld), dir: pl.dir.clone().applyQuaternion(q) }; }
  const qOf = (x, y) => new Quaternion().setFromRotationMatrix(basis.makeBasis(x, y, x.clone().cross(y)));
  // the PC's end: straight into its port, the plug's wide side level, a dark slot round the metal
  const plugPC = plugAt(false), endPC = pose(plugPC, USB_PC, qOf(V3(0, 0, -1), V3(-1, 0, 0)));
  { const pt = new Mesh(new PlaneGeometry(0.104, 0.036), new MeshBasicMaterial({ color: C.edge })); pt.rotation.y = Math.PI / 2; pt.position.copy(USB_PC).add(V3(0.0006, 0, 0)); scene.add(pt); }
  // the phone's end: angled, in the port on its bottom edge, on the phone's own axes
  const phQ = tilt.getWorldQuaternion(new Quaternion());
  const PH_X = V3(1, 0, 0).applyQuaternion(phQ), PH_Y = V3(0, 1, 0).applyQuaternion(phQ);
  const FRONT_H = V3(PHONE_FRONT.x, 0, PHONE_FRONT.z).normalize();
  const plugPhone = plugAt(true); plugPhone.g.userData.noBake = true;
  const qIn = qOf(PH_X, PH_Y);
  // the cable on the mat from the PC to J, in front of the stand; from J its last stretch goes to the phone's plug
  const J = PORT.clone().addScaledVector(FRONT_H, 0.9).setY(LY);
  const cableMain = new CatmullRomCurve3([endPC.at, endPC.at.clone().addScaledVector(endPC.dir, 0.16).add(V3(0, -0.01, 0.01)), V3(-6.49, 0.27, 1.95), V3(-6.43, 0.15, 2.22), V3(-6.37, LY + 0.004, 2.5), V3(-6.3, LY, 2.75), V3(-4.9, LY, 3.7), V3(-3.0, LY, 4.0), V3(-1.7, LY, 3.85), J], false, 'centripetal');
  const TJ = cableMain.getTangentAt(1);
  function tailTo(end) { const h = J.distanceTo(end.at) * 0.4; return new CubicBezierCurve3(J.clone(), J.clone().addScaledVector(TJ, h), end.at.clone().addScaledVector(end.dir, h), end.at.clone()); }
  let tailCur = tailTo(pose(plugPhone, PORT, qIn));
  const cable = new CurvePath(); cable.add(cableMain); cable.add(tailCur);   // the whole cable at rest: labels, shots, packets, the light on the mat
  const fJ = cableMain.getLength() / cable.getLength();
  // a tube that narrows to the plugs: TubeGeometry at radius 1, each ring scaled round its point on the curve
  function tube(curve, segs, rOf) {
    const g = new TubeGeometry(curve, segs, 1, 12), p = g.attributes.position, c = V3(0, 0, 0), v = V3(0, 0, 0);
    for (let i = 0; i <= segs; i++) { curve.getPointAt(i / segs, c); const r = rOf(i / segs); for (let j = 0; j <= 12; j++) { const k = i * 13 + j; v.fromBufferAttribute(p, k).sub(c).multiplyScalar(r).add(c); p.setXYZ(k, v.x, v.y, v.z); } }
    g.computeBoundingSphere(); return g;
  }
  const LM = cableMain.getLength(), mainR = t => CR_END + (CR - CR_END) * smooth(0, 0.4, t * LM);
  const tailR = L => t => CR_END + (CR - CR_END) * smooth(0, Math.min(0.4, L), (1 - t) * L);
  const air = new QuadraticBezierCurve3(PHONE_TOP, V3(1.6, 6.8, -1.6), mastTop.clone().add(V3(-0.2, -0.95, 0.2)));
  const arc = new CubicBezierCurve3(mastTop.clone(), V3(9, 13, -7.5), V3(15.5, 11, -9.5), serverTop.clone().add(V3(0, 0.25, 0)));
  const SEG = [{ c: cable, a: 0, b: 0.3 }, { c: air, a: 0.3, b: 0.58 }, { c: arc, a: 0.58, b: 1 }];
  const routeFS = `uniform vec3 c1,c2,c3,uDead; uniform float uS,uE,uTime,uOn,uDash,uGain,uDraw,uReveal;
  varying vec2 vUv; varying vec3 vN; varying vec3 vV;
  void main(){
    float u = clamp(mix(uS,uE,vUv.x),0.,1.);   // a guide may stretch a leg past the ends (6 Nov.): the colours stop there
    vec3 col = u < .5 ? mix(c1,c2,u*2.) : mix(c2,c3,u*2.-1.);
    float core = pow(abs(dot(normalize(vN),normalize(vV))),1.2);
    if(uDash > 0.){ if(fract(vUv.x*uDash - uTime*1.6) > .5) discard; }
    float k = fract(u*6. - uTime*.2); float pulse = exp(-pow((k-.5)*9.,2.));
    float on = uOn * step(vUv.x, uDraw) * step(u, uReveal);
    float I = (.5 + 1.9*pulse) * (.3 + .7*core) * on * uGain;
    gl_FragColor = vec4(max(col*I, uDead*(.5+.5*core)), 1.);
  }`;
  function routeMat(a, b, extra = {}) {
    return new ShaderMaterial({
      uniforms: { c1: { value: C.top1 }, c2: { value: C.top2 }, c3: { value: C.top3 }, uDead: { value: mul(C.lineStrong, 1.6) }, uS: { value: a }, uE: { value: b }, uTime: { value: 0 }, uOn: { value: 1 }, uDash: { value: 0 }, uGain: { value: 1.25 }, uDraw: { value: 1 }, uReveal: { value: 1 }, ...extra },
      vertexShader: routeVS, fragmentShader: routeFS,
    });
  }
  const mCable = routeMat(0, 0.3 * fJ), mTail = routeMat(0.3 * fJ, 0.3), mAir = routeMat(0.3, 0.58, { uDash: { value: 46 } }), mArc = routeMat(0.58, 1);
  scene.add(glows(new Mesh(tube(cableMain, 210, mainR), mCable)));
  const tailMesh = glows(new Mesh(tube(tailCur, 40, tailR(tailCur.getLength())), mTail)); tailMesh.userData.noBake = true; scene.add(tailMesh);
  const airTube = glows(new Mesh(new TubeGeometry(air, 200, 0.022, 6), mAir)); scene.add(airTube);
  const arcTube = glows(new Mesh(new TubeGeometry(arc, 220, 0.05, 10), mArc)); scene.add(arcTube);
  { const a = dots.material.uniforms.uRL.value; let n = 0; // the ground's lamps: 6 on the radio leg, 12 on the arc
    for (let j = 0; j < 6; j++) { const t = (j + 0.5) / 6, q = air.getPointAt(t); a[n++].set(q.x, q.y, q.z, lerp(0.3, 0.58, t)); }
    for (let j = 0; j < 12; j++) { const t = (j + 0.5) / 12, q = arc.getPointAt(t); a[n++].set(q.x, q.y, q.z, lerp(0.58, 1, t)); } }
  // the pull, c from 0 (plugged in) to 1 (on the mat): first out along the port's axis (the metal's length and a little air),
  // then forward past the plate's edge and down onto the mat, levelling out on its back (metal up), its cable towards J
  const OUT = 0.11, qLand = qOf(V3(FRONT_H.z, 0, -FRONT_H.x), V3(0, 1, 0));
  const LAND = PORT.clone().addScaledVector(FRONT_H, 0.53).setY(MAT_Y + 0.08);
  const pOut = PORT.clone().addScaledVector(PH_Y, -OUT);
  const drop = new CubicBezierCurve3(pOut.clone(), pOut.clone().addScaledVector(FRONT_H, 0.3), LAND.clone().add(V3(0, 0.12, 0)), LAND.clone());
  const plugPos = V3(0, 0, 0), plugQ = new Quaternion();
  let plugCut = 0;
  function placePhonePlug(c) {
    if (c === plugCut) return; plugCut = c;
    const a = smooth(0, 0.3, c), b = smooth(0.25, 1, c);
    drop.v0.copy(PORT).addScaledVector(PH_Y, -OUT * a);
    drop.getPoint(b, plugPos); plugQ.slerpQuaternions(qIn, qLand, b);
    tailCur = tailTo(pose(plugPhone, plugPos, plugQ));
    tailMesh.geometry.dispose(); tailMesh.geometry = tube(tailCur, 40, tailR(tailCur.getLength()));
  }

  // radio: rings round the sector antennas while mobile data is on
  const toPh = Math.atan2(-(PORT.z - mastBase.z), PORT.x - mastBase.x);
  const ringGeo = new RingGeometry(0.97, 1, 40, 1, toPh - 0.6, 1.2);
  const mastRings = [];
  for (let i = 0; i < 2; i++) {
    const m = new Mesh(ringGeo, new MeshBasicMaterial({ color: C.top2, transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide }));
    m.position.copy(mastTop).add(V3(0, -0.95, 0)); m.rotation.x = -Math.PI / 2; scene.add(glows(m)); mastRings.push(m);
  }

  // the game's packets, PC to server
  function sampleRoute(u, out) {
    for (const s of SEG) if (u <= s.b) return s.c.getPointAt(clamp((u - s.a) / (s.b - s.a)), out);
    return arc.getPointAt(1, out);
  }
  const NP = small() ? 200 : 320;
  const pPos = new Float32Array(NP * 3), pU = new Float32Array(NP), pA = new Float32Array(NP), pSeed = new Float32Array(NP), pJit = [];
  for (let i = 0; i < NP; i++) { pSeed[i] = Math.random(); pJit.push(V3(Math.random() - 0.5, Math.random() - 0.5, Math.random() - 0.5)); }
  const pGeo = new BufferGeometry();
  pGeo.setAttribute('position', new BufferAttribute(pPos, 3)); pGeo.setAttribute('aU', new BufferAttribute(pU, 1)); pGeo.setAttribute('aA', new BufferAttribute(pA, 1));
  const pMat = new ShaderMaterial({
    transparent: true, depthWrite: false, blending: AdditiveBlending,
    uniforms: { c1: { value: C.top1 }, c2: { value: C.top2 }, c3: { value: C.top3 }, uPx: { value: 1 } },
    vertexShader: `attribute float aU,aA; uniform float uPx; varying float vU,vA; void main(){ vU = aU; vA = aA; vec4 mv = modelViewMatrix*vec4(position,1.); gl_PointSize = uPx*44./-mv.z; gl_Position = projectionMatrix*mv; }`,
    fragmentShader: `uniform vec3 c1,c2,c3; varying float vU,vA; void main(){ float d = length(gl_PointCoord-.5); float a = smoothstep(.5,0.,d); a = a*a*(1.+2.*smoothstep(.18,0.,d)); vec3 col = vU < .5 ? mix(c1,c2,vU*2.) : mix(c2,c3,vU*2.-1.); gl_FragColor = vec4(col*a*vA*1.5, 1.); }`,
  });
  const packets = new Points(pGeo, pMat); packets.frustumCulled = false; scene.add(glows(packets));

  // the USB cable lights the cloth under it along its whole length (blue to violet), not only round one point light
  const SPN = 16, spU = { uSp: { value: [] }, uSpC1: { value: C.top1 }, uSpC2: { value: C.top2 }, uSpK: { value: 10 }, uSpRev: { value: 1 } };
  for (let i = 0; i < SPN; i++) spU.uSp.value.push(cable.getPointAt(i / (SPN - 1)));
  M.mat.onBeforeCompile = sh => {
    Object.assign(sh.uniforms, spU);
    sh.vertexShader = 'varying vec3 vSpW;\n' + sh.vertexShader.replace('#include <worldpos_vertex>', '#include <worldpos_vertex>\n vSpW = (modelMatrix*vec4(transformed,1.)).xyz;');
    sh.fragmentShader = `varying vec3 vSpW; uniform vec3 uSp[${SPN}]; uniform vec3 uSpC1,uSpC2; uniform float uSpK,uSpRev;
    vec3 spill(vec3 p){ vec3 acc = vec3(0.);
      if(p.x < -9. || p.x > .5 || p.z < .5 || p.z > 5.5) return acc;
      for(int i=0;i<${SPN - 1};i++){ vec3 a = uSp[i], b = uSp[i+1]-a; float t = clamp(dot(p-a,b)/dot(b,b),0.,1.);
        float d = length(p-a-b*t), u = (float(i)+t)/${(SPN - 1).toFixed(1)}*.3;
        acc = max(acc, mix(uSpC1,uSpC2,u*2.)*step(u,uSpRev)/(1.+d*d*30.)); }
      return acc*uSpK; }\n` + sh.fragmentShader.replace('#include <lights_fragment_end>', '#include <lights_fragment_end>\n reflectedLight.indirectDiffuse += spill(vSpW)*diffuseColor.rgb;');
  };
  M.mat.customProgramCacheKey = () => 'spill';

  yield;
  // ---------- shots: one per beat. Positions are proposal A's where A had them ----------
  scene.updateMatrixWorld(true);
  let apex = arc.getPoint(0); for (let t = 0; t <= 1; t += 0.02) { const p = arc.getPoint(t); if (p.y > apex.y) apex = p; }
  const monW = (x, y) => mon.localToWorld(V3(x, y, 0.04));
  const scrC = monW(0, SY), scrN = mon.localToWorld(V3(0, SY, 1)).sub(mon.localToWorld(V3(0, SY, 0))).normalize();
  const towerPts = [V3(TX - TW / 2, TH, TZ - TD / 2), V3(TX - TW / 2, 0, TZ + TD / 2), V3(TX + TW / 2, TH, TZ + TD / 2), V3(TX - TW / 2, TH, TZ + TD / 2)];
  const rackPts = [V3(SX - 2.95, RH + 0.2, SZ - 0.75), V3(SX + 2.95, RH + 0.2, SZ + 0.75), V3(SX - 2.95, 0, SZ + 0.75), V3(SX + 2.95, 0, SZ + 0.75)];
  const mastPts = [mastTop.clone().add(V3(0, 0.2, 0)), mastBase.clone().add(V3(-1.5, 0, 2)), mastBase.clone().add(V3(2.7, 0, 2))];
  // pad: room a point needs round it, in px [t, r, b, l] (labels need room above and beside)
  const K = (p, t = 0, r = 0, b = 0, l = 0) => ({ p, t, r, b, l });
  // 9 Nov. (session 6): the cover (shot 0) keeps the tower in, wide and tall: its chapter is "PC", and on a phone and at 4K the
  // tower was cut by the edge (UI-002); upright it also keeps the racks' far corner. The kill switch, wide, comes down to
  // about 28 degrees (it looked straight down on the floor), and upright it keeps the whole phone in (it was cut at the right)
  const SHOTS = {
    wide: [
      { pos: V3(-21, 10.5, 26), tgt: V3(3.5, 3.4, -4), fov: 30, keys: [K(apex, 10), K(mastTop, 130, 60, 0, 180), K(serverTop, 110, 10, 0, 170), ...rackPts.map(p => K(p, 0, 70, 10, 6)), K(PORT, 100, 120, 14), K(cable.getPointAt(0.35), 0, 0, 16, 0), ...towerPts.map(p => K(p, 0, 0, 10, 10))] },
      { pos: V3(-12, 6.5, 13), tgt: V3(-4.5, 0.8, 0.8), fov: 34, keys: [K(mastTop, 96, 40, 0, 10), K(V3(SX + 2.95, RH + 0.2, SZ - 0.75), 20, 60), K(V3(SX + 2.95, 0, SZ + 0.75), 0, 40, 10), K(V3(SX + 2.95, RH, SZ + 0.75), 0, 40), K(apex, 30), ...towerPts.map(p => K(p, 0, 0, 10, 10)), K(monW(0, SY + 1.15), 100, 0, 0, 0), K(PORT, 100, 140, 14, 0), K(PHONE_TOP, 0, 20), K(routerPos.clone().add(V3(0, 1.4, 0)), 110, 40, 0, 20), K(cable.getPointAt(0.5), 0, 0, 40, 0)] },
      { pos: V3(-1, 2.4, 11), tgt: V3(10, 6, -8), fov: 36, keys: [K(mastTop, 130, 40, 0, 180), K(mastBase, 0, 0, 10), K(apex, 10), K(serverTop, 110, 10, 0, 170), K(V3(SX + 2.95, 0, SZ + 0.75), 0, 8, 8)] },
      { pos: V3(-13.5, 9.5, 15), tgt: V3(-6.2, 0.6, -0.6), fov: 34, keys: [...towerPts.map(p => K(p, 0, 0, 10, 10)), K(gatePos.clone().add(V3(0, 1.7, 0)), 110, 60, 0, 30), K(routerPos.clone().add(V3(0, 1.4, 0)), 0, 20, 0, 20), K(PORT, 90, 120, 20), K(monW(1.75, SY + 1.15))] },
      { pos: V3(-1, 4, 10), tgt: V3(12, 5.5, -8), fov: 38, keys: [K(mastTop, 130, 40, 0, 180), K(mastBase, 0, 0, 10), K(apex, 10), K(serverTop, 120, 20, 0, 190), ...rackPts.map(p => K(p, 0, 10, 10, 6))] },
      { pos: scrC.clone().add(scrN.clone().multiplyScalar(9)), tgt: scrC.clone(), fov: 26, keys: [K(monW(-SW / 2, SY - SH / 2)), K(monW(SW / 2, SY + SH / 2)), K(monW(-SW / 2, SY + SH / 2)), K(monW(SW / 2, SY - SH / 2))] },
      { pos: V3(-8, 24, 26), tgt: V3(4, 1, -4), fov: 28, keys: [K(V3(SX + 2.95, RH + 0.2, SZ - 0.75), 20, 60), K(apex, 30), ...towerPts.map(p => K(p)), K(routerPos), ...rackPts.map(p => K(p, 0, 40)), K(V3(SX + 2.95, RH, SZ - 0.75), 0, 40), K(apex, 20), K(mastTop), K(PORT)] },
    ],
    tall: [
      { pos: V3(-15, 5.2, 17.5), tgt: V3(3, 3, -5), fov: 40, keys: [K(apex, 6), K(mastTop, 8), K(serverTop, 6), K(PORT, 0, 0, 10), ...towerPts.map(p => K(p, 0, 0, 6, 6)), K(V3(SX + 2.95, RH, SZ + 0.75), 0, 4)] },
      { pos: V3(-4, 7.5, 15.5), tgt: V3(-4.8, 0.8, 0.6), fov: 46, keys: [...towerPts.map(p => K(p, 0, 0, 6, 6)), K(PORT, 0, 10, 10), K(PHONE_TOP, 70, 10), K(monW(0, SY + 1.15), 0)] },
      { pos: V3(-3, 2, 13), tgt: V3(8, 6, -7), fov: 50, keys: [K(PHONE_TOP, 0, 10, 10), K(mastTop, 70, 20), K(apex, 6), K(serverTop, 70, 10), K(V3(SX + 2.95, 0, SZ + 0.75))] },
      { pos: V3(-10.5, 17, 16), tgt: V3(-6.4, 0.3, -0.6), fov: 46, keys: [...towerPts.map(p => K(p, 0, 0, 6, 6)), K(gatePos.clone().add(V3(0, 1.7, 0)), 70, 20, 0, 20), K(PORT, 0, 10, 10), K(tw(PW / 2, PH, 0), 0, 8), K(routerPos, 0, 6, 6, 6)] },
      { pos: V3(9, 4.5, 6), tgt: V3(SX, 2.6, SZ), fov: 46, keys: [K(V3(SX, RH, SZ), 60), K(V3(SX, 0, SZ + 0.75)), K(V3(SX - 1, RH, SZ + 0.75)), K(V3(SX + 1, 0, SZ + 0.75))] },
      { pos: scrC.clone().add(scrN.clone().multiplyScalar(11)), tgt: scrC.clone(), fov: 30, keys: [K(monW(-SW / 2, SY - SH / 2)), K(monW(SW / 2, SY + SH / 2)), K(monW(-SW / 2, SY + SH / 2)), K(monW(SW / 2, SY - SH / 2))] },
      { pos: V3(-6, 40, 36), tgt: V3(4, 1, -4), fov: 44, keys: [...towerPts.map(p => K(p)), K(routerPos), ...rackPts.map(p => K(p)), K(mastTop)] },
    ],
  };
  const NS = SHOTS.wide.length;
  if (opts.cam) for (const c of opts.cam) Object.assign(SHOTS[c.layout || 'wide'][c.i], { pos: V3(...c.pos), tgt: V3(...c.tgt), fov: c.fov, keys: c.keys === false ? [] : SHOTS[c.layout || 'wide'][c.i].keys });

  // fit each shot into the free part of the canvas the page hands over: the biggest zoom that keeps every key point (and its room) inside
  const fits = [];
  let W = 1, H = 1, layout = 'wide';
  const pv = V3(0, 0, 0);
  function fitShot(i) {
    const S = SHOTS[layout][i];
    const fr = (opts.frame && opts.frame(W, H, i, layout)) || { box: [W * 0.04, H * 0.06, W * 0.96, H * 0.94] };
    camera.position.copy(S.pos); camera.lookAt(S.tgt); camera.fov = S.fov; camera.zoom = 1; camera.clearViewOffset();
    camera.aspect = W / H; camera.updateProjectionMatrix(); camera.updateMatrixWorld();
    if (!S.keys.length) { fits[i] = { zoom: 1, dx: 0, dy: 0, box: fr.box }; return; }
    const pts = S.keys.map(q => { pv.copy(q.p).project(camera); return { x: pv.x * W / 2, y: -pv.y * H / 2, q }; });
    const [L, T, R, B] = fr.box;
    // the room labels need round their points shrinks when the box is too small for it, so the picture never becomes a dot
    // the labels are sized in rem, and the page's rem grows on big screens: their room grows with it
    let pad = Math.max(1, (parseFloat(getComputedStyle(document.documentElement).fontSize) || 16) / 16);
    const range = z => {
      let xl = -Infinity, xh = Infinity, yl = -Infinity, yh = Infinity;
      for (const { x, y, q } of pts) {
        xl = Math.max(xl, L + q.l * pad - W / 2 - x * z); xh = Math.min(xh, R - q.r * pad - W / 2 - x * z);
        yl = Math.max(yl, T + q.t * pad - H / 2 - y * z); yh = Math.min(yh, B - q.b * pad - H / 2 - y * z);
      }
      return xl <= xh && yl <= yh ? [xl, xh, yl, yh] : null;
    };
    while (!range(0.05) && pad > 0.1) pad *= 0.8;
    let lo = 0.05, hi = 6;
    for (let k = 0; k < 30; k++) { const m = (lo + hi) / 2; if (range(m)) lo = m; else hi = m; }
    const r = range(lo) || [0, 0, 0, 0];
    const bx = fr.bias ? fr.bias[0] : 0.5, by = fr.bias ? fr.bias[1] : 0.5;
    fits[i] = { zoom: lo, dx: lerp(r[0], r[1], bx), dy: lerp(r[2], r[3], by), box: fr.box };
  }

  yield;
  // ---------- labels ----------
  addCSS();
  const wrap = document.createElement('div');
  wrap.className = 'wd-labels'; wrap.setAttribute('aria-hidden', 'true');
  const LAB = {
    pc: { at: V3(TX + 0.2, TH + 0.25, TZ), beats: [0, 1], html: '<b>Your PC</b><span>CS2 and Steam</span>' },
    phone: { at: PHONE_TOP.clone().add(V3(0, 0.1, 0)), atTall: tw(-PW / 2 - 0.22, PH * 0.82, 0), beats: [0, 1, 2, 3], side: { wide: [0, 0], tall: [1, 1] }, html: '<b>Phone</b><span data-k="ph">mobile data on</span>' },
    cable: { at: cable.getPointAt(0.62), beats: [1], side: { wide: [1, 1], tall: [1, 1] }, html: '<b>USB cable</b><span>USB tethering, or the phone\'s hotspot</span>' },
    router: { at: routerPos.clone().add(V3(0, 1.45, 0)), beats: [1, 3], side: { wide: [0, 0], tall: [0, 0] }, html: '<b>Home router</b><span>browser, Discord, downloads</span>' },
    mast: { at: mastTop.clone().add(V3(0, 0.3, 0)), beats: [0, 2, 4], r: 1, one: 1, html: '<b>Your carrier</b><span data-k="mast">4G / 5G</span>' },
    server: { at: serverTop.clone().add(V3(0, 0.45, 0)), beats: [0, 2, 4], r: 1, side: { wide: [1, 0], tall: [1, 1] }, html: '<b>CS2 server</b><span data-k="srv">sees <em>' + IP_OLD + '</em></span><small data-k="srvn">example IP, your carrier\'s</small>' },
    // on the red gate itself, where the home cable leaves the PC; the box on the empty floor below it, left of the PC
    gate: { at: gatePos.clone().add(V3(0, 0.62, 0)), beats: [3], hot: 1, one: 1, side: { wide: [1, 1], tall: [0, 0] }, html: '<b>Windows Firewall on your PC</b><span>CS2 and your servers: blocked</span>' },
    cut: { at: PORT.clone().add(V3(0, 0.05, 0)), beats: [3], dn: 1, side: { wide: [1, 1], tall: [1, 1] }, html: '<b>Cable pulled</b><span>CS2 is cut off, not sent home</span>' },
  };
  // the guides build on this world: they may add props, labels and shots, and run their own story each frame
  const __gx = (opts.extend && opts.extend({ scene, camera, renderer, C, M, LAB, SHOTS, K, rbox, cable, air, arc, mCable, mAir, mArc, homeMat, hatch, tryMat, gate, gateFrameMat, Lbad, L2, L3, leds, mastTop, mastBase, serverTop, routerPos, gatePos, PORT, PHONE_TOP, SX, SZ, monW, scrC, scrN, mon, REFL, glows, spU })) || {};
  for (const k in LAB) {
    const el = document.createElement('div'); el.className = 'wd-lab' + (LAB[k].hot ? ' wd-hot' : ''); el.dataset.lab = k; el.innerHTML = '<i></i><div>' + LAB[k].html + '</div>';
    wrap.appendChild(el); LAB[k].el = el; LAB[k].s = {};
  }
  canvas.after(wrap);
  const $l = s => wrap.querySelector('[data-k="' + s + '"]');
  const labPh = $l('ph'), labSrv = $l('srv'), labSrvN = $l('srvn'), labMast = $l('mast');
  let labTxt = {};
  let labGen = 0;
  const setTxt = (el, k, html) => { if (labTxt[k] !== html) { labTxt[k] = html; el.innerHTML = html; labGen++; } };

  const proj = V3(0, 0, 0);
  const placed = [];
  const LINE_PTS = [];
  for (let i = 0; i <= 28; i++) LINE_PTS.push(arc.getPoint(i / 28));
  for (let i = 0; i <= 14; i++) LINE_PTS.push(air.getPoint(i / 14));
  for (let i = 0; i <= 10; i++) LINE_PTS.push(mastBase.clone().lerp(mastTop, i / 10));
  for (let i = 0; i <= 16; i++) LINE_PTS.push(cable.getPoint(i / 16));
  for (const x of [TX - TW / 2, TX + TW / 2]) for (const y of [0.2, TH / 2, TH]) for (const z of [TZ - TD / 2, TZ, TZ + TD / 2]) LINE_PTS.push(V3(x, y, z));
  for (const dy of [-1.9, -0.95, 0]) for (const [dx, dz] of [[-0.7, 0], [0.7, 0], [0, -0.7], [0, 0.7]]) LINE_PTS.push(mastTop.clone().add(V3(dx, dy, dz)));
  for (const y of [0.1, PH / 4, PH / 2, PH * 3 / 4, PH]) for (const x of [-PW / 2, 0, PW / 2]) LINE_PTS.push(tw(x, y, 0));
  // the monitor's face: a label over the app's screen hides what the beat is about
  for (const fy of [-0.5, -0.25, 0, 0.25, 0.5]) for (const fx of [-0.5, -0.25, 0, 0.25, 0.5]) LINE_PTS.push(monW(fx * SW, SY + fy * SH));
  let lineScr = [];
  function placeLabels(p, box) {
    placed.length = 0;
    const [L, T, R, B] = box;
    const av = opts.avoid ? opts.avoid() : [];
    lineScr = LINE_PTS.map(q => { proj.copy(q).project(camera); return [(proj.x * 0.5 + 0.5) * W, (-proj.y * 0.5 + 0.5) * H]; });
    // nearest shots first, so the beat on screen keeps its labels
    // the labels that carry a beat go first (the hot ones, then those of fewer beats), so they get the room
    const order = Object.keys(LAB).sort((a, b) => (LAB[b].hot ? 1 : 0) - (LAB[a].hot ? 1 : 0) || LAB[a].beats.length - LAB[b].beats.length);
    for (const k of order) {
      const lb = LAB[k], el = lb.el, s = lb.s;
      // full while its beat holds, gone a third of the way to the next: the box is never left half see-through (6 Nov.)
      let o = 0; for (const b of lb.beats) o = Math.max(o, 1.6 * (1 - Math.max(Math.abs(p - b), Math.abs(st.p - b)) * 3.2));
      if (layout === 'tall' && !lb.tall && (!(k === 'phone' || k === 'server' || k === 'gate' || k === 'mast') || B - T < 180 || p < 0.5)) o = 0;
      o = clamp(o);
      let x = 0, y = 0, r = !!lb.r, dn = !!lb.dn;
      if (o > 0.01) {
        proj.copy(layout === 'tall' && lb.atTall ? lb.atTall : lb.at).project(camera);
        x = (proj.x * 0.5 + 0.5) * W; y = (-proj.y * 0.5 + 0.5) * H;
        const box2 = el.lastElementChild;
        if (s.gen !== labGen) { s.w = box2.offsetWidth || 170; s.h = box2.offsetHeight || 50; s.gen = labGen; }
        const w = s.w, h = s.h, stem = layout === 'tall' ? 30 : 44;
        const rect = (rr, dd) => { const y0 = dd ? y + 32 : y - stem - 12 - h, x0 = rr ? x + 10 - w : x - 10; return [x0, y0, x0 + w, y0 + h]; };
        const ok = q => q[0] >= L - 4 && q[2] <= Math.min(W - 8, R + 8) && q[1] >= T - 4 && q[3] <= B + 6 && !placed.some(o2 => q[0] < o2[2] + 10 && o2[0] < q[2] + 10 && q[1] < o2[3] + 8 && o2[1] < q[3] + 8) && !lineScr.some(([lx, ly]) => lx > q[0] - 4 && lx < q[2] + 4 && ly > q[1] - 4 && ly < q[3] + 4) && !av.some(a => q[0] < a[2] && a[0] < q[2] && q[1] < a[3] && a[1] < q[3]);
        // each label has its own place for each layout (side: { wide: [r, dn], tall: [r, dn] }), tried first; another side only
        // when it comes in, never while it is on screen (6 Nov.: the carrier's and the firewall's jumped). one: that place only
        const pref = lb.side && lb.side[layout]; if (pref) { r = !!pref[0]; dn = !!pref[1]; }
        const tries = lb.one ? [[r, dn]] : [[r, dn], [!r, dn], [r, !dn], [!r, !dn]];
        const loose = q => q[0] >= L - 4 && q[2] <= Math.min(W - 8, R + 8) && q[1] >= T - 4 && q[3] <= B + 6 && !placed.some(o2 => q[0] < o2[2] + 10 && o2[0] < q[2] + 10 && q[1] < o2[3] + 8 && o2[1] < q[3] + 8) && !av.some(a => q[0] < a[2] && a[0] < q[2] && q[1] < a[3] && a[1] < q[3]);
        const inBox = proj.z < 1 && x > L - 6 && x < R + 6 && y > T && y < B + 4;
        // a label keeps the side it came in with for as long as its beat lasts: choosing again each frame made the carrier's
        // and the firewall's boxes jump from below to above and back (6 Nov.). Its text may grow (the ban changes it): it
        // stays. If a label that goes first now takes its place, it fades out there and comes back there; it never moves
        const keep = s.side || null;
        const hit = q => placed.some(o2 => q[0] < o2[2] + 10 && o2[0] < q[2] + 10 && q[1] < o2[3] + 8 && o2[1] < q[3] + 8);
        if (!inBox) { o = 0; s.side = null; s.hitT = 0; }
        else if (keep) {
          [r, dn] = keep;
          // taken: it fades out; once it is gone (the fade is .25 s) it may come back somewhere else, never while it shows
          // the same when its kept place now runs out of the picture's box (6 Nov.: on the guides the phone's was cut by the
          // edge, and the profiles' template label, placed while the page was still loading, sat on the text column)
          const q = rect(r, dn);
          if (hit(q) || q[0] < Math.max(2, L - 12) || q[2] > Math.min(W - 2, R + 16) || q[1] < Math.max(2, T - 12) || q[3] > Math.min(H - 2, B + 14)) { o = 0; if (!s.hitT) s.hitT = time; else if (time - s.hitT > 0.35) { s.side = null; s.hitT = 0; } }
          else { s.hitT = 0; placed.push(rect(r, dn)); }
        }
        else {
          const t = tries.find(([a, b]) => ok(rect(a, b))) || tries.find(([a, b]) => loose(rect(a, b)));
          if (t) { [r, dn] = t; placed.push(rect(r, dn)); s.side = t; } else o = 0;
        }
      } else s.side = null; // out of its beats: the next time it comes in, it may take another place
      if (s.o !== o) { el.style.setProperty('--o', o.toFixed(3)); s.o = o; }
      if (o > 0) {
        const xs = x.toFixed(1) + 'px', ys = y.toFixed(1) + 'px';
        if (s.x !== xs) { el.style.setProperty('--x', xs); s.x = xs; }
        if (s.y !== ys) { el.style.setProperty('--y', ys); s.y = ys; }
        if (s.r !== r) { el.classList.toggle('wd-r', r); s.r = r; }
        if (s.dn !== dn) { el.classList.toggle('wd-dn', dn); s.dn = dn; }
      }
    }
  }

  yield;
  // ---------- post: proposal A's bloom, on an MSAA half float buffer ----------
  const rt = new WebGLRenderTarget(4, 4, { type: HalfFloatType, samples: 4 });
  const composer = new EffectComposer(renderer, rt);
  composer.addPass(new RenderPass(scene, camera));
  const bloom = new UnrealBloomPass(new Vector2(256, 256), 0.95, 0.55, 0.12);
  // 6c (3D-010): the same bloom on the route, without its widest layers. The two widest of its five (a sixteenth and a thirty-second
  // of the frame) spread every light over the whole picture: a grey-violet veil that lifted the night sky from 6 to 30 levels
  // and laid a white haze over the desk (measured with the bloom off, lab/session6c-probe.mjs). The halo close to the lines stays
  [1, 0.9, 0.55, 0.2, 0.05].forEach((k, i) => bloom.bloomTintColors[i].setScalar(k));
  // the bloom is taken from a second render of the scene in which the screens are black; then added on top of the real one
  const bloomIn = new WebGLRenderTarget(4, 4, { type: HalfFloatType, samples: 0 });
  const black = new MeshBasicMaterial({ color: 0 });
  const addBloom = new ShaderPass(new ShaderMaterial({
    uniforms: { tDiffuse: { value: null }, tBloom: { value: null } },
    vertexShader: `varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }`,
    // 6b: the shadows graded to night blue and a hair deeper. The darks were a warm grey (the route's magenta and yellow halo
    // over neutral black) that the owner read as washed out and yellowish next to the generated pictures (blue-black darks,
    // measured by lab/session6b-tint.mjs). Only the darks move: the route, its glow and every highlight stay as they were
    fragmentShader: `uniform sampler2D tDiffuse,tBloom; varying vec2 vUv; void main(){ vec4 c = texture2D(tDiffuse,vUv) + vec4(texture2D(tBloom,vUv).rgb,0.);
      float l = dot(c.rgb, vec3(.2126,.7152,.0722)), s = 1. - smoothstep(.004, .07, l);
      c.rgb = max(c.rgb*mix(vec3(1.), vec3(.76, .92, 1.16), s) - .0012*s, 0.);
      // 6c: an OLED toe. The darks go down towards black (0.4 at black, 0.7 at the page's own background), while the
      // route and its glow, far brighter, keep 98 to 100 % of themselves
      c.rgb *= (l + .002)/(l + .005);
      // 6c-bis (the owner let us decide, 10 Nov.): the route stays neon, not white. Over 1 a channel used to clip on its own, so
      // the magenta and yellow cores went white; now the brightest channel rolls off to 1 and the others keep their ratio to it
      // (the hue and its saturation stay), with a fifth of the old clip kept for a hot centre. Nothing under 0.75 moves
      float mx = max(c.r, max(c.g, c.b));
      if (mx > .75) { float f = .75 + .25*(1. - exp(-(mx - .75)/.25)); c.rgb = mix(c.rgb*(f/mx), min(c.rgb, vec3(1.)), .2); }
      gl_FragColor = c; }`,
  }));
  composer.addPass(addBloom);
  const outPass = new OutputPass();
  // 6c (3D-011): the dither, last, in the screen's own 8-bit steps: interleaved gradient noise (no sine, the same on every GPU), half
  // a step either way, and none on black, so an OLED's black pixels stay off (the sky's old dither lit and unlit them)
  outPass.material.fragmentShader = outPass.material.fragmentShader.replace('gl_FragColor = sRGBTransferOETF( gl_FragColor );',
    'gl_FragColor = sRGBTransferOETF( gl_FragColor ); { float n = fract(52.9829189*fract(dot(gl_FragCoord.xy, vec2(.06711056, .00583715)))); gl_FragColor.rgb += (n - .5)/255.*smoothstep(.5/255., 4./255., max(gl_FragColor.r, max(gl_FragColor.g, gl_FragColor.b))); }');
  composer.addPass(outPass);
  const noBloom = [sky, skyline, farMasts, dots];
  function renderAll() {
    frameNo++;
    // the ground under the radio leg and the arc is lit as they are (guides may hide or change them)
    const lu = dots.material.uniforms;
    lu.uLeg.value.set(0, airTube.visible ? mAir.uniforms.uOn.value : 0, arcTube.visible ? mArc.uniforms.uOn.value * mArc.uniforms.uGain.value / 1.25 : 0);
    lu.uDraw.value = mArc.uniforms.uDraw.value; lu.uReveal.value = mArc.uniforms.uReveal.value;
    const sm = screen.material, pm = phScreen.material, ki = key.intensity, ri = rim.intensity, hi = hemi.intensity;
    screen.material = black; phScreen.material = black;
    // 6c: the racks' lights glow less (OLED: three green blocks of halo); in the real render they stay sharp and bright
    leds.uniforms.uK.value = 0.45 + 0.55 * leds.uniforms.uBan.value;
    // the bloom sees only what glows: no studio reflections, no white key or rim highlights on the metals
    scene.environmentIntensity = 0; key.intensity = rim.intensity = hemi.intensity = 0;
    const hv = noBloom.map(o => o.visible); noBloom.forEach(o => (o.visible = false)); // the far world never reaches the threshold
    inBloom = true; renderer.setRenderTarget(bloomIn); renderer.clear(); renderer.render(scene, camera); inBloom = false;
    noBloom.forEach((o, i) => (o.visible = hv[i]));
    scene.environmentIntensity = ENV_K; key.intensity = ki; rim.intensity = ri; hemi.intensity = hi;
    screen.material = sm; phScreen.material = pm; leds.uniforms.uK.value = 1;
    bloom.render(renderer, null, bloomIn, 0, false);
    addBloom.uniforms.tBloom.value = bloom.renderTargetsHorizontal[0].texture;
    renderer.setRenderTarget(null);
    composer.render();
  }

  function resize() {
    W = Math.max(1, canvas.clientWidth); H = Math.max(1, canvas.clientHeight);
    layout = W / H < 0.9 ? 'tall' : 'wide';
    wrap.classList.toggle('wd-tall', layout === 'tall');
    renderer.setPixelRatio(dpr()); renderer.setSize(W, H, false);
    composer.setPixelRatio(renderer.getPixelRatio()); composer.setSize(W, H);
    const pr = renderer.getPixelRatio(); mirror.getRenderTarget().setSize(Math.round(W * pr / 2), Math.round(H * pr / 2)); bloomIn.setSize(Math.round(W * pr / 2), Math.round(H * pr / 2)); bloom.setSize(Math.round(W * pr), Math.round(H * pr));
    leds.uniforms.uPx.value = pr; leds.uniforms.uVH.value = H * pr; // the rack lights: their size in the frame's own pixels
    for (let i = 0; i < NS; i++) fitShot(i);
    labGen++;
    renderer.shadowMap.needsUpdate = true;
    dirty = true; request();
  }

  yield;
  // ---------- state ----------
  const st = { p: 0, sub: 0, cta: 0 };
  let pNow = 0, time = 0, reveal = still || opts.lit ? 1 : 0, dirty = true;
  const mouse = { x: 0, y: 0, sx: 0, sy: 0 };
  const camPos = V3(0, 0, 0), camTgt = V3(0, 0, 0), tmp = V3(0, 0, 0);
  // the move between two shots: a quintic ease (no jerk where it starts and stops); a crane on an arc round the middle of what
  // the two shots look at, the distance changing in log space like a dolly, the head turning a little before the body moves.
  // Each shot is still met exactly at 0 and 1, so the fitted frames, the labels and the cuts of reduced motion do not change
  const ease5 = f => { const t = clamp((f - 0.12) / 0.76); return t * t * t * (t * (t * 6 - 15) + 10); };
  const piv = V3(0, 0, 0), oA = V3(0, 0, 0), oB = V3(0, 0, 0);
  function travel(a, b, e, k) {
    if (e <= 0) { camPos.copy(a.pos); camTgt.copy(a.tgt); return; }
    if (e >= 1) { camPos.copy(b.pos); camTgt.copy(b.tgt); return; }
    const et = k === 2 ? smooth(0.25, 1, e) : smooth(0, 0.8, e), ep = k === 2 ? smooth(0, 0.75, e) : smooth(0.08, 1, e);
    camTgt.lerpVectors(a.tgt, b.tgt, et);
    piv.lerpVectors(a.tgt, b.tgt, 0.5); oA.subVectors(a.pos, piv); oB.subVectors(b.pos, piv);
    const ra = oA.length(), rb = oB.length(), za = Math.atan2(oA.x, oA.z);
    let dz = Math.atan2(oB.x, oB.z) - za; dz -= Math.round(dz / (2 * Math.PI)) * 2 * Math.PI;
    const el = lerp(Math.asin(oA.y / ra), Math.asin(oB.y / rb), ep), az = za + dz * ep, rr = ra * Math.pow(rb / ra, ep);
    camPos.set(Math.sin(az) * Math.cos(el), Math.sin(el), Math.cos(az) * Math.cos(el)).multiplyScalar(rr).add(piv);
  }
  let ipShown = IP_OLD, banShown = -1;

  function stateAt(p, sub) {
    // the kill switch (beat 3): the cable comes out of the phone
    const cut = smooth(2.45, 2.85, p) * (1 - smooth(3.25, 3.6, p));
    // the ban (beat 4), driven by the log line on screen: kicked, mobile data off, on, new IP, back on the server
    const b4 = smooth(3.5, 3.8, p) * (1 - smooth(4.3, 4.6, p));
    const banned = b4 * (sub < 0.62 ? 1 : 0);
    const dataOff = b4 * (sub >= 0.4 && sub < 0.52 ? 1 : 0);
    const redraw = b4 > 0 && sub >= 0.4 ? smooth(0.52, 0.62, sub) : 1;
    const fresh = b4 * smooth(0.6, 0.66, sub) * (1 - smooth(0.78, 0.9, sub));
    const newIP = p > 4.3 || (b4 > 0.5 && sub >= 0.62);
    const back = b4 * (sub >= 0.86 ? 1 : 0);
    return { cut, dataOff, redraw, fresh, newIP, banned, back, b4 };
  }

  function update(dt) {
    if (!still) time += dt;
    // the scroll position, eased; with reduced motion it is taken as it is (the page sends cuts)
    // reduced motion, or Pause motion: the story still follows the text, in cuts
    const calm = still || paused;
    if (calm) { pNow = st.p; reveal = 1; } else pNow += (st.p - pNow) * (1 - Math.exp(-dt * (6 + 12 * Math.min(1, Math.abs(st.p - pNow)))));
    if (Math.abs(st.p - pNow) < 0.0005) pNow = st.p;
    const p = pNow;
    if (!still && reveal < 1) reveal = clamp(reveal + dt / 1.1);

    // camera between the two nearest shots: it travels only between 12% and 88% of the way, so text reads with the camera still
    const S = SHOTS[layout];
    const k = Math.min(Math.floor(p), NS - 2), f = p - k;
    const e = calm ? Math.round(f) : ease5(f);
    const a = S[k], b = S[k + 1], fa = fits[k], fb = fits[k + 1];
    // into the screen and back out of it the camera keeps its straight move (on an arc it swung past the mast, the phone huge in front)
    if (k < 4) travel(a, b, e, k); else { camPos.lerpVectors(a.pos, b.pos, e); camTgt.lerpVectors(a.tgt, b.tgt, e); }
    if (!calm && k < 4) camPos.y += Math.sin(e * Math.PI) * (k === 2 ? 3 : 1.2); // a lighter crane: the arc already lifts
    if (!calm) {
      const kk = 1 - Math.exp(-dt * 3);
      mouse.sx += (mouse.x - mouse.sx) * kk; mouse.sy += (mouse.y - mouse.sy) * kk;
      const near = 1 - smooth(4.4, 5, p) * (1 - smooth(5.2, 5.8, p)); // still in front of the screen
      camPos.x += mouse.sx * 1.2 * near;
      camPos.y += -mouse.sy * 0.7 * near;
      // a held camera, not a locked one: a sway of about a tenth of a degree while a beat holds, none while it travels
      const sw = camPos.distanceTo(camTgt) * 0.0017 * (1 - Math.sin(Math.PI * e)) * near * (opts.drift === false ? 0 : 1);
      camTgt.x += sw * (0.6 * Math.sin(time * 0.23) + 0.4 * Math.sin(time * 0.61 + 1.3));
      camTgt.y += sw * (0.42 * Math.sin(time * 0.19 + 0.7) + 0.28 * Math.sin(time * 0.47 + 2.1));
      camPos.y += sw * 0.9 * Math.sin(time * 0.31 + 0.4);
    }
    const zoom = lerp(fa.zoom, fb.zoom, e), dx = k === 4 ? lerp(fa.dx, fb.dx, smooth(0, 0.35, f)) : lerp(fa.dx, fb.dx, e), dy = lerp(fa.dy, fb.dy, e);
    camera.position.copy(camPos); camera.lookAt(camTgt); camera.fov = lerp(a.fov, b.fov, e) + (calm || k >= 4 ? 0 : 4 * Math.sin(Math.PI * e)); camera.zoom = zoom; // the lens opens a little mid-move
    camera.aspect = W / H; camera.setViewOffset(W, H, -dx, -dy, W, H); camera.updateProjectionMatrix(); camera.updateMatrixWorld();
    const box = [0, 1, 2, 3].map(j => lerp(fa.box[j], fb.box[j], e));

    const s = stateAt(p, st.sub);
    const live = 1 - s.cut;
    const radio = live * (1 - s.dataOff);
    placePhonePlug(s.cut);   // the pulled cable: its plug and last stretch move together
    mAir.uniforms.uOn.value = radio;
    mArc.uniforms.uOn.value = radio;
    mArc.uniforms.uDraw.value = s.redraw;
    mArc.uniforms.uGain.value = mAir.uniforms.uGain.value = 1.25 + 0.9 * s.fresh;
    for (const m of [mCable, mTail, mAir, mArc]) { m.uniforms.uTime.value = time; m.uniforms.uReveal.value = reveal * 1.02; }
    spU.uSpRev.value = s.cut > 0.02 ? Math.min(reveal * 1.02, 0.3 * fJ) : reveal * 1.02; // no light on the mat where the cable was (0.92 of reveal never cut it)
    homeMat.uniforms.uTime.value = time;
    homeMat.uniforms.uLive.value = smooth(0.4, 0.85, p) * (1 - smooth(1.4, 1.8, p)) + smooth(2.5, 2.9, p) * (1 - smooth(3.2, 3.6, p));
    // the firewall: comes up with the cut, CS2's run home stops at it
    const hot = s.cut;
    hatch.uniforms.uHot.value = hot; tryMat.uniforms.uHot.value = hot; tryMat.uniforms.uTime.value = time;
    gateFrameMat.emissiveIntensity = 0.9 * hot; Lbad.intensity = 5 * hot;
    gate.scale.setScalar(Math.max(0.001, 0.62 * smooth(0, 0.5, hot)));
    gate.visible = hot > 0.002;
    L2.intensity = 26 * radio; L3.intensity = 22 * live; L1.intensity = 14 * (0.6 + 0.4 * live);
    beacon.material.color.copy(C.bad).multiplyScalar(!still && s.banned > 0.5 ? 0.25 + 0.75 * smooth(0.6, 1, 0.5 + 0.5 * Math.sin(time * 2.1)) : 0.55);
    leds.uniforms.uTime.value = time; leds.uniforms.uAct.value = radio * s.redraw; leds.uniforms.uBan.value = s.banned;
    if (s.banned > 0.01) { Lsrv.color.copy(C.bad); Lsrv.intensity = 7 * s.banned; } else { Lsrv.color.copy(C.good); Lsrv.intensity = 5 * s.back * (1 - smooth(0.9, 1, st.sub)); }
    if (stitchRef) stitchRef.visible = p < 4.5 || p > 5.6;
    phone.visible = !(layout === 'wide' && p > 1.6 && p < 2.4);
    plugPhone.g.visible = tailMesh.visible = phone.visible; // never a plug or a cable end in mid air where the phone was

    // the phone and the labels say what the scene shows
    const tether = s.cut < 0.5, data = s.dataOff < 0.5;
    drawPhone(data, tether);
    setTxt(labPh, 'ph', !tether ? 'unplugged' : data ? 'mobile data on' : 'mobile data off');
    setTxt(labMast, 'mast', radio > 0.5 ? '4G / 5G' : 'no connection from your phone');
    const ip = s.newIP ? IP_NEW : IP_OLD;
    if (radio < 0.5) { setTxt(labSrv, 'srv', 'No connection'); setTxt(labSrvN, 'srvn', s.dataOff > 0.5 ? 'getting a new IP' : 'CS2 is cut off'); }
    else if (s.redraw < 0.98) { setTxt(labSrv, 'srv', 'getting a new IP'); setTxt(labSrvN, 'srvn', 'example IPs'); }
    else if (s.banned > 0.5 && !s.newIP) { setTxt(labSrv, 'srv', 'banned <em>' + ip + '</em>'); setTxt(labSrvN, 'srvn', '<span class="bad">kicked: banned, example IP</span>'); }
    else if (s.back > 0.5) { setTxt(labSrv, 'srv', 'sees <em>' + ip + '</em>'); setTxt(labSrvN, 'srvn', '<span class="good">back on the server</span>'); }
    else { setTxt(labSrv, 'srv', 'sees <em>' + ip + '</em>'); setTxt(labSrvN, 'srvn', s.newIP ? 'example IP, new range' : 'example IP, your carrier\'s'); }
    if (ip !== ipShown && scrImg) { ipShown = ip; drawScreen(ip); }

    // ambient: radio rings and packets only where they mean something
    mastRings.forEach((r, i) => { const kk = ((still ? 0.3 : time * 0.32) + i / 2) % 1; r.scale.setScalar(0.4 + kk * 2.2); r.material.opacity = (1 - kk) * 0.5 * radio; });
    for (let i = 0; i < NP; i++) {
      const u = (pSeed[i] + time * (0.035 + pSeed[i] * 0.025)) % 1;
      sampleRoute(u, tmp).addScaledVector(pJit[i], u < 0.3 ? 0 : u < 0.58 ? 0.03 : 0.02);
      pPos[i * 3] = tmp.x; pPos[i * 3 + 1] = tmp.y; pPos[i * 3 + 2] = tmp.z;
      pU[i] = u;
      let al = still ? 0 : 0.9;
      if (u > 0.3) al *= radio;
      if (u > 0.58 && (u - 0.58) / 0.42 > s.redraw) al = 0;
      if (s.cut > 0.02 && u > 0.3 * fJ) al = 0;   // none on the cable's moving last stretch
      if (u > reveal) al = 0;
      pA[i] = al * smooth(0, 0.03, u) * (1 - smooth(0.97, 1, u));
    }
    pGeo.attributes.position.needsUpdate = true; pGeo.attributes.aU.needsUpdate = true; pGeo.attributes.aA.needsUpdate = true;

    if (__gx.tick) __gx.tick(dt, time, s, p);
    placeLabels(p, box);
    return Math.abs(st.p - pNow) > 0.0005 || (!still && reveal < 1);
  }

  yield;
  // ---------- loop ----------
  let raf = 0, last = 0, paused = false, ready = false, first = false, destroyed = false, hidden = false;
  // the first frame is drawn even off screen (hidden): it is what tells the page the world is ready (worldready), and the
  // loading screen waits for it. 9 Nov.: a guide's world below the fold (a phone on its side) never drew, so it never said so
  function request() { if (raf || destroyed || !ready || (hidden && first)) return; if (paused && !dirty) return; raf = requestAnimationFrame(frame); }
  function frame(now) {
    raf = 0;
    const dt = last ? Math.min(0.05, (now - last) / 1000) : 1 / 60;
    last = now;
    const moving = update(paused ? 0 : dt);
    renderAll();
    dirty = false;
    if (!first) { first = true; canvas.dispatchEvent(new Event('worldready')); }
    if ((!still && !paused) || moving) request(); else last = 0;
  }
  function stop() { if (raf) cancelAnimationFrame(raf); raf = 0; last = 0; }

  const fine = !still && matchMedia('(pointer: fine)').matches;
  const onPtr = e => { mouse.x = e.clientX / innerWidth - 0.5; mouse.y = e.clientY / innerHeight - 0.5; };
  if (fine) addEventListener('pointermove', onPtr, { passive: true });
  let rz = 0; const ro = new ResizeObserver(() => { clearTimeout(rz); rz = setTimeout(resize, 150); }); ro.observe(canvas);

  async function comp(obj, cam, target, into) {
    let pr; const prev = renderer.getRenderTarget();
    try { renderer.setRenderTarget(into); pr = renderer.compileAsync(obj, cam, target); } catch (e) { /* compiles on first draw */ }
    finally { renderer.setRenderTarget(prev); }
    try { await pr; } catch (e) { /* same */ }
  }

  // the real Connection page for the monitor, decoded off the main thread
  const scrURL = opts.screen || new URL('img/connection.webp', document.baseURI).href;
  // as a bitmap at the screen's size, made from the file: drawn from an <img>, the browser decoded it again on the main thread
  // when the screen was drawn (~50 ms on a slow phone, 6 Nov.), and a bitmap made from the <img> decodes there too (~90 ms).
  // From the file's bytes it is decoded and scaled off the main thread, so drawing the screen is a plain copy
  const viaImg = () => { const img = new Image(); img.decoding = 'async'; img.src = scrURL; return (img.decode ? img.decode() : new Promise(r => { img.onload = r; })).then(() => img); };
  const imgReady = (window.createImageBitmap && window.fetch
    ? fetch(scrURL).then(r => { if (!r.ok) throw new Error(r.status); return r.blob(); }).then(b => createImageBitmap(b, { resizeWidth: 1240, resizeHeight: 800, resizeQuality: 'high' })).catch(viaImg)
    : viaImg()).then(b => { scrImg = b; }).catch(() => {});

  yield;
  // ---------- start: build, environment, compile every program without blocking, then draw ----------
  resize();
  const fontsReady = (document.fonts ? Promise.all(['600 30px "Funnel Sans"', '400 28px "Funnel Sans"', '600 19px "Fragment Mono"'].map(f => document.fonts.load(f))) : Promise.resolve()).catch(() => {});
  // how far the start has got, 0 to 1, for the page's loading screen (it listens for worldprogress on the document)
  const tell = f => { try { canvas.dispatchEvent(new CustomEvent('worldprogress', { detail: f })); } catch (e) { /* old browser */ } };
  (async () => {
    await yieldTask(); if (destroyed) return;
    // the studio's reflections, for every material (on GPUs where its program links slowly this is the longest step: it has a task of its own)
    { const pmrem = new PMREMGenerator(renderer); envRT = pmrem.fromScene(envScene, 0, 0.1, 80, { size: 128 }); pmrem.dispose(); }
    envScene.traverse(o => { if (o.isMesh) { o.geometry.dispose(); o.material.dispose(); } });
    scene.environment = envRT.texture; scene.environmentIntensity = ENV_K;
    // the ground stays dark: no studio reflections on it (6b)
    await yieldTask(); if (destroyed) return;
    update(0); tell(0.1);
    bakeContact(); aoMat.dispose();
    await yieldTask(); if (destroyed) return;
    // the programs start compiling three objects at a time, the page getting the thread back in between (6 Nov.: all at once
    // it was one 200 ms task on a slow phone). Objects with a light inside are left to the whole-scene pass below: compiled on
    // their own they would count their light twice and get a program the real frame never uses. That pass then finds the
    // rest ready, so it is short; the GPU compiles them all side by side either way
    {
      const prs = [], kids0 = scene.children.filter(o => !o.isLight && !o.getObjectByProperty('isLight', true)), prev = renderer.getRenderTarget();
      for (let i = 0; i < kids0.length; i += 3) {
        renderer.setRenderTarget(composer.readBuffer);
        for (const o of kids0.slice(i, i + 3)) { try { prs.push(renderer.compileAsync(o, camera, scene).catch(() => {})); } catch (e) { /* compiles below */ } }
        renderer.setRenderTarget(prev);
        await yieldTask(); if (destroyed) return;
      }
    }
    await comp(scene, camera, scene, composer.readBuffer);
    if (destroyed) return;
    tell(0.3);
    {
      const tiny = new WebGLRenderTarget(2, 2, { type: HalfFloatType, samples: 4 });
      const kids = scene.children.filter(o => !o.isLight);
      const vis = kids.map(o => o.visible);
      kids.forEach(o => (o.visible = false));
      let done = 0;
      for (const o of kids) { tell(0.3 + 0.55 * (done++ / kids.length)); o.visible = true; renderer.setRenderTarget(tiny); try { await renderer.compileAsync(o, camera, scene); } catch (e) { /* drawn below anyway */ } renderer.render(scene, camera); o.visible = false; await yieldTask(); if (destroyed) return; }
      kids.forEach((o, i) => (o.visible = vis[i]));
      renderer.setRenderTarget(null); tiny.dispose();
    }
    if (ColorManagement.getTransfer(renderer.outputColorSpace) === SRGBTransfer) outPass.material.defines = { SRGB_TRANSFER: '' };
    outPass._outputColorSpace = renderer.outputColorSpace; outPass._toneMapping = renderer.toneMapping; outPass.material.needsUpdate = true;
    const quad = new PlaneGeometry(2, 2), ortho = new OrthographicCamera(-1, 1, 1, -1, 0, 1);
    quad.deleteAttribute('normal');
    const toRT = new Scene(), toScreen = new Scene();
    const addMat = (s, m) => { const q = new Mesh(quad, m); q.frustumCulled = false; s.add(q); };
    for (const kk of Object.keys(bloom)) {
      if (kk[0] === '_') continue;
      const v = bloom[kk];
      if (v instanceof Material) addMat(toRT, v); else if (Array.isArray(v)) v.forEach(m => m instanceof Material && addMat(toRT, m));
    }
    addMat(toRT, addBloom.material);
    addMat(toScreen, outPass.material);
    await comp(toRT, ortho, toRT, bloom.renderTargetBright);
    await comp(toScreen, ortho, toScreen, null);
    quad.dispose(); tell(0.9);
    await Promise.all([fontsReady, imgReady]);
    if (destroyed) return;
    // the two screens and the shadow map each on a task of their own: in the first frame together they made it ~105 ms on a
    // slow phone (6 Nov.). The shadows are drawn from a camera that sees nothing, so only the shadow map costs anything
    phState = ''; drawPhone(true, true); renderer.initTexture(phTex);
    await yieldTask(); if (destroyed) return;
    drawScreen(ipShown); renderer.initTexture(scrTex);
    await yieldTask(); if (destroyed) return;
    {
      const away = new OrthographicCamera(-0.001, 0.001, 0.001, -0.001, 0, 0.001), tiny = new WebGLRenderTarget(2, 2);
      away.position.set(0, -900, 0); away.lookAt(0, -901, 0); away.updateMatrixWorld();
      renderer.shadowMap.needsUpdate = true;
      renderer.setRenderTarget(tiny); renderer.render(scene, away); renderer.setRenderTarget(null); tiny.dispose();
    }
    await yieldTask(); if (destroyed) return;
    ready = true; dirty = true; request();
  })();

  if (opts.debug) window.__wd = { contact: { phParts, plugPhone, plugPC, tailMesh, place: placePhonePlug, tail: () => tailCur, endPC, cableMain, J, PORT, USB_PC, MAT_Y, PLATE_TOP, CR }, scene, mArc, mAir, screenLight, key, rim, hemi, Lrack, L1, L2, L3, M, bloom, mirror, dots, floor, sky, skyline, leds, request: () => { dirty = true; request(); } };
  return {
    set(o) {
      let ch = false;
      for (const k of ['p', 'sub', 'cta']) if (o[k] != null && o[k] !== st[k]) { st[k] = o[k]; ch = true; }
      if (o.jump && Math.abs(st.p - pNow) > 0.5) pNow = st.p - Math.sign(st.p - pNow) * 0.5;
      if (ch) { dirty = true; request(); }
    },
    pause() { paused = true; stop(); dirty = true; request(); },  // one more still frame, then nothing until the scroll changes it
    resume() { if (!paused) return; paused = false; request(); },
    hide(v) { hidden = !!v; if (hidden) { if (first) stop(); } else { dirty = true; request(); } },
    resize,
    get ready() { return ready; },
    destroy() {
      if (destroyed) return;
      destroyed = true; stop();
      if (fine) removeEventListener('pointermove', onPtr);
      ro.disconnect(); wrap.remove();
      const seen = new Set();
      scene.traverse(o => {
        if (o.geometry && !seen.has(o.geometry)) { seen.add(o.geometry); o.geometry.dispose(); }
        const m = o.material; if (m && !seen.has(m)) { seen.add(m); m.dispose(); }
      });
      composer.dispose(); bloom.dispose(); bloomIn.dispose(); if (envRT) envRT.dispose(); aoB.dispose(); mirror.dispose(); renderer.dispose();
    },
  };
}

// The world is built a part at a time (each `yield` above), the page getting the thread back in between: built in one go it
// was one ~220 ms task on a slow phone (6 Nov.). The renderer comes first, so a browser without WebGL still gets null at once.
// Until the last part is built, the api keeps what it is told (the newest set, pause or hide) and hands it on.
function mount(canvas, opts = {}) {
  // for profiling: with window.__wdSteps = [] set before the scene starts, each part's time in ms lands in it
  const steps = Array.isArray(window.__wdSteps) ? window.__wdSteps : null;
  const step = () => { const t = performance.now(), v = it.next(); if (steps) steps.push(Math.round(performance.now() - t)); return v; };
  const it = build(canvas, opts);
  let r = step();
  if (r.done) return r.value;
  let real = null, dead = false, want = null, paused = null, hidden = null;
  (async () => {
    while (!r.done) { await yieldTask(); r = step(); }
    real = r.value;
    if (!real) return;
    if (dead) { real.destroy(); return; }
    if (want) real.set(want);
    if (paused === true) real.pause();
    if (hidden !== null) real.hide(hidden);
  })();
  return {
    set(o) {
      if (real) return real.set(o);
      want = Object.assign(want || {}, o); if (!o.jump) delete want.jump;
    },
    pause() { if (real) real.pause(); else paused = true; },
    resume() { if (real) real.resume(); else paused = false; },
    hide(v) { if (real) real.hide(v); else hidden = !!v; },
    resize() { if (real) real.resize(); },
    get ready() { return !!real && real.ready; },
    destroy() { if (real) real.destroy(); else dead = true; },
  };
}

window.WorldScene = { mount };
