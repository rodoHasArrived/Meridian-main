# Interactive Brokers `server-version`

- Result: `bounded`
- Bound by: version bounds are tested in repo, but a live handshake capture is still manual.
- Repo evidence: `IBApiVersionValidatorTests` and `IBApiVersionValidator`.
- Wave 1 treatment: treat higher server versions as bounded forward-compatibility, not implied validation.
