# Avalonia UI Design Style Guide
**Modern Fluent-Inspired Visual Design for .NET Applications**

---

## 1. Overview

This style guide defines a consistent visual language for Avalonia UI applications, combining modern Fluent design principles with clean, professional aesthetics. It provides color schemes, typography, spacing, and component styling guidelines for building cohesive user interfaces.

---

## 2. Color Palette

### Light Theme

| Element | Color | Usage |
|---------|-------|-------|
| **Background - Outer Frame** | `#F3F3F3` | Window background, outer container |
| **Background - Panels** | `#FFFFFF` | Content panels, cards, main areas |
| **Background - Sidebar** | `#F0F0F0` | Side navigation, toolbars |
| **Border - Light** | `#D0D0D0` | Panel separators, subtle borders |
| **Border - Section** | `#E0E0E0` | Section dividers, group separators |
| **Accent - Primary** | `#0078D7` | Active states, focus indicators, links |
| **Text - Primary** | `#202020` | Main content text |
| **Selection - Background** | `#CDE8FF` | Selected items, highlighted rows |
| **Hover - Background** | `#EAF6FF` | Hover state for interactive elements |

### Dark Theme

| Element | Color | Usage |
|---------|-------|-------|
| **Background - Outer Frame** | `#1E1E1E` | Window background, outer container |
| **Background - Panels** | `#252526` | Content panels, cards, main areas |
| **Background - Sidebar** | `#2D2D30` | Side navigation, toolbars |
| **Border - Dark** | `#3C3C3C` | Panel separators, subtle borders |
| **Border - Section** | `#404040` | Section dividers, group separators |
| **Accent - Primary** | `#0078D7` | Active states, focus indicators, links |
| **Text - Primary** | `#CCCCCC` | Main content text |
| **Selection - Background** | `#094771` | Selected items, highlighted rows |
| **Hover - Background** | `#2A2D2E` | Hover state for interactive elements |

---

## 3. Typography

### Font Family
- **Primary:** Segoe UI (Windows), Inter (cross-platform), San Francisco (macOS)
- **Monospace:** Consolas, Courier New, monospace (for technical content, logs)

### Font Sizes
| Element | Size | Weight | Usage |
|---------|------|--------|-------|
| **Heading 1** | 24px | SemiBold | Main window titles |
| **Heading 2** | 18px | SemiBold | Section headers |
| **Heading 3** | 15px | SemiBold | Subsection headers |
| **Body Text** | 13px | Regular | Main content, labels |
| **Small Text** | 11px | Regular | Status bars, hints, metadata |
| **Button Text** | 13px | SemiBold | Button labels, menu items |

---

## 4. Spacing and Layout

### Spacing Scale
- **XS:** 4px - Tight spacing within components
- **SM:** 8px - Component padding
- **MD:** 12px - Default spacing between elements
- **LG:** 16px - Section spacing
- **XL:** 24px - Major section breaks
- **XXL:** 32px - Window margins

### Border Radius
- **Small Components:** 4px (buttons, inputs)
- **Panels and Cards:** 6px (content panels, dialogs)
- **Large Containers:** 8px (main windows, modals)

### Border Width
- **Subtle Separator:** 1px - Default borders
- **Active Indicator:** 2px - Focus/active state borders
- **Emphasis Border:** 3px - Strong visual boundaries

---

## 5. Component Styles

### Panels and Containers
```
Background: #FFFFFF (light) / #252526 (dark)
Border: 1px solid #D0D0D0 (light) / #3C3C3C (dark)
Border-Radius: 6px
Padding: 12px
Shadow: 0 2px 8px rgba(0, 0, 0, 0.1)
```

### Buttons

#### Primary Button
```
Background: #0078D7
Text Color: #FFFFFF
Border: none
Border-Radius: 4px
Padding: 8px 16px
Hover: #005A9E (darker blue)
Active: #004578 (even darker)
```

#### Secondary Button
```
Background: #F0F0F0 (light) / #3C3C3C (dark)
Text Color: #202020 (light) / #CCCCCC (dark)
Border: 1px solid #D0D0D0 (light) / #555555 (dark)
Border-Radius: 4px
Padding: 8px 16px
Hover: #E5E5E5 (light) / #464646 (dark)
```

#### Disabled Button
```
Background: #F3F3F3 (light) / #2D2D30 (dark)
Text Color: #A0A0A0
Border: 1px solid #E0E0E0 (light) / #3C3C3C (dark)
Cursor: not-allowed
Opacity: 0.6
```

### Text Input Fields
```
Background: #FFFFFF (light) / #1E1E1E (dark)
Border: 1px solid #D0D0D0 (light) / #3C3C3C (dark)
Border-Radius: 4px
Padding: 6px 10px
Font-Size: 13px
Focus Border: 2px solid #0078D7
```

### Lists and DataGrids
```
Background: #FFFFFF (light) / #252526 (dark)
Border: 1px solid #D0D0D0 (light) / #3C3C3C (dark)
Row Height: 28px
Alternate Row: #FAFAFA (light) / #2D2D30 (dark)
Selection: #CDE8FF (light) / #094771 (dark)
Hover: #EAF6FF (light) / #2A2D2E (dark)
```

### Active/Focus Indicators
```
Border-Left: 2px solid #0078D7
Background Tint: #EAF6FF (light) / #2A2D2E (dark)
Transition: 150ms ease
```

### Status Bar
```
Background: #F0F0F0 (light) / #2D2D30 (dark)
Border-Top: 1px solid #D0D0D0 (light) / #3C3C3C (dark)
Height: 24px
Padding: 4px 12px
Font-Size: 11px
Text Color: #606060 (light) / #A0A0A0 (dark)
```

### Sidebar/Navigation
```
Background: #F0F0F0 (light) / #2D2D30 (dark)
Border-Right: 1px solid #E0E0E0 (light) / #404040 (dark)
Width: 200px (default, resizable)
Section Separator: 1px solid #E0E0E0 (light) / #404040 (dark)
Active Item Border-Left: 2px solid #0078D7
```

### Tooltips
```
Background: #2D2D30
Text Color: #FFFFFF
Border: 1px solid #454545
Border-Radius: 4px
Padding: 6px 10px
Font-Size: 11px
Shadow: 0 4px 12px rgba(0, 0, 0, 0.3)
```

---

## 6. Visual Effects

### Drop Shadows
- **Subtle:** `0 2px 8px rgba(0, 0, 0, 0.1)` - Panels, cards
- **Medium:** `0 4px 12px rgba(0, 0, 0, 0.15)` - Dialogs, modals
- **Prominent:** `0 8px 24px rgba(0, 0, 0, 0.2)` - Floating windows, popups

### Animations and Transitions
- **Fast:** 100ms - Quick state changes (hover, clicks)
- **Normal:** 150ms - Default transitions (selection, focus)
- **Slow:** 300ms - Major state changes (panel open/close, page transitions)
- **Easing:** ease-in-out for smooth motion

### Opacity States
- **Disabled:** 0.6
- **Hover:** 0.85 (for icons, subtle elements)
- **Active:** 1.0

---

## 7. Icon Guidelines

### Icon Set
- Use **Fluent System Icons** or **Segoe MDL2 Assets** for consistency
- Maintain 16px or 20px size for toolbar icons
- Use 24px for prominent action buttons
- Ensure icons have clear meaning and follow platform conventions

### Icon Colors
- **Default:** `#606060` (light) / `#A0A0A0` (dark)
- **Hover:** `#202020` (light) / `#CCCCCC` (dark)
- **Active/Selected:** `#0078D7` (accent color)
- **Disabled:** `#A0A0A0` with 0.6 opacity

---

## 8. Layout Patterns

### Window Structure
```
┌─────────────────────────────────────────┐
│ Title Bar                               │
├─────────────────────────────────────────┤
│ Menu Bar (optional)                     │
├─────────────────────────────────────────┤
│ Toolbar / Quick Access (optional)       │
├───────┬─────────────────────────────────┤
│ Side  │ Main Content Area               │
│ bar   │                                 │
│ (opt) │                                 │
├───────┴─────────────────────────────────┤
│ Status Bar                              │
└─────────────────────────────────────────┘
```

### Responsive Breakpoints
- **Compact:** < 800px width - Hide sidebar, stack panels
- **Normal:** 800px - 1200px - Standard layout
- **Wide:** > 1200px - Expand panels, show additional details

---

## 9. Theme Support

### Automatic Theme Switching
- Detect system theme preference (light/dark)
- Provide manual override in application settings
- Apply theme consistently across all windows and components
- Smooth transition between themes (300ms fade)

### Theme Variables
Define all colors as theme-aware variables/resources:
```xml
<Color x:Key="BackgroundColor">#FFFFFF</Color>
<Color x:Key="BackgroundColorDark">#252526</Color>
<Color x:Key="AccentColor">#0078D7</Color>
<Color x:Key="TextColor">#202020</Color>
<Color x:Key="TextColorDark">#CCCCCC</Color>
```

---

## 10. Accessibility

### Contrast Ratios
- **Normal Text:** Minimum 4.5:1 contrast ratio
- **Large Text:** Minimum 3:1 contrast ratio
- **Interactive Elements:** Clear focus indicators with 2px borders

### Keyboard Navigation
- All interactive elements accessible via Tab key
- Focus indicators visible and clear (2px accent border)
- Logical tab order following visual layout
- Support for standard keyboard shortcuts

### Screen Reader Support
- Proper ARIA labels and roles
- Descriptive button and link text
- Status updates announced appropriately

---

## 11. Dialog and Modal Windows

### Modal Dialogs
```
Background: #FFFFFF (light) / #252526 (dark)
Border: 1px solid #D0D0D0 (light) / #3C3C3C (dark)
Border-Radius: 8px
Padding: 24px
Shadow: 0 8px 24px rgba(0, 0, 0, 0.3)
Max-Width: 600px
Backdrop: rgba(0, 0, 0, 0.5) overlay
```

### Dialog Buttons
- Right-aligned button group
- Primary action on the right
- Cancel/Secondary on the left
- 8px spacing between buttons
- Standard button styling (see Component Styles)

---

## 12. Best Practices

### Do's
✅ Use consistent spacing throughout the application
✅ Maintain clear visual hierarchy with typography and color
✅ Provide clear hover and focus states for all interactive elements
✅ Use accent color sparingly for emphasis
✅ Support both light and dark themes
✅ Test for accessibility and keyboard navigation
✅ Use subtle animations for smooth transitions
✅ Provide clear visual feedback for user actions

### Don'ts
❌ Don't mix multiple accent colors without purpose
❌ Don't use thin borders (< 1px) - they're hard to see
❌ Don't create overly complex layouts with too many nested panels
❌ Don't use bright, saturated colors for large areas
❌ Don't animate everything - be selective
❌ Don't ignore contrast ratios for accessibility
❌ Don't hardcode colors - use theme-aware resources

---

## 13. Avalonia-Specific Implementation

### Resource Dictionary Structure
```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceInclude Source="/Styles/Colors.axaml"/>
            <ResourceInclude Source="/Styles/Buttons.axaml"/>
            <ResourceInclude Source="/Styles/TextBoxes.axaml"/>
            <ResourceInclude Source="/Styles/DataGrids.axaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

### StyleInclude for DataGrid
```xml
<Window.Styles>
    <StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"/>
</Window.Styles>
```

### Theme Binding Example
```xml
<Border Background="{DynamicResource BackgroundBrush}"
        BorderBrush="{DynamicResource BorderBrush}"
        BorderThickness="1"
        CornerRadius="6"
        Padding="12">
    <!-- Content -->
</Border>
```

---

## 14. Summary

This style guide provides a foundation for building modern, consistent Avalonia UI applications with:
- **Professional color palette** for light and dark themes
- **Clear typography** hierarchy and spacing guidelines
- **Comprehensive component** styling for common UI elements
- **Accessibility** considerations for inclusive design
- **Best practices** for maintainable, scalable interfaces

Apply these guidelines consistently to create cohesive, user-friendly applications that feel native across Windows, Linux, and macOS platforms.

---

**End of Document**
