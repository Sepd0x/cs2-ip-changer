/* CS2 IP Changer home: the story. One world (scenes.min.js, WorldScene) on a fixed stage behind the page; each beat of the
   story is a block of text, and where the reader is picks the camera shot and the state of the route. The rest of the page
   slides over the stage on plain ground, and the close shows the world once more.
   Shared motion (smooth scroll, header line, reveals, guides index, footer) is readout.js. Reduced motion: the same shots
   as cuts, the scene draws only when something changes. Without WebGL: the How it works drawing, and the text as it is. */
(function () {
  'use strict';
  var d = document, root = d.documentElement, W = window;
  var FX = root.classList.contains('fx');
  var $ = function (s, el) { return (el || d).querySelector(s); };
  var $$ = function (s, el) { return [].slice.call((el || d).querySelectorAll(s)); };
  var clamp = function (v, a, b) { return Math.min(b === undefined ? 1 : b, Math.max(a || 0, v)); };
  var topH = function () { var h = $('.top'); return h ? h.offsetHeight : 64; };
  var docTop = function (el) { var y = 0; while (el) { y += el.offsetTop; el = el.offsetParent; } return y; };

  var stage = $('#stage'), cv = $('#world'), rail = $('#rail'), pauseBtn = $('#pause');
  if (!stage || !cv) return;
  var beats = $$('[data-beat]').sort(function (a, b) { return +a.getAttribute('data-beat') - +b.getAttribute('data-beat'); });
  var NB = 6; // beats 0..5 are the story; 6 is the close
  var copies = beats.map(function (b) { return $('.copy', b); });
  // the plain text the pause and the chip must not sit over either: the ban's note after its card, the solid sections
  var plain = $$('.ba-copy, section.solid');
  var appBody = $('.app-body'), cta = $('.cta'), railItems = rail ? $$('li', rail) : [], railOl = rail ? $('ol', rail) : null;
  var whereEl = $('#where'), whereBtn = whereEl ? $('button', whereEl) : null, whereList = whereEl ? $('.where-list', whereEl) : null, whereLinks = whereEl ? $$('ol a', whereEl) : [];
  var logLis = $$('#ban-log li'), banIP = $('#ban-ip'), banNote = $('#ban-ip-note'), banSaid = banIP ? banIP.parentNode : null;
  // where each log line starts, as a share of the ban beat; the scene uses the same marks
  var LINES = [0, .12, .26, .4, .62, .74, .86];

  var tall = function () { return innerWidth < 640 || (innerWidth / innerHeight < .9 && innerWidth < 1000); };

  /* ------------------------------------------------------------ where the story is, from the scroll */
  // each beat holds the camera while its text is read: an anchor (or, for the ban, a stretch) in scroll px
  var A = [], ctaAt = 0, ctaH = 0, storyEnd = 0, H = innerHeight;
  function measure() {
    H = innerHeight;
    var T = tall();
    A = beats.slice(0, NB).map(function (b, i) {
      var c = copies[i], bt = docTop(b), bh = b.offsetHeight, ch = c ? c.offsetHeight : 0;
      if (i === 0) return { s: 0, e: 0 };
      if (b.classList.contains('beat-long')) {
        c.style.setProperty('--ch', ch + 'px');
        // upright, the card rises only after the gap above it (the wrap's padding), once the card before has faded
        if (T) return { s: Math.max(0, bt + (parseFloat(getComputedStyle(c.parentNode).paddingTop) || 0) + ch + 24 - H,docTop(copies[i - 1]) + copies[i - 1].offsetHeight - topH()), e: bt + bh - H };
        var stuck = topH() + Math.max(16, (H - topH() - ch) / 2);
        return { s: bt + 32 - stuck, e: bt + bh - ch - stuck - 32 };
      }
      var ct = docTop(c);
      var a = T ? ct + ch - H + 12 : ct + ch / 2 - H / 2;
      return { s: a, e: a };
    });
    // a beat never anchors before the one above it
    for (var i = 1; i < A.length; i++) { A[i].s = Math.max(A[i].s, A[i - 1].e + 1); A[i].e = Math.max(A[i].e, A[i].s); }
    // the story ends when the page's own app window docks where the 3D screen is
    // measured where the window sits in the page, not where it is stuck: a reload or a turn of the phone with the window
    // stuck put the end of the story at the page being read (6 Nov.), and the 3D came back over the window's pages
    var wr = winRect(), fp = fig ? fig.style.position : '';
    if (fig) fig.style.position = 'static';
    var figTop = fig ? docTop(fig) : 0;
    if (fig) fig.style.position = fp;
    // whole px: the snap rests the window on a whole px, and half a px short left the 3D on and the window hidden (6 Nov.)
    storyEnd = Math.floor(wr ? figTop - wr.fig : (appBody ? docTop(appBody) : 1e9));
    ctaAt = cta ? docTop(cta) : 1e9; ctaH = cta ? cta.offsetHeight : 0;
  }
  function where(y) {
    if (cta && y > ctaAt - H) return { p: NB, sub: 1, cta: 1 };
    for (var i = 0; i < A.length; i++) {
      if (y <= A[i].e) {
        if (y >= A[i].s) return { p: i, sub: A[i].e > A[i].s ? (y - A[i].s) / (A[i].e - A[i].s) : 0 };
        if (i === 0) return { p: 0, sub: 0 };
        var g = A[i].s - A[i - 1].e;
        return { p: i - 1 + clamp((y - A[i - 1].e) / g), sub: i - 1 === 4 ? 1 : 0 };
      }
    }
    return { p: NB - 1, sub: 1 };
  }

  /* ------------------------------------------------------------ the scene */
  var api = null, still = !FX, held = false;
  try { held = localStorage.getItem('cs2-motion') === 'paused'; } catch (x) {}
  // the free part of the screen for a shot: right of the copy column on a PC, above the beat's card held upright
  // where the app window sits once it is stuck: the camera flies into the 3D screen until it covers that same rectangle
  var fig = $('#win-fig'), win = fig ? $('.win', fig) : null;
  function winRect() {
    if (!fig || !win) return null;
    var th = topH(), r = fig.getBoundingClientRect(), wr = win.getBoundingClientRect();
    var top = getComputedStyle(fig).position === 'sticky' ? parseFloat(getComputedStyle(fig).top) || th : th;
    var pad = wr.top - r.top;
    return { fig: top, l: wr.left, t: top + pad, w: wr.width, h: wr.height };
  }
  function frame(Wd, Hh, shot, layout) {
    var th = topH();
    if (shot === 5) { var q = winRect(); if (q && q.w > 50) return { box: [q.l, q.t, q.l + q.w, q.t + q.h], bias: [.5, .5] }; }
    if (layout === 'tall') {
      var i = Math.min(shot, NB - 1), c = shot === NB ? $('.copy', cta) : copies[i];
      var ch = c ? c.offsetHeight : Hh * .45;
      if (shot === 4) ch = Math.min(ch, Hh * .62);
      var bot = Hh - ch - (shot === 0 ? 56 : 24);
      if (bot - th < 150) bot = Math.min(Hh * .5, th + 260);
      return { box: [10, th + 60, Wd - 10, bot], bias: [.5, .55] };
    }
    var col = shot === NB ? $('.copy', cta) : copies[Math.min(shot, NB - 1)];
    var r = col ? col.getBoundingClientRect() : null;
    var right = r && r.width ? r.left + r.width : Wd * .4;
    var land = innerHeight < 541;
    return { box: [right + Math.max(24, Wd * .025), th + (land ? 8 : 18), Wd - Math.max(16, Wd * .02), Hh - (land ? 12 : 70)], bias: [.5, .5] };
  }
  // what the scene's labels must not sit under: the beat cards on screen and the pause button (css px of the canvas)
  var pauseRect = null, whereRect = null; addEventListener('resize', function () { pauseRect = whereRect = null; });
  function avoid() {
    var out = [], y = scrollY;
    if (tall()) for (var i = 0; i < copies.length; i++) { var c = copies[i]; if (!c) continue; var r = c.getBoundingClientRect(); if (r.bottom > 0 && r.top < H) out.push([r.left, r.top, r.right, r.bottom]); }
    if (pauseBtn && !pauseBtn.hidden && !pauseBtn.classList.contains('under')) { if (!pauseRect) { var q = pauseBtn.getBoundingClientRect(); pauseRect = [q.left - 6, q.top - 6, q.right + 6, q.bottom + 6]; } out.push(pauseRect); }
    if (whereBtn && !whereEl.classList.contains('under')) { if (!whereRect) { var q2 = whereBtn.getBoundingClientRect(); whereRect = q2.width ? [q2.left - 8, q2.top - 8, q2.right + 8, q2.bottom + 14] : []; } if (whereRect.length) out.push(whereRect); }
    return out;
  }
  function veil() {
    if (tall()) return;
    var c = copies[0], r = c.getBoundingClientRect();
    var b = (r.left + r.width) / innerWidth * 100;
    stage.style.setProperty('--veil-a', Math.max(10, b - 14).toFixed(1) + '%');
    stage.style.setProperty('--veil-b', (b + 6).toFixed(1) + '%');
  }

  function start() {
    if (api || !W.WorldScene) return;
    try { api = W.WorldScene.mount(cv, { still: still, frame: frame, avoid: avoid, lit: true }); } catch (x) { api = null; }
    if (!api) { root.classList.add('no-gl'); return; }
    W.__world = api;
    cv.addEventListener('worldready', function () { stage.classList.add('ready'); root.classList.add('gl'); update(); }, { once: true });
    if (held) api.pause();
    update();
  }

  var railGo = null;
  if (rail) rail.addEventListener('click', function (e) { var a = e.target.closest && e.target.closest('a'); if (!a) return; var i = railItems.indexOf(a.parentNode); if (i >= 0) { railGo = { p: i, until: performance.now() + 1300, first: true }; mark(i); requestAnimationFrame(update); } });

  /* ------------------------------------------------------------ every scroll: the scene, the rail, the log */
  var lastLine = -2, uq = 0, onStage = true;
  function snap(s) { for (var k = LINES.length - 1; k >= 0; k--) if (s >= LINES[k]) return LINES[k] + .001; return 0; }
  function update() {
    uq = 0;
    var y = scrollY, w = where(y), p = w.p, sub = w.sub;
    seen = { inStory: y < storyEnd, p: w.p, sub: w.sub, pg: pageNow(y) };
    dwell(y);
    // past the story (the close, the footer) the chip has no step to show, so it goes (it said "6/6 The app" at the FAQ's end)
    if (whereEl) { var away = !stepAt(y); if (whereEl.classList.contains('away') !== away) { whereEl.classList.toggle('away', away); if (away) whereShut(); } }
    if (!FX || held) { p = Math.round(p); if (p === 4) sub = snap(sub); else if (p > 4) sub = 1; }
    if (p < 4) sub = 0;
    // the stage is seen while the story is on screen and again at the close; under the plain sections it rests
    var vis = y < storyEnd || (cta && y > ctaAt - H - 10);
    // the page's window takes over from the 3D screen the moment it docks on the same rectangle
    if (fig && api) fig.classList.toggle('docked', y >= storyEnd - 1);
    if (vis !== onStage) { onStage = vis; stage.classList.toggle('off', !vis); if (api) api.hide(!vis); }
    root.classList.toggle('story-off', !vis || (y > storyEnd - H * .6 && y < ctaAt - H) || (cta && y > ctaAt + ctaH - H * .6));
    var jump = 0;
    if (railGo) { if (performance.now() < railGo.until) { p = railGo.p; sub = 0; jump = railGo.first ? 1 : 0; railGo.first = false; } else railGo = null; }
    if (api) api.set({ p: p, sub: sub, cta: w.cta || 0, jump: jump });
    // flying into the monitor: the screen passes under the copy column, so the veil is denser for that stretch
    stage.style.setProperty('--veil-x', (p > 4.4 && p < 5 ? 16 * Math.sin((p - 4.4) / .6 * Math.PI) : 0).toFixed(1) + '%');
    // held upright, the ground under the cover's text stays plain; past the cover each beat has its own card
    if (tall()) { var cr = copies[0].getBoundingClientRect(), on0 = cr.bottom > 0; stage.style.setProperty('--vt', (on0 ? Math.max(0, cr.top / innerHeight * 100) : 100).toFixed(1) + '%'); stage.style.setProperty('--vb', (on0 ? Math.max(0, cr.bottom / innerHeight * 100) : 100).toFixed(1) + '%'); }
    // held upright, a beat's card fades as it rises into the upper part of the screen, so the next card never stacks
    // under a readable one and the route above is clear (6 Nov., the owner saw cards pile up between beats on a phone)
    var T = tall(), th = topH(), av = H - th;
    for (var k = 1; k < NB; k++) { var cc = copies[k]; if (!cc) continue; var o = T ? clamp((cc.getBoundingClientRect().bottom - th - av * .3) / (av * .32)) : 1; if (cc.__o !== o) { cc.__o = o; cc.style.opacity = o < 1 ? o.toFixed(3) : ''; } }
    // the pause button sits on the free part of the scene; while a card passes under it, it steps aside
    // (the "where you are" chip sits at the same height, top left, and steps aside with it)
    if ((pauseBtn || whereEl) && T) { var pt = th + 10, lo = pt - 24, hi = pt + 44 + 64, under = copies.concat([$('.copy', cta)]).some(function (c) { if (!c || c.__o < .2) return false; var r = c.getBoundingClientRect(); return r.top < hi && r.bottom > lo; }) || plain.some(function (c) { var r = c.getBoundingClientRect(); return r.top < hi && r.bottom > lo; }); if (pauseBtn) pauseBtn.classList.toggle('under', under); if (whereEl && whereEl.classList.contains('under') !== under) { whereEl.classList.toggle('under', under); if (under) whereShut(); } }
    if (whereEl && root.classList.contains('story-off') !== whereOff) { whereOff = !whereOff; if (whereOff) whereShut(); }
    // the rail: how far along the route (which beat is marked once the scroll rests, settle below)
    if (railOl) railOl.style.setProperty('--p', clamp(p / (NB - 1)).toFixed(4));
    // the ban: the log line on screen, and the IP the server sees
    var line = -1;
    if (p > 3.5 && p < 4.5) { for (var k = 0; k < LINES.length; k++) if (sub >= LINES[k]) line = k; }
    else if (p >= 4.5) line = LINES.length;
    if (line !== lastLine) {
      lastLine = line;
      logLis.forEach(function (li, k) { li.classList.toggle('now', k === line); li.classList.toggle('past', k < line); });
      if (banIP) {
        var st = line < 3 ? 'banned' : line < 4 ? 'wait' : 'new';
        if (banSaid.getAttribute('data-st') !== st) {
          banSaid.setAttribute('data-st', st);
          banSaid.classList.toggle('banned', st === 'banned'); banSaid.classList.toggle('wait', st === 'wait');
          var ip = st === 'banned' ? '198.51.100.23' : st === 'wait' ? '•••.•.•.•••' : '192.0.2.140';
          banNote.textContent = st === 'banned' ? 'IP servers see. Example, banned on this server' : st === 'wait' ? 'Mobile data off, then on: getting a new IP' : 'New IP, new /16 range. Example IP';
          var R = W.CS2Readout && W.CS2Readout.Readout;
          if (R && FX) { if (!banIP.__r) banIP.__r = new R(banIP); banIP.__r.set(ip, { blank: st === 'wait' }); } else banIP.textContent = ip;
        }
      }
    }
  }
  W.__story = function () { return { A: A, ctaAt: ctaAt, storyEnd: storyEnd }; };
  function onScroll() { if (!uq) { uq = 1; requestAnimationFrame(update); } clearTimeout(settleT); settleT = setTimeout(settle, 160); }
  addEventListener('scroll', onScroll, { passive: true });

  /* ------------------------------------------------------------ once the scroll rests: the step (6 Nov., from the study of
     the best story sites). The rail, the chip and aria-current mark it then, not on every frame on the way past; and the
     address takes it (#carrier, #ban, #servers...), so a reload or a shared link opens on that step */
  var STEPS = ['', '#how', '#carrier', '#kill-switch', '#ban', '#features'], PGS = $$('#pages > li').map(function (li) { return li.getAttribute('data-pg'); });
  var ours = function (h) { return STEPS.indexOf(h) > 0 || (h.length > 1 && PGS.indexOf(h.slice(1)) > 0); };
  var settleT = 0, stepOn = -1, whereOff = false;
  function stepAt(y) {
    var pg = pageNow(y); if (pg) return { i: NB - 1, h: pg === 'connection' ? '#features' : '#' + pg };
    if (y < storyEnd) { var i = Math.min(NB - 1, Math.max(0, Math.round(where(y).p))); return { i: i, h: STEPS[i] }; }
    return null;
  }
  function mark(i) {
    if (i === stepOn) return; stepOn = i;
    var set = function (a, on) { if (on) a.setAttribute('aria-current', 'step'); else a.removeAttribute('aria-current'); };
    railItems.forEach(function (li, k) { li.classList.toggle('on', k === i); set(li.firstElementChild, k === i); });
    whereLinks.forEach(function (a, k) { set(a, k === i); });
    if (whereBtn && whereLinks[i]) { $('b', whereBtn).textContent = i + 1; $('.where-at', whereBtn).textContent = whereLinks[i].textContent; whereRect = null; }
  }
  function settle() {
    clearTimeout(settleT); settleT = 0;
    // a step picked on the rail or the chip is marked already; the scroll there is still going
    if (railGo && performance.now() < railGo.until) { settleT = setTimeout(settle, railGo.until - performance.now() + 30); return; }
    take(stepAt(scrollY));
  }
  function take(s) {
    if (s) mark(s.i);
    var h = s ? s.h : '';
    if ((s || ours(location.hash)) && h !== location.hash) try { history.replaceState(history.state, '', location.pathname + location.search + h); } catch (x) {}
  }
  // a slow, steady wheel never rests: a step that stays the same for 400 ms is taken too, so the rail keeps up with a reader
  // and only a quick pass over a step leaves it out (6 Nov.: at 100 px every 450 ms the rail stayed on PC for a minute)
  var cand = -2, candT = 0;
  function dwell(y) {
    if (railGo) return;
    var s = stepAt(y), i = s ? s.i : -1, now = performance.now();
    if (i !== cand) { cand = i; candT = now; } else if (now - candT > 400 && (i !== stepOn || (s ? s.h : '') !== location.hash) && (s || ours(location.hash))) take(s);
  }
  // the smooth scroll moves the page a little every frame, and without it each notch of a wheel is a scroll of its own:
  // each can end in a scrollend, so one counts only when the wheel has stopped too (else the timer above settles)
  var lastWheel = 0; addEventListener('wheel', function () { lastWheel = performance.now(); }, { passive: true });
  addEventListener('scrollend', function () { if (!(W.__lenis && W.__lenis.isScrolling) && performance.now() - lastWheel > 150) settle(); });
  // a link to a step opens on it (a reload keeps its own place)
  function deep() {
    var h = location.hash, nav = performance.getEntriesByType ? performance.getEntriesByType('navigation')[0] : null;
    if (!ours(h) || (nav && nav.type !== 'navigate')) return;
    var i = STEPS.indexOf(h);
    if (i > 0 && A[i]) scrollTo(0, Math.round(A[i].s)); else if (i < 0) pageGo(h.slice(1));
    update(); settle();
  }

  /* ------------------------------------------------------------ the "where you are" chip: under 1000 px, where the rail is hidden */
  var whereShut = function () {};
  if (whereEl) {
    whereEl.hidden = false;
    var whereOpen = function (o) { whereBtn.setAttribute('aria-expanded', o); whereList.hidden = !o; };
    whereShut = function () { if (!whereList.hidden) whereOpen(false); };
    whereBtn.addEventListener('click', function () { whereOpen(whereList.hidden); });
    whereList.addEventListener('click', function (e) {
      var a = e.target.closest && e.target.closest('a'); if (!a) return;
      var i = whereLinks.indexOf(a);
      if (i >= 0) { railGo = { p: i, until: performance.now() + 1300, first: true }; mark(i); requestAnimationFrame(update); }
      whereOpen(false);
    });
    d.addEventListener('click', function (e) { if (!whereEl.contains(e.target)) whereShut(); });
    d.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !whereList.hidden) { whereOpen(false); whereBtn.focus(); } });
  }
  var rq = 0;
  var lastW = innerWidth;
  var seen = null, pageNow = function () { return null; }, pageGo = function () {};
  // a new width (the phone turned, a window resized): back to the beat, or the window's page, that was on screen. Where that
  // was is kept from the last scroll, before the browser moved the page for the new layout (6 Nov.: turned on a page of the
  // window, a phone landed in the ban, and stayed there when turned back)
  var was = null;
  addEventListener('resize', function () { var coarse = matchMedia('(pointer:coarse)').matches; if (coarse && innerWidth === lastW) { measure(); update(); return; } lastW = innerWidth; if (!was) was = seen; clearTimeout(rq); rq = setTimeout(function () { var w0 = was || { inStory: scrollY < storyEnd, p: where(scrollY).p, sub: where(scrollY).sub, pg: pageNow(scrollY) }; was = null; measure(); veil(); if (api) api.resize(); if (w0.inStory && A.length) { var i = Math.min(Math.round(w0.p), A.length - 1), a = A[i]; scrollTo(0, Math.round(a.s + (a.e - a.s) * (i === 4 ? w0.sub : 0))); } else if (w0.pg) pageGo(w0.pg); update(); }, 120); });
  d.addEventListener('visibilitychange', function () { if (api) api.hide(d.hidden || !onStage); });
  measure(); veil(); update();
  if (d.fonts && d.fonts.ready) d.fonts.ready.then(function () { measure(); veil(); if (api) api.resize(); update(); });
  addEventListener('load', function () { measure(); update(); deep(); settle(); });

  /* ------------------------------------------------------------ keyboard in the story: one beat per PageDown or Space, one log
     line in the ban (6 Nov.: like a finger on a phone, a key never lands between two beats with the camera half way) */
  var keyGo = function (to) { if (W.__lenis) W.__lenis.scrollTo(to, { duration: .7 }); else scrollTo({ top: to, behavior: FX ? 'smooth' : 'auto' }); };
  var keyDir = function (e) {
    if (e.defaultPrevented || e.altKey || e.ctrlKey || e.metaKey) return 0;
    var t = e.target; if (t && (t.isContentEditable || /^(INPUT|TEXTAREA|SELECT|BUTTON)$/.test(t.tagName))) return 0;
    return e.key === 'PageDown' || (e.key === ' ' && !e.shiftKey) ? 1 : e.key === 'PageUp' || (e.key === ' ' && e.shiftKey) ? -1 : 0;
  };
  d.addEventListener('keydown', function (e) {
    var dir = keyDir(e); if (!dir || !A[4]) return;
    // where the story rests: each beat's place, and in the ban each log line; past the last beat the key does what it always does
    var y = scrollY, a = A[4], span = a.e - a.s, stops = [], i, to = null;
    for (i = 0; i < NB; i++) if (i !== 4 || span <= 0) stops.push(Math.round(A[i].s));
    if (span > 0) LINES.forEach(function (l) { stops.push(Math.round(a.s + span * (l + .02))); });
    stops.sort(function (x, z) { return x - z; });
    if (y > stops[stops.length - 1] + 2) return;
    if (dir > 0) { for (i = 0; i < stops.length; i++) if (stops[i] > y + 4) { to = stops[i]; break; } }
    else { for (i = stops.length - 1; i >= 0; i--) if (stops[i] < y - 4) { to = stops[i]; break; } }
    if (to === null) return;
    e.preventDefault(); keyGo(to);
  });

  /* ------------------------------------------------------------ Pause motion: remembered, and it stops the scene's own loops */
  if (pauseBtn && FX) {
    pauseBtn.hidden = false; pauseBtn.setAttribute('aria-pressed', held);
    pauseBtn.addEventListener('click', function () {
      held = !held; pauseBtn.setAttribute('aria-pressed', held);
      try { localStorage.setItem('cs2-motion', held ? 'paused' : ''); } catch (x) {}
      if (api) { if (held) api.pause(); else api.resume(); }
      update();
    });
  }

  /* ------------------------------------------------------------ the app's six pages: the window shows the page being read */
  if (fig) {
    var pics = $$('.views picture', fig), zl = $('.views', fig), pages = $$('#pages > li'), cur = 'connection';
    // the page being read while the window is docked, and the way back to it (its title where the snap rests it)
    pageNow = function (y) {
      if (car) { var pe = $('#pages'), b = docTop(pe) + pe.offsetHeight; return y >= storyEnd - 1 && y <= b - innerHeight * .35 ? cur : null; }
      return rests.length && y >= storyEnd - 1 && y <= rests[rests.length - 1] + innerHeight * .6 ? cur : null;
    };
    /* held upright on a phone (6 Nov.) the six pages sit side by side (CSS): a swipe sideways is a page, the numbers above
       go straight to one, and each of a page's three things gets a cut of the real window at full size, ring and all */
    var CAR = matchMedia('(max-width:759px) and (orientation:portrait)'), car = CAR.matches, pagesBox = $('#pages'), nav = null, tabs = [];
    if (pagesBox) {
      nav = d.createElement('div'); nav.className = 'pg-nav'; nav.hidden = !car; nav.setAttribute('aria-label', 'The six pages');
      pages.forEach(function (li, i) {
        var pg = li.getAttribute('data-pg'), bt = d.createElement('button'), name = ($('h3', li).textContent || '').replace(/^\s*\d+\s*/, '');
        bt.type = 'button'; bt.textContent = ('0' + (i + 1)); bt.setAttribute('aria-label', name); bt.setAttribute('aria-controls', 'pages');
        bt.addEventListener('click', function () { slide(i, true); });
        nav.appendChild(bt); tabs.push(bt);
        // the cuts: the page's own picture, placed so its mark sits in the middle of the strip (the window's edges stop it)
        var pic = pics.filter(function (pc) { return pc.getAttribute('data-pg') === pg; })[0], keys = $$('.mk-key > li', li);
        $$('.marks .mk[data-pg="' + pg + '"]', fig).forEach(function (mk, k) {
          if (!pic || !keys[k]) return;
          var v = function (n) { return parseFloat(mk.style.getPropertyValue('--' + n)) || 0; };
          var cut = d.createElement('div'), inn = d.createElement('div'), ring = d.createElement('i'), cp = pic.cloneNode(true);
          cut.className = 'cut'; cut.setAttribute('aria-hidden', 'true'); inn.className = 'cut-in'; ring.className = 'cring';
          cp.removeAttribute('class'); cp.removeAttribute('aria-hidden'); var im = $('img', cp); im.alt = ''; im.loading = 'lazy'; im.decoding = 'async';
          cut.style.setProperty('--cx', ((v('x') + v('w') / 2) * 12.4).toFixed(1) + 'px'); cut.style.setProperty('--cy', ((v('y') + v('h') / 2) * 8).toFixed(1) + 'px');
          cut.style.setProperty('--mw', (v('w') * 12.4).toFixed(1) + 'px'); cut.style.setProperty('--mh', (v('h') * 8).toFixed(1) + 'px');
          inn.appendChild(cp);
          if (pg === 'connection') $$('.patch', win).forEach(function (pt) { inn.appendChild(pt.cloneNode(true)); });
          inn.appendChild(ring); cut.appendChild(inn); keys[k].insertBefore(cut, keys[k].firstChild);
        });
      });
      fig.appendChild(nav);
      // the caption goes under the pages while they sit side by side (the window above is kept small, CSS)
      var cap = $('figcaption', fig); if (cap) { var pc = d.createElement('p'); pc.className = 'pg-cap'; pc.innerHTML = cap.innerHTML; pagesBox.parentNode.insertBefore(pc, pagesBox.nextSibling); } tabs[0].setAttribute('aria-current', 'true');
      // the page whose card is most in view is the one shown; the address and the chip follow once the swipe rests
      var step = function () { return pages.length > 1 ? pages[1].offsetLeft - pages[0].offsetLeft : 1; };
      var slide = function (i, smooth) { pagesBox.scrollTo({ left: i * step(), behavior: smooth && FX ? 'smooth' : 'auto' }); show(pages[i].getAttribute('data-pg')); holdPg = performance.now() + (smooth ? 500 : 0); };
      var sq = 0, sT = 0;
      pagesBox.addEventListener('scroll', function () {
        if (!car) return;
        if (!sq) { sq = 1; requestAnimationFrame(function () { sq = 0; pick(); }); }
        clearTimeout(sT); sT = setTimeout(function () { holdPg = 0; pick(); take(stepAt(scrollY)); }, 160);
      }, { passive: true });
      var carSet = function () {
        car = CAR.matches; nav.hidden = !car;
        if (car) pagesBox.scrollLeft = Math.max(0, pages.map(function (l) { return l.getAttribute('data-pg'); }).indexOf(cur)) * step();
      };
      if (CAR.addEventListener) CAR.addEventListener('change', carSet); else if (CAR.addListener) CAR.addListener(carSet);
      // a reload on a page keeps its place and its page (the address has it); a link to one goes there with deep() below
      var hp = PGS.indexOf(location.hash.slice(1));
      if (hp > 0) addEventListener('load', function () { if (car) slide(hp, false); });
    }
    // the page's title goes where the snap rests it, and the window keeps that page while the page settles
    var holdPg = 0;
    pageGo = function (pg) {
      var i = pages.map(function (l) { return l.getAttribute('data-pg'); }).indexOf(pg); if (i < 0) return;
      if (car) { scrollTo(0, Math.round(docTop(pagesBox) - topH() - fig.offsetHeight - 8)); slide(i, false); }
      else if (side) scrollTo(0, rests[i]); else ($('h3', pages[i]) || pages[i]).scrollIntoView({ block: 'start', behavior: 'instant' });
      show(pg); holdPg = performance.now() + 600;
    };
    var show = function (pg) {
      if (pg === cur) return; cur = pg;
      if (seen) seen.pg = pageNow(scrollY); // the scroll's own update ran before this page was picked
      pics.forEach(function (pc) {
        var on = pc.getAttribute('data-pg') === pg; pc.classList.toggle('on', on);
        if (on) { pc.removeAttribute('aria-hidden'); var im = $('img', pc); if (im.loading === 'lazy') im.loading = 'eager'; zl.href = 'img/' + pg + '.png'; }
        else pc.setAttribute('aria-hidden', 'true');
      });
      pages.forEach(function (li, i) { var on = li.getAttribute('data-pg') === pg; li.classList.toggle('on', on); if (tabs[i]) { if (on) tabs[i].setAttribute('aria-current', 'true'); else tabs[i].removeAttribute('aria-current'); } });
      if (win) win.setAttribute('data-pg', pg);
    };
    // rests: the scroll where each page is read (the keyboard and a turn of the phone go there). A phone on its side has
    // the window beside a short column: each page rests with its title level with the window's top, and the window shows
    // the page that fills most of the column, its title above the column's middle (6 Nov.: by the middle of the screen it showed the next page
    // while the text on the left was still the one before)
    var centres = [], rests = [], line0 = 0, side = false, figTop = 0;
    var mPages = function () {
      car = CAR.matches;
      var pr = $('#pages').getBoundingClientRect(), fr = fig.getBoundingClientRect();
      side = innerWidth < 1000 && fr.left >= pr.right - 4;
      figTop = parseFloat(getComputedStyle(fig).top) || topH();
      centres = pages.map(function (li) { return docTop(li) + li.offsetHeight / 2; });
      line0 = tall() ? Math.min(innerHeight * .78, topH() + fig.offsetHeight + innerHeight * .25) : innerHeight * .5;
      rests = side ? pages.map(function (li) { return Math.round(docTop($('h3', li) || li) - figTop); }) : centres.map(function (c) { return Math.round(c - line0); });
      if (car) rests = [];
    };
    var pick = function () {
      if (performance.now() < holdPg) return;
      var best = -1, bd = 1e9;
      if (car) best = Math.max(0, Math.min(pages.length - 1, Math.round(pagesBox.scrollLeft / step())));
      else if (side) { var lim = scrollY + (innerHeight - figTop) * .5; rests.forEach(function (r, i) { if (r <= lim) best = i; }); best = Math.max(best, 0); }
      else { var line = scrollY + line0; centres.forEach(function (c, i) { var dd = Math.abs(c - line); if (dd < bd) { bd = dd; best = i; } }); }
      if (best >= 0) show(pages[best].getAttribute('data-pg'));
    };
    var pq = 0, onPg = function () { if (!pq) { pq = 1; requestAnimationFrame(function () { pq = 0; pick(); }); } };
    mPages(); addEventListener('resize', mPages); addEventListener('load', mPages); if (d.fonts && d.fonts.ready) d.fonts.ready.then(mPages);
    if ('IntersectionObserver' in W) new IntersectionObserver(function (es) { es.forEach(function (e) { if (e.isIntersecting) { mPages(); pick(); addEventListener('scroll', onPg, { passive: true }); } else removeEventListener('scroll', onPg); }); }).observe($('#pages'));
    else addEventListener('scroll', onPg, { passive: true });
    pages.forEach(function (li) { li.addEventListener('focusin', function () { show(li.getAttribute('data-pg')); }); });
    // the keyboard: one page of the window per PageDown or Space, from the first page to the last
    d.addEventListener('keydown', function (e) {
      var dir = keyDir(e); if (!dir || !rests.length) return;
      var y = scrollY, at = rests, to = null, i;
      if (y < at[0] - innerHeight * .6 || y > at[at.length - 1] + 2) return;
      if (dir > 0) { for (i = 0; i < at.length; i++) if (at[i] > y + 4) { to = at[i]; break; } }
      else { for (i = at.length - 1; i >= 0; i--) if (at[i] < y - 4) { to = at[i]; break; } }
      if (to === null) return;
      e.preventDefault(); keyGo(to);
    });
    fig.addEventListener('focusin', function () { if (scrollY < storyEnd) scrollTo(0, Math.ceil(storyEnd)); });
    // by touch on a phone each page rests with its text right under the sticky window (the snap in the CSS; the
    // margin leaves out the page's scroll-padding, which the snap adds on its own)
    // On its side the window is beside the text, so a page's title rests level with the window's top instead
    var pagesEl = $('#pages'), setWin = function () {
      mPages(); fig.style.setProperty('--win-h', fig.offsetHeight + 'px');
      var sp = parseFloat(getComputedStyle(root).scrollPaddingTop) || 0, rem = parseFloat(getComputedStyle(root).fontSize) || 16;
      if (pagesEl) pagesEl.style.setProperty('--pg-top', (side ? figTop - sp - 2.2 * rem : figTop + fig.offsetHeight - sp) + 'px');
    };
    setWin(); addEventListener('resize', setWin); addEventListener('load', setWin);
  }

  /* ------------------------------------------------------------ start the scene once its script is in */
  var idle = W.requestIdleCallback || function (fn) { return setTimeout(fn, 600); };
  // after the cover has come in (its lines take about 1.3 s), so building the world never takes a frame from it
  var later = function () { setTimeout(function () { idle(start, { timeout: 1200 }); }, FX ? 900 : 0); };
  if (W.WorldScene) later(); else W.__worldReady = later;
})();
/* Android or iPhone, on a phone: a switch of two tabs over the two cards (6 Nov., the owner asked for it). It opens on the
   iPhone for a visitor on iOS, on Android for everyone else. Arrows, Home and End move between the tabs. On a PC, or
   without JS, both cards show side by side as before (index.html, .ph-tabs and .duo.tabbed) */
(function () {
  'use strict';
  var d = document, duo = d.querySelector('.duo'); if (!duo || !window.matchMedia) return;
  var cards = [].slice.call(duo.children); if (cards.length !== 2) return;
  var Q = matchMedia('(max-width:759px),(max-height:540px) and (orientation:landscape)');
  var ios = /iPhone|iPad|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
  var names = ['Android', 'iPhone'], on = ios ? 1 : 0, tabs = [];
  var list = d.createElement('div'); list.className = 'ph-tabs'; list.setAttribute('role', 'tablist'); list.setAttribute('aria-label', 'Your phone');
  names.forEach(function (n, i) {
    var b = d.createElement('button'); b.type = 'button'; b.textContent = n; b.id = 'ph-tab-' + i; b.setAttribute('role', 'tab'); b.setAttribute('aria-controls', 'ph-card-' + i);
    b.addEventListener('click', function () { pick(i); });
    list.appendChild(b); tabs.push(b); cards[i].id = 'ph-card-' + i;
  });
  list.addEventListener('keydown', function (e) {
    var k = e.key, i = k === 'ArrowRight' || k === 'ArrowDown' ? (on + 1) % 2 : k === 'ArrowLeft' || k === 'ArrowUp' ? (on + 1) % 2 : k === 'Home' ? 0 : k === 'End' ? 1 : -1;
    if (i < 0) return; e.preventDefault(); pick(i); tabs[i].focus();
  });
  duo.parentNode.insertBefore(list, duo);
  function pick(i) {
    on = i;
    tabs.forEach(function (b, k) { b.setAttribute('aria-selected', k === i); b.tabIndex = k === i ? 0 : -1; });
    if (duo.classList.contains('tabbed')) cards.forEach(function (c, k) { c.setAttribute('aria-hidden', k !== i); });
  }
  function set() {
    var t = Q.matches; duo.classList.toggle('tabbed', t);
    cards.forEach(function (c, k) {
      if (t) { c.setAttribute('role', 'tabpanel'); c.setAttribute('aria-labelledby', 'ph-tab-' + k); c.tabIndex = 0; }
      else { c.removeAttribute('role'); c.removeAttribute('aria-labelledby'); c.removeAttribute('aria-hidden'); c.removeAttribute('tabindex'); }
    });
    if (t) duo.removeAttribute('role'); else duo.setAttribute('role', 'group');
    pick(on);
  }
  set(); if (Q.addEventListener) Q.addEventListener('change', set); else if (Q.addListener) Q.addListener(set);
})();
