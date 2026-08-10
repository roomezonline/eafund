(function () {
  function clamp(v, min, max) {
    return Math.max(min, Math.min(max, v));
  }

  function getEls(id) {
    var btn = document.getElementById(id + '-btn');
    var menu = document.getElementById(id + '-menu');
    return { btn: btn, menu: menu };
  }

  function position(id) {
    var els = getEls(id);
    if (!els.btn || !els.menu) return;

    var rect = els.btn.getBoundingClientRect();
    var menu = els.menu;

    // Ensure menu is visible and measured
    menu.style.position = 'fixed';
    menu.style.zIndex = '2000000';
    menu.style.margin = '0';

    // Match trigger width
    var width = rect.width;
    menu.style.minWidth = width + 'px';
    menu.style.width = width + 'px';

    // Default placement below
    var gap = 6;
    var top = rect.bottom + gap;
    var left = rect.left;

    // Clamp horizontally
    var vw = window.innerWidth || document.documentElement.clientWidth;
    left = clamp(left, 8, Math.max(8, vw - width - 8));

    // If overflow bottom, try above
    var vh = window.innerHeight || document.documentElement.clientHeight;
    var menuHeight = menu.offsetHeight || 240;
    if (top + menuHeight > vh - 8) {
      var aboveTop = rect.top - gap - menuHeight;
      if (aboveTop >= 8) top = aboveTop;
      else top = clamp(top, 8, vh - menuHeight - 8);
    }

    menu.style.left = left + 'px';
    menu.style.top = top + 'px';
  }

  function open(id) {
    position(id);

    // Reposition on scroll/resize while open
    var handler = function () { position(id); };
    close(id);
    window.addEventListener('scroll', handler, true);
    window.addEventListener('resize', handler, true);
    window.__dsDropdownFixedHandlers = window.__dsDropdownFixedHandlers || {};
    window.__dsDropdownFixedHandlers[id] = handler;
  }

  function close(id) {
    var map = window.__dsDropdownFixedHandlers;
    if (!map || !map[id]) return;
    var handler = map[id];
    window.removeEventListener('scroll', handler, true);
    window.removeEventListener('resize', handler, true);
    delete map[id];
  }

  window.dsDropdownFixed = {
    open: open,
    close: close,
    position: position
  };
})();
