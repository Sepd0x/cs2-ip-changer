/* A portrait-sized reading of the real Windows app. Content and crops come from
   the existing six pages; the desktop presentation remains the source. */
(function () {
  'use strict';
  window.CS2MobileTour = function (options) {
    var d = document, pages = options.pages, current = 0;
    var detailsAt = [0, 0, 2, 0, 2, 1];
    var summaries = [
      'See the IP servers see. Change it and rejoin from one place.',
      'Find your server, check its players and ping, then join.',
      'Switch saved Steam accounts. Keep bans and cooldowns in view.',
      'Choose where CS2 can match you, with ping through your phone.',
      'Take your settings to another account. Keep a backup to undo.',
      'One template for your Steam profiles. Preview before applying.'
    ];
    var section = d.createElement('div');
    section.className = 'mobile-tour';
    section.innerHTML = '<div class="tour-tabs" role="tablist" aria-label="Explore the Windows app"></div>' +
      '<div class="tour-panel" role="tabpanel" id="mobile-app-panel" tabindex="0">' +
      '<div class="tour-heading"><h3></h3><span class="tour-index" aria-hidden="true"></span></div>' +
      '<p class="tour-intro"></p><figure class="tour-detail"><div class="tour-crop"></div>' +
      '<figcaption><button type="button" class="tour-prev" aria-label="Previous detail">←</button>' +
      '<span class="tour-caption" aria-live="polite"></span><button type="button" class="tour-next" aria-label="Next detail">→</button></figcaption></figure>' +
      '<div class="tour-actions"><a class="zoom tour-full" href="img/connection.png">Full screenshot <span aria-hidden="true">↗</span></a>' +
      '<button type="button" class="tour-more" aria-expanded="false" aria-controls="mobile-app-more">More details <span aria-hidden="true">+</span></button></div>' +
      '<div id="mobile-app-more" class="tour-more-content" hidden></div>' +
      '<p class="tour-note">Windows app · Example IPs and names</p></div>';
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
      var crop = find('.tour-crop'), cut = crop.firstElementChild;
      if (!cut || !crop.clientWidth) return;
      var width = parseFloat(cut.style.getPropertyValue('--mw')) + 32;
      var wide = width > crop.clientWidth;
      crop.tabIndex = wide ? 0 : -1;
      crop.setAttribute('role', 'region');
      crop.setAttribute('aria-label', wide ? 'Screenshot detail. Scroll horizontally to see the full row.' : 'Screenshot detail');
      find('.tour-pan').hidden = !wide;
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
      var hint = d.createElement('small'); hint.className = 'tour-pan'; hint.textContent = 'Scroll image sideways ↔'; hint.hidden = true;
      find('.tour-caption').appendChild(hint);
      fitDetail(); crop.scrollLeft = 0;
    }
    function move(dir) { detailsAt[current] = (detailsAt[current] + dir + 3) % 3; detail(); }
    find('.tour-prev').addEventListener('click', function () { move(-1); });
    find('.tour-next').addEventListener('click', function () { move(1); });
    find('.tour-more').addEventListener('click', function () {
      var open = find('.tour-more-content').hidden;
      find('.tour-more-content').hidden = !open;
      find('.tour-more').setAttribute('aria-expanded', open);
      find('.tour-more').innerHTML = (open ? 'Less detail' : 'More details') + ' <span aria-hidden="true">' + (open ? '−' : '+') + '</span>';
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
      find('.tour-full').href = 'img/' + pg + '.png';
      find('.tour-full').setAttribute('aria-label', 'Full screenshot: ' + tabs[current].textContent);
      var more = find('.tour-more-content'); more.replaceChildren(); more.hidden = true;
      Array.from(pages[current].children).forEach(function (el) {
        if (el.tagName === 'H3') return;
        var copy = el.cloneNode(true); copy.querySelectorAll('.cut').forEach(function (c) { c.remove(); });
        more.appendChild(copy);
      });
      find('.tour-more').setAttribute('aria-expanded', 'false');
      find('.tour-more').innerHTML = 'More details <span aria-hidden="true">+</span>';
      detail();
    }
    options.fig.parentNode.appendChild(section);
    select('connection');
    if ('ResizeObserver' in window) new ResizeObserver(fitDetail).observe(find('.tour-crop'));
    else window.addEventListener('resize', fitDetail);
    return {element:section, select:select};
  };
})();
