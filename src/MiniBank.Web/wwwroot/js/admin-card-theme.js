(function(){
  'use strict';

  var KEY = 'mb.adminCardTheme';

  function safeParse(raw){
    try { return raw ? JSON.parse(raw) : null; } catch { return null; }
  }

  function normalizeTheme(t){
    t = t && typeof t === 'object' ? t : {};
    return {
      grad1: (typeof t.grad1 === 'string' && t.grad1.trim()) ? t.grad1.trim() : '#667eea',
      grad2: (typeof t.grad2 === 'string' && t.grad2.trim()) ? t.grad2.trim() : '#6f5bd3',
      grad3: (typeof t.grad3 === 'string' && t.grad3.trim()) ? t.grad3.trim() : '#764ba2',
      text: (typeof t.text === 'string' && t.text.trim()) ? t.text.trim() : '#ffffff'
    };
  }

  function applyTheme(t){
    t = normalizeTheme(t);
    try {
      var r = document.documentElement;
      r.style.setProperty('--admin-card-grad-1', t.grad1);
      r.style.setProperty('--admin-card-grad-2', t.grad2);
      r.style.setProperty('--admin-card-grad-3', t.grad3);
      r.style.setProperty('--admin-card-text', t.text);
    } catch {}
    return t;
  }

  function load(){
    var t = normalizeTheme(safeParse(localStorage.getItem(KEY)));
    applyTheme(t);
    return t;
  }

  function save(t){
    t = normalizeTheme(t);
    try { localStorage.setItem(KEY, JSON.stringify(t)); } catch {}
    applyTheme(t);
    return t;
  }

  function reset(){
    try { localStorage.removeItem(KEY); } catch {}
    return applyTheme(null);
  }

  window.MBAdminCardTheme = { load: load, save: save, reset: reset, apply: applyTheme };

  try {
    window.addEventListener('DOMContentLoaded', function(){
      load();
    });
  } catch {}
})();
