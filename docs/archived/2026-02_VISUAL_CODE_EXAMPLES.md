# Visual Code Examples - UI Improvements

This document shows the actual code changes and visual effects added to the Meridian UI.

## 1. Provider Card Animations

### Before:
```css
.provider-card {
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  padding: 16px;
  position: relative;
}
```

### After:
```css
.provider-card {
  border: 1px solid #e0e0e0;
  border-radius: 8px;
  padding: 16px;
  position: relative;
  transition: all 0.3s ease;              /* ← Smooth transitions */
  background: white;
}

.provider-card:hover {
  transform: translateY(-2px);             /* ← Lift on hover */
  box-shadow: 0 8px 16px rgba(0,0,0,0.1); /* ← Enhanced shadow */
}

.provider-card.status-expiring {
  animation: pulse-warning 2s ease-in-out infinite; /* ← Pulsing effect */
}

@keyframes pulse-warning {
  0%, 100% { box-shadow: 0 0 0 0 rgba(245, 158, 11, 0.4); }
  50% { box-shadow: 0 0 0 8px rgba(245, 158, 11, 0); }
}
```

**Visual Effect:** Cards now lift up smoothly when hovered, and expiring credentials pulse with an orange glow.

---

## 2. Animated Status Badges

### Before:
```css
.status-badge {
  padding: 4px 12px;
  border-radius: 20px;
  font-size: 12px;
  font-weight: 500;
}
```

### After:
```css
.status-badge {
  padding: 4px 12px;
  border-radius: 20px;
  font-size: 12px;
  font-weight: 500;
  display: inline-flex;           /* ← Flexbox for alignment */
  align-items: center;
  gap: 6px;
  transition: all 0.2s;
}

.status-badge::before {           /* ← Animated dot indicator */
  content: '';
  width: 8px;
  height: 8px;
  border-radius: 50%;
  display: inline-block;
}

.status-badge.valid::before {
  background: #10b981;
  box-shadow: 0 0 6px #10b981;   /* ← Glowing effect */
  animation: pulse-green 2s ease-in-out infinite;
}

@keyframes pulse-green {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}
```

**Visual Effect:** Status badges now have a pulsing dot indicator that draws attention to valid/active statuses.

---

## 3. Search and Filter Bar

### New Feature (Added):
```html
<!-- Statistics Bar -->
<div class="stats-bar" id="statsBar">
  <div class="stat-item valid">
    <div>
      <div class="stat-value" id="validCount">0</div>
      <div class="stat-label">Valid</div>
    </div>
  </div>
  <!-- More stat items... -->
</div>

<!-- Search and Filter Bar -->
<div class="search-filter-bar">
  <input type="text" class="search-input" id="searchInput" 
         placeholder="Search providers..." 
         oninput="filterProviders()">
  <div class="filter-group">
    <button class="filter-btn active" onclick="setStatusFilter('all')">All</button>
    <button class="filter-btn" onclick="setStatusFilter('valid')">Valid</button>
    <!-- More filters... -->
  </div>
</div>
```

```css
.search-input {
  flex: 1;
  min-width: 250px;
  padding: 10px 16px 10px 40px;
  border: 2px solid #e0e0e0;
  border-radius: 8px;
  transition: all 0.2s;
  background: white url('data:image/svg+xml;...') no-repeat 12px center;
}

.search-input:focus {
  border-color: #667eea;                    /* ← Accent color on focus */
  box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.1); /* ← Focus ring */
}
```

**Visual Effect:** Professional search bar with icon and real-time filtering, plus statistics showing counts at a glance.

---

## 4. Toast Notifications

### New Feature (Added):
```javascript
function showToast(title, message, type = 'success') {
  const container = document.getElementById('toastContainer');
  const toast = document.createElement('div');
  toast.className = `toast ${type}`;
  
  const icons = {
    success: '<!-- SVG checkmark -->',
    error: '<!-- SVG X -->',
    warning: '<!-- SVG warning -->'
  };
  
  toast.innerHTML = `
    <div class="toast-icon">${icons[type]}</div>
    <div class="toast-content">
      <div class="toast-title">${title}</div>
      <div class="toast-message">${message}</div>
    </div>
    <button class="toast-close" onclick="removeToast('${id}')">&times;</button>
  `;
  
  container.appendChild(toast);
  setTimeout(() => removeToast(id), 5000); /* Auto-dismiss */
}
```

```css
.toast {
  background: white;
  border-radius: 8px;
  padding: 16px;
  box-shadow: 0 8px 16px rgba(0,0,0,0.2);
  animation: slideIn 0.3s ease;
  border-left: 4px solid #667eea;
}

@keyframes slideIn {
  from {
    transform: translateX(100%);
    opacity: 0;
  }
  to {
    transform: translateX(0);
    opacity: 1;
  }
}
```

**Visual Effect:** Notifications slide in from the right, display for 5 seconds, then slide out. Color-coded by type.

---

## 5. Enhanced Dashboard Cards

### Before:
```css
.card {
  background: white;
  border-radius: 12px;
  padding: 24px;
  box-shadow: 0 4px 6px rgba(0,0,0,0.1);
}
```

### After:
```css
.card {
  background: white;
  border-radius: 12px;
  padding: 24px;
  box-shadow: 0 4px 6px rgba(0,0,0,0.1);
  transition: all 0.3s ease;               /* ← Smooth transitions */
  border: 1px solid transparent;
  animation: fadeInUp 0.5s ease backwards; /* ← Fade in on load */
}

.card:hover {
  box-shadow: 0 8px 16px rgba(0,0,0,0.15);   /* ← Enhanced shadow */
  transform: translateY(-2px);                /* ← Lift effect */
  border-color: rgba(102, 126, 234, 0.2);    /* ← Accent border */
}

.card h2::before {                           /* ← Accent bar */
  content: '';
  width: 4px;
  height: 24px;
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  border-radius: 2px;
}

@keyframes fadeInUp {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

/* Staggered animation */
.row .card:nth-child(1) { animation-delay: 0.1s; }
.row .card:nth-child(2) { animation-delay: 0.2s; }
.row .card:nth-child(3) { animation-delay: 0.3s; }
```

**Visual Effect:** Cards fade in with a staggered timing, have a gradient accent bar, and lift up on hover.

---

## 6. Gradient Buttons with Ripple Effect

### Before:
```css
.btn-primary {
  background: #667eea;
  color: white;
}

.btn-primary:hover {
  background: #5a67d8;
}
```

### After:
```css
button {
  position: relative;
  overflow: hidden;
  transition: all 0.3s ease;
}

button::before {                              /* ← Ripple effect */
  content: '';
  position: absolute;
  top: 50%;
  left: 50%;
  width: 0;
  height: 0;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.3);
  transform: translate(-50%, -50%);
  transition: width 0.6s, height 0.6s;
}

button:hover::before {
  width: 300px;                               /* ← Expand on hover */
  height: 300px;
}

.btn-primary {
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); /* ← Gradient */
  color: white;
  box-shadow: 0 2px 8px rgba(102, 126, 234, 0.3);
}

.btn-primary:hover {
  box-shadow: 0 4px 16px rgba(102, 126, 234, 0.5); /* ← Enhanced shadow */
  transform: translateY(-2px);                       /* ← Lift effect */
}
```

**Visual Effect:** Buttons have gradient backgrounds and create a ripple effect when clicked, with smooth hover animations.

---

## 7. Shimmer Progress Bars

### Before:
```html
<div style="height: 100%; width: 0%; background: linear-gradient(90deg, #667eea, #764ba2);"></div>
```

### After:
```css
.progress-container {
  position: relative;
  background: #f7fafc;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: inset 0 2px 4px rgba(0,0,0,0.1);
}

.progress-bar {
  height: 100%;
  background: linear-gradient(90deg, #667eea 0%, #764ba2 50%, #667eea 100%);
  background-size: 200% 100%;                /* ← Shimmer setup */
  animation: shimmer 2s infinite linear;      /* ← Animated gradient */
  transition: width 0.3s ease;
}

.progress-bar::after {                        /* ← Sliding shine */
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: linear-gradient(90deg, transparent, rgba(255,255,255,0.3), transparent);
  animation: slide 1.5s infinite;
}

@keyframes shimmer {
  0% { background-position: 200% 0; }
  100% { background-position: -200% 0; }
}

@keyframes slide {
  0% { transform: translateX(-100%); }
  100% { transform: translateX(100%); }
}
```

**Visual Effect:** Progress bars have an animated shimmer effect with a sliding shine, making them look dynamic and modern.

---

## 8. Enhanced Metric Cards

### Before:
```css
.metric {
  text-align: center;
  padding: 12px;
  background: #f7fafc;
  border-radius: 8px;
}

.metric-value {
  font-size: 24px;
  font-weight: 700;
  color: #667eea;
}
```

### After:
```css
.metric {
  text-align: center;
  padding: 12px;
  background: linear-gradient(135deg, #f7fafc 0%, #edf2f7 100%); /* ← Gradient */
  border-radius: 8px;
  transition: all 0.3s ease;
  border: 2px solid transparent;
  position: relative;
  overflow: hidden;
}

.metric::before {                              /* ← Top accent bar */
  content: '';
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  height: 3px;
  background: linear-gradient(90deg, #667eea 0%, #764ba2 100%);
  opacity: 0;
  transition: opacity 0.3s ease;
}

.metric:hover {
  transform: translateY(-4px);                 /* ← Lift effect */
  box-shadow: 0 8px 16px rgba(102, 126, 234, 0.2);
  border-color: #667eea;
}

.metric:hover::before {
  opacity: 1;                                  /* ← Show accent bar */
}

.metric-value {
  font-size: 24px;
  font-weight: 700;
  color: #667eea;
  font-family: 'SF Mono', 'Consolas', monospace; /* ← Monospace font */
  transition: all 0.3s ease;
}

.metric:hover .metric-value {
  transform: scale(1.1);                       /* ← Scale on hover */
  color: #5a67d8;
}
```

**Visual Effect:** Metric cards have gradient backgrounds, lift on hover, reveal a top accent bar, and scale the value for emphasis.

---

## 9. Interactive Table Rows

### Before:
```css
th, td {
  border-bottom: 1px solid #eee;
  padding: 12px 8px;
  text-align: left;
}
```

### After:
```css
table {
  border-collapse: separate;
  border-spacing: 0;
  border-radius: 8px;
  overflow: hidden;
}

th {
  background: linear-gradient(135deg, #f7fafc 0%, #edf2f7 100%);
  color: #4a5568;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  font-size: 12px;
  border-bottom: 2px solid #e2e8f0;
}

tbody tr {
  transition: all 0.2s ease;
  border-bottom: 1px solid #f7fafc;
}

tbody tr:hover {
  background: linear-gradient(90deg, rgba(102, 126, 234, 0.05) 0%, transparent 100%);
  transform: scale(1.01);                      /* ← Slight scale */
  box-shadow: 0 2px 8px rgba(0,0,0,0.05);
}
```

**Visual Effect:** Table headers have gradient backgrounds, rows highlight with a gradient and lift slightly on hover.

---

## 10. Status Indicator Animations

### Dashboard Connection Status:
```css
.status-connected {
  background: #c6f6d5;
  color: #22543d;
  animation: pulse-green 2s ease-in-out infinite;
}

@keyframes pulse-green {
  0%, 100% { box-shadow: 0 0 0 0 rgba(72, 187, 120, 0.4); }
  50% { box-shadow: 0 0 0 8px rgba(72, 187, 120, 0); }
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: currentColor;
  animation: blink 1.5s ease-in-out infinite;
}

@keyframes blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}
```

**Visual Effect:** Connected status badges pulse with a green glow, and the dot indicator blinks to show activity.

---

## Summary of Effects

| Element | Effect | Duration | Trigger |
|---------|--------|----------|---------|
| Cards | Fade in, lift on hover | 0.5s / 0.3s | Load / Hover |
| Buttons | Ripple effect, gradient | 0.6s | Click |
| Status Badges | Pulsing dot, glow | 2s loop | Always |
| Progress Bars | Shimmer, sliding shine | 2s / 1.5s loops | Always |
| Metric Cards | Lift, scale value | 0.3s | Hover |
| Table Rows | Gradient highlight, scale | 0.2s | Hover |
| Toast Notifications | Slide in/out | 0.3s | Show/Hide |
| Filter Buttons | Color transition | 0.2s | Click |

All animations use CSS transforms and opacity for GPU acceleration, ensuring smooth 60fps performance.
