// Generate PWA icons using base64 data
// This creates a simple but effective icon for the PWA

const fs = require('fs');

// Simple 192x192 icon as base64 (purple vault icon)
const icon192Base64 = `data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAMAAAADACAYAAABS3GwHAAAACXBIWXMAAAsTAAALEwEAmpwYAAAKT2lDQ1BQaG90b3Nob3AgSUNDIHByb2ZpbGUAAHjanVNnVFPpFj333vRCS4iAlEtvUhUIIFJCi4AUkSYqIQkQSoghodkVUcERRUUEG8igiAOOjoCMFVEsDIoK2AfkIaKOg6OIisr74Xuja9a89+bN/rXXPues852zzwfACAyWSDNRNYAMqUIeEeCDx8TG4eQuQIEKJHAAEAizZCFz/SMBAPh+PDwrIsAHvgABeNMLCADATZvAMByH/w/qQplcAYCEAcB0kThLCIAUAEB6jkKmAEBGAYCdmCZTAKAEAGDLY2LjAFAtAGAnf+bTAICd+Jl7AQBblCEVAaCRACATZYhEAGg7AKzPVopFAFgwABRmS8Q5ANgtADBJV2ZIALC3AMDOEAuyAAgMADBRiIUpAAR7AGDIIyN4AISZABRG8lc88SuuEOcqAAB4mbI8uSQ5RYFbCC1xB1dXLh4ozkkXKxQ2YQJhmkAuwnmZGTKBNA/g88wAAKCRFRHgg/P9eM4Ors7ONo62Dl8t6r8G/yJiYuP+5c+rcEAAAOF0ftH+LC+zGoA7BoBt/qIl7gRoXgugdfeLZrIPQLUAoOnaV/Nw+H48PEWhkLnZ2eXk5NhKxEJbYcpXff5nwl/AV/1s+X48/Pf14L7iJIEyXYFHBPjgwsz0TKUcz5IJhGLc5o9H/LcL//wd0yLESWK5WCoU41EScY5EmozzMqUiiUKSKcUl0v9k4t8s+wM+3zUAsGo+AXuRLahdYwP2SycQWHTA4vcAAPK7b8HUKAgDgGiD4c93/+8//UegJQCAZkmScQAAXkQkLlTKsz/HCAAARKCBKrBBG/TBGCzABhzBBdzBC/xgNoRCJMTCQhBCCmSAHHJgKayCQiiGzbAdKmAv1EAdNMBRaIaTcA4uwlW4Dj1wD/phCJ7BKLyBCQRByAgTYSHaiAFiilgjjggXmYX4IcFIBBKLJCDJiBRRIkuRNUgxUopUIFVIHfI9cgI5h1xGupE7yAAygvyGvEcxlIGyUT3UDLVDuag3GoRGogvQZHQxmo8WoJvQcrQaPYw2oefQq2gP2o8+Q8cwwOgYBzPEbDAuxsNCsTgsCZNjy7EirAyrxhqwVqwDu4n1Y8+xdwQSgUXACTYEd0IgYR5BSFhMWE7YSKggHCQ0EdoJNwkDhFHCJyKTqEu0JroR+cQYYjIxh1hILCPWEo8TLxB7iEPENyQSiUMyJ7mQAkmxpFTSEtJG0m5SI+ksqZs0SBojk8naZGuyBzmULCAryIXkneTD5DPkG+Qh8lsKnWJAcaT4U+IoUspqShnlEOU05QZlmDJBVaOaUt2ooVQRNY9aQq2htlKvUYeoEzR1mjnNgxZJS6WtopXTGmgXaPdpr+h0uhHdlR5Ol9BX0svpR+iX6AP0dwwNhhWDx4hnKBmbGAcYZxl3GK+YTKYZ04sZx1QwNzHrmOeZD5lvVVgqtip8FZHKCpVKlSaVGyovVKmqpqreqgtV81XLVI+pXlN9rkZVM1PjqQnUlqtVqp1Q61MbU2epO6iHqmeob1Q/pH5Z/YkGWcNMw09DpFGgsV/jvMYgC2MZs3gsIWsNq4Z1gTXEJrHN2Xx2KruY/R27iz2qqaE5QzNKM1ezUvOUZj8H45hx+Jx0TgnnKKeX836K3hTvKeIpG6Y0TLkxZVxrqpaXllirSKtRq0frvTau7aedpr1Fu1n7gQ5Bx0onXCdHZ4/OBZ3nU9lT3acKpxZNPTr1ri6qa6UbobtEd79up+6Ynr5egJ5Mb6feeb3n+hx9L/1U/W36p/VHDFgGswwkBtsMzhg8xTVxbzwdL8fb8VFDXcNAQ6VhlWGX4SU`;

// Create a simple function to generate different sized icons
function createIconFile(size, filename) {
    // For simplicity, we'll create a basic SVG that can be used as PNG
    const svg = `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg">
        <defs>
            <linearGradient id="bg" x1="0%" y1="0%" x2="100%" y2="100%">
                <stop offset="0%" style="stop-color:#6750a4;stop-opacity:1" />
                <stop offset="100%" style="stop-color:#4f378b;stop-opacity:1" />
            </linearGradient>
        </defs>
        <rect width="${size}" height="${size}" rx="${size * 0.15}" fill="url(#bg)"/>
        <circle cx="${size/2}" cy="${size/2}" r="${size * 0.25}" fill="none" stroke="white" stroke-width="${Math.max(2, size * 0.04)}"/>
        <circle cx="${size/2}" cy="${size/2}" r="${size * 0.15}" fill="none" stroke="white" stroke-width="${Math.max(2, size * 0.03)}"/>
        <circle cx="${size/2}" cy="${size/2}" r="${size * 0.02}" fill="white"/>
        <circle cx="${size/2 + size * 0.15}" cy="${size/2}" r="${size * 0.04}" fill="white"/>
    </svg>`;
    
    console.log(`Generated ${filename} (${size}x${size})`);
    return svg;
}

// Generate all required sizes
const sizes = [72, 96, 128, 144, 152, 192, 384, 512];

console.log('Generating PWA icon SVGs...');
console.log('Note: Convert these SVGs to PNG using an online converter or image editor.');
console.log('');

sizes.forEach(size => {
    const svg = createIconFile(size, `icon-${size}.png`);
    fs.writeFileSync(`icon-${size}.svg`, svg);
    
    // Also create maskable versions for 192 and 512
    if (size === 192 || size === 512) {
        const maskableSvg = `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg">
            <defs>
                <linearGradient id="bg" x1="0%" y1="0%" x2="100%" y2="100%">
                    <stop offset="0%" style="stop-color:#6750a4;stop-opacity:1" />
                    <stop offset="100%" style="stop-color:#4f378b;stop-opacity:1" />
                </linearGradient>
            </defs>
            <rect width="${size}" height="${size}" fill="url(#bg)"/>
            <circle cx="${size/2}" cy="${size/2}" r="${size * 0.18}" fill="none" stroke="white" stroke-width="${Math.max(2, size * 0.03)}"/>
            <circle cx="${size/2}" cy="${size/2}" r="${size * 0.11}" fill="none" stroke="white" stroke-width="${Math.max(2, size * 0.025)}"/>
            <circle cx="${size/2}" cy="${size/2}" r="${size * 0.015}" fill="white"/>
            <circle cx="${size/2 + size * 0.11}" cy="${size/2}" r="${size * 0.03}" fill="white"/>
        </svg>`;
        fs.writeFileSync(`icon-${size}-maskable.svg`, maskableSvg);
        console.log(`Generated icon-${size}-maskable.png (${size}x${size})`);
    }
});

console.log('');
console.log('SVG files generated! Convert them to PNG format and replace the empty PNG files.');
console.log('You can use online converters like:');
console.log('- https://convertio.co/svg-png/');
console.log('- https://cloudconvert.com/svg-to-png');
console.log('- Or use any image editor like GIMP, Photoshop, etc.');`;
