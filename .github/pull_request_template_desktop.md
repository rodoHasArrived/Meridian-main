## Desktop Change Checklist

Use this checklist for PRs that touch:
- `src/Meridian.Wpf/**`
- `src/Meridian.Ui.Services/**`

### Validation

- [ ] Ran `make test-desktop-services`
- [ ] Ran `make build-wpf` (or CI equivalent)

### Reliability and lifecycle

- [ ] Navigation registrations updated for new pages/routes
- [ ] Event subscriptions are unsubscribed on unload/dispose
- [ ] Async operations support cancellation/shutdown correctly
- [ ] Config changes include persistence/migration considerations

### UX/behavior

- [ ] Keyboard shortcuts updated/tested where relevant
- [ ] Theme behavior verified where relevant
- [ ] Status/connection indicators validated where relevant

### Documentation

- [ ] Updated desktop documentation for workflow or behavior changes
- [ ] Added notes for any known limitations or follow-up work
