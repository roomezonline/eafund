// Footer nav active-state sync for Blazor NavLink + <li> wrapper
(function(){
  function syncListItems(root){
    var items = root.querySelectorAll('.footer-nav .nav-item');
    items.forEach(function(li){
      var a = li.querySelector('.nav-link');
      if(!a) return;
      if(a.classList.contains('active')) li.classList.add('active');
      else li.classList.remove('active');
    });
  }

  function setupClickOptimistic(root){
    var links = root.querySelectorAll('.footer-nav .nav-link');
    links.forEach(function(a){
      a.addEventListener('click', function(){
        // Optimistically mark its parent active (do not prevent navigation)
        var li = a.closest('.nav-item');
        if(!li) return;
        root.querySelectorAll('.footer-nav .nav-item.active').forEach(function(x){ x.classList.remove('active'); });
        li.classList.add('active');
      });
    });
  }

  function observeNavLinkChanges(root){
    var container = root.querySelector('.footer-nav');
    if(!container) return;
    var mo = new MutationObserver(function(){ syncListItems(root); });
    mo.observe(container, { attributes: true, subtree: true, attributeFilter: ['class'] });
  }

  function init(){
    var root = document;
    syncListItems(root);
    setupClickOptimistic(root);
    observeNavLinkChanges(root);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
