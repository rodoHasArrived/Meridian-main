Single date input with a calendar popover. Click the field to open; click a day to select and close automatically.

```jsx
<DatePicker label="Effective date" value={date} onChange={setDate} />
```

`value`/`onChange` use plain ISO strings (`YYYY-MM-DD`), not `Date` objects. Today gets a 2px accent outline in the grid; the selected day is solid accent-filled. Closes on outside click.
