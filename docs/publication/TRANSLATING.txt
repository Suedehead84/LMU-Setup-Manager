# Translations / Übersetzungen

German and English are complete (731 keys, 449 safety-protected). Browser and tray
language are separate. Contributions under MIT with sufficient rights; no exclusive
assignment. [Contribution guide](CONTRIBUTING.md). Source/build instructions are
available in the prepared full source snapshot, not a resources-only scheme.

Add a validated locale JSON using en.json as reference. Preserve canonical IDs,
physical numbers, placeholders and user names. Partial languages fall back to the
complete English safety bundle; never mix fragments of security/restore warnings.
Keep restore confirmation literals unchanged. Do not add raw private screenshots,
logs, credentials or unlicensed translations. Run check-translations.cjs and the
full build; test keyboard, narrow screens, dialogs and language switching with
isolated data. English fallback is explicit; machine translation is not manual QA.

Public repository URL and translator contact are pending actual release creation;
no destination is invented. User-owned setup names/notes are never translated.
