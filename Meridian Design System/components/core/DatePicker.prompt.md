Single date field with a calendar popover. Click the named button, or press Enter, Space, or ArrowDown, to open; select a day to close and return focus.

```jsx
<DatePicker label="Effective date" value={date} onChange={setDate} />
```

`value`/`onChange` use civil-date strings (`YYYY-MM-DD`), not timestamps or `Date` objects. Display and selection preserve that day across timezones. Invalid values stay explicit until replaced.

Arrow keys move the focused day; Home/End move within the week; PageUp/PageDown change month (Shift changes year). Escape closes only the calendar and restores the trigger. Today gets a 2px accent outline; the selected day uses accent fill with its contrasting ink. Outside click or moving focus away closes the popup.

Provide `label`, an external label with the same `id`, or an explicit ARIA name. Standard button IDs, descriptions, keyboard handlers, and disabled state are supported.
