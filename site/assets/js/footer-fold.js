/* 26 Nov. 2026, direction D: on a phone the footer's lists (Product, Guides, Project) open in place under their heading, so the
   guides' list is not a second long list at the end of the page. The heading's own text becomes its button; wider than 759 px,
   or without JS, the lists are as before */
(function () {
  'use strict';
  var Q = matchMedia('(max-width:759px)');
  var navs = [].slice.call(document.querySelectorAll('footer .fgrid nav'));
  var parts = navs.map(function (nav) {
    var h = nav.querySelector('h3'), ul = nav.querySelector('ul');
    if (!h || !ul) return null;
    var b = document.createElement('button'); b.type = 'button';
    ul.id = ul.id || (h.id ? h.id + '-list' : '');
    if (ul.id) b.setAttribute('aria-controls', ul.id);
    b.addEventListener('click', function () { var open = b.getAttribute('aria-expanded') !== 'true'; b.setAttribute('aria-expanded', open); ul.hidden = !open; });
    return { nav: nav, h: h, ul: ul, b: b, text: h.textContent };
  }).filter(Boolean);
  function set() {
    parts.forEach(function (p) {
      if (Q.matches) {
        if (p.b.parentNode !== p.h) { p.b.textContent = p.text; p.h.textContent = ''; p.h.appendChild(p.b); }
        p.nav.classList.add('fold'); p.b.setAttribute('aria-expanded', 'false'); p.ul.hidden = true;
      } else {
        if (p.b.parentNode === p.h) p.h.textContent = p.text;
        p.nav.classList.remove('fold'); p.ul.hidden = false;
      }
    });
  }
  set(); if (Q.addEventListener) Q.addEventListener('change', set); else if (Q.addListener) Q.addListener(set);
})();
