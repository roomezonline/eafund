# PWA Icons Generation

The current PNG icon files are empty (0 bytes) which prevents the PWA install prompt from appearing in the browser address bar.

## Quick Fix Options:

### Option 1: Use the HTML Generator
1. Open `create-simple-icons.html` in your browser
2. Click "تولید همه آیکون‌ها" (Generate All Icons)
3. Right-click each icon and save as PNG with the correct filename
4. Replace the empty PNG files in this directory

### Option 2: Use Existing SVG Icons
The directory already contains `icon-192.svg` and `icon-512.svg`. You can:
1. Use an online SVG to PNG converter
2. Convert these to all required sizes
3. Replace the empty PNG files

### Option 3: Use Any Image Editor
1. Create a 512x512 purple icon with a vault/safe symbol
2. Resize to all required sizes: 72, 96, 128, 144, 152, 192, 384, 512
3. Create maskable versions for 192 and 512 (full bleed background)

## Required Files:
- icon-72.png
- icon-96.png  
- icon-128.png
- icon-144.png
- icon-152.png
- icon-192.png
- icon-192-maskable.png
- icon-384.png
- icon-512.png
- icon-512-maskable.png

## Colors:
- Background: Linear gradient from #6750a4 to #4f378b
- Icon: White (#ffffff)
- Theme: Vault/Safe symbol for financial app

Once the PNG files are properly created, the PWA install icon should appear in the browser address bar.
