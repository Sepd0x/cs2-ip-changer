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
    var target = document.querySelector(link.getAttribute('href'));
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
