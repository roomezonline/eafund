window.appNav = (function(){
  let barrierUrl = '/admin/dashboard';
  let barrierEnabled = false;
  let popHandlerAttached = false;
  const backClosers = new Set(); // allow components to register custom back-closers

  function ensureBarrier() {
    if (!barrierEnabled) return;
    try { history.pushState({ barrier: true }, '', location.href); } catch {}
  }

  function dispatchEscape(){
    try {
      const evt = new KeyboardEvent('keydown', { key: 'Escape', keyCode: 27, which: 27, bubbles: true, cancelable: true });
      document.dispatchEvent(evt);
    } catch {}
  }

  function closeMenus(){
    try { document.querySelectorAll('details.header-menu[open]').forEach(d => d.open = false); } catch {}
    try { document.querySelectorAll('details[open]').forEach(d => d.open = false); } catch {}
  }

  function closeSheets(){
    let closed = false;
    try {
      // Try to click explicit cancel buttons on custom iOS sheets
      document.querySelectorAll('.ios-sheet-overlay.visible .ios-cancel').forEach(btn => { btn.click(); closed = true; });
    } catch {}
    return closed;
  }

  function closeMudDialogsAndPopovers(){
    // Many UI libs close on Escape; also check overlays to decide whether to consume back
    const hasMud = !!document.querySelector('.mud-overlay, .mud-dialog, .mud-popover, .mud-menu');
    if (hasMud) dispatchEscape();
    return hasMud;
  }

  function runRegisteredClosers(){
    let consumed = false;
    backClosers.forEach(fn => {
      try { if (fn && fn() === true) consumed = true; } catch {}
    });
    return consumed;
  }

  function consumeBackIfUIOpen(){
    let consumed = false;
    try {
      closeMenus();
      const closedSheet = closeSheets();
      const closedMud = closeMudDialogsAndPopovers();
      const closedCustom = runRegisteredClosers();
      consumed = closedSheet || closedMud || closedCustom;
    } catch {}
    return consumed;
  }

  function cancelBackNavigation(){
    try { history.pushState({ stay: true }, '', location.href); } catch {}
  }

  function popHandler(ev) {
    // First priority: if any menu/dialog/sheet is open, close it and cancel navigation
    if (consumeBackIfUIOpen()) { cancelBackNavigation(); return; }

    // Barrier behavior when at dashboard
    if (barrierEnabled) {
      try {
        const path = location.pathname;
        if (path === barrierUrl) {
          cancelBackNavigation();
          return;
        }
      } catch {}
    }
    // Otherwise allow normal back
  }

  function attachPopHandler(){
    if (popHandlerAttached) return;
    window.addEventListener('popstate', popHandler);
    popHandlerAttached = true;
  }

  // Close three-dot menu after clicking any menu item (present or future)
  document.addEventListener('click', function(e){
    try {
      var t = e.target;
      if (!t) return;
      var item = t.closest && t.closest('details.header-menu .menu-item');
      if (item) setTimeout(closeMenus, 0);
    } catch {}
  }, true);

  // Close header-menu when clicking/tapping outside of it
  function outsideMenuCloser(ev){
    try {
      var t = ev.target;
      if (!t) return;
      var isInsideMenu = t.closest && t.closest('details.header-menu');
      if (isInsideMenu) return; // clicks inside menu are handled elsewhere
      var opens = document.querySelectorAll('details.header-menu[open]');
      if (opens && opens.length) opens.forEach(function(d){ d.open = false; });
    } catch {}
  }
  document.addEventListener('click', outsideMenuCloser, true);
  document.addEventListener('touchstart', outsideMenuCloser, true);

  // Attach global back handler immediately for all pages
  attachPopHandler();

  return {
    goBack: function () {
      if (history.length > 1) history.back();
      else window.location.href = '/';
    },
    exitApp: function(){
      closeMenus();
      try { history.pushState({ exit: true }, '', '/'); } catch {}
      try { window.close(); } catch {}
      try { location.replace('/'); } catch { location.href = '/'; }
    },
    onNavigated: function(currentUrl){
      try {
        const a = document.createElement('a');
        a.href = currentUrl;
        const path = a.pathname || currentUrl;
        if (path === barrierUrl) {
          barrierEnabled = true;
          ensureBarrier();
        }
      } catch {}
    },
    setBarrierUrl: function(url){ barrierUrl = url || '/admin/dashboard'; },
    closeMenus: closeMenus,
    // Components can register a closer: it should close its UI and return true if it consumed back
    registerBackCloser: function(fn){ if (fn) backClosers.add(fn); },
    unregisterBackCloser: function(fn){ if (fn) backClosers.delete(fn); },
    // Scroll helpers
    scrollSave: function(){ try { return window.scrollY || window.pageYOffset || 0; } catch { return 0; } },
    scrollRestore: function(y){ try { window.scrollTo({ top: y || 0, behavior: 'auto' }); } catch {} },
    scrollToId: function(id){ try { var el = document.getElementById(id); if (el) el.scrollIntoView({ behavior: 'auto', block: 'center' }); } catch {} },
    scrollToIdSmooth: function(id){ try { var el = document.getElementById(id); if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' }); } catch {} },
    scrollToIdSmoothRetry: function(id, tries, delayMs){
      var remaining = (typeof tries === 'number' && tries > 0) ? tries : 8;
      var delay = (typeof delayMs === 'number' && delayMs >= 0) ? delayMs : 60;
      function attempt(){
        try {
          var el = document.getElementById(id);
          if (el) { el.scrollIntoView({ behavior: 'smooth', block: 'start' }); return; }
        } catch {}
        remaining -= 1;
        if (remaining <= 0) return;
        try { setTimeout(attempt, delay); } catch {}
      }
      attempt();
    },
    scrollToIdSmoothRetryOffset: function(id, offsetTop, tries, delayMs){
      var remaining = (typeof tries === 'number' && tries > 0) ? tries : 10;
      var delay = (typeof delayMs === 'number' && delayMs >= 0) ? delayMs : 60;
      var offset = (typeof offsetTop === 'number') ? offsetTop : 0;
      function attempt(){
        try {
          var el = document.getElementById(id);
          if (el) {
            var rect = el.getBoundingClientRect();
            var top = (window.scrollY || window.pageYOffset || 0) + rect.top - offset;
            if (top < 0) top = 0;
            window.scrollTo({ top: top, behavior: 'smooth' });
            return;
          }
        } catch {}
        remaining -= 1;
        if (remaining <= 0) return;
        try { setTimeout(attempt, delay); } catch {}
      }
      attempt();
    }
  };
})();

// Browser-side auth helper (Blazor Server safe): sets auth cookie on the client
window.authApi = window.authApi || {};
window.authApi.login = async function(username, password){
  try {
    const resp = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ Username: username || '', Password: password || '' })
    });

    let data = null;
    try { data = await resp.json(); } catch {}
    const error = (data && data.error) ? String(data.error) : '';
    const debug = (data && data.debug) ? data.debug : null;
    return { ok: resp.ok, status: resp.status, error: error, debug: debug };
  } catch (e) {
    return { ok: false, status: 0, error: 'NETWORK' };
  }
};

// Use browser navigation directly (safe when Blazor circuit is about to disconnect)
window.authApi.goto = function(url){
  try {
    const u = (typeof url === 'string' && url.trim()) ? url.trim() : '/';
    window.location.assign(u);
  } catch {
    try { window.location.href = '/'; } catch {}
  }
};

// Global function to blur active element (close mobile keyboard)
window.blurActiveElement = function() {
  try {
    if (document.activeElement && document.activeElement.blur) {
      document.activeElement.blur();
    }
  } catch (e) {
    console.log('Could not blur active element:', e);
  }
};
