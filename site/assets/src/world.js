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
  CatmullRomCurve3, QuadraticBezierCurve3, CubicBezierCurve3, InstancedMesh, Object3D,
  MeshStandardMaterial, MeshPhysicalMaterial, MeshBasicMaterial, ShaderMaterial, Material,
  CanvasTexture, HemisphereLight, DirectionalLight, PointLight, RectAreaLight, FogExp2, PMREMGenerator, EquirectangularReflectionMapping,
  SRGBColorSpace, AdditiveBlending, DoubleSide, BackSide, ColorManagement, SRGBTransfer, PCFShadowMap, RepeatWrapping, LinearFilter,
} from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
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
.wd-lab{position:absolute;left:0;top:0;opacity:var(--o,0);transform:translate3d(var(--x,-999px),var(--y,-999px),0);will-change:transform,opacity}
.wd-lab i{position:absolute;left:-4px;top:-4px;width:8px;height:8px;border-radius:50%;background:var(--t-text);box-shadow:0 0 0 4px color-mix(in srgb,var(--t-text) 14%,transparent)}
.wd-lab::before{content:"";position:absolute;left:-.5px;bottom:8px;width:1px;height:var(--stem,44px);background:linear-gradient(0deg,color-mix(in srgb,var(--t-text) 55%,transparent),transparent)}
.wd-lab>div{position:absolute;left:-10px;bottom:calc(var(--stem,44px) + 12px);white-space:nowrap;padding:.5rem .7rem;border-radius:7px;background:color-mix(in srgb,var(--t-bg) 90%,transparent);-webkit-backdrop-filter:blur(8px);backdrop-filter:blur(8px);box-shadow:inset 0 0 0 1px color-mix(in srgb,var(--t-text) 11%,transparent)}
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

function mount(canvas, opts = {}) {
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

  // ---------- renderer: MSAA into a half float buffer, so edges are clean and the bloom has room ----------
  let renderer;
  try { renderer = new WebGLRenderer({ canvas, antialias: false, powerPreference: 'high-performance' }); }
  catch (e) { return null; }
  if (!renderer.capabilities.isWebGL2) { renderer.dispose(); return null; }
  renderer.debug.checkShaderErrors = false;
  const small = () => (canvas.clientWidth || innerWidth) < 761;
  const dpr = () => Math.min(devicePixelRatio, small() ? 2 : 1.75);
  renderer.setPixelRatio(dpr());
  renderer.setClearColor(C.bg, 1);
  renderer.shadowMap.enabled = true; renderer.shadowMap.type = PCFShadowMap; renderer.shadowMap.autoUpdate = false;
  RectAreaLightUniformsLib.init();
  const scene = new Scene();
  scene.fog = new FogExp2(C.bg, 0.02);
  const camera = new PerspectiveCamera(30, 1, 0.1, 320);

  // ---------- a studio environment for reflections: dark, two soft boxes and the route's colours low on the horizon ----------
  const envCv = document.createElement('canvas'); envCv.width = 512; envCv.height = 256;
  {
    const g = envCv.getContext('2d');
    const sky = g.createLinearGradient(0, 0, 0, 256);
    sky.addColorStop(0, cssv('--t-surface')); sky.addColorStop(0.5, cssv('--t-surface3')); sky.addColorStop(0.56, cssv('--t-edge')); sky.addColorStop(1, cssv('--t-edge'));
    g.fillStyle = sky; g.fillRect(0, 0, 512, 256);
    const box = (x, y, w, h, col, a) => { g.globalAlpha = a; const r = g.createRadialGradient(x + w / 2, y + h / 2, 2, x + w / 2, y + h / 2, Math.max(w, h)); r.addColorStop(0, col); r.addColorStop(1, 'transparent'); g.fillStyle = r; g.fillRect(x - w, y - h, w * 3, h * 3); g.globalAlpha = 1; };
    box(60, 96, 140, 22, cssv('--t-text'), 0.55); box(330, 100, 110, 18, cssv('--t-muted'), 0.35); box(200, 20, 60, 14, cssv('--t-faint'), 0.12);
    box(10, 110, 120, 18, cssv('--t-top1'), 0.5); box(200, 112, 120, 16, cssv('--t-top2'), 0.45); box(380, 110, 120, 18, cssv('--t-top3'), 0.4);
  }
  const envTex = new CanvasTexture(envCv); envTex.mapping = EquirectangularReflectionMapping; envTex.colorSpace = SRGBColorSpace;

  // ---------- light: a dim world; each part of the route lights what is near it, the screen lights the desk ----------
  const hemi = new HemisphereLight(C.surface3, C.bg, 0.9); scene.add(hemi);
  const key = new DirectionalLight(mul(C.muted, 1), 0.75); key.position.set(-14, 26, 16); key.target.position.set(2, 0, -3);
  key.castShadow = true; key.shadow.mapSize.set(2048, 2048); key.shadow.bias = -0.0004; key.shadow.normalBias = 0.02; key.shadow.radius = 4;
  Object.assign(key.shadow.camera, { left: -22, right: 24, top: 18, bottom: -16, near: 4, far: 70 });
  scene.add(key, key.target);
  const rim = new DirectionalLight(mul(C.text, 1), 1.0); rim.position.set(8, 1.6, -22); scene.add(rim); // separates the silhouettes from the dark
  const SX = 17, SZ = -9.5;
  const L1 = new PointLight(C.top1, 14, 14, 1.6); L1.position.set(-3.5, 1.4, 3.6); scene.add(L1);
  const L2 = new PointLight(C.top2, 26, 18, 1.6); L2.position.set(5.5, 8.7, -5.5); scene.add(L2);
  const L3 = new PointLight(C.top3, 22, 16, 1.6); L3.position.set(SX - 1, 6, SZ + 2.5); scene.add(L3);
  const Lrack = new PointLight(C.muted, 7, 9, 1.6); Lrack.position.set(SX, 2.8, SZ + 3.6); scene.add(Lrack);
  const Lsrv = new PointLight(C.bad, 0, 7, 1.6); Lsrv.position.set(SX, 2.6, SZ + 2.4); scene.add(Lsrv);
  const Lbad = new PointLight(C.bad, 0, 6, 1.8); Lbad.position.set(-8.4, 1.4, -1.6); scene.add(Lbad);

  // sky: a dark dome with a faint cool-to-warm horizon, so the world has an edge
  scene.add(new Mesh(new SphereGeometry(150, 32, 16), new ShaderMaterial({
    side: BackSide, depthWrite: false, fog: false,
    uniforms: { uBg: { value: C.bg }, uA: { value: C.top1 }, uB: { value: C.top2 } },
    vertexShader: `varying vec3 vP; void main(){ vP = normalize(position); gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }`,
    fragmentShader: `uniform vec3 uBg,uA,uB; varying vec3 vP; void main(){ float h = exp(-abs(vP.y)*14.); float side = smoothstep(-1.,1.,vP.x); vec3 tint = mix(uA,uB,side); gl_FragColor = vec4(uBg + tint*h*.014, 1.); }`,
  })));

  // ---------- materials ----------
  const M = {
    metal: new MeshStandardMaterial({ color: C.surface3, emissive: C.surface2, emissiveIntensity: 0.25, metalness: 0.35, roughness: 0.4, envMapIntensity: 0.6 }),
    metalDark: new MeshStandardMaterial({ color: C.surface2, emissive: C.surface2, emissiveIntensity: 0.35, metalness: 0.3, roughness: 0.6, envMapIntensity: 0.55 }),
    plastic: new MeshStandardMaterial({ color: C.side, metalness: 0, roughness: 0.55, envMapIntensity: 0.5 }),
    rubber: new MeshStandardMaterial({ color: C.edge, metalness: 0, roughness: 0.9 }),
    mat: new MeshStandardMaterial({ color: C.surface, metalness: 0, roughness: 0.95 }),
    galv: new MeshStandardMaterial({ color: mul(C.muted, 0.5), metalness: 0.4, roughness: 0.45, envMapIntensity: 0.7 }),
    radome: new MeshStandardMaterial({ color: mul(C.muted, 0.62), metalness: 0.05, roughness: 0.62, envMapIntensity: 0.5 }),
    glass: new MeshPhysicalMaterial({ color: C.edge, metalness: 0.1, roughness: 0.08, transparent: true, opacity: 0.55, envMapIntensity: 1.1, clearcoat: 1, clearcoatRoughness: 0.05 }),
    // bead-blasted aluminium: the tower and the phone stand catch the light without a hot spot
    matte: new MeshStandardMaterial({ color: C.surface3, emissive: C.surface2, emissiveIntensity: 0.25, metalness: 0.3, roughness: 0.85 }),
    matteDark: new MeshStandardMaterial({ color: C.surface2, emissive: C.surface2, emissiveIntensity: 0.35, metalness: 0.25, roughness: 0.85 }),
    chrome: new MeshStandardMaterial({ color: C.muted, metalness: 0.5, roughness: 0.3, envMapIntensity: 1 }),
  };
  const shadowy = o => { o.traverse(m => { if (m.isMesh) { m.castShadow = true; m.receiveShadow = true; } }); return o; };
  const rbox = (w, h, d, r, mat, seg = 3) => new Mesh(new RoundedBoxGeometry(w, h, d, seg, Math.min(r, w / 2, h / 2, d / 2)), mat);

  // small canvas textures, drawn with the app's tokens
  function cvTex(w, h, draw, srgb = true) {
    const cv = document.createElement('canvas'); cv.width = w; cv.height = h; draw(cv.getContext('2d'), w, h);
    const t = new CanvasTexture(cv); if (srgb) t.colorSpace = SRGBColorSpace; t.anisotropy = 8; return t;
  }

  // ---------- ground: a matte floor with A's quiet dot lattice, lit for real by the route's lights ----------
  const floor = new Mesh(new PlaneGeometry(260, 260), new MeshStandardMaterial({ color: mul(C.bg, 0.6), roughness: 1, metalness: 0 }));
  floor.rotation.x = -Math.PI / 2; floor.receiveShadow = true; scene.add(floor);
  const dots = new Mesh(new PlaneGeometry(260, 260), new ShaderMaterial({
    transparent: true, depthWrite: false, blending: AdditiveBlending,
    uniforms: { uDot: { value: C.faint }, uCam: { value: camera.position } },
    vertexShader: `varying vec3 vW; void main(){ vec4 w = modelMatrix*vec4(position,1.); vW = w.xyz; gl_Position = projectionMatrix*viewMatrix*w; }`,
    fragmentShader: `uniform vec3 uDot,uCam; varying vec3 vW;
    void main(){
      vec2 p = vW.xz;
      vec2 g = abs(fract(p+.5)-.5); float d = length(g);
      float aa = fwidth(d)*1.1; float dotm = 1.-smoothstep(.026-aa,.026+aa,d);
      vec2 g5 = abs(fract(p/6.+.5)-.5)*6.; float ln = min(g5.x,g5.y); float la = fwidth(ln); float line = 1.-smoothstep(0.,la*1.1,ln);
      float fade = exp(-length(vW-uCam)*.028);
      gl_FragColor = vec4(uDot*dotm*.42*fade, 1.);
    }`,
  }));
  dots.rotation.x = -Math.PI / 2; dots.position.y = 0.003; scene.add(dots);
  // contact shadows: soft dark pools where things meet the ground
  const blobTex = cvTex(128, 128, (g) => { const r = g.createRadialGradient(64, 64, 0, 64, 64, 64); r.addColorStop(0, 'rgba(0,0,0,.85)'); r.addColorStop(.55, 'rgba(0,0,0,.35)'); r.addColorStop(1, 'rgba(0,0,0,0)'); g.fillStyle = r; g.fillRect(0, 0, 128, 128); }, false);
  const blobMat = new MeshBasicMaterial({ map: blobTex, transparent: true, depthWrite: false, opacity: 1 });
  function blob(x, z, w, d, ry = 0) { const m = new Mesh(new PlaneGeometry(w, d), blobMat); m.rotation.set(-Math.PI / 2, 0, ry); m.position.set(x, 0.006, z); scene.add(m); }

  let stitchRef = null;
  // ---------- your desk: a mat that groups PC, screen, keyboard and phone ----------
  {
    const deskMat = rbox(11.6, 0.05, 6.8, 0.025, M.mat); deskMat.position.set(-4.1, 0.025, 1.75); deskMat.receiveShadow = true; scene.add(deskMat);
    const stitch = new Mesh(new RingGeometry(0.995, 1, 4, 1), new MeshBasicMaterial({ color: C.line })); stitchRef = stitch;
    stitch.rotation.set(-Math.PI / 2, 0, Math.PI / 4); stitch.scale.set(8.05, 4.72, 1); stitch.position.set(-4.1, 0.052, 1.75); scene.add(stitch);
  }

  // ---------- the PC tower: brushed side, a glass side panel with the parts inside, a vented front with the app's top line ----------
  const pc = new Group(); scene.add(pc);
  const TW = 1.15, TH = 2.5, TD = 2.5, TX = -7.6, TZ = 0.6;
  {
    const t = new Group(); t.position.set(TX, 0, TZ); pc.add(t);
    const body = rbox(TW, TH, TD, 0.07, M.matteDark); body.position.y = TH / 2 + 0.06; t.add(body);
    // feet
    for (const [x, z] of [[-0.4, -1], [0.4, -1], [-0.4, 1], [0.4, 1]]) { const f = new Mesh(new CylinderGeometry(0.08, 0.09, 0.06, 16), M.rubber); f.position.set(x, 0.03, z); t.add(f); }
    // front panel (+z): fine vents and the top line, vertical
    const frontTex = cvTex(256, 560, (g, w, h) => {
      g.fillStyle = cssv('--t-side'); g.fillRect(0, 0, w, h);
      g.fillStyle = cssv('--t-edge');
      for (let y = 40; y < h - 40; y += 9) for (let x = 30 + ((y / 9) % 2) * 4; x < w - 60; x += 9) { g.beginPath(); g.arc(x, y, 2.4, 0, 7); g.fill(); }
      g.fillStyle = cssv('--t-surface3'); g.fillRect(w - 40, 40, 2, h - 80);
    });
    const front = new Mesh(new PlaneGeometry(TW - 0.12, TH - 0.12), new MeshStandardMaterial({ map: frontTex, roughness: 0.7, metalness: 0.3, envMapIntensity: 0.4 }));
    front.position.set(0, TH / 2 + 0.06, TD / 2 + 0.002); t.add(front);
    const lineGeo = new PlaneGeometry(0.035, TH - 0.5);
    const topLine = new Mesh(lineGeo, new ShaderMaterial({
      uniforms: { c1: { value: C.top1 }, c2: { value: C.top2 }, c3: { value: C.top3 } },
      vertexShader: `varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }`,
      fragmentShader: `uniform vec3 c1,c2,c3; varying vec2 vUv; void main(){ float u = 1.-vUv.y; vec3 c = u < .5 ? mix(c1,c2,u*2.) : mix(c2,c3,u*2.-1.); gl_FragColor = vec4(c*1.15,1.); }`,
    }));
    topLine.position.set(TW / 2 - 0.2, TH / 2 + 0.06, TD / 2 + 0.004); t.add(topLine);
    // power button
    const pb = new Mesh(new CylinderGeometry(0.06, 0.06, 0.02, 24), M.chrome); pb.rotation.x = Math.PI / 2; pb.position.set(-0.28, TH - 0.18, TD / 2 + 0.01); t.add(pb);
    const pl = new Mesh(new RingGeometry(0.062, 0.075, 32), new MeshBasicMaterial({ color: mul(C.top1, 1.2) })); pl.position.set(-0.28, TH - 0.18, TD / 2 + 0.021); t.add(pl);
    // glass side (-x, towards the camera) and what is inside
    const glass = new Mesh(new PlaneGeometry(TD - 0.24, TH - 0.24), M.glass); glass.rotation.y = -Math.PI / 2; glass.position.set(-TW / 2 - 0.003, TH / 2 + 0.06, 0); t.add(glass);
    const inside = new Group(); inside.position.set(-0.15, 0, 0); t.add(inside);
    const board = new Mesh(new PlaneGeometry(TD - 0.5, TH - 0.5), new MeshStandardMaterial({ color: C.side, roughness: 0.8 })); board.rotation.y = -Math.PI / 2; board.position.set(0.3, TH / 2 + 0.1, -0.05); inside.add(board);
    const gpu = rbox(0.36, 0.3, 1.6, 0.03, M.metal); gpu.position.set(0.05, 1.0, 0.1); inside.add(gpu);
    const gpuLine = new Mesh(new PlaneGeometry(1.3, 0.012), new MeshBasicMaterial({ color: mul(C.top1, 0.8) })); gpuLine.rotation.y = -Math.PI / 2; gpuLine.position.set(-0.131, 1.08, 0.1); inside.add(gpuLine);
    const cooler = new Mesh(new CylinderGeometry(0.24, 0.24, 0.22, 28), M.metal); cooler.rotation.z = Math.PI / 2; cooler.position.set(0.12, 1.85, -0.2); inside.add(cooler);
    for (const y of [1.9, 1.15]) { const fan = new Mesh(new TorusGeometry(0.3, 0.025, 8, 40), new MeshBasicMaterial({ color: mul(C.top1, 0.45) })); fan.position.set(0.05, y, TD / 2 - 0.12); inside.add(fan); const hub = new Mesh(new CylinderGeometry(0.08, 0.08, 0.04, 16), M.metalDark); hub.rotation.x = Math.PI / 2; hub.position.set(0.05, y, TD / 2 - 0.12); inside.add(hub); }
    const psu = rbox(0.5, 0.35, 0.9, 0.02, M.metalDark); psu.position.set(0.1, 0.32, -0.6); inside.add(psu);
    const glow = new PointLight(C.top1, 3, 1.8, 2); glow.position.set(-0.1, 1.6, 0.2); inside.add(glow); // the parts read through the glass
    shadowy(t); glass.castShadow = false; front.castShadow = false;
    blob(TX, TZ, 2.2, 3.4);
  }
  // the ports the two cables leave from
  const USB_PC = V3(TX + TW / 2 + 0.03, 0.42, TZ + 1.08);
  const LAN_PC = V3(TX - 0.15, 0.32, TZ - TD / 2 - 0.02);

  // ---------- the monitor: thin panel, real app on screen, its light falls on the desk ----------
  const mon = new Group(); mon.position.set(-4.5, 0.05, 0.1); mon.rotation.y = -0.28; pc.add(mon);
  const SW = 3.42, SH = 2.21, SY = 2.5;
  {
    const panel = rbox(SW + 0.12, SH + 0.14, 0.07, 0.03, M.plastic); panel.position.set(0, SY, 0); mon.add(panel);
    const back = rbox(1.6, 1.1, 0.26, 0.12, M.metalDark); back.position.set(0, SY - 0.1, -0.14); mon.add(back);
    const chin = new Mesh(new PlaneGeometry(SW + 0.06, 0.02), new MeshBasicMaterial({ color: C.lineStrong })); chin.position.set(0, SY - SH / 2 - 0.035, 0.037); mon.add(chin);
    const neck = rbox(0.34, 1.6, 0.1, 0.04, M.metalDark); neck.position.set(0, 1.08, -0.3); neck.rotation.x = 0.12; mon.add(neck);
    const foot = rbox(1.3, 0.05, 0.85, 0.02, M.metalDark); foot.position.set(0, 0.025, -0.2); mon.add(foot);
    shadowy(mon);
    blob(-4.5, 0, 2.2, 1.6, 0.28);
  }
  // the screen: the real Connection page, with the IP the server sees patched in as the story changes it
  const scrCv = document.createElement('canvas'); scrCv.width = 1240; scrCv.height = 800;
  const scrG = scrCv.getContext('2d');
  scrG.fillStyle = cssv('--t-surface'); scrG.fillRect(0, 0, 1240, 800);
  const scrTex = new CanvasTexture(scrCv); scrTex.colorSpace = SRGBColorSpace; scrTex.anisotropy = 8; scrTex.minFilter = LinearFilter; scrTex.generateMipmaps = false;
  const screenMat = new MeshBasicMaterial({ map: scrTex, toneMapped: false }); screenMat.color.setScalar(0.78);
  const screen = new Mesh(new PlaneGeometry(SW, SH), screenMat); screen.position.set(0, SY, 0.037); mon.add(screen);
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
    const base = rbox(2.75, 0.07, 0.9, 0.03, M.metalDark); base.position.y = 0.035; kb.add(base);
    const keyGeo = new RoundedBoxGeometry(0.14, 0.05, 0.14, 2, 0.02);
    const keys = new InstancedMesh(keyGeo, M.plastic, 15 * 5); const o = new Object3D(); let n = 0;
    for (let r = 0; r < 4; r++) for (let c = 0; c < 15; c++) { o.position.set(-1.27 + c * 0.18 + (r % 2) * 0.03, 0.09, -0.34 + r * 0.17); o.scale.set(1, 1, 1); o.updateMatrix(); keys.setMatrixAt(n++, o.matrix); }
    for (const [x, w] of [[-1.17, 2.2], [-0.62, 1], [0.17, 5.6], [0.98, 1], [1.17, 1.4]]) { o.position.set(x, 0.09, -0.34 + 4 * 0.17); o.scale.set(w, 1, 1); o.updateMatrix(); keys.setMatrixAt(n++, o.matrix); }
    keys.count = n;
    kb.add(keys);
    const mouse = new Mesh(new SphereGeometry(0.15, 24, 16), M.plastic); mouse.scale.set(0.75, 0.35, 1.15); mouse.position.set(1.9, 0.06, 0.1); kb.add(mouse);
    shadowy(kb); blob(-4.25, 1.75, 3.2, 1.3, 0.28);
  }

  // ---------- the phone on a stand, its screen shows its real state ----------
  const phone = new Group(); phone.position.set(-0.55, 0.05, 2.85); phone.rotation.y = -0.6; scene.add(phone);
  const PW = 0.9, PH = 1.86, PD = 0.085, TILT = -0.36;
  const tilt = new Group(); tilt.position.set(0, 0.16, 0.12); tilt.rotation.x = TILT; phone.add(tilt);
  {
    const stand = new Group(); phone.add(stand);
    const plate = rbox(1.0, 0.04, 0.95, 0.02, M.matte); plate.position.set(0, 0.02, -0.08); stand.add(plate);
    const back = rbox(0.6, 1.2, 0.05, 0.02, M.matte); back.position.set(0, 0.62, -0.3); back.rotation.x = TILT; stand.add(back);
    const lip = rbox(1.0, 0.12, 0.08, 0.03, M.matte); lip.position.set(0, 0.1, 0.2); stand.add(lip);
    const body = rbox(PW, PH, PD, 0.1, M.metalDark, 4); body.position.set(0, PH / 2, 0); tilt.add(body);
    if (opts.ios) { const cam = rbox(0.36, 0.36, 0.035, 0.08, M.metalDark); cam.position.set(-0.2, PH - 0.3, -PD / 2 - 0.014); tilt.add(cam); for (const [lx, ly] of [[-0.08, 0.08], [-0.08, -0.08], [0.08, 0]]) { const l = new Mesh(new CylinderGeometry(0.055, 0.055, 0.03, 20), M.chrome); l.rotation.x = Math.PI / 2; l.position.set(-0.2 + lx, PH - 0.3 + ly, -PD / 2 - 0.035); tilt.add(l); } }
    else { const cam = rbox(0.7, 0.18, 0.035, 0.08, M.plastic); cam.position.set(0, PH - 0.25, -PD / 2 - 0.014); tilt.add(cam); }
    shadowy(phone); blob(-0.55, 2.85, 1.4, 1.4, 0.6);
  }
  const phCv = document.createElement('canvas'); phCv.width = 512; phCv.height = 1060;
  const phTex = new CanvasTexture(phCv); phTex.colorSpace = SRGBColorSpace; phTex.anisotropy = 8;
  const phScreen = new Mesh(new PlaneGeometry(PW - 0.07, PH - 0.08), new MeshBasicMaterial({ map: phTex, toneMapped: false }));
  phScreen.material.color.setScalar(0.85);
  phScreen.position.set(0, PH / 2, PD / 2 + 0.002); tilt.add(phScreen);
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

  // ---------- the carrier's cell site: a monopole with three sector antennas, a dish, a cabinet and a fence ----------
  const mastBase = V3(5.5, 0, -6.5), mastH = 8.2;
  const mastTop = mastBase.clone().add(V3(0, mastH + 0.15, 0));
  {
    const s = new Group(); s.position.copy(mastBase); scene.add(s);
    const pole = new Mesh(new CylinderGeometry(0.16, 0.34, mastH, 28, 1), M.galv); pole.position.y = mastH / 2; s.add(pole);
    for (const y of [2.6, 5.2]) { const j = new Mesh(new CylinderGeometry(0.3 - y * 0.022, 0.3 - y * 0.022, 0.12, 28), M.galv); j.position.y = y; s.add(j); } // the flanged joints
    const tray = new Mesh(new BoxGeometry(0.08, mastH - 0.6, 0.05), M.metalDark); tray.position.set(0.25, (mastH - 0.6) / 2, 0.12); tray.rotation.z = -0.017; s.add(tray);
    const ring = new Mesh(new TorusGeometry(0.62, 0.035, 8, 48), M.galv); ring.rotation.x = Math.PI / 2; ring.position.y = mastH - 0.35; s.add(ring);
    const ring2 = ring.clone(); ring2.position.y = mastH - 1.55; s.add(ring2);
    for (let k = 0; k < 3; k++) {
      const a = (k / 3) * Math.PI * 2 + 0.4;
      const arm = new Mesh(new BoxGeometry(0.62, 0.05, 0.05), M.galv); arm.position.set(Math.cos(a) * 0.31, mastH - 0.95, Math.sin(a) * 0.31); arm.rotation.y = -a; s.add(arm);
      const ant = rbox(0.3, 1.45, 0.12, 0.05, M.radome); ant.position.set(Math.cos(a) * 0.64, mastH - 0.95, Math.sin(a) * 0.64); ant.rotation.y = -a + Math.PI / 2; s.add(ant);
      const rru = rbox(0.2, 0.32, 0.12, 0.02, M.metalDark); rru.position.set(Math.cos(a) * 0.42, mastH - 1.9, Math.sin(a) * 0.42); rru.rotation.y = -a + Math.PI / 2; s.add(rru);
    }
    const dishG = new Group(); dishG.position.set(-0.4, mastH - 2.7, 0.26); dishG.rotation.y = -0.9; s.add(dishG);
    const dish = new Mesh(new SphereGeometry(0.34, 28, 10, 0, Math.PI * 2, 0, 0.55), new MeshStandardMaterial({ color: mul(C.muted, 0.62), roughness: 0.6, side: DoubleSide })); dish.rotation.x = -Math.PI / 2; dish.position.z = 0.29; dishG.add(dish);
    const dishRim = new Mesh(new TorusGeometry(0.175, 0.012, 6, 32), M.galv); dishRim.position.z = 0.13; dishG.add(dishRim);
    const feed = new Mesh(new CylinderGeometry(0.012, 0.012, 0.25, 6), M.galv); feed.rotation.x = Math.PI / 2; feed.position.z = 0.24; dishG.add(feed);
    const mount = new Mesh(new BoxGeometry(0.05, 0.05, 0.3), M.galv); mount.position.z = -0.12; dishG.add(mount);
    const cab = rbox(1.1, 1.5, 0.75, 0.04, M.metalDark); cab.position.set(1.3, 0.75, 0.5); s.add(cab);
    const door = new Mesh(new PlaneGeometry(0.004, 1.3), new MeshBasicMaterial({ color: C.surface })); door.position.set(1.3, 0.75, 0.876); s.add(door);
    const pad = rbox(4.2, 0.12, 3.6, 0.03, M.mat); pad.position.set(0.6, 0.06, 0.2); s.add(pad);
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
  beacon.position.copy(mastBase).add(V3(0, mastH + 0.05, 0)); scene.add(beacon);

  // ---------- the CS2 server: three cabinets on a raised floor; the middle one is the server you play on ----------
  const serverTop = V3(SX, 4.62, SZ);
  const RW = 1.75, RH = 4.6, RD = 1.5;
  const unitTex = cvTex(256, 512, (g, w, h) => {
    g.fillStyle = cssv('--t-edge'); g.fillRect(0, 0, w, h);
    const U = h / 30;
    for (let u = 0; u < 30; u++) {
      const y = u * U; if (u % 7 === 6) continue; // gaps, as real racks have
      g.fillStyle = (u % 3) ? cssv('--t-surface2') : cssv('--t-surface3'); g.fillRect(10, y + 1, w - 20, U - 2);
      g.fillStyle = cssv('--t-side');
      for (let b = 0; b < 8; b++) g.fillRect(70 + b * 20, y + 4, 16, U - 8);
      g.fillStyle = cssv('--t-line-strong'); g.fillRect(16, y + U / 2 - 1, 30, 2);
    }
  });
  const doorTex = cvTex(256, 512, (g, w, h) => {
    g.fillStyle = '#000'; g.fillRect(0, 0, w, h); g.fillStyle = '#fff';
    for (let y = 8; y < h - 8; y += 6) for (let x = 8 + ((y / 6) % 2) * 3; x < w - 8; x += 6) { g.beginPath(); g.arc(x, y, 1.9, 0, 7); g.fill(); }
  }, false);
  const ledPts = [];
  {
    const s = new Group(); scene.add(s);
    const floorTiles = rbox(7.2, 0.18, 3.2, 0.03, M.metalDark); floorTiles.position.set(SX, 0.09, SZ); s.add(floorTiles);
    const tileLines = new Mesh(new PlaneGeometry(7.1, 3.1), new MeshBasicMaterial({ map: cvTex(512, 224, (g, w, h) => { g.fillStyle = cssv('--t-surface2'); g.fillRect(0, 0, w, h); g.strokeStyle = cssv('--t-edge'); g.lineWidth = 3; for (let x = 0; x <= w; x += w / 11) { g.beginPath(); g.moveTo(x, 0); g.lineTo(x, h); g.stroke(); } for (let y = 0; y <= h; y += h / 5) { g.beginPath(); g.moveTo(0, y); g.lineTo(w, y); g.stroke(); } }) }));
    tileLines.material.color.setScalar(0.5); tileLines.rotation.x = -Math.PI / 2; tileLines.position.set(SX, 0.181, SZ); s.add(tileLines);
    for (const [i, x] of [[0, SX - 2.05], [1, SX], [2, SX + 2.05]]) {
      const r = new Group(); r.position.set(x, 0.18, SZ); s.add(r);
      const frame = rbox(RW, RH, RD, 0.04, M.metalDark); frame.position.y = RH / 2; r.add(frame);
      const units = new Mesh(new PlaneGeometry(RW - 0.2, RH - 0.3), new MeshStandardMaterial({ map: unitTex, roughness: 0.6, metalness: 0.4, envMapIntensity: 0.4 }));
      units.position.set(0, RH / 2, RD / 2 + 0.005); r.add(units);
      const door = new Mesh(new PlaneGeometry(RW - 0.12, RH - 0.16), new MeshStandardMaterial({ color: C.surface3, metalness: 0.45, roughness: 0.55, alphaMap: doorTex, transparent: true, side: DoubleSide, envMapIntensity: 0.5 }));
      door.position.set(0, RH / 2, RD / 2 + 0.05); r.add(door);
      const handle = rbox(0.04, 0.5, 0.05, 0.015, M.chrome); handle.position.set(RW / 2 - 0.16, RH / 2, RD / 2 + 0.09); r.add(handle);
      const top = rbox(RW, 0.06, RD, 0.02, M.metal); top.position.y = RH + 0.03; r.add(top);
      // power and activity lights on each unit: the middle cabinet's blink with the game's packets
      const U = (RH - 0.3) / 30;
      for (let u = 0; u < 30; u++) { if (u % 7 === 6) continue; ledPts.push([x - RW / 2 + 0.24, 0.18 + 0.15 + RH - 0.3 - (u + 0.5) * U, SZ + RD / 2 + 0.085, i === 1 ? 1 : 0, Math.random()]); }
      shadowy(r); door.castShadow = false;
    }
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
      uniforms: { uTime: { value: 0 }, uG: { value: C.good }, uB: { value: C.bad }, uPx: { value: 1 }, uAct: { value: 1 }, uBan: { value: 0 } },
      vertexShader: `attribute float aK,aSeed; uniform float uTime,uPx,uAct,uBan; varying float vA; varying float vBan;
      void main(){ vec4 mv = modelViewMatrix*vec4(position,1.);
        float tick = floor(uTime*14. + aSeed*31.); float r = fract(sin(aSeed*91.7 + tick*1.37)*43758.5);
        float act = aK > .5 ? step(1.-uAct*.6, r) : .55;   // the game server's lights follow the traffic
        vA = max(act, aK*uBan); vBan = aK*uBan; gl_PointSize = min(uPx*(46. + 46.*aK*uBan)/-mv.z, 4.*uPx); gl_Position = projectionMatrix*mv; }`,
      fragmentShader: `uniform vec3 uG,uB; varying float vA; varying float vBan; void main(){ float d = length(gl_PointCoord-.5); float a = smoothstep(.5,.0,d); gl_FragColor = vec4(mix(uG,uB,vBan)*a*vA*1.3, 1.); }`,
    });
    scene.add(new Points(g, m)); return m;
  })();

  // ---------- home: the router and the grey home route (browser, Discord, downloads) ----------
  const routerPos = V3(-11, 0, -4.8);
  {
    const r = new Group(); r.position.copy(routerPos); r.rotation.y = 0.35; scene.add(r);
    const body = rbox(1.9, 0.34, 1.2, 0.12, M.plastic); body.position.y = 0.2; r.add(body);
    const face = new Mesh(new PlaneGeometry(1.5, 0.05), new MeshBasicMaterial({ color: C.surface3 })); face.position.set(0, 0.2, 0.602); r.add(face);
    for (let i = 0; i < 4; i++) { const led = new Mesh(new CircleGeometry(0.025, 12), new MeshBasicMaterial({ color: mul(C.text, 0.9) })); led.position.set(-0.55 + i * 0.16, 0.26, 0.604); r.add(led); }
    for (const [dx, rz] of [[-0.75, 0.22], [-0.25, 0.06], [0.25, -0.06], [0.75, -0.22]]) {
      const a = new Group(); a.position.set(dx, 0.36, -0.5); a.rotation.z = rz; r.add(a);
      const st = new Mesh(new CylinderGeometry(0.035, 0.045, 1.0, 12), M.plastic); st.position.y = 0.5; a.add(st);
      const tip = new Mesh(new SphereGeometry(0.035, 12, 8), M.plastic); tip.position.y = 1.0; a.add(tip);
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
  scene.add(new Mesh(new TubeGeometry(homeCable, 80, 0.04, 8), homeMat));
  scene.add(new Mesh(new TubeGeometry(homeOut, 90, 0.035, 6), homeMat));

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
  { const pane = new Mesh(new PlaneGeometry(1.55, 1.45), hatch); pane.position.set(0, 0.79, 0); gate.add(pane); }
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
  scene.add(new Mesh(new TubeGeometry(homeTry, 60, 0.035, 6), tryMat));

  // ---------- the route itself: proposal A's line, at full strength ----------
  const toPhone = PORT.clone().add(PHONE_FRONT.clone().multiplyScalar(0.55)).setY(0.08);
  const cable = new CatmullRomCurve3([USB_PC, USB_PC.clone().add(V3(0.35, -0.3, 0.25)), V3(-6.3, 0.07, 2.75), V3(-4.9, 0.07, 3.7), V3(-3.0, 0.07, 4.0), V3(-1.7, 0.07, 3.85), toPhone, PORT.clone().add(PHONE_FRONT.clone().multiplyScalar(0.12)).add(V3(0, -0.06, 0)), PORT], false, 'centripetal');
  const air = new QuadraticBezierCurve3(PHONE_TOP, V3(1.6, 6.8, -1.6), mastTop.clone().add(V3(-0.2, -0.95, 0.2)));
  const arc = new CubicBezierCurve3(mastTop.clone(), V3(9, 13, -7.5), V3(15.5, 11, -9.5), serverTop.clone().add(V3(0, 0.25, 0)));
  const SEG = [{ c: cable, a: 0, b: 0.3 }, { c: air, a: 0.3, b: 0.58 }, { c: arc, a: 0.58, b: 1 }];
  const routeFS = `uniform vec3 c1,c2,c3,uDead; uniform float uS,uE,uTime,uOn,uDash,uCut,uGain,uDraw,uReveal;
  varying vec2 vUv; varying vec3 vN; varying vec3 vV;
  void main(){
    float u = mix(uS,uE,vUv.x);
    vec3 col = u < .5 ? mix(c1,c2,u*2.) : mix(c2,c3,u*2.-1.);
    float core = pow(abs(dot(normalize(vN),normalize(vV))),1.2);
    if(uDash > 0.){ if(fract(vUv.x*uDash - uTime*1.6) > .5) discard; }
    if(uCut > 0. && vUv.x > 1. - uCut) discard;              // the cable pulled out of the phone
    float k = fract(u*6. - uTime*.2); float pulse = exp(-pow((k-.5)*9.,2.));
    float on = uOn * step(vUv.x, uDraw) * step(u, uReveal);
    float I = (.5 + 1.9*pulse) * (.3 + .7*core) * on * uGain;
    gl_FragColor = vec4(max(col*I, uDead*(.5+.5*core)), 1.);
  }`;
  function routeMat(a, b, extra = {}) {
    return new ShaderMaterial({
      uniforms: { c1: { value: C.top1 }, c2: { value: C.top2 }, c3: { value: C.top3 }, uDead: { value: mul(C.lineStrong, 1.6) }, uS: { value: a }, uE: { value: b }, uTime: { value: 0 }, uOn: { value: 1 }, uDash: { value: 0 }, uCut: { value: 0 }, uGain: { value: 1.25 }, uDraw: { value: 1 }, uReveal: { value: 1 }, ...extra },
      vertexShader: routeVS, fragmentShader: routeFS,
    });
  }
  const mCable = routeMat(0, 0.3), mAir = routeMat(0.3, 0.58, { uDash: { value: 46 } }), mArc = routeMat(0.58, 1);
  scene.add(new Mesh(new TubeGeometry(cable, 220, 0.06, 12), mCable));
  scene.add(new Mesh(new TubeGeometry(air, 200, 0.022, 6), mAir));
  scene.add(new Mesh(new TubeGeometry(arc, 220, 0.05, 10), mArc));
  // the two USB-C plugs: real ends on a real cable; the phone's one comes out when the cable is pulled
  function plug(at, dir) {
    const g = new Group(); g.position.copy(at); g.lookAt(at.clone().add(dir)); scene.add(g);
    const sh = rbox(0.13, 0.08, 0.3, 0.035, M.plastic); sh.position.z = -0.15; g.add(sh);
    const tipm = rbox(0.085, 0.032, 0.09, 0.014, M.chrome); tipm.position.z = 0.03; g.add(tipm);
    return g;
  }
  const plugPC = plug(USB_PC.clone(), V3(-1, 0, 0));
  const pdir = PORT.clone().sub(cable.getPointAt(0.985)).normalize();
  const plugPhone = plug(PORT.clone().sub(pdir.clone().multiplyScalar(0.05)), pdir);
  const plugHome = plugPhone.position.clone();

  // radio: rings round the sector antennas while mobile data is on
  const toPh = Math.atan2(-(PORT.z - mastBase.z), PORT.x - mastBase.x);
  const ringGeo = new RingGeometry(0.97, 1, 40, 1, toPh - 0.6, 1.2);
  const mastRings = [];
  for (let i = 0; i < 2; i++) {
    const m = new Mesh(ringGeo, new MeshBasicMaterial({ color: C.top2, transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide }));
    m.position.copy(mastTop).add(V3(0, -0.95, 0)); m.rotation.x = -Math.PI / 2; scene.add(m); mastRings.push(m);
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
  const packets = new Points(pGeo, pMat); packets.frustumCulled = false; scene.add(packets);

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
  const SHOTS = {
    wide: [
      { pos: V3(-21, 14, 26), tgt: V3(3.5, 3.4, -4), fov: 30, keys: [K(apex, 10), K(mastTop, 96, 60, 0, 10), K(serverTop, 110, 10, 0, 170), ...rackPts.map(p => K(p, 0, 10, 10, 6)), K(PORT, 100, 120, 14), K(cable.getPointAt(0.35), 0, 0, 16, 0)] },
      { pos: V3(-12, 6.5, 13), tgt: V3(-4.5, 0.8, 0.8), fov: 34, keys: [K(mastTop, 96, 40, 0, 10), K(V3(SX + 2.95, RH + 0.2, SZ - 0.75), 20, 60), K(V3(SX + 2.95, 0, SZ + 0.75), 0, 40, 10), K(V3(SX + 2.95, RH, SZ + 0.75), 0, 40), K(apex, 30), ...towerPts.map(p => K(p, 0, 0, 10, 10)), K(monW(0, SY + 1.15), 100, 0, 0, 0), K(PORT, 100, 140, 14, 0), K(PHONE_TOP, 0, 20), K(routerPos.clone().add(V3(0, 1.4, 0)), 110, 40, 0, 20), K(cable.getPointAt(0.5), 0, 0, 40, 0)] },
      { pos: V3(-1, 2.4, 11), tgt: V3(10, 6, -8), fov: 36, keys: [K(mastTop, 96, 40, 0, 10), K(mastBase, 0, 0, 10), K(apex, 10), K(serverTop, 110, 10, 0, 170), K(V3(SX + 2.95, 0, SZ + 0.75), 0, 8, 8)] },
      { pos: V3(-12.5, 13, 13), tgt: V3(-6.2, 0.4, -0.6), fov: 34, keys: [...towerPts.map(p => K(p, 0, 0, 10, 10)), K(gatePos.clone().add(V3(0, 1.7, 0)), 110, 60, 0, 30), K(routerPos.clone().add(V3(0, 1.4, 0)), 0, 20, 0, 20), K(PORT, 90, 120, 20), K(monW(1.75, SY + 1.15))] },
      { pos: V3(-1, 4, 10), tgt: V3(12, 5.5, -8), fov: 38, keys: [K(mastTop, 96, 40, 0, 10), K(mastBase, 0, 0, 10), K(apex, 10), K(serverTop, 120, 20, 0, 190), ...rackPts.map(p => K(p, 0, 10, 10, 6))] },
      { pos: scrC.clone().add(scrN.clone().multiplyScalar(9)), tgt: scrC.clone(), fov: 26, keys: [K(monW(-SW / 2, SY - SH / 2)), K(monW(SW / 2, SY + SH / 2)), K(monW(-SW / 2, SY + SH / 2)), K(monW(SW / 2, SY - SH / 2))] },
      { pos: V3(-8, 24, 26), tgt: V3(4, 1, -4), fov: 28, keys: [K(V3(SX + 2.95, RH + 0.2, SZ - 0.75), 20, 60), K(apex, 30), ...towerPts.map(p => K(p)), K(routerPos), ...rackPts.map(p => K(p, 0, 40)), K(V3(SX + 2.95, RH, SZ - 0.75), 0, 40), K(apex, 20), K(mastTop), K(PORT)] },
    ],
    tall: [
      { pos: V3(-9, 4.2, 26), tgt: V3(3.5, 4.2, -4), fov: 34, keys: [K(apex, 6), K(mastTop, 8), K(serverTop, 6), K(PORT, 0, 0, 10)] },
      { pos: V3(-4, 7.5, 15.5), tgt: V3(-4.8, 0.8, 0.6), fov: 46, keys: [...towerPts.map(p => K(p, 0, 0, 6, 6)), K(PORT, 0, 10, 10), K(PHONE_TOP, 70, 10), K(monW(0, SY + 1.15), 0)] },
      { pos: V3(-3, 2, 13), tgt: V3(8, 6, -7), fov: 50, keys: [K(PHONE_TOP, 0, 10, 10), K(mastTop, 70, 20), K(apex, 6), K(serverTop, 70, 10), K(V3(SX + 2.95, 0, SZ + 0.75))] },
      { pos: V3(-10.5, 17, 16), tgt: V3(-6.4, 0.3, -0.6), fov: 46, keys: [...towerPts.map(p => K(p, 0, 0, 6, 6)), K(gatePos.clone().add(V3(0, 1.7, 0)), 70, 20, 0, 20), K(PORT, 0, 10, 10), K(routerPos, 0, 6, 6, 6)] },
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
    let pad = 1;
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

  // ---------- labels ----------
  addCSS();
  const wrap = document.createElement('div');
  wrap.className = 'wd-labels'; wrap.setAttribute('aria-hidden', 'true');
  const LAB = {
    pc: { at: V3(TX + 0.2, TH + 0.25, TZ), beats: [0, 1], html: '<b>Your PC</b><span>CS2 and Steam</span>' },
    phone: { at: PHONE_TOP.clone().add(V3(0, 0.1, 0)), beats: [0, 1, 2, 3], html: '<b>Phone</b><span data-k="ph">mobile data on</span>' },
    cable: { at: cable.getPointAt(0.62), beats: [1], html: '<b>USB cable</b><span>USB tethering, or the phone\'s hotspot</span>' },
    router: { at: routerPos.clone().add(V3(0, 1.45, 0)), beats: [1, 3], html: '<b>Home router</b><span>browser, Discord, downloads</span>' },
    mast: { at: mastTop.clone().add(V3(0, 0.3, 0)), beats: [0, 2, 4], html: '<b>Your carrier</b><span data-k="mast">4G / 5G</span>' },
    server: { at: serverTop.clone().add(V3(0, 0.45, 0)), beats: [0, 2, 4], r: 1, html: '<b>CS2 server</b><span data-k="srv">sees <em>' + IP_OLD + '</em></span><small data-k="srvn">example IP, your carrier\'s</small>' },
    gate: { at: gatePos.clone().add(V3(-0.9, 3.0, -0.6)), beats: [3], hot: 1, html: '<b>Windows Firewall on your PC</b><span>CS2 and your servers: blocked</span>' },
    cut: { at: PORT.clone().add(V3(0, 0.05, 0)), beats: [3], dn: 1, html: '<b>Cable pulled</b><span>CS2 is cut off, not sent home</span>' },
  };
  // the guides build on this world: they may add props, labels and shots, and run their own story each frame
  const __gx = (opts.extend && opts.extend({ scene, camera, renderer, C, M, LAB, SHOTS, K, rbox, cable, air, arc, mCable, mAir, mArc, homeMat, hatch, tryMat, gate, gateFrameMat, Lbad, L2, L3, leds, mastTop, mastBase, serverTop, routerPos, gatePos, PORT, PHONE_TOP, SX, SZ, monW, scrC, scrN, mon })) || {};
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
  for (const y of [0.1, PH / 2, PH]) for (const x of [-PW / 2, PW / 2]) LINE_PTS.push(tw(x, y, 0));
  let lineScr = [];
  function placeLabels(p, box) {
    placed.length = 0;
    const [L, T, R, B] = box;
    const av = opts.avoid ? opts.avoid() : [];
    lineScr = LINE_PTS.map(q => { proj.copy(q).project(camera); return [(proj.x * 0.5 + 0.5) * W, (-proj.y * 0.5 + 0.5) * H]; });
    // nearest shots first, so the beat on screen keeps its labels
    for (const k in LAB) {
      const lb = LAB[k], el = lb.el, s = lb.s;
      let o = 0; for (const b of lb.beats) o = Math.max(o, 1 - Math.max(Math.abs(p - b), Math.abs(st.p - b)) * 2.4);
      if (layout === 'tall' && !lb.tall && (!(k === 'phone' || k === 'server' || k === 'gate' || k === 'mast') || B - T < 180 || p < 0.5)) o = 0;
      o = clamp(o);
      let x = 0, y = 0, r = !!lb.r, dn = !!lb.dn;
      if (o > 0.01) {
        proj.copy(lb.at).project(camera);
        x = (proj.x * 0.5 + 0.5) * W; y = (-proj.y * 0.5 + 0.5) * H;
        const box2 = el.lastElementChild;
        if (s.gen !== labGen) { s.w = box2.offsetWidth || 170; s.h = box2.offsetHeight || 50; s.gen = labGen; }
        const w = s.w, h = s.h, stem = layout === 'tall' ? 30 : 44;
        const rect = (rr, dd) => { const y0 = dd ? y + 32 : y - stem - 12 - h, x0 = rr ? x + 10 - w : x - 10; return [x0, y0, x0 + w, y0 + h]; };
        const ok = q => q[0] >= L - 4 && q[2] <= Math.min(W - 8, R + 8) && q[1] >= T - 4 && q[3] <= B + 6 && !placed.some(o2 => q[0] < o2[2] + 10 && o2[0] < q[2] + 10 && q[1] < o2[3] + 8 && o2[1] < q[3] + 8) && !lineScr.some(([lx, ly]) => lx > q[0] - 4 && lx < q[2] + 4 && ly > q[1] - 4 && ly < q[3] + 4) && !av.some(a => q[0] < a[2] && a[0] < q[2] && q[1] < a[3] && a[1] < q[3]);
        const tries = [[r, dn], [!r, dn], [r, !dn], [!r, !dn]];
        const loose = q => q[0] >= L - 4 && q[2] <= Math.min(W - 8, R + 8) && q[1] >= T - 4 && q[3] <= B + 6 && !placed.some(o2 => q[0] < o2[2] + 10 && o2[0] < q[2] + 10 && q[1] < o2[3] + 8 && o2[1] < q[3] + 8) && !av.some(a => q[0] < a[2] && a[0] < q[2] && q[1] < a[3] && a[1] < q[3]);
        const inBox = proj.z < 1 && x > L - 6 && x < R + 6 && y > T && y < B + 4;
        const t = inBox ? (tries.find(([a, b]) => ok(rect(a, b))) || tries.find(([a, b]) => loose(rect(a, b)))) : null;
        if (t) { [r, dn] = t; placed.push(rect(r, dn)); } else o = 0;
      }
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

  // ---------- post: proposal A's bloom, on an MSAA half float buffer ----------
  const rt = new WebGLRenderTarget(4, 4, { type: HalfFloatType, samples: 4 });
  const composer = new EffectComposer(renderer, rt);
  composer.addPass(new RenderPass(scene, camera));
  const bloom = new UnrealBloomPass(new Vector2(256, 256), 0.95, 0.55, 0.12);
  // the bloom is taken from a second render of the scene in which the screens are black; then added on top of the real one
  const bloomIn = new WebGLRenderTarget(4, 4, { type: HalfFloatType, samples: 0 });
  const black = new MeshBasicMaterial({ color: 0 });
  const addBloom = new ShaderPass(new ShaderMaterial({
    uniforms: { tDiffuse: { value: null }, tBloom: { value: null } },
    vertexShader: `varying vec2 vUv; void main(){ vUv = uv; gl_Position = projectionMatrix*modelViewMatrix*vec4(position,1.); }`,
    fragmentShader: `uniform sampler2D tDiffuse,tBloom; varying vec2 vUv; void main(){ gl_FragColor = texture2D(tDiffuse,vUv) + vec4(texture2D(tBloom,vUv).rgb,0.); }`,
  }));
  composer.addPass(addBloom);
  const outPass = new OutputPass();
  composer.addPass(outPass);
  function renderAll() {
    const sm = screen.material, pm = phScreen.material;
    screen.material = black; phScreen.material = black;
    renderer.setRenderTarget(bloomIn); renderer.clear(); renderer.render(scene, camera);
    screen.material = sm; phScreen.material = pm;
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
    const pr = renderer.getPixelRatio(); bloomIn.setSize(Math.round(W * pr / 2), Math.round(H * pr / 2)); bloom.setSize(Math.round(W * pr), Math.round(H * pr));
    for (let i = 0; i < NS; i++) fitShot(i);
    labGen++;
    renderer.shadowMap.needsUpdate = true;
    dirty = true; request();
  }

  // ---------- state ----------
  const st = { p: 0, sub: 0, cta: 0 };
  let pNow = 0, time = 0, reveal = still || opts.lit ? 1 : 0, dirty = true;
  const mouse = { x: 0, y: 0, sx: 0, sy: 0 };
  const camPos = V3(0, 0, 0), camTgt = V3(0, 0, 0), tmp = V3(0, 0, 0);
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
    const e = calm ? Math.round(f) : smooth(0.12, 0.88, f);
    const a = S[k], b = S[k + 1], fa = fits[k], fb = fits[k + 1];
    camPos.lerpVectors(a.pos, b.pos, e); camTgt.lerpVectors(a.tgt, b.tgt, e);
    if (!calm && k !== 4 && k !== 5) camPos.y += Math.sin(e * Math.PI) * 2.2; // a crane, not a slide
    if (!calm) {
      const kk = 1 - Math.exp(-dt * 3);
      mouse.sx += (mouse.x - mouse.sx) * kk; mouse.sy += (mouse.y - mouse.sy) * kk;
      const near = 1 - smooth(4.4, 5, p) * (1 - smooth(5.2, 5.8, p)); // still in front of the screen
      camPos.x += mouse.sx * 1.2 * near;
      camPos.y += -mouse.sy * 0.7 * near;
    }
    const zoom = lerp(fa.zoom, fb.zoom, e), dx = k === 4 ? lerp(fa.dx, fb.dx, smooth(0, 0.35, f)) : lerp(fa.dx, fb.dx, e), dy = lerp(fa.dy, fb.dy, e);
    camera.position.copy(camPos); camera.lookAt(camTgt); camera.fov = lerp(a.fov, b.fov, e); camera.zoom = zoom;
    camera.aspect = W / H; camera.setViewOffset(W, H, -dx, -dy, W, H); camera.updateProjectionMatrix(); camera.updateMatrixWorld();
    const box = [0, 1, 2, 3].map(j => lerp(fa.box[j], fb.box[j], e));

    const s = stateAt(p, st.sub);
    const live = 1 - s.cut;
    const radio = live * (1 - s.dataOff);
    mCable.uniforms.uCut.value = s.cut > 0.02 ? 0.045 * s.cut : 0;
    mAir.uniforms.uOn.value = radio;
    mArc.uniforms.uOn.value = radio;
    mArc.uniforms.uDraw.value = s.redraw;
    mArc.uniforms.uGain.value = mAir.uniforms.uGain.value = 1.25 + 0.9 * s.fresh;
    for (const m of [mCable, mAir, mArc]) { m.uniforms.uTime.value = time; m.uniforms.uReveal.value = reveal * 1.02; }
    homeMat.uniforms.uTime.value = time;
    homeMat.uniforms.uLive.value = smooth(0.4, 0.85, p) * (1 - smooth(1.4, 1.8, p)) + smooth(2.5, 2.9, p) * (1 - smooth(3.2, 3.6, p));
    // the firewall: comes up with the cut, CS2's run home stops at it
    const hot = s.cut;
    hatch.uniforms.uHot.value = hot; tryMat.uniforms.uHot.value = hot; tryMat.uniforms.uTime.value = time;
    gateFrameMat.emissiveIntensity = 0.9 * hot; Lbad.intensity = 5 * hot;
    gate.scale.setScalar(Math.max(0.001, 0.62 * smooth(0, 0.5, hot)));
    gate.visible = hot > 0.002;
    // the plug comes out of the phone
    plugPhone.position.copy(plugHome).addScaledVector(pdir, -0.42 * smooth(0, 1, s.cut));
    L2.intensity = 26 * radio; L3.intensity = 22 * live; L1.intensity = 14 * (0.6 + 0.4 * live);
    beacon.material.color.copy(C.bad).multiplyScalar(!still && s.banned > 0.5 ? 0.25 + 0.75 * smooth(0.6, 1, 0.5 + 0.5 * Math.sin(time * 2.1)) : 0.55);
    leds.uniforms.uTime.value = time; leds.uniforms.uAct.value = radio * s.redraw; leds.uniforms.uBan.value = s.banned;
    if (s.banned > 0.01) { Lsrv.color.copy(C.bad); Lsrv.intensity = 7 * s.banned; } else { Lsrv.color.copy(C.good); Lsrv.intensity = 5 * s.back * (1 - smooth(0.9, 1, st.sub)); }
    if (stitchRef) stitchRef.visible = p < 4.5 || p > 5.6;
    phone.visible = !(layout === 'wide' && p > 1.6 && p < 2.4);

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
      if (s.cut > 0.02 && u > 0.27) al = 0;
      if (u > reveal) al = 0;
      pA[i] = al * smooth(0, 0.03, u) * (1 - smooth(0.97, 1, u));
    }
    pGeo.attributes.position.needsUpdate = true; pGeo.attributes.aU.needsUpdate = true; pGeo.attributes.aA.needsUpdate = true;

    if (__gx.tick) __gx.tick(dt, time, s, p);
    placeLabels(p, box);
    return Math.abs(st.p - pNow) > 0.0005 || (!still && reveal < 1);
  }

  // ---------- loop ----------
  let raf = 0, last = 0, paused = false, ready = false, first = false, destroyed = false, hidden = false;
  function request() { if (raf || destroyed || !ready || hidden) return; if (paused && !dirty) return; raf = requestAnimationFrame(frame); }
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
  const img = new Image(); img.decoding = 'async'; img.src = scrURL;
  const imgReady = (img.decode ? img.decode() : new Promise(r => { img.onload = r; })).then(() => { scrImg = img; }).catch(() => {});

  // ---------- start: build, environment, compile every program without blocking, then draw ----------
  resize();
  const fontsReady = (document.fonts ? Promise.all(['600 30px "Funnel Sans"', '400 28px "Funnel Sans"', '600 19px "Fragment Mono"'].map(f => document.fonts.load(f))) : Promise.resolve()).catch(() => {});
  (async () => {
    await yieldTask(); if (destroyed) return;
    // reflections from a small studio environment; on GPUs where its programs link slowly this is the longest step, so it runs only when asked
    if (opts.env) { const pmrem = new PMREMGenerator(renderer); scene.environment = pmrem.fromEquirectangular(envTex).texture; pmrem.dispose(); }
    envTex.dispose();
    await yieldTask(); if (destroyed) return;
    update(0);
    await comp(scene, camera, scene, composer.readBuffer);
    if (destroyed) return;
    {
      const tiny = new WebGLRenderTarget(2, 2, { type: HalfFloatType, samples: 4 });
      const kids = scene.children.filter(o => !o.isLight);
      const vis = kids.map(o => o.visible);
      kids.forEach(o => (o.visible = false));
      for (const o of kids) { o.visible = true; renderer.setRenderTarget(tiny); try { await renderer.compileAsync(o, camera, scene); } catch (e) { /* drawn below anyway */ } renderer.render(scene, camera); o.visible = false; await yieldTask(); if (destroyed) return; }
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
    quad.dispose();
    await Promise.all([fontsReady, imgReady]);
    if (destroyed) return;
    phState = ''; drawPhone(true, true); drawScreen(ipShown);
    renderer.initTexture(scrTex); renderer.initTexture(phTex);
    renderer.shadowMap.needsUpdate = true;
    await yieldTask(); if (destroyed) return;
    ready = true; dirty = true; request();
  })();

  if (opts.debug) window.__wd = { scene, mArc, mAir, screenLight, key, rim, hemi, Lrack, L1, L2, L3, M, request: () => { dirty = true; request(); } };
  return {
    set(o) {
      let ch = false;
      for (const k of ['p', 'sub', 'cta']) if (o[k] != null && o[k] !== st[k]) { st[k] = o[k]; ch = true; }
      if (o.jump && Math.abs(st.p - pNow) > 0.5) pNow = st.p - Math.sign(st.p - pNow) * 0.5;
      if (ch) { dirty = true; request(); }
    },
    pause() { paused = true; stop(); dirty = true; request(); },  // one more still frame, then nothing until the scroll changes it
    resume() { if (!paused) return; paused = false; request(); },
    hide(v) { hidden = !!v; if (hidden) stop(); else { dirty = true; request(); } },
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
      composer.dispose(); bloom.dispose(); bloomIn.dispose(); renderer.dispose();
    },
  };
}

window.WorldScene = { mount };
