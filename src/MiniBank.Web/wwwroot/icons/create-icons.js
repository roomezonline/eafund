// Simple icon creator using Canvas API in Node.js
// Run with: node create-icons.js

const fs = require('fs');
const { createCanvas } = require('canvas');

const sizes = [72, 96, 128, 144, 152, 192, 384, 512];

function createIcon(size, isMaskable = false) {
    const canvas = createCanvas(size, size);
    const ctx = canvas.getContext('2d');

    // Background gradient
    const gradient = ctx.createLinearGradient(0, 0, size, size);
    gradient.addColorStop(0, '#6750a4');
    gradient.addColorStop(1, '#4f378b');
    ctx.fillStyle = gradient;
    
    if (isMaskable) {
        // Maskable icons need full background
        ctx.fillRect(0, 0, size, size);
    } else {
        // Regular icons with rounded corners
        const radius = size * 0.15;
        ctx.beginPath();
        ctx.roundRect(0, 0, size, size, radius);
        ctx.fill();
    }

    // Icon content - Vault/Safe symbol
    ctx.fillStyle = '#ffffff';
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = Math.max(2, size * 0.03);

    const centerX = size / 2;
    const centerY = size / 2;
    const iconSize = size * (isMaskable ? 0.4 : 0.6);

    // Draw vault door
    const vaultRadius = iconSize * 0.4;
    ctx.beginPath();
    ctx.arc(centerX, centerY, vaultRadius, 0, 2 * Math.PI);
    ctx.stroke();

    // Draw vault handle
    const handleSize = iconSize * 0.15;
    ctx.beginPath();
    ctx.arc(centerX + vaultRadius * 0.5, centerY, handleSize, 0, 2 * Math.PI);
    ctx.fill();

    // Draw combination dial
    const dialRadius = iconSize * 0.2;
    ctx.beginPath();
    ctx.arc(centerX, centerY, dialRadius, 0, 2 * Math.PI);
    ctx.stroke();

    // Draw dial marks
    for (let i = 0; i < 12; i++) {
        const angle = (i * 30) * Math.PI / 180;
        const x1 = centerX + Math.cos(angle) * dialRadius * 0.8;
        const y1 = centerY + Math.sin(angle) * dialRadius * 0.8;
        const x2 = centerX + Math.cos(angle) * dialRadius * 0.9;
        const y2 = centerY + Math.sin(angle) * dialRadius * 0.9;
        
        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.lineTo(x2, y2);
        ctx.stroke();
    }

    return canvas;
}

function generateIcons() {
    console.log('Generating PWA icons...');
    
    sizes.forEach(size => {
        // Regular icon
        const canvas = createIcon(size);
        const buffer = canvas.toBuffer('image/png');
        fs.writeFileSync(`icon-${size}.png`, buffer);
        console.log(`Generated icon-${size}.png`);

        // Maskable versions for 192 and 512
        if (size === 192 || size === 512) {
            const maskableCanvas = createIcon(size, true);
            const maskableBuffer = maskableCanvas.toBuffer('image/png');
            fs.writeFileSync(`icon-${size}-maskable.png`, maskableBuffer);
            console.log(`Generated icon-${size}-maskable.png`);
        }
    });
    
    console.log('All icons generated successfully!');
}

// Check if canvas module is available
try {
    generateIcons();
} catch (error) {
    console.log('Canvas module not available. Please install with: npm install canvas');
    console.log('Alternative: Use the generate-icons.html file in a browser to create icons manually.');
}
