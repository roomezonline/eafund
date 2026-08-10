// Theme initialization and toggle with persistence
(function () {
  'use strict';

  var KEY = 'mb.theme';

  function getPreferred() {
    var saved = localStorage.getItem(KEY);
    if (saved === 'light' || saved === 'dark') return saved;
    // System preference fallback
    try {
      return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    } catch (_) {
      return 'dark';
    }
  }

  function applyTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme);
  }

  function setTheme(theme) {
    localStorage.setItem(KEY, theme);
    applyTheme(theme);
    updateToggleLabel();
  }

  function toggleTheme() {
    var cur = document.documentElement.getAttribute('data-theme') || getPreferred();
    setTheme(cur === 'dark' ? 'light' : 'dark');
  }

  function updateToggleLabel() {
    var el = document.getElementById('theme-toggle');
    if (!el) return;
    var theme = document.documentElement.getAttribute('data-theme') || 'dark';
    var isDark = theme === 'dark';
    // Icon-only (no text): sun for dark (to switch to light), moon for light (to switch to dark)
    el.innerHTML = isDark ? '<i class="fa-solid fa-sun"></i>' : '<i class="fa-solid fa-moon"></i>';
    el.setAttribute('aria-label', isDark ? 'Switch to light theme' : 'Switch to dark theme');
  }

  // Expose for Blazor or other scripts if needed
  window.MBTheme = { setTheme: setTheme, toggleTheme: toggleTheme };

  // Event delegation so it survives Blazor re-renders
  document.addEventListener('click', function (e) {
    var t = e.target;
    if (!t) return;
    var btn = t.closest && t.closest('#theme-toggle');
    if (btn) {
      e.preventDefault();
      toggleTheme();
    }
    var menuItem = t.closest && t.closest('#menu-theme-toggle');
    if (menuItem) {
      e.preventDefault();
      toggleTheme();
    }
  }, true);

  // Keep the label in sync on initial load and when the button is re-created
  window.addEventListener('DOMContentLoaded', function () {
    updateToggleLabel();
    // A short period of polling to catch first Blazor render swaps
    var tries = 0;
    var iv = setInterval(function(){
      updateToggleLabel();
      tries++;
      if (tries > 10) clearInterval(iv);
    }, 200);
  });

})();
