# Security

## Reporting a problem

Please don't open a public issue for a security problem. Use **[Report a vulnerability](https://github.com/Aelshi-nui/NestZone-Rpc/security/advisories/new)** on this repository instead.

## Review for version 1.0.1

The extension and the helper were reviewed line by line before this release. These issues were found and fixed:

| Area | Problem | Fix |
| --- | --- | --- |
| Extension | A script running on nestzone.tech could set the status button to any https link, for example a phishing page. | The **Watch / Read on NestZone** button only accepts `https://nestzone.tech/...`. AniList links are built from a whole-number ID only. Covers must be `https://`. |
| Extension | The background script accepted connections and settings messages without checking who sent them. | Ports must come from this extension's content script in a `https://nestzone.tech` tab, and settings changes from its own popup page. |
| Extension | The popup built the download link with `innerHTML`. | It builds the link with DOM calls and `rel="noopener noreferrer"`. |
| Extension | An unhandled promise rejection when the popup was closed. | Caught. |
| Helper | Started from the Downloads folder, Windows could load a planted DLL from that folder (DLL search order hijacking). | `SetDefaultDllDirectories(System32)` and `SetDllDirectory("")` run first thing at startup. |
| Helper | Uninstall started `cmd.exe` by name, which Windows can look up in the exe's own folder first. | It uses the full path in `System32`. |
| Helper | Uninstall deleted whatever folder the profile path resolved to. | It only deletes `%LOCALAPPDATA%\NestZone\Discord`. |
| Helper | Message text was written to the log as is, so a message with line breaks could forge log lines. | Only short printable ASCII is logged. |
| Helper | No version information or manifest. | Added version info and an `asInvoker` manifest, so it never asks for admin rights. |

## How it is built to stay safe

- **Small surface.** The extension has two permissions (`nativeMessaging`, `storage`) and one content script on `https://nestzone.tech/*`. It has no host permissions and runs no remote code.
- **Fixed ID.** The Chrome manifest carries a public key, so the extension ID is always `fkpdelihmdmckifbemjjfpblaoingmen`. The native host manifest lists only that ID (and `discord@nestzone.tech` for Firefox) in its allowed origins, so other extensions and websites can't start the helper.
- **Checked input.** The content script copies only known fields, with fixed types and a length limit of 300 characters. The helper rejects messages over 1 MB and unknown message types, and Discord pipe frames over 1 MB.
- **No network.** The helper only uses standard input and output with the browser and the local `discord-ipc-*` pipe. It does not download or update itself.
- **Per user.** Everything is installed under `%LOCALAPPDATA%` and `HKEY_CURRENT_USER`. No service, no scheduled task, no admin rights.

## Known limits

- The helper is not yet signed with an Authenticode certificate, so SmartScreen warns on first run. Releases are built by GitHub Actions and carry SHA256 checksums and a build provenance attestation instead. See [Check your download](README.md#check-your-download).
- Any program already running as your Windows user can talk to the Discord app's pipe too. That's how Discord's local RPC works and isn't specific to this helper.
- Discord shows the cover and links you send to everyone who can see your status. 18+ titles never show a cover.
