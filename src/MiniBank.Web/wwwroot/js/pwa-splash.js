// PWA Splash Screen Manager
class PWASplashManager {
    constructor() {
        this.flagKey = 'pwa-splash-shown';
        this.init();
    }

    init() {
        // If splash already shown this session, do nothing
        if (this.hasShown()) {
            return;
        }

        // Mark as shown immediately to avoid any re-entrancy
        this.markShown();

        // Create dynamic splash screen for Android and other platforms
        this.createDynamicSplashScreen();

        // Handle app launch detection (will only run once now)
        this.handleAppLaunch();
    }

    createDynamicSplashScreen() {
        // Only create if not iOS (iOS uses apple-touch-startup-image)
        const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
        if (isIOS) return;

        // Create splash screen element
        const splash = document.createElement('div');
        splash.id = 'pwa-splash-screen';
        splash.innerHTML = `
            <div class="splash-content">
                <div class="splash-icon">
                    <img src="/icons/logo_emam_ali.png" alt="لوگو" class="splash-logo-img" />
                </div>
                <h1 class="splash-title">صندوق امام علی (ع)</h1>
                <p class="splash-subtitle">سیستم مدیریت صندوق قرض‌الحسنه</p>
                <div class="splash-loader">
                    <div class="loader-ring"></div>
                </div>
            </div>
        `;

        // Add styles
        const styles = `
            #pwa-splash-screen {
                position: fixed;
                top: 0;
                left: 0;
                width: 100%;
                height: 100%;
                background: linear-gradient(135deg, #10101a 0%, #1a1a2e 50%, #16213e 100%);
                display: flex;
                align-items: center;
                justify-content: center;
                z-index: 10000;
                opacity: 1;
                transition: opacity 0.5s ease-out;
            }
            
            .splash-content {
                text-align: center;
                color: white;
                animation: splashFadeIn 0.8s ease-out;
            }
            
            .splash-icon {
                margin-bottom: 30px;
                animation: splashIconFloat 2s ease-in-out infinite;
            }

            .splash-logo-img {
                width: 140px;
                height: 140px;
                object-fit: contain;
                filter: drop-shadow(0 4px 8px rgba(0,0,0,0.2));
            }
            
            .splash-title {
                font-size: 28px;
                font-weight: 600;
                margin: 0 0 10px 0;
                color: #ffffff;
                text-shadow: 0 2px 4px rgba(0,0,0,0.3);
                font-family: Vazir, Tahoma, sans-serif;
            }
            
            .splash-subtitle {
                font-size: 16px;
                margin: 0 0 40px 0;
                color: #b0b0b0;
                opacity: 0.9;
                font-family: Vazir, Tahoma, sans-serif;
            }
            
            .splash-loader {
                display: flex;
                justify-content: center;
            }
            
            .loader-ring {
                width: 40px;
                height: 40px;
                border: 3px solid rgba(103, 80, 164, 0.3);
                border-top: 3px solid #6750a4;
                border-radius: 50%;
                animation: spin 1s linear infinite;
            }
            
            @keyframes splashFadeIn {
                from { opacity: 0; transform: translateY(20px); }
                to { opacity: 1; transform: translateY(0); }
            }
            
            @keyframes splashIconFloat {
                0%, 100% { transform: translateY(0px); }
                50% { transform: translateY(-10px); }
            }
            
            @keyframes spin {
                0% { transform: rotate(0deg); }
                100% { transform: rotate(360deg); }
            }
            
            @media (max-width: 768px) {
                .splash-title { font-size: 24px; }
                .splash-subtitle { font-size: 14px; }
                .splash-icon svg { width: 100px; height: 100px; }
            }
        `;

        // Add styles to document
        const styleSheet = document.createElement('style');
        styleSheet.textContent = styles;
        document.head.appendChild(styleSheet);

        // Add splash to document
        document.body.appendChild(splash);

        // Auto-hide after app loads
        this.hideSplashAfterLoad();
    }

    handleAppLaunch() {
        // Detect if launched from home screen
        const isStandalone = window.matchMedia('(display-mode: standalone)').matches || 
                            window.navigator.standalone || 
                            document.referrer.includes('android-app://');

        if (isStandalone) {
            // Show splash for PWA launch
            this.showSplashForPWALaunch();
        }
    }

    showSplashForPWALaunch() {
        // Ensure splash is visible for PWA launches
        const splash = document.getElementById('pwa-splash-screen');
        if (splash) {
            splash.style.display = 'flex';
            // Hide after content loads
            setTimeout(() => this.hideSplash(), 2000);
        }
    }

    hideSplashAfterLoad() {
        // Hide splash when page is fully loaded
        if (document.readyState === 'complete') {
            setTimeout(() => this.hideSplash(), 1500);
        } else {
            window.addEventListener('load', () => {
                setTimeout(() => this.hideSplash(), 1500);
            });
        }

        // Hide when Blazor is ready (don't call start as it's already started)
        if (window.Blazor) {
            // Blazor is already available and started
            setTimeout(() => this.hideSplash(), 500);
        } else {
            // Wait for Blazor to be available
            const checkBlazor = setInterval(() => {
                if (window.Blazor) {
                    clearInterval(checkBlazor);
                    setTimeout(() => this.hideSplash(), 500);
                }
            }, 100);
        }
    }

    hideSplash() {
        const splash = document.getElementById('pwa-splash-screen');
        if (splash) {
            splash.style.opacity = '0';
            setTimeout(() => {
                if (splash.parentNode) {
                    splash.parentNode.removeChild(splash);
                }
            }, 500);
        }
    }

    // Track if splash was already shown in this browser session
    hasShown() {
        try {
            return sessionStorage.getItem(this.flagKey) === '1';
        } catch (e) {
            return false;
        }
    }

    markShown() {
        try {
            sessionStorage.setItem(this.flagKey, '1');
        } catch (e) {
            // ignore storage errors (private mode, etc.)
        }
    }
}

// Initialize splash screen manager
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        new PWASplashManager();
    });
} else {
    new PWASplashManager();
}
