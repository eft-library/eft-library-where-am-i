# AGENTS.md

## Project goal

This repository contains the Windows desktop companion client for EFT Library. The long-term goal is to make the core player experience usable without opening the EFT Library website while keeping the existing web frontend available for discovery and browser users.

The desktop client should reuse the existing server API, reproduce relevant frontend behavior as native WPF features, and add desktop-only automation based on user-created screenshots and read-only game logs. Do not embed the production website as the final implementation unless a task explicitly asks for WebView2.

The desktop client is not a replacement for every EFT Library website page. Its scope is the complete live-map experience plus desktop-only gameplay companion features such as quest auto-sync, player tracking, item recognition, party sharing, and overlays.

## Product scope

The intended desktop experience includes:

- Native interactive maps and the relevant quest, item, marker, search, and filter features from the web frontend
- Automatic player-position and direction updates from Escape from Tarkov screenshots
- Automatic quest acceptance, progress, and completion updates when supported by observable game-log events
- In-raid item identification initiated by an explicit user action, such as a configured Shift+Click workflow
- Optional synchronization of quest state, settings, and position through the EFT Library server
- Future support for party position sharing, a second-screen client, and a lightweight desktop overlay

The following general website areas are out of scope unless the user explicitly expands the product boundary:

- Home, discovery, SEO, news, and patch-note pages
- Community posts, comments, moderation, and general social features
- Minigames and unrelated browser utilities
- Full website administration
- Any page that does not materially support the in-raid live-map companion experience

Quest, item, hideout, trader, price, story, and account data may be used only to the extent needed by live-map and gameplay-companion workflows. Do not port their complete website pages by default.

Do not claim that every quest-progress field is detectable until representative game logs prove it. Unsupported or ambiguous progress must remain manual and be represented honestly in the UI.

## Repository layout

- `where-am-i/`: .NET 10 WPF application
- `where-am-i/Views/`: XAML views and minimal code-behind
- `where-am-i/ViewModels/`: presentation logic and commands
- `where-am-i/Services/`: API, filesystem, and platform integrations
- `where-am-i/Models/`: request, response, and domain models
- `docs/`: user-facing documentation and images

## Related projects

The authoritative paths for related projects will be supplied by the user before cross-project work begins.

- Server API repository: `/Users/poeynus/Desktop/work/my/eft-library-back`
- Web frontend repository: `/Users/poeynus/Desktop/work/my/eft-library-front`

When paths are supplied, read the related repository's `AGENTS.md` before inspecting or changing its files. Treat the server implementation and frontend behavior as references; do not modify another repository unless the user explicitly includes it in the requested scope.

## Architecture rules

- Keep the application native WPF using MVVM-style separation.
- Keep view code-behind limited to view lifecycle and UI-only behavior.
- Put HTTP communication in services, not view models.
- Use typed request/response models instead of parsing JSON directly in views or view models.
- Centralize API base addresses, timeouts, error mapping, and authentication handling.
- Keep screenshot detection and local position rendering independent from server latency.
- Treat local state as the immediate source for in-raid interactions; synchronize with the server asynchronously.
- Represent locally detected changes as typed, idempotent domain events before applying or uploading them.
- Keep log watching, log parsing, screen capture, OCR, item matching, map rendering, and server synchronization in separate services.
- Version persisted state and sync payloads so migrations and backward compatibility can be handled deliberately.
- Reuse the server's existing API contracts. Never guess a contract when the server source or observed frontend request can establish it.
- Port behavior and coordinate calculations from the frontend, but adapt interaction and layout for desktop use.
- Preserve the current screenshot-folder discovery and monitoring behavior unless the task explicitly changes it.

## Game-integration safety boundary

All game integration must remain external, passive, and read-only.

- Do not read or write the game process's memory.
- Do not inject DLLs, hook the renderer, install a kernel driver, or modify game/anti-cheat files.
- Do not automate gameplay input, create macros, or synthesize clicks and keystrokes for the game.
- Do not inspect network traffic or bypass authentication, encryption, anti-cheat, or access controls.
- Read only user-accessible logs and screenshots needed for the requested feature.
- Implement an overlay, if requested, as a separate transparent desktop window without attaching to the game process.
- Item recognition must use an explicit user trigger and screen capture/OCR or image matching; it must not inspect game memory.
- Make each watcher, capture, overlay, and synchronization feature individually visible and disableable.
- Never describe the application as guaranteed ban-safe. Document what it reads and how it interacts with the game.

If a proposed implementation crosses this boundary, stop and request a safer product decision instead of implementing it.

## Detection and synchronization rules

- Preserve raw local evidence only as long as needed for parsing, retry, or user-approved diagnostics.
- Upload parsed events instead of complete log files or screenshots whenever possible.
- Never upload screen captures or log excerpts without clear user disclosure and consent.
- Record the parser version and stable source-event identity so duplicate filesystem events do not duplicate quest progress.
- Expect logs to be appended, rotated, truncated, localized, delayed, or partially written.
- Treat OCR and fuzzy item matches as uncertain. Expose confidence and require confirmation below an agreed threshold.
- Resolve sync conflicts explicitly; do not silently overwrite newer server or local progress.
- Keep a bounded local retry queue for transient network failures and avoid blocking the UI thread.

## Security and privacy

- Never commit credentials, access tokens, cookies, private API keys, production dumps, or user information.
- Do not log full authentication tokens or unnecessary personal information.
- Store sensitive local state with an appropriate Windows-protected mechanism when authentication is introduced.
- Send only the data required by the existing API contract.
- Treat remote text, filenames, map metadata, and API responses as untrusted input.
- Give users a clear way to inspect and delete locally persisted application data.
- Redact Windows usernames and unrelated log content from diagnostics.

## UI and product behavior

- Korean is the default user-facing language unless a task introduces localization.
- Match the existing EFT Library visual identity and desktop theme.
- Keep loading, empty, offline, and error states explicit.
- Network failure must not crash the application or stop local screenshot monitoring.
- Avoid requiring the browser for features that have been migrated into the desktop client.
- Remote images required for normal startup should eventually be packaged or cached so transient network failure does not break the UI.
- Distinguish locally detected, pending-sync, synced, uncertain, and manually entered state.
- Keep monitoring and synchronization status visible without interrupting gameplay.
- Require user confirmation when an item or quest event cannot be identified confidently.

## Development workflow

Before editing:

1. Inspect the affected view, view model, service, and model together.
2. For frontend parity work, identify the frontend screen, API calls, state, assets, and coordinate transformations.
3. For API work, verify request and response contracts against the server source.
4. For log-driven work, collect sanitized representative samples and document which events are actually observable.
5. For capture/OCR work, define supported resolutions, scaling modes, languages, UI states, and confidence thresholds.

After editing:

1. Run `dotnet build where-am-i/where-am-i.slnx` on a Windows-capable .NET environment.
2. Test the changed flow manually when it depends on WPF, Windows registry access, folder dialogs, or `FileSystemWatcher`.
3. Confirm existing screenshot detection, folder persistence, and error handling still work.
4. Update `docs/USER_GUIDE.md` when user-visible behavior changes.
5. Test duplicate, partial, rotated, and malformed inputs for file-based detectors.
6. Confirm the feature stays usable offline and synchronizes correctly after reconnection where applicable.

If the current environment cannot build Windows WPF, report that limitation clearly and still perform all available static checks.

## Change discipline

- Make incremental, reviewable changes; do not attempt the entire frontend migration in one patch.
- Preserve unrelated user changes in the working tree.
- Do not change server endpoints or frontend behavior merely to simplify the desktop implementation.
- Avoid adding dependencies when the .NET/WPF platform already provides a suitable implementation.
- Add comments for non-obvious coordinate math, protocol constraints, and Windows-specific behavior, not for self-explanatory code.
- Build vertical slices in this order unless the user changes priority: native map, local position, log discovery and diagnostics, quest event parsing, quest sync, item-recognition prototype, then party/overlay features.
- Introduce new server endpoints only after documenting the client event, authentication, idempotency, conflict, privacy, and versioning requirements.

## Definition of done

A feature is complete when its desktop behavior matches the agreed product behavior, detection limitations are represented honestly, duplicate and malformed inputs are handled, API failures do not break local operation, privacy controls are present where needed, user-visible text is appropriate, relevant documentation is updated, and the available build or verification checks pass.
