/* CS2 IP Changer site: the readout (the IP as a number on a ruler) and the motion of every page.
   The calm page is the HTML itself. html.fx (set in <head> when the system allows motion, or the visitor pressed Play motion) turns the motion on, and only
   then are GSAP, ScrollTrigger and Lenis loaded. scenes.min.js (the two 3D scenes, three.js) loads either way: with
   reduced motion they cut between states and draw only when something changes. Everything is served from this site. */
(function () {
  'use strict';
  var d = document, root = d.documentElement, W = window;
  // the words this script shows: T(text) reads the page's own table (window.__i18n, put there by tools/site-i18n.mjs
  // on the translated pages); ROOT is the way from the page to the site's folder ('../' under /pt-br/, /es/ and /ru/)
  var T = function (s) { var t = W.__i18n; return t && t[s] || s; }, ROOT = W.__root || '';
  var FX = root.classList.contains('fx');
  var G = W.gsap, ST = W.ScrollTrigger, HAS = FX && !!(G && ST);
  var $ = function (s, el) { return (el || d).querySelector(s); };
  var $$ = function (s, el) { return [].slice.call((el || d).querySelectorAll(s)); };
  var clamp = function (v, a, b) { return Math.min(b === undefined ? 1 : b, Math.max(a || 0, v)); };
  var lerp = function (a, b, t) { return a + (b - a) * t; };
  var smooth = function (t) { return t * t * (3 - 2 * t); };
  var ease = function (t) { return t < .5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2; };
  var hash = function (n) { var x = Math.sin(n * 127.1) * 43758.5453; return x - Math.floor(x); };
  var tok = function (n) { return getComputedStyle(root).getPropertyValue(n).trim(); };
  var wait = function (ms) { return new Promise(function (r) { setTimeout(r, ms); }); };
  var fine = matchMedia('(hover: hover) and (pointer: fine)').matches;
  var topH = function () { var h = $('.top'); return h ? h.offsetHeight : 64; };
  var TEXT = tok('--t-text');

  /* ------------------------------------------------------------ smooth scroll, and anchors that land under the header */
  var lenis = null;
  if (HAS) {
    G.registerPlugin(ST);
    // the guides lay out their text, questions and footer only when they come near (content-visibility); each time one is
    // laid out for the first time the page's height changes, so the scroll-linked parts measure again (6 Nov.)
    if (d.querySelector('.gtext') && 'ResizeObserver' in W) {
      var rhH = 0, rhT = 0;
      new ResizeObserver(function () { var h = d.documentElement.scrollHeight; if (Math.abs(h - rhH) < 2) return; rhH = h; clearTimeout(rhT); rhT = setTimeout(function () { ST.refresh(); }, 200); }).observe(d.body);
    }
    if (W.Lenis) {
      lenis = new W.Lenis({ lerp: .14, wheelMultiplier: 1, respectReducedMotion: false }); W.__lenis = lenis;
      lenis.on('scroll', ST.update);
      G.ticker.add(function (t) { lenis.raf(t * 1000); });
      G.ticker.lagSmoothing(500, 33);
      d.addEventListener('click', function (e) {
        var a = e.target.closest && e.target.closest('a[href*="#"]');
        if (!a || e.button || e.ctrlKey || e.metaKey || e.shiftKey) return;
        var u = new URL(a.href, location.href);
        if (u.pathname !== location.pathname || !u.hash) return;
        var t = d.getElementById(decodeURIComponent(u.hash.slice(1)));
        if (!t) return;
        e.preventDefault();
        lenis.scrollTo(Math.round(t.getBoundingClientRect().top + scrollY - topH() - 16), { duration: 1.1, lock: true, onComplete: function () { t.setAttribute('tabindex', '-1'); t.focus({ preventScroll: true }); } });
        history.pushState(null, '', u.hash);
      });
    }
  }

  /* ------------------------------------------------------------ header: the app's top line fills as you read */
  var prog = $('.prog');
  if (prog) {
    var sh = root.scrollHeight; if ('ResizeObserver' in W) new ResizeObserver(function () { sh = root.scrollHeight; }).observe(d.body);
    var pq = 0, pdraw = function () { pq = 0; var h = sh - innerHeight; prog.style.transform = 'scaleX(' + (h > 0 ? clamp(scrollY / h) : 0) + ')'; };
    addEventListener('scroll', function () { if (!pq) { pq = 1; requestAnimationFrame(pdraw); } }, { passive: true }); pdraw();
  }

  /* ------------------------------------------------------------ the readout: digits on rolling strips */
  var GLYPHS = '0123456789';
  function Readout(el) { this.el = el; this.cur = el.textContent.trim(); this.render(this.cur); }
  Readout.prototype.render = function (str, strips) {
    var octs = str.split('.'), idx = 0, html = '';
    octs.forEach(function (o, k) {
      if (k === 2 && octs.length === 4) html += '<span class="oct2"></span>';
      html += '<span class="oct">';
      for (var i = 0; i < o.length; i++) {
        var seq = strips ? strips[idx] : [o[i]], sp = o[i] === ' ' ? ' sp' : o[i] === '/' ? ' sl' : '';
        html += '<span class="ch' + sp + '"><span class="strip">' + seq.map(function (x) { return '<span class="g">' + x + '</span>'; }).join('') + '</span></span>';
        idx++;
      }
      if (k < octs.length - 1) { html += '<span class="dot">.</span>'; idx++; }
      html += '</span>';
    });
    this.el.innerHTML = html;
  };
  /* roll to a new value. The first glyph of each strip is what stood there before (or nothing), never a made-up IP. */
  Readout.prototype.set = function (next, opt) {
    opt = opt || {};
    var self = this, prev = opt.blank ? '' : this.cur;
    if (next === this.cur && !opt.blank) return Promise.resolve();
    this.cur = next;
    if (!HAS || opt.now) { this.render(next); return Promise.resolve(); }
    var pc = prev.split(''), dur = opt.dur || .8, st = opt.stagger || .03;
    var strips = next.split('').map(function (c, i) {
      if (!/[0-9]/.test(c)) return [c];
      var first = /[0-9]/.test(pc[i] || '') ? pc[i] : ' ', n = opt.quick ? 3 : 4 + (i % 3);
      var seq = [first];
      for (var k = 0; k < n; k++) seq.push(GLYPHS[Math.floor(hash(i * 31 + k * 7 + next.length) * 10)]);
      seq.push(c);
      return seq;
    });
    this.render(next, strips);
    var chs = $$('.ch', this.el).filter(function (ch) { return ch.firstChild.children.length > 1; });
    if (!chs.length) { this.render(next); return Promise.resolve(); }
    return new Promise(function (res) {
      chs.forEach(function (ch, i) {
        var strip = ch.firstChild, n = strip.children.length - 1;
        ch.classList.add('hot');
        G.fromTo(strip, { yPercent: 0 }, {
          yPercent: -100 * n / (n + 1), duration: dur, delay: i * st, ease: 'expo.out',
          onComplete: function () {
            setTimeout(function () { ch.classList.remove('hot'); }, 350);
            if (i === chs.length - 1) { if (self.cur === next) self.render(next); res(); }
          }
        });
      });
    });
  };

  /* the number spans its column: the size is worked out from the characters, dots and spaces narrower than digits */
  var emOf = function (str) { var em = 0; String(str).split('').forEach(function (c) { em += (c === '.' || c === ' ') ? .34 : c === '/' ? .44 : .6; }); return em; };
  function fit(el, str, cap) {
    var num = el.closest('.ro-num'); if (!num) return function () {};
    var cur = str;
    var go = function (s) {
      if (s) cur = s;
      if (innerWidth < 760) { num.style.removeProperty('--ro-size'); return; }
      var w = num.parentNode.clientWidth;
      num.style.setProperty('--ro-size', Math.min(w / emOf(cur), innerHeight * (cap || .25)) + 'px');
    };
    go(); addEventListener('resize', function () { go(); });
    return go;
  }

  /* the old number leaves as dust in the app's three colours (by octet), and is gone before the new one rolls in */
  function dust(host, digitsEl) {
    if (!HAS) return;
    var cv = $('canvas.dust', host);
    if (!cv) { cv = d.createElement('canvas'); cv.className = 'dust'; cv.setAttribute('aria-hidden', 'true'); host.appendChild(cv); }
    var r = cv.getBoundingClientRect(), dpr = Math.min(2, devicePixelRatio || 1), hd = $('.ro-head', host);
    // the dust stays under the instrument's head, so its words are never read through it
    cv.style.clipPath = hd ? 'inset(' + Math.max(0, Math.round(hd.getBoundingClientRect().bottom - r.top + 6)) + 'px 0 0 0)' : '';
    cv.width = Math.round(r.width * dpr); cv.height = Math.round(r.height * dpr);
    var cx = cv.getContext('2d'), dr = digitsEl.getBoundingClientRect();
    var fs = parseFloat(getComputedStyle(digitsEl.parentNode).fontSize);
    var off = d.createElement('canvas'), step = Math.max(3, Math.round(fs / 40));
    off.width = Math.ceil(dr.width / step); off.height = Math.ceil(dr.height / step);
    var o = off.getContext('2d');
    o.fillStyle = '#fff'; o.textBaseline = 'middle'; o.textAlign = 'center';
    o.font = '400 ' + (fs / step) + 'px "Fragment Mono", monospace';
    var bounds = [];
    $$('.oct', digitsEl).forEach(function (oc) { var b = oc.getBoundingClientRect(); bounds.push(b.right - dr.left); });
    $$('.ch .g:first-child, .dot', digitsEl).forEach(function (g) {
      var b = (g.classList.contains('dot') ? g : g.parentNode.parentNode).getBoundingClientRect();
      o.fillText(g.textContent, (b.left - dr.left + b.width / 2) / step, (b.top - dr.top + b.height * .5) / step);
    });
    var a = o.getImageData(0, 0, off.width, off.height).data, P = [];
    var cols = [tok('--t-top1'), tok('--t-top1'), tok('--t-top2'), tok('--t-top3')];
    for (var y = 0; y < off.height; y++) for (var x = 0; x < off.width; x++) {
      if (a[(y * off.width + x) * 4 + 3] < 110) continue;
      var lx = x * step, oi = 0; while (oi < bounds.length - 1 && lx > bounds[oi]) oi++;
      var px = dr.left - r.left + lx, py = dr.top - r.top + y * step, h = hash(x * 13.7 + y * 7.1);
      P.push({ x: px, y: py, vx: 40 + 240 * h + (px / r.width) * 110, vy: -50 - 200 * hash(x + y * 3.3), s: Math.max(2, step * .6), c: cols[Math.min(3, oi)], l: .65 + .55 * hash(y * 9 + x) });
    }
    digitsEl.style.opacity = 0;
    var t0 = performance.now();
    (function frame(now) {
      var t = (now - t0) / 1000;
      cx.setTransform(dpr, 0, 0, dpr, 0, 0); cx.clearRect(0, 0, r.width, r.height);
      var alive = false;
      for (var i = 0; i < P.length; i++) {
        var p = P[i], k = t / p.l; if (k >= 1) continue; alive = true;
        var e = 1 - Math.pow(1 - k, 3);
        cx.globalAlpha = (1 - k) * (1 - k); cx.fillStyle = p.c;
        cx.fillRect(p.x + p.vx * e * .9, p.y + p.vy * e * .9 + 40 * k * k, p.s, p.s);
      }
      if (alive && t < 1.5) requestAnimationFrame(frame); else cx.clearRect(0, 0, r.width, r.height);
    })(t0);
  }

  /* ------------------------------------------------------------ the cover readout */
  var ro = $('#readout');
  var EX = ['192.0.2.140', '198.51.100.7', '203.0.113.88', '198.51.100.23'];   // RFC 5737 examples, each in another /16
  var HOME = '203.0.•••.•••';
  var listeners = [];
  var onIP = function (f) { listeners.push(f); };
  var ipNow = ro ? ro.getAttribute('data-ip') : '';
  var R = null, finishIntro = function () {}, enterIntro = function () {};
  var narrow = function () { return innerWidth < 760; };
  if (ro) {
    var digitsEl = $('#ro-digits', ro), led = $('#ro-led'), state = $('#ro-state'), label = $('#ro-label');
    R = new Readout(digitsEl);
    var refit = fit(digitsEl, '198.51.100.23', innerHeight < 780 ? .13 : .21);
    var phone = $('#ro-phone'), live = $('#ro-live'), text = $('#ro-text'), btn = $('#ro-demo'), rule = $('.ro-rule', ro);
    var status = function (cls, t, short) { led.className = 'led ' + cls; state.textContent = narrow() && short ? short : t; };
    var tellAll = function (ip) { ipNow = ip; text.textContent = T('Example: IP servers see ') + ip; listeners.forEach(function (f) { f(ip); }); };
    if (HAS) {
      // the intro: the home IP, masked and grey, then protection comes on and the carrier's IP rolls in
      var introT = [], introDone = false;
      R.cur = HOME; R.render(HOME); digitsEl.classList.add('home'); label.textContent = T('Your home IP'); status('idle', T('Protection off'));
      root.classList.remove('intro');
      btn.setAttribute('aria-disabled', 'true');
      rule.style.setProperty('--fill', 0);
      // the copy comes in once the 3D cover is mounted (its build takes a few frames), so no frame of the entrance is lost
      G.set(['#h1', '.hero-side > *'], { y: 24, autoAlpha: 0 });
      G.set('.hero .ro-head, .hero .ro-foot', { autoAlpha: 0 });
      var entered = false;
      enterIntro = function () {
        if (entered) return; entered = true;
        G.to(['#h1', '.hero-side > *'], { y: 0, autoAlpha: 1, duration: .9, ease: 'expo.out', stagger: .06, delay: .05 });
        G.to('.hero .ro-head, .hero .ro-foot', { autoAlpha: 1, duration: .6, ease: 'power2.out', delay: .3 });
        (d.fonts ? d.fonts.ready : Promise.resolve()).then(function () {
          if (introDone) return;
          introT.push(setTimeout(function () {
            status('on', T('Turning on protection')); rule.style.setProperty('--fill', .45);
            introT.push(setTimeout(finishIntro, 450));
          }, 300));
        });
      };
      // if the 3D cover is slow to arrive, the page does not wait for it
      introT.push(setTimeout(enterIntro, 1500));
      finishIntro = function () {
        enterIntro();
        if (introDone) return; introDone = true; btn.removeAttribute('aria-disabled');
        introT.forEach(clearTimeout);
        label.textContent = T('IP servers see'); digitsEl.classList.remove('home'); rule.style.setProperty('--fill', 1);
        R.set(ipNow, { now: R.cur === HOME && scrollY > 24 }).then(function () { status('', 'Protected'); W.dispatchEvent(new Event('cs2protected')); });
      };
      addEventListener('scroll', function f() { if (scrollY > 24) { removeEventListener('scroll', f); finishIntro(); } }, { passive: true });
    }
    var k = 0, busy = false, skip = false;
    btn.addEventListener('click', async function () {
      if (busy) { skip = true; return; }
      busy = true; skip = false; finishIntro();
      if (innerWidth < 760) { var hg = $('.hero-gl'); if (hg) { var g = hg.getBoundingClientRect().top - topH(); if (Math.abs(g) > 8) { if (lenis) lenis.scrollTo(scrollY + g, { duration: .6 }); else scrollTo({ top: scrollY + g, behavior: 'smooth' }); await wait(650); } } }
      var next = EX[k++ % EX.length], was = R.cur;
      if (next === was) next = EX[k++ % EX.length];
      if (HAS) {
        status('off', T('Android: mobile data off'), T('Mobile data off')); data(false); label.textContent = T('No connection'); phone.classList.add('down'); digitsEl.classList.add('home'); rule.style.setProperty('--fill', 0);
        await wait(skip ? 0 : 600);
        status('on', T('Android: mobile data on'), T('Mobile data on')); data(true); label.textContent = T('Getting a new IP'); phone.classList.remove('down'); rule.style.setProperty('--fill', .5);
        await wait(skip ? 0 : 350);
        if (!skip) dust(ro, digitsEl);
        await wait(skip ? 0 : 300);
        digitsEl.classList.remove('home'); label.textContent = T('IP servers see');
        digitsEl.style.opacity = '';
        rule.style.setProperty('--fill', 1);
        await R.set(next, { blank: true, now: skip });
        status('new', T('New IP, checked: new /16 range'), T('New IP, new /16'));
        setTimeout(function () { if (!busy) status('', 'Protected'); }, 1800);
      } else {
        // reduced motion: the same steps as cuts, so the scene still shows mobile data off, then on, then the new IP
        status('off', T('Android: mobile data off'), T('Mobile data off')); data(false); label.textContent = T('No connection'); phone.classList.add('down');
        await wait(skip ? 0 : 900);
        status('on', T('Android: mobile data on'), T('Mobile data on')); data(true); phone.classList.remove('down');
        await wait(skip ? 0 : 500);
        label.textContent = T('IP servers see'); R.set(next); status('new', T('New IP, checked: new /16 range'), T('New IP, new /16'));
        setTimeout(function () { if (!busy) status('', 'Protected'); }, 1800);
      }
      live.textContent = T('Example: IP servers see ') + next + T(', a new range. Was ') + (was === HOME ? T('your masked home IP') : was) + '.';
      tellAll(next);
      busy = false;
    });
  }

  /* the close repeats the cover's readout, the same size rule and the same IP */
  var ctaD = $('#cta-digits'), CR = ctaD ? new Readout(ctaD) : null, ctaFit = ctaD ? fit(ctaD, ctaD.textContent.trim(), .22) : function () {};

  /* ------------------------------------------------------------ the cover's 3D route */
  var hcv = $('#hero-route'), HAPI = null;
  // a box in the cover's own coordinates, from layout offsets (so the intro's transforms never move it)
  var cover = $('.cover');
  var boxIn = function (el) {
    var l = 0, t = 0, e = el;
    while (e && e !== cover) { l += e.offsetLeft; t += e.offsetTop; e = e.offsetParent; }
    return [l, t, l + el.offsetWidth, t + el.offsetHeight];
  };
  // the same for what a block holds: a paragraph is as wide as its column, its words are not (the right edge is the ink's)
  var inkIn = function (el) {
    var b = boxIn(el), rg = d.createRange(); rg.selectNodeContents(el);
    var right = rg.getBoundingClientRect().right - cover.getBoundingClientRect().left;
    if (right > b[0]) b[2] = Math.min(b[2], right);
    return b;
  };
  // the free part of the cover, where proposal A's route is drawn as large as it fits: right of the copy, or under it
  // when that is bigger (a tall window), down to the number; the title and the instrument's head are kept clear
  var coverRoom = function (W, H, layout) {
    var gl = hcv.parentNode;
    // phones: the scene has a band of its own between the copy and the number
    // (a tablet held upright too, with A's own wide shot in its wider band)
    if (getComputedStyle(gl).position !== 'absolute') { gl.style.removeProperty('--gl-cut'); gl.style.removeProperty('--gl-l'); return { box: [10, H * .08, W - 10, H * .9], avoid: [], layout: W / H >= 1.5 ? 'wide' : 'tall' }; }
    var head = inkIn($('.ro-head', ro)), side = inkIn($('.hero-side')), h1 = boxIn($('#h1')), num = boxIn($('.ro-num', ro));
    var g = parseFloat(getComputedStyle($('.hero')).paddingLeft) || 24, top = 8, bot = num[1] + (num[3] - num[1]) * .06;
    var right = [side[2] + 40, top, W - 16, bot], under = [g, Math.max(side[3], h1[3]) + 24, W - g, bot];
    // the route rises under the glass header; the labels stay below it
    var size = function (b) { return Math.min(b[2] - b[0], (b[3] - b[1]) * 1.1); };
    // a phone on its side has no room under the copy: always beside it
    var box = size(under) > size(right) && !matchMedia('(orientation:landscape) and (max-height:540px)').matches ? under : right;
    var avoid = [[h1[0] - 18, h1[1] - 18, h1[2] + 18, h1[3] + 18], [head[0] - 16, head[1] - 16, head[2] + 24, head[3] + 12], [side[0], side[1] - 12, side[2] + 24, side[3] + 12]];
    // the veil follows: clear over the route, the ground fading out under the number and behind the copy
    gl.style.setProperty('--gl-cut', (bot / H * 100).toFixed(1) + '%');
    gl.style.setProperty('--gl-l', (box[0] / W * 100).toFixed(1) + '%');
    return { box: box, avoid: avoid, labTop: topH() + 10, layout: (box[2] - box[0]) / Math.max(1, box[3] - box[1]) < .9 ? 'tall' : 'wide' };
  };
  var startHero = function () {
    // the cover is proposal A's own scene (cover.js in scenes.min.js)
    if (!hcv || HAPI || !W.CoverScene) return;
    var hp = $('.hero-pause'), held = false;
    try { held = localStorage.getItem('cs2-motion') === 'paused'; } catch (x) {}
    if (hp && FX) { hp.hidden = false; hp.setAttribute('aria-pressed', held); }
    try { HAPI = W.CoverScene.mount(hcv, { still: !FX, frame: coverRoom }); } catch (x) { HAPI = null; }
    requestAnimationFrame(function () { enterIntro(); });
    if (!HAPI) { if (hp) hp.hidden = true; return; }
    var box = hcv.parentNode;
    hcv.addEventListener('routeready', function () { box.classList.add('ready'); });
    HAPI.setIP(ipNow); onIP(function (ip) { HAPI.setIP(ip); });
    // the copy can change size once the fonts are in: fit the route again
    (d.fonts ? d.fonts.ready : Promise.resolve()).then(function () { HAPI.resize(); });
    // Pause motion: the same remembered switch as the one in How it works
    if (hp && FX) {
      hp.addEventListener('click', function () { held = !held; hp.setAttribute('aria-pressed', held); try { localStorage.setItem('cs2-motion', held ? 'paused' : ''); } catch (x) {} if (held) HAPI.pause(); else HAPI.resume(); W.dispatchEvent(new CustomEvent('cs2motion', { detail: { held: held, from: hp } })); });
      // the switch in How it works is the same switch (it starts again on screen, by the observer below)
      addEventListener('cs2motion', function (e) { if (e.detail.from === hp) return; held = e.detail.held; hp.setAttribute('aria-pressed', held); if (held) HAPI.pause(); });
    }
    if (held) hcv.addEventListener('routeready', function () { HAPI.pause(); });
    if ('IntersectionObserver' in W) new IntersectionObserver(function (es) { es.forEach(function (e) { if (e.isIntersecting) { if (!held) HAPI.resume(); } else HAPI.pause(); }); }).observe(box);
    addEventListener('resize', function () { HAPI.resize(); });
  };
  var data = function (on) { if (HAPI && HAPI.setData) HAPI.setData(on); };

  /* ------------------------------------------------------------ the app window: live values, and the six pages */
  var win = $('#live-app'), stage = $('#stage');
  if (win) {
    var pip = $('.p-ip span', win), PR = pip ? new Readout(pip) : null;
    onIP(function (ip) {
      win.classList.add('live'); $$('.p-bar span', win).forEach(function (s) { s.textContent = ip; });
      if (PR) PR.set(ip, { now: true });
      var big = $('#pages li[data-pg=connection] .bn'); if (big) big.textContent = ip;
      if (CR) { CR.set(ip, { now: true }); ctaFit(ip); }
      $$('#cta-win .patch span').forEach(function (s) { s.textContent = ip; }); var cw = $('#cta-win'); if (cw) cw.classList.add('live');
      // the kit shows the last change: the IP before and the new one, always in another /16
      var ka = $('.k-ip .a'), kb = $('.k-ip .b'); if (ka && kb && kb.textContent !== ip) { ka.textContent = kb.textContent; kb.textContent = ip; }
    });
    var pics = $$('.views picture', win), zl = $('.views', win), pages = $$('#pages > li');
    var bigs = {};
    pages.forEach(function (li) { var bn = $('.bn', li); if (bn) bigs[li.getAttribute('data-pg')] = bn; });
    var cur = 'connection';
    var show = function (pg) {
      if (pg === cur) return; cur = pg;
      pics.forEach(function (p) {
        var on = p.getAttribute('data-pg') === pg; p.classList.toggle('on', on); if (on) p.removeAttribute('aria-hidden'); else p.setAttribute('aria-hidden', 'true');
        if (on) { var im = $('img', p); if (im.loading === 'lazy') im.loading = 'eager'; zl.href = ROOT + 'img/' + pg + '.png';  }
      });
      win.classList.toggle('pg-other', pg !== 'connection');
      win.setAttribute('data-pg', pg);
      pages.forEach(function (li) { li.classList.toggle('on', li.getAttribute('data-pg') === pg); });
      // the page's number rolls in as its page comes up
      var bn = bigs[pg];
      if (bn && HAS) { var v = bn.textContent; var RB = new Readout(bn); RB.cur = ''; RB.set(v, { blank: true, dur: .55, stagger: .025 }); }
    };
    var pio = null;
    var watchPages = function () {
      if (!('IntersectionObserver' in W) || !pages.length) return;
      if (pio) pio.disconnect();
      var m = '-48% 0px -48% 0px';
      if (innerWidth < 1000 && stage) { var b = stage.getBoundingClientRect().height + topH() + 24; m = '-' + Math.round(b) + 'px 0px -35% 0px'; }
      pio = new IntersectionObserver(function (es) { es.forEach(function (e) { if (e.isIntersecting) show(e.target.getAttribute('data-pg')); }); }, { rootMargin: m });
      pages.forEach(function (li) { pio.observe(li); });
    };
    var centre = function () { if (stage) stage.style.setProperty('--win-h', stage.offsetHeight + 'px'); };
    centre(); watchPages(); addEventListener('resize', function () { centre(); watchPages(); });
  }

  /* the number flies from the cover into the app's "IP servers see" field, as the window comes up in perspective.
     On a phone there is no flight: the field rolls to the IP when the window comes on screen. */
  if (HAS && ro && stage && win) {
    var fly = d.createElement('p'); fly.className = 'fly ro-num'; fly.setAttribute('aria-hidden', 'true');
    var flyD = d.createElement('span'); flyD.className = 'digits'; fly.appendChild(flyD); d.body.appendChild(fly);
    var FR = new Readout(flyD), M = null, patch = $('.p-ip', win);
    var measure = function () {
      M = null; fly.style.visibility = 'hidden';
      if (innerWidth < 1000) return;
      var sy = scrollY, dr = $('#ro-digits').getBoundingClientRect(), st = stage.getBoundingClientRect();
      var top = parseFloat(getComputedStyle(stage).top) || 0;
      M = { sx: dr.left, sy: dr.top + sy, sw: dr.width, sh: dr.height, fs: getComputedStyle($('.ro-num', ro)).fontSize, end: Math.max(1, st.top + sy - top) };
      fly.style.fontSize = M.fs;
    };
    var drawFly = function () {
      if (!M) { ro.style.removeProperty('--fly'); $('#ro-digits').style.visibility = ''; return; }
      var p = clamp(scrollY / M.end), w = win.getBoundingClientRect();
      var tp = clamp((innerHeight - w.top) / (innerHeight * .9));
      win.style.transform = 'rotateX(' + ((1 - smooth(tp)) * 24).toFixed(2) + 'deg) scale(' + (.93 + .07 * smooth(tp)).toFixed(4) + ')';
      ro.style.setProperty('--fly', p.toFixed(3));
      if (p <= 0 || p >= 1) {
        fly.style.visibility = 'hidden'; $('#ro-digits').style.visibility = p <= 0 ? '' : 'hidden';
        win.classList.toggle('await', false);
        if (p >= 1 && ipNow !== '198.51.100.23') win.classList.add('live');
        return;
      }
      if (FR.cur !== ipNow) { FR.cur = ipNow; FR.render(ipNow); }
      // the patch covers the screenshot's own IP while the number travels, then the number lands in it
      win.classList.add('live'); win.classList.toggle('await', p < .97);
      var t = patch.getBoundingClientRect(), e = ease(p);
      var s1 = (t.height * .52) / (M.sh * .86), s = Math.exp(Math.log(s1) * e);
      var tx = t.left + t.width * .02, ty = t.bottom - M.sh * s1 * .92;
      var x = lerp(M.sx, tx, e), y = lerp(M.sy - scrollY, ty, Math.pow(e, 1.6));
      fly.style.transform = 'translate3d(' + x.toFixed(1) + 'px,' + y.toFixed(1) + 'px,0) scale(' + s.toFixed(4) + ')';
      fly.style.visibility = 'visible'; fly.style.opacity = p > .9 ? (1 - (p - .9) / .1).toFixed(3) : p < .35 ? (.35 + p * 1.86).toFixed(3) : 1;
      fly.style.color = p > .6 ? TEXT : '';
      fly.classList.toggle('ground', p > .55);
      $('#ro-digits').style.visibility = 'hidden';
    };
    ST.create({ trigger: d.body, start: 0, end: 'max', onUpdate: drawFly, onRefresh: function () { measure(); drawFly(); } });
    addEventListener('resize', function () { measure(); drawFly(); });
    (d.fonts ? d.fonts.ready : Promise.resolve()).then(function () { measure(); drawFly(); });
    if ('IntersectionObserver' in W) {
      var once = new IntersectionObserver(function (es) {
        es.forEach(function (e) { if (e.isIntersecting && innerWidth < 1000 && PR) { once.disconnect(); win.classList.add('live'); PR.cur = ''; PR.set(ipNow, { blank: true, dur: .6, stagger: .03 }); } });
      }, { threshold: .6 });
      once.observe(win);
    }
  }

  /* ------------------------------------------------------------ trust: VirusTotal squares */
  var vg = $('.vt-grid'), vn = $('.instr .rd');
  if (vg && vn) {
    var m = vn.textContent.match(/(\d+) of (\d+)/);
    if (m) { var bad = +m[1], all = +m[2], h = ''; for (var j = 0; j < all; j++) h += '<i' + (j < bad ? ' class="bad"' : '') + ' style="--d:' + j + '"></i>'; vg.style.setProperty('--cols', Math.ceil(all / 4)); vg.innerHTML = h; }
  }

  /* ------------------------------------------------------------ who: the sentence lights up part by part */
  var says = $$('.say span');
  if (says.length && FX) {
    var lit = function () { var y = innerHeight * .64; says.forEach(function (el) { el.classList.toggle('lit', el.getBoundingClientRect().top < y); }); }, lq = 0;
    addEventListener('scroll', function () { if (!lq) { lq = 1; requestAnimationFrame(function () { lq = 0; lit(); }); } }, { passive: true }); lit();
  }

  /* ------------------------------------------------------------ the ban: clock, poster line, ruler */
  var ban = $('.ban');
  if (ban) {
    var TIMES = [2, 2, 3, 4, 12, 20, 41], SPAN = 42;
    var logs = $$('#log li', ban), odoT = $('#odo-t'), odoO = $('#odo-o');
    odoT.innerHTML = '<div>' + '0123450'.split('').map(function (x) { return '<span>' + x + '</span>'; }).join('') + '</div>';
    odoO.innerHTML = '<div>' + '01234567890'.split('').map(function (x) { return '<span>' + x + '</span>'; }).join('') + '</div>';
    var odo = function (t) {
      var v = Math.floor(t) + smooth(clamp(((t % 1) - .82) / .18)), ones = v % 10, carry = Math.max(0, ones - 9), tens = Math.floor(v / 10) + carry;
      odoO.firstChild.style.transform = 'translateY(' + (-ones * 100 / 11) + '%)';
      odoT.firstChild.style.transform = 'translateY(' + (-tens * 100 / 7) + '%)';
    };
    var rT = $('#r-ticks'), rh = '';
    for (var i = 0; i <= SPAN; i++) { rh += '<i class="' + (i % 5 === 0 ? 'maj' : '') + '" style="left:' + (i / SPAN * 100) + '%"></i>'; if (i % 10 === 0) rh += '<em style="left:' + (i / SPAN * 100) + '%">21:14:' + (i < 10 ? '0' : '') + i + '</em>'; }
    TIMES.forEach(function (t, i) { rh += '<b style="left:' + (t / SPAN * 100) + '%"></b>'; });
    rT.innerHTML = rh;
    var dots = $$('b', rT), fill = $('#r-fill'), head = $('#r-head'), ruler = $('.ruler', ban), lastB = -1;
    var setTime = function (t) { odo(t); fill.style.transform = 'scaleX(' + (t / SPAN) + ')'; head.style.transform = 'translateX(' + (t / SPAN * ruler.clientWidth).toFixed(1) + 'px)'; };
    var setLine = function (now) {
      if (now === lastB) return; lastB = now;
      logs.forEach(function (li, i) { li.className = i < now ? 'past' : i === now ? 'now' : ''; });
      dots.forEach(function (dd, i) { dd.classList.toggle('hit', i <= now); });
    };
    // without motion, or before any script: the end state (back on the server, 21:14:41, ruler full)
    setTime(41); setLine(6);
    if (HAS) {
      var evs = $$('.poster p', ban);
      evs.forEach(function (p) {
        var out = [];
        [].slice.call(p.childNodes).forEach(function (n) {
          if (n.nodeType === 3) n.textContent.split(/ +/).forEach(function (w) { if (w) out.push(w.replace(/&/g, '&amp;').replace(/</g, '&lt;')); });
          else out.push(n.outerHTML);
        });
        p.innerHTML = out.map(function (w) { return '<span class="w"><span>' + w + '</span></span>'; }).join(' ');
      });
      var words = evs.map(function (p) { return $$('.w > span', p); }), ipx = $('.poster .ipx', ban);
      var FROM = '•••.•.•.•••', TO = '192.0.2.140';
      var mm = G.matchMedia();
      // a pin only where the whole block fits on the screen; elsewhere the clock sticks and the log drives it
      var mobBan = function () {
        ban.classList.add('mob');
        var io = new IntersectionObserver(function (es) { es.forEach(function (e) { if (e.isIntersecting) { var i = logs.indexOf(e.target); setLine(i); setTime(TIMES[i]); evs.forEach(function (p, k) { p.classList.toggle('cur', k === i); }); } }); }, { rootMargin: '-45% 0px -50% 0px' });
        logs.forEach(function (li) { io.observe(li); });
        setTime(TIMES[0]); setLine(0); evs.forEach(function (p, k) { p.classList.toggle('cur', k === 0); });
        return function () { io.disconnect(); ban.classList.remove('mob'); setTime(41); setLine(6); };
      };
      mm.add('(min-width:760px) and (min-height:600px)', function () {
        ban.classList.add('pinned');
        if (ban.offsetHeight > innerHeight + 4) { ban.classList.remove('pinned'); return mobBan(); }
        words.forEach(function (ws, i) { G.set(ws, { yPercent: i ? 110 : 0 }); });
        var tl = G.timeline({ paused: true });
        // each step holds 60% of its scroll; the change happens in the last 40%, so a stop always shows one whole line
        for (var s = 1; s < evs.length; s++) {
          tl.to(words[s - 1], { yPercent: -110, duration: .16, stagger: .012, ease: 'power3.in' }, s - 1 + .6);
          tl.to(words[s], { yPercent: 0, duration: .24, stagger: .018, ease: 'expo.out' }, s - 1 + .76);
        }
        var draw = function () {
          var T = clamp(tl.time(), 0, evs.length - 1), seg = Math.min(evs.length - 2, Math.floor(T)), f = smooth(clamp((T - seg - .6) / .4));
          setTime(T >= evs.length - 1 ? TIMES[evs.length - 1] : lerp(TIMES[seg], TIMES[seg + 1], f));
          setLine(Math.min(evs.length - 1, Math.floor(T + .25)));
          if (ipx) {
            var q = clamp((T - 3.7) / .3), out = '';
            for (var j = 0; j < TO.length; j++) out += (q >= 1 || TO[j] === '.' || j / TO.length < q) ? TO[j] : '•';
            ipx.textContent = q <= 0 ? FROM : out;
          }
        };
        tl.eventCallback('onUpdate', draw);
        var st = ST.create({ trigger: ban, start: 'top top', end: '+=250%', pin: true, scrub: .35, animation: tl });
        draw();
        // keyboard: one line per PageDown or Space while the ban is pinned
        var key = function (e) {
          if (!st.isActive || !lenis || e.altKey || e.ctrlKey || e.metaKey) return;
          var dn = e.key === 'PageDown' || (e.key === ' ' && !e.shiftKey) || e.key === 'ArrowDown', up = e.key === 'PageUp' || (e.key === ' ' && e.shiftKey) || e.key === 'ArrowUp';
          if (!dn && !up) return;
          var now = Math.floor(tl.time() + .001), n = dn ? now + 1 : now - 1;
          if (n < 0 || n > evs.length - 1) return;
          e.preventDefault();
          lenis.scrollTo(st.start + (st.end - st.start) * Math.min(1, (n + .3) / (evs.length - 1)), { duration: .7 });
        };
        addEventListener('keydown', key);
        return function () { removeEventListener('keydown', key); st.kill(); tl.kill(); ban.classList.remove('pinned'); words.forEach(function (ws) { G.set(ws, { clearProps: 'all' }); }); setTime(41); setLine(6); if (ipx) ipx.textContent = TO; };
      });
      mm.add('(max-width:759px), (max-height:599px)', mobBan);
    }
  }

  /* ------------------------------------------------------------ how it works: the route in 3D, mounted as it comes near */
  var scene = $('#scene'), cvs = $('#route');
  var startRoute = function () {
    if (!W.RouteScene || scene.classList.contains('gl')) return;
    var api = null;
    try { api = W.RouteScene.mount(cvs, { still: !FX }); } catch (x) { api = null; }
    if (!api) return;
    scene.classList.add('gl'); W.__route = api;
    onIP(function (ip) { api.setIP(ip); $$('.sip', scene).forEach(function (s) { s.textContent = ip; }); });
    if (ipNow && ipNow !== '198.51.100.23') { api.setIP(ipNow); $$('.sip', scene).forEach(function (s) { s.textContent = ipNow; }); }
    var lbls = $$('.lbl', scene), hud = $$('.hud span', scene), lis = $$('#beats > li'), beats = $$('#beats .copy'), hudBox = $('.hud', scene), P = 0, B = 0;
    var SHOW = [['pc', 'phone'], ['server', 'carrier', 'phone', 'router'], ['gate', 'pc', 'router']];
    var place = function () {
      var want = SHOW[B], sr = scene.getBoundingClientRect(), wide = isWide(), show = [];
      var blocks = (wide ? beats.map(function (c) { return c.getBoundingClientRect(); }) : []).concat(hudBox && wide ? [hudBox.getBoundingClientRect()] : []);
      var taken = [];
      want.forEach(function (at) {
        var l = $('.lbl[data-at="' + at + '"]', scene), q = l && api.project(at);
        if (!q || !q.visible) return;
        var lw = l.offsetWidth, lh = l.offsetHeight;
        var L = lw > sr.width - 16 ? 8 : clamp(q.x - lw / 2, 8, sr.width - lw - 8), T = clamp(q.y - lh - 14, 8, sr.height - lh - 8);
        if (at === 'gate' && lw <= sr.width - 16) { L = clamp(q.x - lw - 18, 8, sr.width - lw - 8); T = clamp(q.y - lh / 2, 8, sr.height - lh - 8); }
        var ax = sr.left + L, ay = sr.top + T;
        var hit = blocks.some(function (c) { return ax < c.right + 12 && ax + lw > c.left - 12 && ay < c.bottom + 8 && ay + lh > c.top - 8; })
          || taken.some(function (o) { return L < o[0] + o[2] + 6 && L + lw + 6 > o[0] && T < o[1] + o[3] + 4 && T + lh + 4 > o[1]; });
        if (hit) return;
        taken.push([L, T, lw, lh]);
        l.style.transform = 'translate(' + L.toFixed(1) + 'px,' + T.toFixed(1) + 'px)';
        show.push(l);
      });
      lbls.forEach(function (l) { l.classList.toggle('on', show.indexOf(l) > -1); });
    };
    // progress from the texts themselves: a step is done once its text reaches the reading line, and holds while you read
    var isWide = function () { return innerWidth >= 1000 || matchMedia('(orientation:landscape) and (max-height:540px)').matches; };
    var readAt = function () {
      if (isWide()) return innerHeight / 2;
      var sb = scene.getBoundingClientRect().bottom; return (sb + innerHeight) / 2;
    };
    var update = function () {
      var ref = readAt(), p = 0;
      lis.forEach(function (li, i) { var r = li.getBoundingClientRect(); if (r.top <= ref) p = Math.max(p, i + clamp((ref - r.top) / Math.max(1, r.height))); });
      p = Math.min(3, p);
      var i = Math.min(2, Math.floor(p)), t = p - i;
      var line = isWide() ? innerHeight * .55 : ref;
      B = 0; lis.forEach(function (li, k) { if (li.getBoundingClientRect().top < line) B = k; });
      P = i + smooth(clamp((t - .05) / .4));
      api.setProgress(i === 0 ? smooth(clamp((p - .6) / .4)) * .999 : Math.min(3, i === 2 ? P : Math.min(P, i + .999)));
      hud.forEach(function (h, k) { h.classList.toggle('on', k === B); });
      var tr = scene.parentNode.getBoundingClientRect(); scene.classList.toggle('end', tr.bottom < innerHeight * 1.05);
      place();
    };
    var uq = 0;
    addEventListener('scroll', function () { if (!uq) { uq = 1; requestAnimationFrame(function () { uq = 0; update(); }); } }, { passive: true });
    update();
    var pause = $('#pause'), paused = false;
    try { paused = localStorage.getItem('cs2-motion') === 'paused'; } catch (x) {}
    if (FX && pause) {
      pause.hidden = false; pause.setAttribute('aria-pressed', paused); if (paused) api.pause();
      pause.addEventListener('click', function () {
        paused = !paused; pause.setAttribute('aria-pressed', paused);
        try { localStorage.setItem('cs2-motion', paused ? 'paused' : ''); } catch (x) {}
        if (paused) api.pause(); else api.resume();
        W.dispatchEvent(new CustomEvent('cs2motion', { detail: { held: paused, from: pause } }));
      });
      addEventListener('cs2motion', function (e) { if (e.detail.from === pause) return; paused = e.detail.held; pause.setAttribute('aria-pressed', paused); if (paused) api.pause(); });
    }
    if ('IntersectionObserver' in W) {
      new IntersectionObserver(function (es) {
        es.forEach(function (e) { if (e.isIntersecting) { if (!paused) api.resume(); } else api.pause(); });
      }).observe(scene);
    }
    addEventListener('resize', function () { api.resize(); update(); });
    cvs.addEventListener('routeready', place);
    cvs.addEventListener('routeframe', place);
  };
  W.__routeReady = function () { startHero(); if (W.__startRoute) W.__startRoute(); };
  startHero();
  if (scene && cvs) {
    var idle = W.requestIdleCallback || function (f) { return setTimeout(f, 200); };
    var go = function () { idle(function () { if (W.RouteScene) startRoute(); else W.__startRoute = startRoute; }, { timeout: 4000 }); };
    if (ro && HAS) { var gone = false; var once1 = function () { if (gone) return; gone = true; go(); }; addEventListener('cs2protected', once1); setTimeout(once1, 3500); }
    else go();
    if ('IntersectionObserver' in W) {
      var near = new IntersectionObserver(function (es) { es.forEach(function (e) { if (e.isIntersecting) { near.disconnect(); if (W.RouteScene) startRoute(); } }); }, { rootMargin: '60% 0px' });
      near.observe(scene.parentNode.parentNode);
    }
  }

  /* ------------------------------------------------------------ phone or VPN: one IP shared by many phones, and a list of datacenter IPs */
  var cg = $('#cgnat');
  if (cg) {
    var s = '', hx = 300, hy = 96, n = 0;
    for (var r1 = 0; r1 < 6; r1++) for (var c1 = 0; c1 < 8; c1++) {
      var px = 12 + c1 * 26, py = 18 + r1 * 26;
      s += '<path class="cg-line" pathLength="1" style="--d:' + (n++) + '" d="M' + (px + 4) + ' ' + py + 'C' + (px + 90) + ' ' + py + ' ' + (hx - 80) + ' ' + hy + ' ' + (hx - 14) + ' ' + hy + '"/>';
      s += '<rect class="cg-dot" x="' + (px - 4) + '" y="' + (py - 6) + '" width="8" height="12" rx="2"/>';
    }
    s += '<circle class="cg-hub" cx="' + hx + '" cy="' + hy + '" r="12"/><text class="cg-ip" x="' + (hx + 22) + '" y="' + (hy - 2) + '">198.51.100.23</text><text class="cg-lbl" x="' + (hx + 22) + '" y="' + (hy + 18) + '">' + T('one IP, example') + '</text><text class="cg-lbl" x="8" y="' + (18 + 5 * 26 + 30) + '">' + T('many phones on one carrier') + '</text>';
    cg.innerHTML = s;
  }
  var dc = $('#dclist');
  if (dc) {
    var rows = ['203.0.113.4', '203.0.113.9', '203.0.113.17', '203.0.113.23', '203.0.113.42', '203.0.113.58'], s3 = '';
    rows.forEach(function (ip, i) {
      var y = 4 + i * 25, hit = i === 3;
      s3 += '<rect class="dc-bar' + (hit ? ' hit' : '') + '" x="0" y="' + y + '" width="420" height="21" rx="2"/><text class="dc-row' + (hit ? ' hit' : '') + '" x="12" y="' + (y + 15) + '">' + ip + '</text><text class="dc-row' + (hit ? ' hit' : '') + '" x="408" y="' + (y + 15) + '" text-anchor="end">' + (hit ? T('VPN, on the list: ban') : T('datacenter')) + '</text>';
    });
    s3 += '<text class="cg-lbl" x="0" y="' + (4 + 6 * 25 + 26) + '">' + T('a VPN provider\'s addresses, example') + '</text>';
    dc.innerHTML = s3;
  }

  /* ------------------------------------------------------------ guides index: the guide's own readout follows the pointer */
  var peek = $('#peek');
  if (peek && fine) {
    var pb = $('b', peek), ps = $('span', peek), tx = 0, ty = 0, cx2 = 0, cy2 = 0, on = false, pr = 0;
    var put = function () { peek.style.setProperty('--x', cx2 + 'px'); peek.style.setProperty('--y', cy2 + 'px'); };
    var mv = function () { cx2 += (tx - cx2) * .18; cy2 += (ty - cy2) * .18; put(); if (on || Math.abs(tx - cx2) > .5) pr = requestAnimationFrame(mv); };
    $$('ul.guides li').forEach(function (li) {
      li.addEventListener('pointerenter', function (e) { var v = li.getAttribute('data-peek').split('|'); pb.textContent = v[0]; ps.textContent = v[1]; if (!on) { cx2 = tx = e.clientX; cy2 = ty = e.clientY; put(); } on = true; peek.classList.add('on'); if (FX) { cancelAnimationFrame(pr); mv(); } });
      li.addEventListener('pointermove', function (e) { tx = e.clientX; ty = e.clientY; if (!FX) { cx2 = tx; cy2 = ty; put(); } });
      li.addEventListener('pointerleave', function () { on = false; peek.classList.remove('on'); });
    });
  }

  /* ------------------------------------------------------------ footer: the name lights up as the page reaches it */
  // the three colors draw through its outline with the scroll; then a packet of light runs through the letters and packets
  // run along the horizon, only while it is on screen; Pause motion holds them; the light follows a mouse near it
  var wm = $('.wordmark');
  if (wm && HAS) ST.create({ trigger: wm, start: 'top bottom', end: 'max', scrub: .6, onUpdate: function (s) { wm.style.setProperty('--wmp', s.progress.toFixed(4)); } });
  else if (wm) wm.style.setProperty('--wmp', '1');
  if (wm && FX) {
    var wmHeld = function () { var h = false; try { h = localStorage.getItem('cs2-motion') === 'paused'; } catch (x) {} wm.classList.toggle('wm-hold', h); return h; };
    wm.classList.add('wm-run'); wmHeld();
    addEventListener('cs2motion', function () { wmHeld(); });
    d.addEventListener('click', function (e) { if (e.target.closest && e.target.closest('#pause')) setTimeout(wmHeld, 0); });
    if ('IntersectionObserver' in W) new IntersectionObserver(function (es) { wm.classList.toggle('wm-vis', es[es.length - 1].isIntersecting); }).observe(wm);
    else wm.classList.add('wm-vis');
    var svg = $('svg', wm), spot = $('.wm-spot', wm);
    if (fine && svg && spot && svg.createSVGPoint) {
      var pt = svg.createSVGPoint(), sx = -400, sy = 100, tx2 = -400, ty2 = 100, spr = 0;
      var spMove = function () { sx += (tx2 - sx) * .16; sy += (ty2 - sy) * .16; spot.setAttribute('cx', sx.toFixed(1)); spot.setAttribute('cy', sy.toFixed(1)); spr = Math.abs(tx2 - sx) + Math.abs(ty2 - sy) > .5 ? requestAnimationFrame(spMove) : 0; };
      addEventListener('pointermove', function (e) {
        var r = svg.getBoundingClientRect();
        var near = e.clientY > r.top - 120 && e.clientY < r.bottom + 60 && !wm.classList.contains('wm-hold');
        wm.classList.toggle('wm-near', near);
        if (!near) return;
        var m = svg.getScreenCTM(); if (!m) return;
        pt.x = e.clientX; pt.y = e.clientY; var q = pt.matrixTransform(m.inverse());
        if (sx < -300) { sx = q.x; sy = q.y; }
        tx2 = q.x; ty2 = q.y; if (!spr) spr = requestAnimationFrame(spMove);
      }, { passive: true });
    }
  }

  /* ------------------------------------------------------------ .rv: in when it reaches the screen, once */
  var rvs = $$('.rv, .kit > div, .vs > div');
  if (FX && 'IntersectionObserver' in W) {
    var rio = new IntersectionObserver(function (es) { es.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add('in'); rio.unobserve(e.target); } }); }, { rootMargin: '0px 0px -8% 0px' });
    rvs.forEach(function (el) { rio.observe(el); });
  } else rvs.forEach(function (el) { el.classList.add('in'); });
  /* the small demos (And the rest, Is it safe?, phone or VPN) play when the cell is well on screen, not as it peeks in */
  var demoEls = $$('.kit > div, .vs > div, .instr > li');
  if (FX && 'IntersectionObserver' in W) {
    var dio = new IntersectionObserver(function (es) { es.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add('go'); dio.unobserve(e.target); } }); }, { rootMargin: '0px 0px -38% 0px' });
    demoEls.forEach(function (el) { dio.observe(el); });
  } else demoEls.forEach(function (el) { el.classList.add('go'); });

  /* ------------------------------------------------------------ the guides: each page's own number rolls in once */
  $$('.ro-sig').forEach(function (box) {
    var el = $('.digits', box), val = el.textContent.trim(), Rg = new Readout(el), rl = $('.ro-rule', box);
    fit(el, val, .16);
    if (!HAS) { el.style.visibility = 'visible'; return; }
    var start = val.replace(/[0-9]/g, '•');
    Rg.cur = start; Rg.render(start); el.style.visibility = 'visible'; el.style.color = 'var(--ink3)'; rl.style.setProperty('--fill', 0);
    ST.create({ trigger: box, start: 'top 92%', once: true, onEnter: function () { setTimeout(function () { el.style.color = ''; rl.style.setProperty('--fill', 1); Rg.set(val, { blank: true }); }, 200); } });
  });

  W.CS2Readout = { Readout: Readout, dust: dust, HAS: HAS };
  if (HAS) addEventListener('load', function () { ST.refresh(); });
})();
