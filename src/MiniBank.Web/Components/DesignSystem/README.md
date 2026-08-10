# MiniBank Design System

یک سیستم طراحی متمرکز و قابل توسعه برای پروژه MiniBank که بر پایه Material Design 3 و اصول دسترس‌پذیری ساخته شده است.

## 🎨 ویژگی‌ها

- **سازگار با Material Design 3**: استفاده از توکن‌های طراحی استاندارد
- **پشتیبانی کامل از RTL**: بهینه‌سازی شده برای زبان فارسی
- **ریسپانسیو**: طراحی مناسب برای موبایل و دسکتاپ
- **دسترس‌پذیر**: پیروی از استانداردهای WCAG 2.1
- **تم پویا**: پشتیبانی از حالت روز/شب
- **عملکرد بالا**: بهینه‌سازی شده برای Blazor Server

## 📁 ساختار

```
Components/DesignSystem/
├── DsButton.razor          # دکمه‌های استاندارد
├── DsCard.razor           # کارت‌های محتوایی
├── DsConfirm.razor        # دیالوگ‌های تایید
├── DsInput.razor          # ورودی‌های متنی
├── DsSheet.razor          # شیت‌های iOS-style
├── DsToggle.razor         # سوییچ‌های toggle
└── README.md              # این فایل

wwwroot/css/ds/
├── tokens.css             # متغیرهای طراحی
├── themes.css             # تم‌های روز/شب
└── utilities.css          # کلاس‌های یوتیلیتی
```

## 🚀 نحوه استفاده

### DsButton - دکمه‌ها

```razor
<!-- دکمه اصلی -->
<DsButton Variant="primary" OnClick="HandleClick">
    ذخیره
</DsButton>

<!-- دکمه با آیکون و حالت لودینگ -->
<DsButton Variant="secondary" 
          Icon="fa-solid fa-plus" 
          IsLoading="@_saving"
          Size="large">
    افزودن جدید
</DsButton>
```

**Props:**
- `Variant`: primary, secondary, danger, warn, outline, ghost
- `Size`: small, medium, large
- `Icon`: کلاس آیکون FontAwesome
- `IsLoading`: نمایش اسپینر
- `Disabled`: غیرفعال کردن

### DsInput - ورودی‌ها

```razor
<!-- ورودی متنی ساده -->
<DsInput Label="نام"
         @bind-Value="model.Name"
         Placeholder="نام خود را وارد کنید" />

<!-- ورودی با پیام خطا -->
<DsInput Label="ایمیل"
         Type="email"
         @bind-Value="model.Email"
         ErrorMessage="@GetEmailError()"
         HelperText="ایمیل معتبر وارد کنید" />

<!-- Textarea -->
<DsInput Label="توضیحات"
         Type="textarea"
         @bind-Value="model.Description"
         Rows="4" />
```

**Props:**
- `Type`: text, email, password, number, textarea
- `Label`: برچسب ورودی
- `Placeholder`: متن راهنما
- `ErrorMessage`: پیام خطا
- `HelperText`: متن کمکی
- `Disabled`: غیرفعال
- `Required`: اجباری

### DsCard - کارت‌ها

```razor
<!-- کارت ساده -->
<DsCard Title="عنوان کارت"
        Subtitle="زیرعنوان"
        Elevation="2">
    <Content>
        محتوای کارت
    </Content>
</DsCard>

<!-- کارت قابل کلیک با اکشن -->
<DsCard Title="کارت قابل کلیک"
        IsClickable="true"
        OnClick="HandleCardClick">
    <Actions>
        <DsButton Variant="outline" Size="small">ویرایش</DsButton>
        <DsButton Variant="ghost" Size="small">حذف</DsButton>
    </Actions>
</DsCard>
```

### DsToggle - سوییچ‌ها

```razor
<!-- سوییچ ساده -->
<DsToggle Label="فعال"
          @bind-Value="model.IsActive" />

<!-- سوییچ با توضیحات -->
<DsToggle Label="اعلان‌ها"
          Description="دریافت اعلان‌های ایمیلی"
          Icon="fa-solid fa-bell"
          @bind-Value="model.EmailNotifications" />
```

### DsSheet - شیت‌های iOS

```razor
<DsSheet Show="@_showSheet"
         Title="افزودن آیتم جدید"
         ConfirmText="ذخیره"
         CancelText="انصراف"
         ShowSpinner="@_saving"
         OnConfirm="HandleSave"
         OnCancel="HandleCancel">
    
    <!-- محتوای فرم -->
    <DsInput Label="نام" @bind-Value="model.Name" />
    <DsToggle Label="فعال" @bind-Value="model.IsActive" />
    
</DsSheet>
```

### DsConfirm - دیالوگ‌های تایید

```razor
<DsConfirm Show="@_showDeleteConfirm"
           Title="حذف آیتم"
           Message="آیا از حذف این آیتم اطمینان دارید؟"
           Variant="danger"
           ConfirmText="حذف"
           CancelText="انصراف"
           OnConfirm="ConfirmDelete"
           OnCancel="CancelDelete" />
```

## 🎨 سیستم تم

### فعال‌سازی تم تیره

```javascript
// تنظیم تم تیره
document.documentElement.setAttribute('data-theme', 'dark');

// تنظیم تم روشن
document.documentElement.setAttribute('data-theme', 'light');

// استفاده از تم سیستم
document.documentElement.removeAttribute('data-theme');
```

### متغیرهای تم

```css
/* استفاده از متغیرهای تم */
.my-component {
  background: var(--ds-surface);
  color: var(--ds-text-primary);
  border: 1px solid var(--ds-outline);
}
```

## 📱 کلاس‌های یوتیلیتی

### فاصله‌ها
```html
<div class="p-4 m-2">           <!-- padding: 1rem, margin: 0.5rem -->
<div class="px-6 py-4">        <!-- padding-x: 1.5rem, padding-y: 1rem -->
<div class="mt-8 mb-4">        <!-- margin-top: 2rem, margin-bottom: 1rem -->
```

### نمایش ریسپانسیو
```html
<div class="hidden md:block">   <!-- مخفی در موبایل، نمایش در دسکتاپ -->
<div class="block md:hidden">   <!-- نمایش در موبایل، مخفی در دسکتاپ -->
```

### Flexbox و Grid
```html
<div class="flex items-center justify-between">
<div class="grid grid-cols-1 md:grid-cols-2 gap-4">
```

## 🔧 بهترین شیوه‌ها

### 1. استفاده از توکن‌های طراحی
```css
/* ✅ درست */
.my-component {
  color: var(--ds-primary-500);
  border-radius: var(--ds-radius-md);
}

/* ❌ غلط */
.my-component {
  color: #3b82f6;
  border-radius: 8px;
}
```

### 2. ریسپانسیو اول
```razor
<!-- ✅ درست - موبایل اول -->
<div class="p-4 md:p-6 lg:p-8">
  <DsButton Size="small" class="w-full md:w-auto">
    ذخیره
  </DsButton>
</div>
```

### 3. دسترس‌پذیری
```razor
<!-- ✅ همیشه aria-label اضافه کنید -->
<DsButton Icon="fa-solid fa-trash" 
          Variant="danger"
          aria-label="حذف آیتم">
</DsButton>
```

### 4. مدیریت حالت
```csharp
// ✅ استفاده از الگوی async/await
private async Task HandleSave()
{
    _saving = true;
    StateHasChanged();
    
    try
    {
        await SaveData();
        _showSheet = false;
    }
    finally
    {
        _saving = false;
        StateHasChanged();
    }
}
```

## 🧪 تست

### تست واحد کامپوننت‌ها
```csharp
[Test]
public void DsButton_ShouldRenderCorrectVariant()
{
    // Arrange
    var component = RenderComponent<DsButton>(parameters => parameters
        .Add(p => p.Variant, "primary")
        .Add(p => p.ChildContent, "Test Button"));

    // Assert
    component.Find("button").Should().HaveClass("ds-btn-primary");
}
```

## 📈 عملکرد

### بهینه‌سازی‌های انجام شده:
- **Bulk Loading**: رفع مشکل N+1 در بارگذاری داده‌ها
- **CSS Isolation**: جلوگیری از تداخل استایل‌ها
- **Lazy Loading**: بارگذاری تنها در صورت نیاز
- **Minimal Re-renders**: کاهش رندرهای غیرضروری

## 🚀 آینده

### ویژگی‌های در دست توسعه:
- [ ] کامپوننت DataTable
- [ ] کامپوننت DatePicker فارسی
- [ ] سیستم Notification
- [ ] کامپوننت File Upload
- [ ] Animation System

## 🤝 مشارکت

برای افزودن کامپوننت جدید:

1. کامپوننت را در `Components/DesignSystem/` ایجاد کنید
2. CSS ایزوله اضافه کنید
3. مستندات را به‌روزرسانی کنید
4. تست‌های مربوطه را اضافه کنید

---

**نسخه:** 1.0.0  
**آخرین به‌روزرسانی:** 1403/07/04  
**سازگاری:** .NET 8, Blazor Server
