(function(){
  function setupContentScroller(){
    var content = document.querySelector('.content');
    if (!content) return;

    // Simple approach: just ensure content is the scroll container
    content.style.overscrollBehaviorY = 'contain';
    
    // Force scroll to top on setup
    content.scrollTop = 0;
  }

  // Expose a helper to scroll content to top on navigation
  window.scrollContentTop = function(){
    var content = document.querySelector('.content');
    if (content) {
      try { content.scrollTo({ top: 0, behavior: 'auto' }); } catch(_) { content.scrollTop = 0; }
    }
  };

  // Initialize on load (and after Blazor reconnects)
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', setupContentScroller);
  } else {
    setupContentScroller();
  }

  // Also re-setup after soft navigations if available
  window.addEventListener('blazor:navigation', function(){
    setTimeout(setupContentScroller, 0);
  });
})();
