// Completely disable pull-to-refresh and fix scroll issues
(function() {
  'use strict';

  // Aggressive approach: disable all touch interference
  document.documentElement.style.overscrollBehaviorY = 'none';
  document.body.style.overscrollBehaviorY = 'none';
  document.documentElement.style.touchAction = 'pan-y';
  document.body.style.touchAction = 'pan-y';

  // Remove any existing touch event listeners that might interfere
  const originalAddEventListener = EventTarget.prototype.addEventListener;
  EventTarget.prototype.addEventListener = function(type, listener, options) {
    // Block problematic touch events from other scripts
    if (type === 'touchstart' || type === 'touchmove') {
      return; // Don't add the listener
    }
    return originalAddEventListener.call(this, type, listener, options);
  };

  // Force enable scrolling
  document.addEventListener('DOMContentLoaded', function() {
    document.body.style.overflow = 'auto';
    document.documentElement.style.overflow = 'auto';
    
    // Remove any CSS that might block scrolling
    const style = document.createElement('style');
    style.textContent = `
      * { 
        -webkit-overflow-scrolling: touch !important;
        overscroll-behavior: none !important;
      }
      html, body {
        overflow: auto !important;
        height: auto !important;
        min-height: 100vh !important;
      }
    `;
    document.head.appendChild(style);
  });

  console.log('Scroll fixed and pull-to-refresh disabled');
})();
