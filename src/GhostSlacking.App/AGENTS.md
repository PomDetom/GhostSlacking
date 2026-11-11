# App

`Hosting` owns composition/lifetime and tray coordination; `Settings` owns drafts, pages and single-setting flyouts; `Reveal` owns the optional visual host and scheduling; `Notifications` owns HUD behavior; `Updates` owns release checks and handoff; `Diagnostics` owns logging/export; `Presentation` owns theme, icons and localization. `Program.cs`, project metadata and assets remain at their entry/resource locations.

Read [application services](../../docs/architecture/APP_SERVICES.md) for settings/HUD and [installation updates](../../docs/architecture/INSTALLATION_UPDATES.md) for handoff. UI does not mutate target windows directly. Composition failure must fall back independently of recovery. Keep the controller's initialization, subscriptions and exit/disposal order centralized; responsibility partials share the same state and are not new service boundaries.

Single-setting completion changes only its draft field; global Save applies, Cancel restores the saved draft. Navigation or closing an unfinished flyout abandons that local adjustment. Preserve both languages and themes. Asset URIs and embedded logical names must survive file moves.

App tests are grouped under `tests/GhostSlacking.App.Tests/App`. Follow [test requirements](../../tests/AGENTS.md); inspect affected behavior in a running app. Do not automate the user's real target or write their settings just to obtain screenshots.
