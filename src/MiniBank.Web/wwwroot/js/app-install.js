// PWA App Install Banner
const MANUAL_ONLY = true; // show install UI only when explicitly requested from menu

class AppInstallManager {
    constructor() {
        this.deferredPrompt = null;
        this.isInstalled = false;
        this.installButton = null;
        
        this.init();
    }

    init() {
        // Check if already installed
        this.checkInstallStatus();
        
        // Listen for install prompt
        this.setupInstallPrompt();
        
        // Create install button (disabled when MANUAL_ONLY)
        if (!MANUAL_ONLY) this.createInstallButton();

        // Listen for app installed
        this.setupInstallListener();
        
        // Handle iOS install instructions (manual only)
        if (!MANUAL_ONLY) this.setupIOSInstall();
    }

    checkInstallStatus() {
        // Check if running as PWA
        const standalone = window.matchMedia('(display-mode: standalone)').matches ||
                           window.navigator.standalone ||
                           document.referrer.includes('android-app://');
        const stored = localStorage.getItem('pwa-installed') === 'true';
        this.isInstalled = standalone || stored;
        
        if (this.isInstalled) {
            console.log('App is running as PWA');
            document.body.classList.add('pwa-installed');
        }
    }

    setupInstallPrompt() {
        window.addEventListener('beforeinstallprompt', (e) => {
            console.log('Install prompt available');
            // Don't prevent default to allow browser's native install UI
            this.deferredPrompt = e;
            window.deferredPrompt = e; // Store globally for InstallPromptDialog
            if (!MANUAL_ONLY) this.showInstallButton();
        });
    }

    setupInstallListener() {
        window.addEventListener('appinstalled', () => {
            console.log('PWA was installed');
            this.isInstalled = true;
            try { localStorage.setItem('pwa-installed', 'true'); } catch {}
            this.hideInstallButton();
            this.showInstallSuccess();
            
            // Track install event
            this.trackInstall();
        });
    }

    setupIOSInstall() {
        // Detect iOS Safari
        const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
        const isInStandaloneMode = window.navigator.standalone;
        
        if (isIOS && !isInStandaloneMode) {
            this.showIOSInstallInstructions();
        }
    }

    createInstallButton() {
        this.installButton = document.createElement('button');
        this.installButton.id = 'app-install-button';
        this.installButton.className = 'md-fab app-install-fab';
        this.installButton.innerHTML = `
            <i class="fa-solid fa-download"></i>
            <span class="install-text">نصب اپ</span>
        `;
        this.installButton.style.display = 'none';
        this.installButton.addEventListener('click', () => this.promptInstall());
        
        document.body.appendChild(this.installButton);
    }

    showInstallButton() {
        if (MANUAL_ONLY) return; // never auto-show when manual mode is on
        if (this.installButton && !this.isInstalled && this.deferredPrompt) {
            this.installButton.style.display = 'flex';
            
            // Show with animation
            setTimeout(() => {
                this.installButton.classList.add('visible');
            }, 100);
            
            // Auto-hide after 10 seconds
            setTimeout(() => {
                this.hideInstallButton();
            }, 10000);
        }
    }

    hideInstallButton() {
        if (this.installButton) {
            this.installButton.classList.remove('visible');
            setTimeout(() => {
                this.installButton.style.display = 'none';
            }, 300);
        }
    }

    async promptInstall() {
        if (!this.deferredPrompt) {
            this.showManualInstallInstructions();
            return;
        }

        try {
            // Show the install prompt
            this.deferredPrompt.prompt();
            
            // Wait for user response
            const { outcome } = await this.deferredPrompt.userChoice;
            
            if (outcome === 'accepted') {
                console.log('User accepted install');
                this.trackInstallAccepted();
            } else {
                console.log('User dismissed install');
                this.trackInstallDismissed();
            }
            
            this.deferredPrompt = null;
            this.hideInstallButton();
            
        } catch (error) {
            console.error('Install prompt failed:', error);
            this.showManualInstallInstructions();
        }
    }

    showInstallSuccess() {
        const toast = this.createToast(
            'success',
            'اپ با موفقیت نصب شد!',
            'اکنون می‌توانید از صفحه اصلی دستگاه خود به آن دسترسی داشته باشید.'
        );
        this.showToast(toast);
    }

    showIOSInstallInstructions() {
        // Show iOS install instructions after a delay
        setTimeout(() => {
            const modal = this.createInstallModal('ios');
            document.body.appendChild(modal);
        }, 3000);
    }

    showManualInstallInstructions() {
        const isAndroid = /Android/.test(navigator.userAgent);
        const isIOS = /iPad|iPhone|iPod/.test(navigator.userAgent);
        
        let platform = 'desktop';
        if (isAndroid) platform = 'android';
        if (isIOS) platform = 'ios';
        
        const modal = this.createInstallModal(platform);
        document.body.appendChild(modal);
    }

    createInstallModal(platform) {
        const modal = document.createElement('div');
        modal.className = 'install-modal-overlay';
        
        const instructions = this.getInstallInstructions(platform);
        
        modal.innerHTML = `
            <div class="install-modal">
                <div class="install-modal-header">
                    <h3>نصب اپلیکیشن</h3>
                    <button class="install-modal-close" onclick="this.closest('.install-modal-overlay').remove()">
                        <i class="fa-solid fa-xmark"></i>
                    </button>
                </div>
                <div class="install-modal-content">
                    <div class="install-icon">
                        <i class="fa-solid fa-mobile-screen-button"></i>
                    </div>
                    <div class="install-instructions">
                        ${instructions}
                    </div>
                </div>
                <div class="install-modal-actions">
                    <button class="md-text-button" onclick="this.closest('.install-modal-overlay').remove()">
                        متوجه شدم
                    </button>
                </div>
            </div>
        `;
        
        // Auto-close after 15 seconds
        setTimeout(() => {
            if (modal.parentNode) {
                modal.remove();
            }
        }, 15000);
        
        return modal;
    }

    getInstallInstructions(platform) {
        const instructions = {
            ios: `
                <p>برای نصب این اپلیکیشن در iOS:</p>
                <ol>
                    <li>روی دکمه اشتراک‌گذاری <i class="fa-solid fa-share"></i> در پایین صفحه ضربه بزنید</li>
                    <li>گزینه "Add to Home Screen" را انتخاب کنید</li>
                    <li>روی "Add" ضربه بزنید</li>
                </ol>
            `,
            android: `
                <p>برای نصب این اپلیکیشن در Android:</p>
                <ol>
                    <li>روی منوی مرورگر <i class="fa-solid fa-ellipsis-vertical"></i> ضربه بزنید</li>
                    <li>گزینه "Add to Home screen" را انتخاب کنید</li>
                    <li>روی "Add" ضربه بزنید</li>
                </ol>
            `,
            desktop: `
                <p>برای نصب این اپلیکیشن در دسکتاپ:</p>
                <ol>
                    <li>روی آیکون نصب در نوار آدرس مرورگر کلیک کنید</li>
                    <li>یا از منوی مرورگر گزینه "Install App" را انتخاب کنید</li>
                </ol>
            `
        };
        
        return instructions[platform] || instructions.desktop;
    }

    createToast(type, title, message) {
        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;
        
        const icon = type === 'success' ? 'check-circle' : 'info-circle';
        
        toast.innerHTML = `
            <div class="toast-content">
                <div class="toast-icon">
                    <i class="fa-solid fa-${icon}"></i>
                </div>
                <div class="toast-text">
                    <div class="toast-title">${title}</div>
                    <div class="toast-message">${message}</div>
                </div>
                <button class="toast-close" onclick="this.closest('.toast').remove()">
                    <i class="fa-solid fa-xmark"></i>
                </button>
            </div>
        `;
        
        return toast;
    }

    showToast(toast) {
        const container = document.getElementById('toast-container') || this.createToastContainer();
        container.appendChild(toast);
        
        // Show with animation
        setTimeout(() => {
            toast.classList.add('visible');
        }, 100);
        
        // Auto-remove after 5 seconds
        setTimeout(() => {
            toast.classList.remove('visible');
            setTimeout(() => {
                if (toast.parentNode) {
                    toast.remove();
                }
            }, 300);
        }, 5000);
    }

    createToastContainer() {
        const container = document.createElement('div');
        container.id = 'toast-container';
        document.body.appendChild(container);
        return container;
    }

    trackInstall() {
        // Track install event (you can integrate with analytics)
        console.log('PWA Install Event');
        
        // Example: Google Analytics
        if (typeof gtag !== 'undefined') {
            gtag('event', 'pwa_install', {
                event_category: 'PWA',
                event_label: 'App Installed'
            });
        }
    }

    trackInstallAccepted() {
        console.log('Install prompt accepted');
    }

    trackInstallDismissed() {
        console.log('Install prompt dismissed');
    }
}

// Store deferred prompt globally for InstallPromptDialog
window.deferredPrompt = null;

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        new AppInstallManager();
    });
} else {
    new AppInstallManager();
}

// CSS Styles
const installStyles = `
.app-install-fab {
    position: fixed;
    bottom: 80px;
    left: 16px;
    z-index: 1000;
    background: linear-gradient(45deg, var(--md-sys-color-primary), var(--md-sys-color-tertiary));
    color: white;
    border: none;
    border-radius: 16px;
    padding: 12px 16px;
    box-shadow: var(--md-sys-elevation-level3);
    cursor: pointer;
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    opacity: 0;
    transform: translateY(20px) scale(0.8);
    flex-direction: row;
    align-items: center;
    gap: 8px;
    font-size: 14px;
    font-weight: 500;
    backdrop-filter: blur(20px);
}

.app-install-fab.visible {
    opacity: 1;
    transform: translateY(0) scale(1);
}

.app-install-fab:hover {
    transform: translateY(-2px) scale(1.05);
    box-shadow: var(--md-sys-elevation-level4);
}

.install-modal-overlay {
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background: rgba(0, 0, 0, 0.5);
    z-index: 10000;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 16px;
    backdrop-filter: blur(4px);
}

.install-modal {
    background: var(--md-sys-color-surface-container);
    border-radius: 16px;
    max-width: 400px;
    width: 100%;
    box-shadow: var(--md-sys-elevation-level5);
    overflow: hidden;
    animation: modalSlideUp 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.install-modal-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 16px 20px;
    border-bottom: 1px solid var(--md-sys-color-outline-variant);
}

.install-modal-header h3 {
    margin: 0;
    font-size: 18px;
    font-weight: 600;
    color: var(--md-sys-color-on-surface);
}

.install-modal-close {
    background: none;
    border: none;
    color: var(--md-sys-color-on-surface-variant);
    cursor: pointer;
    padding: 8px;
    border-radius: 8px;
    transition: background-color 0.2s;
}

.install-modal-close:hover {
    background: var(--md-sys-color-surface-variant);
}

.install-modal-content {
    padding: 20px;
    text-align: center;
}

.install-icon {
    font-size: 48px;
    color: var(--md-sys-color-primary);
    margin-bottom: 16px;
}

.install-instructions {
    text-align: right;
    color: var(--md-sys-color-on-surface);
    line-height: 1.5;
}

.install-instructions ol {
    padding-right: 20px;
    margin: 16px 0;
}

.install-instructions li {
    margin: 8px 0;
}

.install-modal-actions {
    padding: 16px 20px;
    border-top: 1px solid var(--md-sys-color-outline-variant);
    text-align: center;
}

#toast-container {
    position: fixed;
    top: 20px;
    right: 20px;
    z-index: 10001;
    display: flex;
    flex-direction: column;
    gap: 8px;
}

.toast {
    background: var(--md-sys-color-surface-container);
    border-radius: 12px;
    box-shadow: var(--md-sys-elevation-level3);
    min-width: 300px;
    opacity: 0;
    transform: translateX(100%);
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    backdrop-filter: blur(20px);
}

.toast.visible {
    opacity: 1;
    transform: translateX(0);
}

.toast-content {
    display: flex;
    align-items: flex-start;
    gap: 12px;
    padding: 16px;
}

.toast-icon {
    color: var(--md-sys-color-primary);
    font-size: 20px;
    flex-shrink: 0;
    margin-top: 2px;
}

.toast-success .toast-icon {
    color: #22c55e;
}

.toast-text {
    flex: 1;
    min-width: 0;
}

.toast-title {
    font-weight: 600;
    color: var(--md-sys-color-on-surface);
    margin-bottom: 4px;
}

.toast-message {
    font-size: 14px;
    color: var(--md-sys-color-on-surface-variant);
    line-height: 1.4;
}

.toast-close {
    background: none;
    border: none;
    color: var(--md-sys-color-on-surface-variant);
    cursor: pointer;
    padding: 4px;
    border-radius: 4px;
    flex-shrink: 0;
}

.toast-close:hover {
    background: var(--md-sys-color-surface-variant);
}

@keyframes modalSlideUp {
    from {
        opacity: 0;
        transform: translateY(20px) scale(0.95);
    }
    to {
        opacity: 1;
        transform: translateY(0) scale(1);
    }
}

@media (max-width: 768px) {
    .app-install-fab {
        bottom: 90px;
        left: 50%;
        transform: translateX(-50%) translateY(20px) scale(0.8);
    }
    
    .app-install-fab.visible {
        transform: translateX(-50%) translateY(0) scale(1);
    }
    
    #toast-container {
        right: 16px;
        left: 16px;
        top: auto;
        bottom: 100px;
    }
    
    .toast {
        min-width: auto;
    }
}
`;

// Inject styles
const installStyleSheet = document.createElement('style');
installStyleSheet.textContent = installStyles;
document.head.appendChild(installStyleSheet);
