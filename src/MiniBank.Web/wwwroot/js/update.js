(function(){
  const SWUpdate = {
    _available: false,
    _inited: false,
    _controllerChanged: false,
    async init(){
      if (this._inited) return; this._inited = true;
      if (!('serviceWorker' in navigator)) return;
      try {
        // Stamp current app version from version.json into localStorage
        try { await this._stampCurrentVersion(); } catch {}
        const reg = await navigator.serviceWorker.getRegistration();
        if (!reg) return;
        // Existing waiting worker means update ready
        if (reg.waiting) {
          this._available = true;
          try { localStorage.setItem('sw-update', 'true'); } catch {}
          document.dispatchEvent(new CustomEvent('sw-update-available'));
        }
        reg.addEventListener('updatefound', () => {
          const nw = reg.installing;
          if (!nw) return;
          nw.addEventListener('statechange', () => {
            if (nw.state === 'installed' && navigator.serviceWorker.controller) {
              this._available = true;
              try { localStorage.setItem('sw-update', 'true'); } catch {}
              document.dispatchEvent(new CustomEvent('sw-update-available'));
            }
          });
        });
        navigator.serviceWorker.addEventListener('controllerchange', () => {
          // New SW took control, reload to get fresh assets
          this._controllerChanged = true;
          location.reload();
        });
      } catch(e) { console.warn('SWUpdate.init failed', e); }
    },
    isAvailable(){
      return this._available || (typeof localStorage !== 'undefined' && localStorage.getItem('sw-update') === 'true');
    },
    async _stampCurrentVersion(){
      try {
        const url = `/version.json?t=${Date.now()}`;
        const resp = await fetch(url, { cache: 'no-store' });
        if (!resp.ok) return;
        const data = await resp.json();
        localStorage.setItem('app-version', data.version || '');
        localStorage.setItem('app-version-date', data.date || '');
        if (Array.isArray(data.notes)) {
          localStorage.setItem('app-version-notes', JSON.stringify(data.notes));
        }
      } catch {}
    },
    getLocalVersion(){
      return {
        version: localStorage.getItem('app-version') || '',
        date: localStorage.getItem('app-version-date') || '',
        notes: (() => { try { return JSON.parse(localStorage.getItem('app-version-notes')||'[]'); } catch { return []; } })()
      };
    },
    async getServerVersion(){
      try {
        const url = `/version.json?t=${Date.now()}`;
        const resp = await fetch(url, { cache: 'no-store' });
        if (!resp.ok) return null;
        const data = await resp.json();
        return { version: data.version||'', date: data.date||'', notes: Array.isArray(data.notes)?data.notes:[] };
      } catch { return null; }
    },
    async apply(){
      try {
        const reg = await navigator.serviceWorker.getRegistration();
        if (reg && reg.waiting) {
          reg.waiting.postMessage({ type: 'SKIP_WAITING' });
        } else if (reg) {
          await reg.update();
        }
        try { localStorage.removeItem('sw-update'); } catch {}
        // Fallback reload if controllerchange doesn't fire quickly
        setTimeout(() => {
          if (!this._controllerChanged) {
            location.reload();
          }
        }, 2000);
      } catch(e) { console.warn('SWUpdate.apply failed', e); }
    }
  };
  window.SWUpdate = SWUpdate;
  // Allow Blazor to register a callback for update availability
  SWUpdate.register = function(dotNetRef){
    try {
      document.addEventListener('sw-update-available', () => {
        if (dotNetRef && dotNetRef.invokeMethodAsync) {
          dotNetRef.invokeMethodAsync('OnUpdateAvailable');
        }
      });
      // If already available (e.g., page loaded after install), notify immediately
      if (SWUpdate.isAvailable()) {
        setTimeout(() => {
          if (dotNetRef && dotNetRef.invokeMethodAsync) {
            dotNetRef.invokeMethodAsync('OnUpdateAvailable');
          }
        }, 0);
      }
    } catch (e) { console.warn('SWUpdate.register failed', e); }
  }
  // Auto-apply updates when available so users always get the latest build
  document.addEventListener('sw-update-available', () => {
    try {
      // Apply shortly after event to allow any UI toast to render if present
      setTimeout(() => SWUpdate.apply(), 500);
    } catch (e) { console.warn('Auto-apply update failed', e); }
  });
  // Auto-init on DOM ready
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => SWUpdate.init());
  } else {
    SWUpdate.init();
  }
})();
