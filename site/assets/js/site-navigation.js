/* Native details remains usable without JavaScript. Close on navigation,
   Escape, outside interaction, and the return to a desktop header. */
(function () {
  'use strict';
  var menu = document.querySelector('.site-menu');
  if (!menu) return;
  var trigger = menu.querySelector('summary');
  menu.addEventListener('keydown', function (event) {
    if (event.key === 'Escape' && menu.open) {
      event.preventDefault(); menu.open = false; trigger.focus();
    }
  });
  menu.addEventListener('click', function (event) {
    var link = event.target.closest('a');
    if (!link) return;
    menu.open = false;
    // only a link to a part of this page ("#how"): the language links ("pt-br/", "./") are not selectors (audit 26 Nov 2026, S7)
    var href = link.getAttribute('href') || '';
    var target = href.charAt(0) === '#' ? document.getElementById(href.slice(1)) : null;
    if (target) {
      var heading = target.querySelector('h2,h3') || target;
      heading.setAttribute('tabindex', '-1');
      requestAnimationFrame(function () { heading.focus({preventScroll:true}); });
    }
  });
  document.addEventListener('pointerdown', function (event) {
    if (menu.open && !menu.contains(event.target)) menu.open = false;
  });
  document.addEventListener('focusin', function (event) {
    if (menu.open && !menu.contains(event.target)) menu.open = false;
  });
  matchMedia('(min-width:1000px)').addEventListener('change', function (event) {
    if (event.matches) menu.open = false;
  });
})();
