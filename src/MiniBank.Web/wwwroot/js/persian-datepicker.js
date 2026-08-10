// Persian Date Picker Integration
window.initPersianDatePicker = function(elementId, dotNetRef) {
    try {
        const element = document.getElementById(elementId);
        if (!element) {
            console.error('Persian date picker element not found:', elementId);
            return;
        }

        // Simple Persian date picker implementation
        // Using a basic approach that works with Persian calendar
        
        // Set default Persian date (today)
        const today = new Date();
        const persianDate = convertToPersianDate(today);
        element.value = persianDate;
        
        // Add click event to show date picker
        element.addEventListener('click', function() {
            showPersianDatePicker(element, dotNetRef);
        });
        
        // Add focus event
        element.addEventListener('focus', function() {
            showPersianDatePicker(element, dotNetRef);
        });
        
    } catch (error) {
        console.error('Error initializing Persian date picker:', error);
    }
};

function convertToPersianDate(gregorianDate) {
    try {
        // Simple Persian date conversion
        const options = { 
            year: 'numeric', 
            month: '2-digit', 
            day: '2-digit',
            calendar: 'persian',
            numberingSystem: 'latn'
        };
        
        return new Intl.DateTimeFormat('fa-IR', options).format(gregorianDate);
    } catch (error) {
        // Fallback to simple format
        const year = gregorianDate.getFullYear();
        const month = String(gregorianDate.getMonth() + 1).padStart(2, '0');
        const day = String(gregorianDate.getDate()).padStart(2, '0');
        return `${year}/${month}/${day}`;
    }
}

function convertToGregorianDate(persianDateString) {
    try {
        // Simple conversion - in a real implementation, you'd use a proper Persian calendar library
        const parts = persianDateString.split('/');
        if (parts.length === 3) {
            const year = parseInt(parts[0]);
            const month = parseInt(parts[1]) - 1;
            const day = parseInt(parts[2]);
            
            // Approximate conversion (this is simplified)
            const gregorianYear = year > 1400 ? year - 621 : year + 621;
            return new Date(gregorianYear, month, day);
        }
    } catch (error) {
        console.error('Error converting Persian date:', error);
    }
    return new Date();
}

function showPersianDatePicker(element, dotNetRef) {
    // Create a simple date picker modal
    const modal = document.createElement('div');
    modal.className = 'persian-date-modal';
    modal.innerHTML = `
        <div class="persian-date-backdrop">
            <div class="persian-date-content">
                <div class="persian-date-header">
                    <h3>انتخاب تاریخ</h3>
                    <button class="persian-date-close">&times;</button>
                </div>
                <div class="persian-date-body">
                    <input type="date" class="gregorian-date-input" />
                    <div class="persian-date-display"></div>
                </div>
                <div class="persian-date-footer">
                    <button class="btn-cancel">انصراف</button>
                    <button class="btn-select">انتخاب</button>
                </div>
            </div>
        </div>
    `;
    
    // Add styles
    const style = document.createElement('style');
    style.textContent = `
        .persian-date-modal {
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            z-index: 9999;
        }
        .persian-date-backdrop {
            width: 100%;
            height: 100%;
            background: rgba(0, 0, 0, 0.5);
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .persian-date-content {
            background: white;
            border-radius: 12px;
            box-shadow: 0 10px 30px rgba(0, 0, 0, 0.3);
            max-width: 400px;
            width: 90%;
        }
        .persian-date-header {
            padding: 1rem 1.5rem;
            border-bottom: 1px solid #e5e7eb;
            display: flex;
            justify-content: space-between;
            align-items: center;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            border-radius: 12px 12px 0 0;
        }
        .persian-date-header h3 {
            margin: 0;
            font-size: 1.1rem;
        }
        .persian-date-close {
            background: none;
            border: none;
            color: white;
            font-size: 1.5rem;
            cursor: pointer;
            padding: 0;
            width: 30px;
            height: 30px;
            display: flex;
            align-items: center;
            justify-content: center;
            border-radius: 50%;
            transition: background 0.2s;
        }
        .persian-date-close:hover {
            background: rgba(255, 255, 255, 0.2);
        }
        .persian-date-body {
            padding: 1.5rem;
        }
        .gregorian-date-input {
            width: 100%;
            padding: 0.75rem;
            border: 2px solid #e5e7eb;
            border-radius: 8px;
            font-size: 1rem;
            margin-bottom: 1rem;
        }
        .persian-date-display {
            text-align: center;
            padding: 1rem;
            background: #f3f4f6;
            border-radius: 8px;
            font-size: 1.1rem;
            font-weight: 600;
            color: #374151;
        }
        .persian-date-footer {
            padding: 1rem 1.5rem;
            border-top: 1px solid #e5e7eb;
            display: flex;
            gap: 0.75rem;
            justify-content: flex-end;
        }
        .btn-cancel, .btn-select {
            padding: 0.5rem 1rem;
            border: none;
            border-radius: 6px;
            cursor: pointer;
            font-weight: 500;
        }
        .btn-cancel {
            background: #f3f4f6;
            color: #374151;
        }
        .btn-select {
            background: linear-gradient(135deg, #10b981 0%, #059669 100%);
            color: white;
        }
        .btn-cancel:hover {
            background: #e5e7eb;
        }
        .btn-select:hover {
            transform: translateY(-1px);
        }
    `;
    
    document.head.appendChild(style);
    document.body.appendChild(modal);
    
    const gregorianInput = modal.querySelector('.gregorian-date-input');
    const persianDisplay = modal.querySelector('.persian-date-display');
    const closeBtn = modal.querySelector('.persian-date-close');
    const cancelBtn = modal.querySelector('.btn-cancel');
    const selectBtn = modal.querySelector('.btn-select');
    
    // Set current date
    const today = new Date();
    gregorianInput.value = today.toISOString().split('T')[0];
    persianDisplay.textContent = convertToPersianDate(today);
    
    // Update Persian display when Gregorian date changes
    gregorianInput.addEventListener('change', function() {
        const selectedDate = new Date(this.value);
        persianDisplay.textContent = convertToPersianDate(selectedDate);
    });
    
    // Close modal
    function closeModal() {
        document.body.removeChild(modal);
        document.head.removeChild(style);
    }
    
    closeBtn.addEventListener('click', closeModal);
    cancelBtn.addEventListener('click', closeModal);
    
    // Select date
    selectBtn.addEventListener('click', function() {
        const selectedDate = new Date(gregorianInput.value);
        const persianDate = convertToPersianDate(selectedDate);
        const gregorianDate = selectedDate.toISOString();
        
        element.value = persianDate;
        
        // Notify Blazor component
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnPersianDateSelected', persianDate, gregorianDate);
        }
        
        closeModal();
    });
    
    // Close on backdrop click
    modal.querySelector('.persian-date-backdrop').addEventListener('click', function(e) {
        if (e.target === this) {
            closeModal();
        }
    });
}
