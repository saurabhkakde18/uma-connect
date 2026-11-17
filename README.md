# UMA Connect — Android Mobile & Windows PC Edition

**UMA Connect** is the rebranded, glossy-glassmorphism mobile and desktop application for **UMA Steel Alloys and Power Limited** and **Omsairam Steels and Alloys Pvt. Ltd.** (package `com.umasteel.erpqueryhub`).

---

## 📱 Android Mobile APK (Fit-to-Screen)

The mobile APK has been built with responsive fit-to-screen engine, v1 + v2 dual signing, and 4-byte resource alignment:

- **APK File:** `UMA_Connect.apk` (and `UMA Connect.apk`) in this folder (310 KB).
- **Fit to Screen:**
  - Locked 1:1 mobile viewport (`viewport-fit=cover, shrink-to-fit=no, user-scalable=no`) preventing unwanted zooming out.
  - Safe-area insets (`env(safe-area-inset-top)` & `env(safe-area-inset-bottom)`) for edge-to-edge camera cutouts and gesture navigation bars.
  - Dynamic responsive header tuned for 320px–480px displays with compact theme switcher and avatar.
  - Horizontal swipeable touch-scrolling for data tables, reports, and Kanban boards with zero full-page overflow.
  - Responsive modals, login screens, and KPI grids automatically adapting to 1–2 columns on mobile.
  - Signed with Android APK Signature Scheme v1 (JAR signing) + v2 (chunked SHA-256 block) and 4-byte `zipalign`.
- **How to Install on Phone:**
  1. Transfer `UMA_Connect.apk` to your Android phone (via USB cable, WhatsApp, Google Drive, or email).
  2. Tap the file in your phone's File Manager / Downloads.
  3. If prompted, allow "Install from unknown sources" for your file manager.
  4. Tap **Install** — **UMA Connect** will appear on your home screen with its official icon and launcher label.

---

## 💻 Windows PC Edition

- **Install System-Wide:** Double-click `UMA_Connect_Setup.exe` to run the Setup Wizard with Desktop & Start Menu shortcuts.
- **Run Standalone:** Double-click `UMA Connect.exe` (or `Run_UMA_Connect.bat`).
- **Web Browser:** Open `index.html` in Chrome, Edge, or Firefox.
- **Uninstall:** Run `Uninstall.exe` or use Windows Settings → Apps.

---

## 🎨 Included Glossy Glass Themes

Switch themes live anytime using the **top header glass theme switcher** or inside **Settings → Appearance & Glass Themes** (`localStorage` persisted):

1. **Crystal Glass (`crystal-glass`)** *(Default)* — Clear frosted prism glass over vibrant cobalt/aurora refraction
2. **Frost Crystal (`frost-crystal`)** — Light frosted crystal glass with soft icy highlights and bright ambient depth
3. **Midnight Obsidian (`midnight-obsidian`)** — Deep obsidian dark glass with translucent slate panels and high-contrast typography
4. **Sapphire Aurora (`sapphire-aurora`)** — Deep cobalt & sapphire blue glass with luminous cyan-blue ambient glow
5. **Emerald Steel (`emerald-steel`)** — Rich industrial emerald-teal glass with subtle metallic highlights


---

## 🔒 100% Offline Local Storage Architecture

- **Zero Cloud / Firebase Dependencies:** All external Firebase Web SDK scripts (`firebase-app-compat.js`, `firebase-firestore-compat.js`), cloud sync buttons, and background listeners have been completely removed.
- **Local Persistence:** All query tickets, comments, root-cause diagnostics, timelines, audit logs, and theme preferences are stored directly and securely on your local device (`localStorage`), requiring no internet connectivity or remote servers to run.
- **Export & Backup:** Full database export and restore to JSON is available anytime under **Settings → Database Backup & Disaster Recovery**.
