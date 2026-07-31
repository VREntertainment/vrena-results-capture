# VRena Results Capture

VRena Results Capture is a Windows 10/11 x64 notification-area utility that saves one full-display PNG whenever a configured VR results screen appears. It can also read the game, exact player names, Hits, Accuracy, Movement and Score, then send those structured results to the VRena web app.

## Install

1. Copy `VRenaResultsCapture-Setup.exe` to the Windows computer that displays the VR game server.
2. Double-click it and choose **OK** to install for the current Windows user.
3. Windows may show a SmartScreen warning because this private build is not code-signed. Choose **More info**, then **Run anyway**.
4. No administrator permission is required.

The application installs under `%LOCALAPPDATA%\VRena Results Capture`, adds itself to the Start menu, and starts automatically when that user signs in.

## First-time setup

1. Show a completed results page in the VR server application.
2. Open **VRena Results Capture** from the notification area.
3. Select the display showing the results.
4. Choose **Configure**.
5. Draw a tight box around the stable word **Results**. Do not include the changing session number, date, time, or scores.
6. Monitoring starts automatically.

## Web app sync

1. Enter the deployed VRena web app HTTPS URL.
2. Enter the same import token configured as `VRENA_RESULTS_INGEST_TOKEN` in the web app.
3. Choose **Test** and confirm that the status says **Connected**.
4. Enable **Send recognized statistics or unresolved screenshots after each capture**.

Recognized results send only the game, exact player name, date/time, Hits, Accuracy, Movement and Score. If the app cannot read the game or all player rows, it automatically sends a compressed screenshot and OCR diagnostic text to private web-app review storage. The original full-resolution screenshot stays local.

Keep the VR server window in the same position and at the same display resolution. Reconfigure recognition if either changes.

Version 2.1.2 adds focused OCR passes for each player row and the bottom-left game label, and tolerates common Windows OCR substitutions in numeric fields. This is designed for the venue server's 3840 × 2160 results display, where a broad full-screen pass can miss the smaller game and player statistics.

Version 2.1.3 keeps the web app URL and import token saved while settings are loaded, saves edits immediately, saves once more during Windows shutdown, and restores valid connection details from an atomic backup if the main settings file is damaged.

Version 2.1.4 adds focused OCR for two player rows on each team (up to four players), automatically uploads incomplete captures to private review storage, and highlights the update button with a moving border light when a new version is available.

Version 2.1.5 recognizes the `MBTowers` label as Mini Block Towers and reads its Hits, Shield, Towers and Total result layout. Hits and Total sync to player statistics while fields that the game does not display remain empty.

Version 2.1.6 corrects the Windows OCR percent-sign artifact (`0/0`) and restores a dropped trailing zero in 10-point shooter scores before syncing player statistics.

## Online updates

Version 2.1.1 is the last version that needs a normal manual installation. Future versions are checked automatically through the configured web app. Choose **Check for updates** at any time, or accept the update prompt after startup. The application downloads the release over HTTPS, verifies its SHA-256 fingerprint, replaces only the installed executable, and restarts. Settings, screenshots, local result history, pending synchronization, and logs are preserved.

## Saved files

The default folder is:

`%USERPROFILE%\Pictures\VRena Results`

Screenshots are organized by year and month:

`2026\07\VRena_Result_2026-07-27_12-47-03-123.png`

`capture-log.csv` records the local timestamp, UTC offset, monitor, and file path. `recognized-results.csv` and the `recognized-results` folder retain the complete local result history. Failed structured result syncs stay in `sync-pending`; failed review uploads stay in `review-pending`. Both retry automatically. The application never automatically deletes local screenshots.

## Diagnostics and support

The application continuously records local diagnostic logs under:

`%USERPROFILE%\Pictures\VRena Results\Diagnostics`

If a capture, OCR, or web sync problem occurs, open the application and choose **Create support bundle**. A ZIP is created under `SupportBundles`.

The ZIP excludes the import token, environment variables, and executable. It includes the latest captured screenshot at a reduced size, recent application logs, OCR text, sanitized settings, screen information, and result-history CSV files. It can contain player names, the computer name, and local paths.

Choose **Upload support bundle** to create the same ZIP and send it to VRena's private support storage after an explicit privacy confirmation. The local copy remains on the computer. Uploaded bundles are not public; retrieval requires a short-lived, single-use support token.

Authorized Codex retrieval is documented in [SUPPORT_RETRIEVAL.md](SUPPORT_RETRIEVAL.md).

## Uninstall

Use **Windows Settings → Apps → Installed apps → VRena Results Capture → Uninstall**.

Uninstalling removes the application but deliberately leaves all screenshots and `capture-log.csv` untouched.
