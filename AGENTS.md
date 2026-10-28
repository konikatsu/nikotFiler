# nikotFiler

Workspace: C:/dev/nikotFiler. Build a Windows file manager with a simple interface and easy operations. User priorities: freely adjustable text size, independently addable/closable tabs, and a clean interface. Reuse safe operations from the prior prototype without modifying C:/dev/filer1.

Use C# / .NET 10 / WPF. Keep a single app-wide text scale, persistent settings, and visible selection as the authority for file commands. Capture and confirm operation source and destination before starting. Never silently overwrite or permanently delete user files. Store local state under %LOCALAPPDATA%/nikotFiler; never commit actual paths, history, review context, CLI logs, credentials or build outputs.

Distribute a framework-dependent Windows x64 EXE without bundling the .NET runtime, as requested by the user. Keep UseAppHost=true and SelfContained=false; document the .NET Desktop Runtime 10 x64 prerequisite and verify that runtime binaries do not enter the ZIP.

Keep the final tab across the whole app open. Closing another pane's last tab must collapse that empty pane without creating a home tab, preserve the surviving controls and selections, and persist that state. History must support search and navigation to the recorded folder or the containing folder of a file; reading history never repeats the recorded file operation.

Run the build and executable smoke tests after behavior changes. UI checks use only temporary dummy files. Keep the source publishable and document preview limitations honestly. Public documentation belongs in docs/public; docs/reviews and docs/qa-local are private and ignored.

The user requested publishing a new, simpler prototype to GitHub. Prepare and verify the actual source and distribution before publishing. Keep license selection explicit; do not invent a license grant.
