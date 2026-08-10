// Enhanced Collapse Toggle functionality for Detailed Balance Sheet
window.toggleSection = function(sectionId) {
    const section = document.getElementById(sectionId);
    const header = section?.previousElementSibling;
    const icon = header?.querySelector('.toggle-icon');
    
    if (section) {
        const isExpanded = section.classList.contains('expanded');
        
        if (isExpanded) {
            // Collapse
            section.classList.remove('expanded');
            section.style.maxHeight = '0';
            section.style.opacity = '0';
            if (header) {
                header.setAttribute('aria-expanded', 'false');
                header.classList.remove('expanded');
            }
            if (icon) icon.style.transform = 'rotate(0deg)';
        } else {
            // Expand
            section.classList.add('expanded');
            section.style.maxHeight = section.scrollHeight + 'px';
            section.style.opacity = '1';
            if (header) {
                header.setAttribute('aria-expanded', 'true');
                header.classList.add('expanded');
            }
            if (icon) icon.style.transform = 'rotate(180deg)';
        }
    }
};

// Initialize all sections as collapsed on page load
document.addEventListener('DOMContentLoaded', function() {
    initializeCollapsibleSections();
});

// Initialize sections - can be called after dynamic content loads
window.initializeCollapsibleSections = function() {
    const sections = document.querySelectorAll('.subsection-content');
    sections.forEach(section => {
        if (!section.classList.contains('initialized')) {
            section.style.maxHeight = '0';
            section.style.opacity = '0';
            section.style.transition = 'max-height 0.3s ease, opacity 0.3s ease';
            section.classList.add('initialized');
            
            const header = section.previousElementSibling;
            if (header) {
                header.setAttribute('aria-expanded', 'false');
                header.style.cursor = 'pointer';
            }
        }
    });
};

// Expand all sections
window.expandAllSections = function() {
    const sections = document.querySelectorAll('.subsection-content');
    sections.forEach(section => {
        if (!section.classList.contains('expanded')) {
            const sectionId = section.id;
            if (sectionId) {
                toggleSection(sectionId);
            }
        }
    });
};

// Collapse all sections
window.collapseAllSections = function() {
    const sections = document.querySelectorAll('.subsection-content');
    sections.forEach(section => {
        if (section.classList.contains('expanded')) {
            const sectionId = section.id;
            if (sectionId) {
                toggleSection(sectionId);
            }
        }
    });
};
