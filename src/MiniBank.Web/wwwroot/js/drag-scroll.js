// Enable mouse/touch drag scrolling for elements with class .drag-scroll
(function(){
  'use strict';

  function enable(el){
    var isDown = false;
    var startX = 0;
    var scrollStart = 0;

    function onDown(e){
      isDown = true;
      el.classList.add('dragging');
      startX = (e.touches ? e.touches[0].clientX : e.clientX);
      scrollStart = el.scrollLeft;
      // Don't prevent default on touch to allow vertical scrolling
      if (!e.touches) e.preventDefault();
    }
    function onMove(e){
      if(!isDown) return;
      var x = (e.touches ? e.touches[0].clientX : e.clientX);
      var dx = x - startX;
      
      // Only prevent default and scroll horizontally if movement is primarily horizontal
      if (e.touches && Math.abs(dx) < 10) {
        // Small horizontal movement, allow vertical scrolling
        return;
      }
      
      // For RTL containers, scrollLeft usually becomes negative in some browsers. This approach works broadly.
      el.scrollLeft = scrollStart - dx;
      
      // Only prevent default for significant horizontal movement
      if (Math.abs(dx) > 5) {
        e.preventDefault();
      }
    }
    function onUp(){
      isDown = false;
      el.classList.remove('dragging');
    }

    el.addEventListener('mousedown', onDown, {passive:false});
    el.addEventListener('mousemove', onMove, {passive:false});
    window.addEventListener('mouseup', onUp);

    el.addEventListener('touchstart', onDown, {passive:false});
    el.addEventListener('touchmove', onMove, {passive:false});
    window.addEventListener('touchend', onUp);

    // Map vertical wheel to horizontal scroll for desktop mice
    el.addEventListener('wheel', function(e){
      // If user is scrolling mostly vertically, convert to horizontal scroll
      var delta = Math.abs(e.deltaY) > Math.abs(e.deltaX) ? e.deltaY : e.deltaX;
      if (delta !== 0) {
        e.preventDefault();
        el.scrollLeft += delta;
      }
    }, {passive:false});
  }

  window.addEventListener('DOMContentLoaded', function(){
    document.querySelectorAll('.drag-scroll').forEach(enable);
  });

  window.dashboardSumDots = window.dashboardSumDots || {};
  window.dashboardSumDots.bind = function(el, dotnetRef){
    if(!el || !dotnetRef) return;

    var lastIndex = -1;
    var ticking = false;

    function compute(){
      ticking = false;
      try{
        var cards = el.querySelectorAll('.sum-card');
        if(!cards || cards.length === 0) return;

        var hostRect = el.getBoundingClientRect();
        var hostCenter = hostRect.left + (hostRect.width / 2);

        var best = 0;
        var bestDist = Infinity;
        for(var i=0;i<cards.length;i++){
          var r = cards[i].getBoundingClientRect();
          var c = r.left + (r.width / 2);
          var d = Math.abs(c - hostCenter);
          if(d < bestDist){ bestDist = d; best = i; }
        }

        if(best !== lastIndex){
          lastIndex = best;
          dotnetRef.invokeMethodAsync('SetActiveSumIndex', best);
        }
      }catch(_e){ }
    }

    function onScroll(){
      if(ticking) return;
      ticking = true;
      window.requestAnimationFrame(compute);
    }

    el.addEventListener('scroll', onScroll, { passive: true });
    compute();
  };
})();
