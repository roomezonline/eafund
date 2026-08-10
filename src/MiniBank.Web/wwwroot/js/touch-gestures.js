// Touch Gestures and App-like Navigation
class TouchGestureHandler {
    constructor() {
        this.startX = 0;
        this.startY = 0;
        this.currentX = 0;
        this.currentY = 0;
        this.isDragging = false;
        this.threshold = 50; // Minimum distance for swipe
        this.velocityThreshold = 0.3;
        this.lastTime = 0;
        
        this.init();
    }

    init() {
        // Swipe to go back
        this.initSwipeBack();
        
        // Pull to refresh
        this.initPullToRefresh();
        
        // Long press menus
        this.initLongPress();
        
        // Hardware back button (Android)
        this.initHardwareBack();
        
        // Haptic feedback
        this.initHapticFeedback();
    }

    // Swipe to go back navigation
    initSwipeBack() {
        let startX = 0;
        let startY = 0;
        let isSwipeBack = false;

        document.addEventListener('touchstart', (e) => {
            const touch = e.touches[0];
            startX = touch.clientX;
            startY = touch.clientY;
            
            // Only trigger on edge swipe (first 20px from right edge for RTL)
            if (startX > window.innerWidth - 20) {
                isSwipeBack = true;
            }
        }, { passive: true });

        document.addEventListener('touchmove', (e) => {
            if (!isSwipeBack) return;
            
            const touch = e.touches[0];
            const deltaX = startX - touch.clientX;
            const deltaY = Math.abs(startY - touch.clientY);
            
            // Horizontal swipe with minimal vertical movement
            if (deltaX > 50 && deltaY < 100) {
                e.preventDefault();
                this.triggerHaptic('light');
                this.navigateBack();
                isSwipeBack = false;
            }
        }, { passive: false });

        document.addEventListener('touchend', () => {
            isSwipeBack = false;
        });
    }

    // Pull to refresh
    initPullToRefresh() {
        let startY = 0;
        let pullDistance = 0;
        let isPulling = false;
        const maxPull = 100;
        const triggerDistance = 60;

        const scrollContainer = document.querySelector('.scroll-area') || document.body;
        
        scrollContainer.addEventListener('touchstart', (e) => {
            if (scrollContainer.scrollTop === 0) {
                startY = e.touches[0].clientY;
                isPulling = true;
            }
        }, { passive: true });

        scrollContainer.addEventListener('touchmove', (e) => {
            if (!isPulling) return;
            
            const currentY = e.touches[0].clientY;
            pullDistance = Math.min(currentY - startY, maxPull);
            
            if (pullDistance > 0) {
                e.preventDefault();
                this.updatePullIndicator(pullDistance, triggerDistance);
            }
        }, { passive: false });

        scrollContainer.addEventListener('touchend', () => {
            if (isPulling && pullDistance > triggerDistance) {
                this.triggerHaptic('medium');
                this.triggerRefresh();
            }
            
            isPulling = false;
            pullDistance = 0;
            this.hidePullIndicator();
        });
    }

    // Long press for context menus
    initLongPress() {
        let pressTimer = null;
        let startPos = { x: 0, y: 0 };
        const longPressDelay = 500;
        const moveThreshold = 10;

        document.addEventListener('touchstart', (e) => {
            const touch = e.touches[0];
            startPos = { x: touch.clientX, y: touch.clientY };
            
            pressTimer = setTimeout(() => {
                const target = e.target.closest('[data-long-press]');
                if (target) {
                    this.triggerHaptic('heavy');
                    this.showContextMenu(target, touch.clientX, touch.clientY);
                }
            }, longPressDelay);
        }, { passive: true });

        document.addEventListener('touchmove', (e) => {
            const touch = e.touches[0];
            const deltaX = Math.abs(touch.clientX - startPos.x);
            const deltaY = Math.abs(touch.clientY - startPos.y);
            
            if (deltaX > moveThreshold || deltaY > moveThreshold) {
                clearTimeout(pressTimer);
            }
        }, { passive: true });

        document.addEventListener('touchend', () => {
            clearTimeout(pressTimer);
        });
    }

    // Hardware back button support
    initHardwareBack() {
        // Listen for Android back button
        window.addEventListener('popstate', (e) => {
            // Check if we have a modal or dialog open
            const openDialog = document.querySelector('.mud-dialog-container');
            const openDrawer = document.querySelector('.mud-drawer-open');
            
            if (openDialog) {
                e.preventDefault();
                this.closeTopDialog();
                return;
            }
            
            if (openDrawer) {
                e.preventDefault();
                this.closeDrawer();
                return;
            }
            
            // Default browser back behavior
        });

        // Add history entry for dialogs/drawers
        document.addEventListener('dialog-opened', () => {
            history.pushState({ modal: true }, '');
        });
    }

    // Haptic feedback
    initHapticFeedback() {
        // Check if haptic feedback is supported
        this.hasHaptic = 'vibrate' in navigator || 
                         ('hapticFeedback' in window && window.hapticFeedback);
    }

    triggerHaptic(type = 'light') {
        if (!this.hasHaptic) return;
        
        try {
            if (window.hapticFeedback) {
                // iOS haptic feedback
                switch (type) {
                    case 'light':
                        window.hapticFeedback.impactOccurred('light');
                        break;
                    case 'medium':
                        window.hapticFeedback.impactOccurred('medium');
                        break;
                    case 'heavy':
                        window.hapticFeedback.impactOccurred('heavy');
                        break;
                    case 'success':
                        window.hapticFeedback.notificationOccurred('success');
                        break;
                    case 'error':
                        window.hapticFeedback.notificationOccurred('error');
                        break;
                }
            } else if (navigator.vibrate) {
                // Android vibration
                const patterns = {
                    light: [10],
                    medium: [20],
                    heavy: [30],
                    success: [10, 50, 10],
                    error: [50, 50, 50]
                };
                navigator.vibrate(patterns[type] || patterns.light);
            }
        } catch (error) {
            console.warn('Haptic feedback failed:', error);
        }
    }

    navigateBack() {
        // Try Blazor navigation first
        if (window.Blazor && window.DotNet) {
            window.DotNet.invokeMethodAsync('MiniBank.Web', 'NavigateBack');
        } else {
            history.back();
        }
    }

    triggerRefresh() {
        // Dispatch custom refresh event
        const refreshEvent = new CustomEvent('pull-refresh', {
            detail: { timestamp: Date.now() }
        });
        document.dispatchEvent(refreshEvent);
        
        // Auto-hide after 2 seconds
        setTimeout(() => {
            this.hidePullIndicator();
        }, 2000);
    }

    updatePullIndicator(distance, trigger) {
        let indicator = document.getElementById('pull-refresh-indicator');
        
        if (!indicator) {
            indicator = document.createElement('div');
            indicator.id = 'pull-refresh-indicator';
            indicator.innerHTML = `
                <div class="pull-refresh-content">
                    <div class="pull-refresh-spinner"></div>
                    <span class="pull-refresh-text">کشیده و رها کنید</span>
                </div>
            `;
            document.body.appendChild(indicator);
        }
        
        const progress = Math.min(distance / trigger, 1);
        const opacity = Math.min(progress, 0.8);
        const scale = 0.5 + (progress * 0.5);
        
        indicator.style.opacity = opacity;
        indicator.style.transform = `translateY(${distance}px) scale(${scale})`;
        
        if (distance > trigger) {
            indicator.classList.add('ready');
            indicator.querySelector('.pull-refresh-text').textContent = 'رها کنید';
        } else {
            indicator.classList.remove('ready');
            indicator.querySelector('.pull-refresh-text').textContent = 'کشیده و رها کنید';
        }
    }

    hidePullIndicator() {
        const indicator = document.getElementById('pull-refresh-indicator');
        if (indicator) {
            indicator.style.opacity = '0';
            indicator.style.transform = 'translateY(-50px) scale(0.5)';
            setTimeout(() => {
                indicator.remove();
            }, 300);
        }
    }

    showContextMenu(target, x, y) {
        const menu = document.createElement('div');
        menu.className = 'context-menu';
        menu.style.left = `${x}px`;
        menu.style.top = `${y}px`;
        
        const actions = target.dataset.longPress.split(',');
        actions.forEach(action => {
            const item = document.createElement('button');
            item.className = 'context-menu-item';
            item.textContent = this.getActionLabel(action.trim());
            item.onclick = () => {
                this.executeAction(target, action.trim());
                menu.remove();
            };
            menu.appendChild(item);
        });
        
        document.body.appendChild(menu);
        
        // Auto-remove after 5 seconds
        setTimeout(() => {
            if (menu.parentNode) {
                menu.remove();
            }
        }, 5000);
        
        // Remove on outside click
        document.addEventListener('click', function removeMenu(e) {
            if (!menu.contains(e.target)) {
                menu.remove();
                document.removeEventListener('click', removeMenu);
            }
        });
    }

    getActionLabel(action) {
        const labels = {
            'edit': 'ویرایش',
            'delete': 'حذف',
            'copy': 'کپی',
            'share': 'اشتراک‌گذاری',
            'details': 'جزئیات'
        };
        return labels[action] || action;
    }

    executeAction(target, action) {
        const event = new CustomEvent('context-action', {
            detail: { target, action }
        });
        target.dispatchEvent(event);
    }

    closeTopDialog() {
        const closeBtn = document.querySelector('.mud-dialog .mud-button-close');
        if (closeBtn) {
            closeBtn.click();
        }
    }

    closeDrawer() {
        const overlay = document.querySelector('.mud-overlay');
        if (overlay) {
            overlay.click();
        }
    }
}

// Initialize when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        new TouchGestureHandler();
    });
} else {
    new TouchGestureHandler();
}

// CSS for pull-to-refresh indicator
const pullRefreshStyles = `
#pull-refresh-indicator {
    position: fixed;
    top: 0;
    left: 50%;
    transform: translateX(-50%) translateY(-50px) scale(0.5);
    z-index: 9999;
    opacity: 0;
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    pointer-events: none;
}

.pull-refresh-content {
    display: flex;
    align-items: center;
    gap: 8px;
    background: var(--md-sys-color-surface-container);
    color: var(--md-sys-color-on-surface);
    padding: 12px 16px;
    border-radius: 20px;
    box-shadow: var(--md-sys-elevation-level3);
    backdrop-filter: blur(20px);
}

.pull-refresh-spinner {
    width: 20px;
    height: 20px;
    border: 2px solid var(--md-sys-color-outline-variant);
    border-top: 2px solid var(--md-sys-color-primary);
    border-radius: 50%;
    animation: spin 1s linear infinite;
}

.pull-refresh-text {
    font-size: 14px;
    font-weight: 500;
    white-space: nowrap;
}

#pull-refresh-indicator.ready .pull-refresh-spinner {
    border-top-color: var(--md-sys-color-tertiary);
}

.context-menu {
    position: fixed;
    background: var(--md-sys-color-surface-container);
    border-radius: 8px;
    box-shadow: var(--md-sys-elevation-level3);
    z-index: 10000;
    min-width: 120px;
    overflow: hidden;
    backdrop-filter: blur(20px);
}

.context-menu-item {
    display: block;
    width: 100%;
    padding: 12px 16px;
    border: none;
    background: transparent;
    color: var(--md-sys-color-on-surface);
    text-align: right;
    font-size: 14px;
    cursor: pointer;
    transition: background-color 0.2s;
}

.context-menu-item:hover {
    background-color: var(--md-sys-color-secondary-container);
}

@keyframes spin {
    0% { transform: rotate(0deg); }
    100% { transform: rotate(360deg); }
}
`;

// Inject styles
const gestureStyleSheet = document.createElement('style');
gestureStyleSheet.textContent = pullRefreshStyles;
document.head.appendChild(gestureStyleSheet);
