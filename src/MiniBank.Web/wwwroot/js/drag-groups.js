// Enable mouse drag-to-scroll for dashboard group rows on desktop
(function(){
  'use strict';
  function enableDrag(el){
    let isDown = false; let startX = 0; let startScroll = 0; let dragged = false;
    const THRESHOLD = 6; // px before we treat as drag to avoid killing clicks

    el.addEventListener('pointerdown', (e) => {
      // Desktop-only drag: allow only mouse pointer and primary button
      if (e.pointerType !== 'mouse' || (e.buttons !== undefined && e.buttons !== 1)) return;
      isDown = true; dragged = false;
      startX = e.clientX;
      startScroll = el.scrollLeft;
      el.classList.add('dragging');
    });
    el.addEventListener('pointermove', (e) => {
      if (!isDown) return;
      if (e.pointerType !== 'mouse') return;
      const dx = e.clientX - startX;
      if (Math.abs(dx) < THRESHOLD) return; // ignore tiny moves => keep click behavior
      dragged = true;
      el.classList.add('was-dragged');
      el.scrollLeft = startScroll - dx;
      // prevent text selection and click when actually dragging
      e.preventDefault();
    }, { passive: false });
    function end(){ isDown = false; el.classList.remove('dragging'); }
    el.addEventListener('pointerup', end);
    el.addEventListener('pointercancel', end);
    el.addEventListener('pointerleave', end);

    // Suppress click after a drag gesture so links don't trigger
    el.addEventListener('click', (e) => {
      if (el.classList.contains('was-dragged')) {
        e.preventDefault();
        e.stopPropagation();
        // clear the flag shortly after to allow normal clicks next time
        setTimeout(() => el.classList.remove('was-dragged'), 0);
      }
    }, true);

    // Make vertical wheel scroll translate to horizontal when possible (desktop convenience)
    el.addEventListener('wheel', (e) => {
      if (Math.abs(e.deltaX) < Math.abs(e.deltaY)) {
        el.scrollLeft += e.deltaY;
        e.preventDefault();
      }
    }, { passive: false });
  }
  function init(){
    document.querySelectorAll('.dashboard-group .group-row').forEach((el) => { if (!el._dragBound){ enableDrag(el); el._dragBound = true; }});
    // Observe future group rows
    const mo = new MutationObserver(() => {
      document.querySelectorAll('.dashboard-group .group-row').forEach((el) => {
        if (!el._dragBound){ enableDrag(el); el._dragBound = true; }
      });
    });
    mo.observe(document.body, { childList: true, subtree: true });
  }
  if (document.readyState === 'loading')
    document.addEventListener('DOMContentLoaded', init);
  else init();
})();
