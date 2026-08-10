(function(){
  function safeStr(v){
    return (v === null || v === undefined) ? '' : String(v);
  }

  async function copyText(text){
    const t = safeStr(text);
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        await navigator.clipboard.writeText(t);
        return true;
      }
    } catch(e) {}

    // fallback
    try {
      const ta = document.createElement('textarea');
      ta.value = t;
      ta.style.position = 'fixed';
      ta.style.top = '-1000px';
      ta.style.left = '-1000px';
      document.body.appendChild(ta);
      ta.focus();
      ta.select();
      const ok = document.execCommand('copy');
      document.body.removeChild(ta);
      return ok;
    } catch(e) {
      return false;
    }
  }

  async function shareText(title, text){
    const payload = { title: safeStr(title), text: safeStr(text) };
    try {
      if (navigator.share) {
        await navigator.share(payload);
        return true;
      }
    } catch(e) {
      // user cancelled or not supported
    }

    // fallback: copy
    await copyText(payload.text);
    return false;
  }

  function drawRoundedRect(ctx, x, y, w, h, r){
    const radius = Math.min(r, w/2, h/2);
    ctx.beginPath();
    ctx.moveTo(x + radius, y);
    ctx.arcTo(x + w, y, x + w, y + h, radius);
    ctx.arcTo(x + w, y + h, x, y + h, radius);
    ctx.arcTo(x, y + h, x, y, radius);
    ctx.arcTo(x, y, x + w, y, radius);
    ctx.closePath();
  }

  function wrapText(ctx, text, x, y, maxWidth, lineHeight, maxLines){
    const words = safeStr(text).split(/\s+/).filter(Boolean);
    let line = '';
    let lines = 0;
    for (let n = 0; n < words.length; n++) {
      const testLine = line ? line + ' ' + words[n] : words[n];
      const metrics = ctx.measureText(testLine);
      if (metrics.width > maxWidth && line) {
        ctx.fillText(line, x, y);
        y += lineHeight;
        lines++;
        if (maxLines && lines >= maxLines) {
          ctx.fillText('…', x, y);
          return y + lineHeight;
        }
        line = words[n];
      } else {
        line = testLine;
      }
    }
    if (line) {
      ctx.fillText(line, x, y);
      y += lineHeight;
    }
    return y;
  }

  async function shareTransactionImage(fundName, operation, amount, memberName, dateTime, description, txId, journalId){
    const W = 900;
    const H = 640;
    const canvas = document.createElement('canvas');
    canvas.width = W;
    canvas.height = H;
    const ctx = canvas.getContext('2d');

    // Background (soft)
    const g = ctx.createLinearGradient(0, 0, W, H);
    g.addColorStop(0, '#f8fafc');
    g.addColorStop(0.5, '#f1f5f9');
    g.addColorStop(1, '#ffffff');
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, W, H);

    // Top ribbon
    const r = ctx.createLinearGradient(0, 0, W, 0);
    r.addColorStop(0, '#0ea5e9');
    r.addColorStop(0.5, '#7c3aed');
    r.addColorStop(1, '#f59e0b');
    ctx.fillStyle = r;
    ctx.fillRect(0, 0, W, 14);

    // Card
    const pad = 40;
    const cardX = pad;
    const cardY = pad + 10;
    const cardW = W - pad*2;
    const cardH = H - pad*2 - 40;

    ctx.save();
    ctx.shadowColor = 'rgba(15,23,42,.12)';
    ctx.shadowBlur = 30;
    ctx.shadowOffsetY = 12;
    ctx.fillStyle = '#ffffff';
    drawRoundedRect(ctx, cardX, cardY, cardW, cardH, 32);
    ctx.fill();
    ctx.restore();

    // Load and draw logo
    try {
      const logoImg = new Image();
      logoImg.src = '/icons/logo_emam_ali.png';
      await new Promise((resolve, reject) => {
        logoImg.onload = resolve;
        logoImg.onerror = reject;
      });
      ctx.drawImage(logoImg, cardX + cardW - 100, cardY + 30, 72, 72);
    } catch (e) {
      console.error('Failed to load logo for receipt image', e);
    }

    // Header
    ctx.fillStyle = '#0f172a';
    ctx.font = '950 32px Vazir, Segoe UI, Tahoma, Arial';
    ctx.textAlign = 'right';
    ctx.fillText(safeStr(fundName), cardX + cardW - 115, cardY + 62);

    ctx.fillStyle = '#64748b';
    ctx.font = '900 20px Vazir, Segoe UI, Tahoma, Arial';
    ctx.fillText('رسید تراکنش', cardX + cardW - 115, cardY + 96);

    // Date badge (top-left)
    ctx.save();
    ctx.fillStyle = '#f8fafc';
    ctx.strokeStyle = '#e2e8f0';
    ctx.lineWidth = 1.5;
    drawRoundedRect(ctx, cardX + 30, cardY + 30, 260, 46, 14);
    ctx.fill();
    ctx.stroke();
    ctx.fillStyle = '#1e293b';
    ctx.font = '900 18px Vazir, Segoe UI, Tahoma, Arial';
    ctx.textAlign = 'center';
    ctx.fillText(safeStr(dateTime), cardX + 30 + 130, cardY + 60);
    ctx.restore();

    // Amount Section
    const rowLeft = cardX + 30;
    const rowRight = cardX + cardW - 30;

    // Operation Type (Centered Badge)
    ctx.save();
    const opText = safeStr(operation);
    ctx.font = '950 24px Vazir, Segoe UI, Tahoma, Arial';
    const opWidth = ctx.measureText(opText).width;
    const badgeW = opWidth + 60;
    const badgeH = 46;
    const badgeX = W/2 - badgeW/2;
    const badgeY = cardY + 140;
    
    ctx.fillStyle = 'rgba(14,165,233,.08)';
    drawRoundedRect(ctx, badgeX, badgeY, badgeW, badgeH, 23);
    ctx.fill();
    
    ctx.fillStyle = '#0ea5e9';
    ctx.textAlign = 'center';
    ctx.fillText(opText, W/2, badgeY + 32);
    ctx.restore();

    let y = badgeY + 100;

    function row(label, value, isLong = false, isAmount = false){
      ctx.fillStyle = '#94a3b8';
      ctx.font = '900 20px Vazir, Segoe UI, Tahoma, Arial';
      ctx.textAlign = 'right';
      ctx.fillText(label, rowRight, y);

      if (isAmount) {
        // Amount Value + Rial (always render a single Rial, and ensure order: 1000 ریال)
        const rawAmt = safeStr(value);
        const amtOnly = rawAmt
          .replace(/\s*ریال\s*/g, ' ')
          .replace(/\s+/g, ' ')
          .trim();
        const unitStr = 'ریال';

        // Rial must be visually on the left side of the numeric amount in the image
        ctx.textAlign = 'left';

        // Draw Rial unit first at the left edge
        ctx.fillStyle = '#64748b';
        ctx.font = '900 18px Vazir, Segoe UI, Tahoma, Arial';
        ctx.fillText(unitStr, rowLeft, y + 2);

        // Draw amount number right after Rial with spacing
        const unitWidth = ctx.measureText(unitStr).width;
        ctx.fillStyle = '#0f172a';
        ctx.font = '950 28px Vazir, Segoe UI, Tahoma, Arial';
        ctx.fillText(amtOnly, rowLeft + unitWidth + 10, y + 2);
      } else {
        ctx.fillStyle = '#0f172a';
        ctx.font = '950 20px Vazir, Segoe UI, Tahoma, Arial';
        ctx.textAlign = 'left';
        
        if (isLong) {
          ctx.textAlign = 'right';
          y += 35;
          y = wrapText(ctx, safeStr(value), rowRight, y, cardW - 60, 32, 3);
        } else {
          ctx.fillText(safeStr(value), rowLeft, y);
        }
      }

      if (!isLong) y += 55;

      ctx.strokeStyle = '#f1f5f9';
      ctx.lineWidth = 2;
      ctx.beginPath();
      ctx.moveTo(cardX + 30, y - 15);
      ctx.lineTo(cardX + cardW - 30, y - 15);
      ctx.stroke();
      y += 15;
    }

    row('مبلغ', amount, false, true);
    row('عضو', memberName);

    const tx = safeStr(txId).trim();
    const j = safeStr(journalId).trim();
    if (tx) row('شناسه تراکنش', tx);
    if (j) row('شناسه سند', j);
    
    if (safeStr(description).trim()) {
        row('توضیحات', description, true);
    }

    // Footer
    ctx.fillStyle = '#94a3b8';
    ctx.font = '900 18px Vazir, Segoe UI, Tahoma, Arial';
    ctx.textAlign = 'center';
    ctx.fillText('سامانه مالی امام علی (ع)', W/2, H - 35);

    const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png', 0.92));
    if (!blob) return false;

    const file = new File([blob], 'transaction.png', { type: 'image/png' });

    // Try Web Share with files
    try {
      if (navigator.canShare && navigator.canShare({ files: [file] }) && navigator.share) {
        await navigator.share({ title: safeStr(fundName), files: [file] });
        return true;
      }
    } catch(e) {}

    // Fallback: download
    try {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'transaction.png';
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      setTimeout(() => URL.revokeObjectURL(url), 8000);
      return false;
    } catch(e) {
      return false;
    }
  }

  window.txShare = {
    copyText,
    shareText,
    shareTransactionImage
  };
})();
