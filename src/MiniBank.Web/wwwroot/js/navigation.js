// Material Design 3 Navigation JavaScript

class MD3Navigation {
    constructor() {
        this.drawer = null;
        this.backdrop = null;
        this.isDrawerOpen = false;
        this.init();
    }

    init() {
        this.setupDrawer();
        this.setupBottomNav();
        this.setupTopAppBar();
        this.setupKeyboardNavigation();
        this.setupGestureNavigation();
    }

    setupDrawer() {
        this.drawer = document.querySelector('.md3-navigation-drawer');
        this.backdrop = document.querySelector('.md3-navigation-backdrop');
        
        if (!this.drawer) return;

        // Create backdrop if it doesn't exist
        if (!this.backdrop) {
            this.backdrop = document.createElement('div');
            this.backdrop.className = 'md3-navigation-backdrop';
            document.body.appendChild(this.backdrop);
        }

        // Setup drawer toggle buttons
        const toggleButtons = document.querySelectorAll('[data-drawer-toggle]');
        toggleButtons.forEach(button => {
            button.addEventListener('click', () => this.toggleDrawer());
        });

        // Setup backdrop click to close
        this.backdrop.addEventListener('click', () => this.closeDrawer());

        // Setup drawer item clicks
        const drawerItems = this.drawer.querySelectorAll('.md3-navigation-drawer-item');
        drawerItems.forEach(item => {
            item.addEventListener('click', (e) => {
                this.setActiveDrawerItem(item);
                // Close drawer on mobile after selection
                if (window.innerWidth < 840) {
                    setTimeout(() => this.closeDrawer(), 150);
                }
            });
        });

        // Handle window resize
        window.addEventListener('resize', () => {
            if (window.innerWidth >= 840) {
                this.closeDrawer();
            }
        });
    }

    setupBottomNav() {
        const bottomNavItems = document.querySelectorAll('.md3-bottom-nav-item');
        bottomNavItems.forEach(item => {
            item.addEventListener('click', (e) => {
                this.setActiveBottomNavItem(item);
            });
        });
    }

    setupTopAppBar() {
        const topAppBar = document.querySelector('.md3-top-app-bar');
        if (!topAppBar) return;

        // Add elevation on scroll
        let lastScrollY = window.scrollY;
        window.addEventListener('scroll', () => {
            const currentScrollY = window.scrollY;
            
            if (currentScrollY > 0) {
                topAppBar.classList.add('elevated');
            } else {
                topAppBar.classList.remove('elevated');
            }
            
            lastScrollY = currentScrollY;
        });

        // Setup menu button
        const menuButton = topAppBar.querySelector('[data-drawer-toggle]');
        if (menuButton) {
            menuButton.addEventListener('click', () => this.toggleDrawer());
        }
    }

    setupKeyboardNavigation() {
        // Handle Escape key to close drawer
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && this.isDrawerOpen) {
                this.closeDrawer();
            }
        });

        // Handle Tab navigation within drawer
        if (this.drawer) {
            this.drawer.addEventListener('keydown', (e) => {
                if (e.key === 'Tab') {
                    this.handleTabNavigation(e);
                }
            });
        }
    }

    setupGestureNavigation() {
        let startX = 0;
        let startY = 0;
        let currentX = 0;
        let currentY = 0;
        let isDragging = false;

        // Edge swipe to open drawer
        document.addEventListener('touchstart', (e) => {
            const touch = e.touches[0];
            startX = touch.clientX;
            startY = touch.clientY;
            
            // Only handle edge swipes (within 20px of edge)
            const isRTL = document.dir === 'rtl';
            const edgeThreshold = 20;
            const isEdgeSwipe = isRTL ? 
                startX > window.innerWidth - edgeThreshold :
                startX < edgeThreshold;
                
            if (isEdgeSwipe && window.innerWidth < 840) {
                isDragging = true;
            }
        });

        document.addEventListener('touchmove', (e) => {
            if (!isDragging) return;
            
            const touch = e.touches[0];
            currentX = touch.clientX;
            currentY = touch.clientY;
            
            // Prevent scrolling while dragging
            e.preventDefault();
        });

        document.addEventListener('touchend', () => {
            if (!isDragging) return;
            
            const deltaX = currentX - startX;
            const deltaY = Math.abs(currentY - startY);
            const isRTL = document.dir === 'rtl';
            
            // Check if it's a horizontal swipe (more horizontal than vertical)
            if (Math.abs(deltaX) > deltaY && Math.abs(deltaX) > 50) {
                const shouldOpen = isRTL ? deltaX < -50 : deltaX > 50;
                if (shouldOpen && !this.isDrawerOpen) {
                    this.openDrawer();
                } else if (!shouldOpen && this.isDrawerOpen) {
                    this.closeDrawer();
                }
            }
            
            isDragging = false;
        });
    }

    toggleDrawer() {
        if (this.isDrawerOpen) {
            this.closeDrawer();
        } else {
            this.openDrawer();
        }
    }

    openDrawer() {
        if (!this.drawer || window.innerWidth >= 840) return;
        
        this.drawer.classList.add('open');
        this.backdrop.classList.add('show');
        this.isDrawerOpen = true;
        
        // Prevent body scroll
        document.body.style.overflow = 'hidden';
        
        // Focus first focusable element in drawer
        const firstFocusable = this.drawer.querySelector('a, button, [tabindex]:not([tabindex="-1"])');
        if (firstFocusable) {
            setTimeout(() => firstFocusable.focus(), 150);
        }
        
        // Announce to screen readers
        this.announceToScreenReader('Navigation drawer opened');
    }

    closeDrawer() {
        if (!this.drawer) return;
        
        this.drawer.classList.remove('open');
        this.backdrop.classList.remove('show');
        this.isDrawerOpen = false;
        
        // Restore body scroll
        document.body.style.overflow = '';
        
        // Announce to screen readers
        this.announceToScreenReader('Navigation drawer closed');
    }

    setActiveDrawerItem(activeItem) {
        const drawerItems = this.drawer.querySelectorAll('.md3-navigation-drawer-item');
        drawerItems.forEach(item => {
            item.classList.remove('active');
            item.setAttribute('aria-current', 'false');
        });
        
        activeItem.classList.add('active');
        activeItem.setAttribute('aria-current', 'page');
    }

    setActiveBottomNavItem(activeItem) {
        const bottomNavItems = document.querySelectorAll('.md3-bottom-nav-item');
        bottomNavItems.forEach(item => {
            item.classList.remove('active');
            item.setAttribute('aria-current', 'false');
        });
        
        activeItem.classList.add('active');
        activeItem.setAttribute('aria-current', 'page');
    }

    handleTabNavigation(e) {
        const focusableElements = this.drawer.querySelectorAll(
            'a, button, [tabindex]:not([tabindex="-1"])'
        );
        const firstFocusable = focusableElements[0];
        const lastFocusable = focusableElements[focusableElements.length - 1];

        if (e.shiftKey) {
            if (document.activeElement === firstFocusable) {
                e.preventDefault();
                lastFocusable.focus();
            }
        } else {
            if (document.activeElement === lastFocusable) {
                e.preventDefault();
                firstFocusable.focus();
            }
        }
    }

    announceToScreenReader(message) {
        const announcement = document.createElement('div');
        announcement.setAttribute('aria-live', 'polite');
        announcement.setAttribute('aria-atomic', 'true');
        announcement.style.position = 'absolute';
        announcement.style.left = '-10000px';
        announcement.style.width = '1px';
        announcement.style.height = '1px';
        announcement.style.overflow = 'hidden';
        announcement.textContent = message;
        
        document.body.appendChild(announcement);
        
        setTimeout(() => {
            document.body.removeChild(announcement);
        }, 1000);
    }

    // Public API methods
    setActivePage(pageId) {
        // Set active state for drawer items
        const drawerItems = document.querySelectorAll('.md3-navigation-drawer-item');
        drawerItems.forEach(item => {
            const isActive = item.getAttribute('data-page') === pageId;
            item.classList.toggle('active', isActive);
            item.setAttribute('aria-current', isActive ? 'page' : 'false');
        });

        // Set active state for bottom nav items
        const bottomNavItems = document.querySelectorAll('.md3-bottom-nav-item');
        bottomNavItems.forEach(item => {
            const isActive = item.getAttribute('data-page') === pageId;
            item.classList.toggle('active', isActive);
            item.setAttribute('aria-current', isActive ? 'page' : 'false');
        });

        // Set active state for rail items
        const railItems = document.querySelectorAll('.md3-navigation-rail-item');
        railItems.forEach(item => {
            const isActive = item.getAttribute('data-page') === pageId;
            item.classList.toggle('active', isActive);
            item.setAttribute('aria-current', isActive ? 'page' : 'false');
        });
    }

    updateBadge(itemSelector, count) {
        const items = document.querySelectorAll(itemSelector);
        items.forEach(item => {
            let badge = item.querySelector('.badge');
            
            if (count > 0) {
                if (!badge) {
                    badge = document.createElement('span');
                    badge.className = 'badge';
                    badge.setAttribute('aria-label', `${count} notifications`);
                    item.appendChild(badge);
                }
                badge.textContent = count > 99 ? '99+' : count.toString();
                badge.style.display = 'block';
            } else if (badge) {
                badge.style.display = 'none';
            }
        });
    }
}

// Initialize navigation when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    window.md3Navigation = new MD3Navigation();
});

// Handle page visibility changes
document.addEventListener('visibilitychange', () => {
    if (document.hidden && window.md3Navigation) {
        window.md3Navigation.closeDrawer();
    }
});

// Export for use in other modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = MD3Navigation;
}
