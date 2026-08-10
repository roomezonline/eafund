// Normalize Persian/Arabic digits to Latin (ASCII) across the entire app
// - Converts Eastern Arabic-Indic (٠١٢٣٤٥٦٧٨٩) and Persian (۰۱۲۳۴۵۶۷۸۹) digits to 0-9
// - Works for existing and future forms via event delegation
(function () {
  'use strict';

  var map = {
    '۰': '0','۱': '1','۲': '2','۳': '3','۴': '4','۵': '5','۶': '6','۷': '7','۸': '8','۹': '9',
    '٠': '0','١': '1','٢': '2','٣': '3','٤': '4','٥': '5','٦': '6','٧': '7','٨': '8','٩': '9'
  };
  var rx = /[۰-۹٠-٩]/g;

  function toLatinDigits(str) {
    if (!str || typeof str !== 'string') return str;
    return str.replace(rx, function (ch) { return map[ch] || ch; });
  }

  function normalizeElement(el) {
    if (!el || el.readOnly || el.disabled) return;
    var t = (el.type || '').toLowerCase();
    if (['text','search','tel','number','email','url','password','date','datetime-local'].indexOf(t) !== -1 || el.tagName === 'TEXTAREA') {
      var v = el.value;
      var nv = toLatinDigits(v);
      if (nv !== v) el.value = nv;
    }
  }

  // Normalize on input for interactive feedback
  document.addEventListener('input', function (e) {
    normalizeElement(e.target);
  }, true);

  // Normalize on change and blur as safety
  document.addEventListener('change', function (e) {
    normalizeElement(e.target);
  }, true);
  document.addEventListener('blur', function (e) {
    normalizeElement(e.target);
  }, true);

  // Normalize all inputs before any form submission
  document.addEventListener('submit', function (e) {
    try {
      var form = e.target;
      var elements = form.querySelectorAll('input, textarea');
      elements.forEach(normalizeElement);
    } catch (_) {}
  }, true);

  // Normalize any pre-filled values on page load (inputs only)
  function normalizeAllInputs(root) {
    var elements = (root || document).querySelectorAll('input, textarea');
    elements.forEach(normalizeElement);
  }

  // Initialize on DOM ready (inputs only). Avoid touching text nodes to not break Blazor DOM diffing.
  window.addEventListener('DOMContentLoaded', function () {
    normalizeAllInputs();

    // Normalize digits for display-only blocks marked with .latin-digits (scoped, safe)
    function normalizeLatinBlocks(root) {
      var scope = (root || document);
      var blocks = scope.querySelectorAll ? scope.querySelectorAll('.latin-digits') : [];
      blocks.forEach(function (blk) {
        try {
          var walker = document.createTreeWalker(blk, NodeFilter.SHOW_TEXT, {
            acceptNode: function (node) {
              if (!node.nodeValue || !/[\u06F0-\u06F9\u0660-\u0669]/.test(node.nodeValue)) return NodeFilter.FILTER_REJECT;
              var p = node.parentNode; if (!p) return NodeFilter.FILTER_REJECT;
              var tag = p.tagName; if (tag === 'SCRIPT' || tag === 'STYLE') return NodeFilter.FILTER_REJECT;
              return NodeFilter.FILTER_ACCEPT;
            }
          });
          var n; while ((n = walker.nextNode())) { var v = n.nodeValue; var nv = toLatinDigits(v); if (nv !== v) n.nodeValue = nv; }
        } catch (_) {}
      });
    }

    normalizeLatinBlocks();

    // Observe only for additions of .latin-digits blocks to keep numbers Latin in dynamic content
    try {
      var obs = new MutationObserver(function (mutations) {
        for (var i = 0; i < mutations.length; i++) {
          var m = mutations[i];
          if (m.type === 'childList' && m.addedNodes && m.addedNodes.length) {
            m.addedNodes.forEach(function (node) {
              if (node.nodeType === Node.ELEMENT_NODE) {
                if (node.matches && node.matches('.latin-digits, .latin-digits *')) {
                  var el = node.matches('.latin-digits') ? node : node.closest('.latin-digits');
                  normalizeLatinBlocks(el);
                }
              }
            });
          }
        }
      });
      obs.observe(document.body || document.documentElement, { subtree: true, childList: true });
    } catch (_) {}
  });
})();
