// Enhance duo-item hover with ripple origin based on pointer position
(function () {
  function setVars(el, x, y) {
    el.style.setProperty('--rx', x + 'px');
    el.style.setProperty('--ry', y + 'px');
  }
  document.addEventListener('mousemove', function (e) {
    var card = e.target && e.target.closest && e.target.closest('.duo-item');
    if (!card) return;
    var rect = card.getBoundingClientRect();
    setVars(card, e.clientX - rect.left, e.clientY - rect.top);
  }, { passive: true });

  document.addEventListener('touchstart', function (e) {
    var t = e.touches && e.touches[0];
    if (!t) return;
    var card = document.elementFromPoint(t.clientX, t.clientY);
    if (card && card.closest) card = card.closest('.duo-item');
    if (!card) return;
    var rect = card.getBoundingClientRect();
    setVars(card, t.clientX - rect.left, t.clientY - rect.top);
  }, { passive: true });
})();
