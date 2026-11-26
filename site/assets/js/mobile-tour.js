/* A portrait-sized reading of the real Windows app. Content and crops come from
   the existing six pages; the desktop presentation remains the source. */
(function () {
  'use strict';
  // the words it shows: T(text) reads the page's table (tools/site-i18n.mjs); ROOT leads from a translated page to the site
  var T = function (s) { var t = window.__i18n; return t && t[s] || s; }, ROOT = window.__root || '';
  window.CS2MobileTour = function (options) {
    var d = document, pages = options.pages, current = 0;
    var detailsAt = [0, 0, 2, 0, 2, 1];
    var summaries = [
      T('See the IP servers see. Change it and rejoin from one place.'),
      T('Find your server, check its players and ping, then join.'),
      T('Switch saved Steam accounts. Keep bans and cooldowns in view.'),
      T('Choose where CS2 can match you, with ping through your phone.'),
      T('Take your settings to another account. Keep a backup to undo.'),
      T('One template for your Steam profiles. Preview before applying.')
    ];
    var section = d.createElement('div');
    section.className = 'mobile-tour';
    section.innerHTML = '<div class="tour-tabs" role="tablist" aria-label="' + T('Explore the Windows app') + '"></div>' +
      '<div class="tour-panel" role="tabpanel" id="mobile-app-panel" tabindex="0">' +
      '<div class="tour-heading"><h3></h3><span class="tour-index" aria-hidden="true"></span></div>' +
      '<p class="tour-intro"></p><figure class="tour-detail"><div class="tour-crop"></div>' +
      '<figcaption><button type="button" class="tour-prev" aria-label="' + T('Previous detail') + '">←</button>' +
      '<span class="tour-caption" aria-live="polite"></span><button type="button" class="tour-next" aria-label="' + T('Next detail') + '">→</button></figcaption></figure>' +
      '<div class="tour-actions"><a class="zoom tour-full" href="' + ROOT + 'img/connection.png">' + T('Full screenshot') + ' <span aria-hidden="true">↗</span></a>' +
      '<button type="button" class="tour-more" aria-expanded="false" aria-controls="mobile-app-more">' + T('More details') + ' <span aria-hidden="true">+</span></button></div>' +
      '<div id="mobile-app-more" class="tour-more-content" hidden></div>' +
      '<p class="tour-note">' + T('Windows app · Example IPs and names') + '</p></div>';
    var find = function (s) { return section.querySelector(s); };
    var tabs = pages.map(function (li, i) {
      var b = d.createElement('button');
      b.type = 'button'; b.id = 'mobile-app-tab-' + i;
      b.textContent = li.querySelector('h3').textContent.replace(/^\s*\d+\s*/, '');
      b.setAttribute('role', 'tab'); b.setAttribute('aria-controls', 'mobile-app-panel');
      b.addEventListener('click', function () { options.onSelect(li.dataset.pg); });
      find('.tour-tabs').appendChild(b); return b;
    });
    find('.tour-tabs').addEventListener('keydown', function (e) {
      var i = e.key === 'Home' ? 0 : e.key === 'End' ? 5 :
        e.key === 'ArrowRight' ? (current + 1) % 6 : e.key === 'ArrowLeft' ? (current + 5) % 6 : -1;
      if (i < 0) return;
      e.preventDefault(); options.onSelect(pages[i].dataset.pg); tabs[i].focus({preventScroll:true});
    });
    function fitDetail() {
      // the marked part, its margin and some room round it, scaled to the box: never over 1.5x (the screenshot is 2x,
      // so it stays sharp on a 3x phone) and never cut (mobile-tour.css clips the rest away, so nothing next to the mark shows half)
      var crop = find('.tour-crop'), cut = crop.firstElementChild;
      if (!cut || !crop.clientWidth) return;
      var mw = parseFloat(cut.style.getPropertyValue('--mw')) || 1, mh = parseFloat(cut.style.getPropertyValue('--mh')) || 1;
      var s = Math.min(1.5, (crop.clientWidth - 40) / (mw + 20), (crop.clientHeight - 36) / (mh + 20));
      cut.style.setProperty('--s', Math.max(.3, s).toFixed(3));
      crop.setAttribute('role', 'img');
      crop.setAttribute('aria-label', T('Screenshot detail'));
    }
    function detail() {
      var keys = pages[current].querySelectorAll('.mk-key>li'), k = detailsAt[current], key = keys[k];
      var crop = find('.tour-crop'); crop.replaceChildren();
      var cut = key.querySelector('.cut');
      if (cut) {
        var copy = cut.cloneNode(true); copy.removeAttribute('aria-hidden');
        // A focused crop can show a card's identity without clipping its top
        // and bottom. Coordinates live beside the shared desktop marker.
        if (copy.dataset.crop) {
          var rect = copy.dataset.crop.split(',').map(Number);
          copy.style.setProperty('--cx', (rect[0] + rect[2] / 2) + 'px');
          copy.style.setProperty('--cy', (rect[1] + rect[3] / 2) + 'px');
          copy.style.setProperty('--mw', rect[2] + 'px');
          copy.style.setProperty('--mh', rect[3] + 'px');
        }
        var im = copy.querySelector('img'); im.alt = key.querySelector(':scope > span').textContent;
        im.loading = 'eager'; crop.appendChild(copy);
      }
      find('.tour-caption').textContent = (k + 1) + ' / ' + keys.length + ' · ' + key.querySelector(':scope > span').textContent;
      fitDetail();
    }
    function move(dir) { detailsAt[current] = (detailsAt[current] + dir + 3) % 3; detail(); }
    find('.tour-prev').addEventListener('click', function () { move(-1); });
    find('.tour-next').addEventListener('click', function () { move(1); });
    find('.tour-more').addEventListener('click', function () {
      var open = find('.tour-more-content').hidden;
      find('.tour-more-content').hidden = !open;
      find('.tour-more').setAttribute('aria-expanded', open);
      find('.tour-more').innerHTML = (open ? T('Less detail') : T('More details')) + ' <span aria-hidden="true">' + (open ? '−' : '+') + '</span>';
      options.onMeasure();
    });
    function select(pg) {
      current = Math.max(0, pages.findIndex(function (li) { return li.dataset.pg === pg; }));
      section.dataset.pg = pg;
      tabs.forEach(function (b, i) { b.setAttribute('aria-selected', i === current); b.tabIndex = i === current ? 0 : -1; });
      find('.tour-panel').setAttribute('aria-labelledby', tabs[current].id);
      find('h3').textContent = tabs[current].textContent;
      find('.tour-index').textContent = '0' + (current + 1) + ' / 06';
      find('.tour-intro').textContent = summaries[current];
      find('.tour-full').href = ROOT + 'img/' + pg + '.png';
      find('.tour-full').setAttribute('aria-label', T('Full screenshot') + ': ' + tabs[current].textContent);
      var more = find('.tour-more-content'); more.replaceChildren(); more.hidden = true;
      Array.from(pages[current].children).forEach(function (el) {
        if (el.tagName === 'H3') return;
        var copy = el.cloneNode(true); copy.querySelectorAll('.cut').forEach(function (c) { c.remove(); });
        more.appendChild(copy);
      });
      find('.tour-more').setAttribute('aria-expanded', 'false');
      find('.tour-more').innerHTML = T('More details') + ' <span aria-hidden="true">+</span>';
      detail();
    }
    options.fig.parentNode.appendChild(section);
    select('connection');
    if ('ResizeObserver' in window) new ResizeObserver(fitDetail).observe(find('.tour-crop'));
    else window.addEventListener('resize', fitDetail);
    return {element:section, select:select};
  };
})();
