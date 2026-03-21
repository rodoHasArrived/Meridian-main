name: Pull Request
description: Submit a pull request to contribute to the Meridian
title: "[PR]: "
labels: ["needs-review"]

body:
  - type: markdown
    attributes:
      value: |
        Thank you for contributing to Meridian! Please fill out this template to help us review your PR.

  - type: textarea
    id: description
    attributes:
      label: Description
      description: Please describe the changes in this PR
      placeholder: This PR adds/fixes/improves...
    validations:
      required: true

  - type: dropdown
    id: type
    attributes:
      label: Type of Change
      description: What type of change does this PR introduce?
      multiple: true
      options:
        - Bug fix
        - New feature
        - Performance improvement
        - Code refactoring
        - Documentation update
        - Build/CI changes
        - Test improvements
        - Dependency updates
    validations:
      required: true

  - type: textarea
    id: motivation
    attributes:
      label: Motivation and Context
      description: Why is this change required? What problem does it solve?
      placeholder: This change is needed because...
    validations:
      required: true

  - type: textarea
    id: testing
    attributes:
      label: How Has This Been Tested?
      description: Please describe the tests you ran to verify your changes
      placeholder: |
        - [ ] Unit tests
        - [ ] Integration tests
        - [ ] Manual testing on Windows
        - [ ] Manual testing on Linux
        - [ ] Benchmarks
    validations:
      required: true

  - type: checkboxes
    id: checklist
    attributes:
      label: Checklist
      description: Please confirm the following
      options:
        - label: My code follows the code style of this project
          required: true
        - label: I have updated the documentation accordingly
          required: false
        - label: I have added tests to cover my changes
          required: false
        - label: All new and existing tests passed
          required: true
        - label: I have checked my code and corrected any misspellings
          required: true
        - label: My changes generate no new warnings
          required: true
        - label: I have run benchmarks for performance-sensitive changes (make bench-quick)
          required: false
        - label: I have checked docs/ai/ai-known-errors.md for relevant patterns
          required: false
        - label: I have updated CHANGELOG.md (if applicable)
          required: false

  - type: textarea
    id: breaking-changes
    attributes:
      label: Breaking Changes
      description: Does this PR introduce any breaking changes? If yes, please describe.
      placeholder: This PR introduces breaking changes in...

  - type: textarea
    id: related-issues
    attributes:
      label: Related Issues
      description: Link any related issues here
      placeholder: |
        Fixes #123
        Related to #456

  - type: textarea
    id: additional-notes
    attributes:
      label: Additional Notes
      description: Any additional information that reviewers should know
