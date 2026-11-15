# Deploy to Meta Quest 3 from Linux

On Linux you **cannot** use Meta Quest Link (PCVR streaming). Build an **Android APK** in Unity and install it on the headset with **adb**.

Unity project path (this machine): `~/projects/HapticFeedbackArm`  
Scripts/docs repo: `~/projects/haptic-feedback-arm`

---

## 1. One-time: headset developer mode

1. Install the **Meta Horizon** app on your phone and log in with the same account as the Quest.
2. Put on the headset → pair it with the phone app if needed.
3. In the phone app: **Menu → Devices → [your Quest] → Headset Settings → Developer Mode → On**.
4. On the Quest, accept any developer / USB prompts when they appear.

---

## 2. One-time: Linux tools (adb)

Install Android platform tools so your PC can talk to the Quest over USB:

```bash
# Debian / Ubuntu
sudo apt update
sudo apt install -y adb android-sdk-platform-tools-common
```

Plug the Quest into the PC with a **data-capable** USB cable, then put on the headset and **Allow USB debugging** when prompted.

Check the device:

```bash
adb devices
```

You want a line like:

```text
XXXXXXXX    device
```

If it says `unauthorized`, put the headset on and accept the prompt, then run `adb devices` again.

**Optional USB udev rule** (if the Quest never shows up):

```bash
# Create rule (Meta / Oculus USB IDs)
echo 'SUBSYSTEM=="usb", ATTR{idVendor}=="2833", MODE="0666"
SUBSYSTEM=="usb", ATTR{idVendor}=="2a70", MODE="0666"' | sudo tee /etc/udev/rules.d/51-android.rules
sudo udevadm control --reload-rules
sudo udevadm trigger
# Unplug/replug the Quest, then:
adb kill-server
adb start-server
adb devices
```

---

## 3. One-time: Unity Android build support

In **Unity Hub**:

1. Install / open your **Unity 6** editor (e.g. `6000.6.x`).
2. **Installs → gear on that editor → Add modules**:
   - **Android Build Support**
   - **Android SDK & NDK Tools**
   - **OpenJDK**
3. Open the project: `~/projects/HapticFeedbackArm`.

### Confirm XR for Android

**Edit → Project Settings → XR Plug-in Management → Android** tab:

- **OpenXR** enabled
- Meta Quest / Meta feature group enabled as you already use for this project

**Edit → Project Settings → Player → Android**:

| Setting | Recommended |
|---------|-------------|
| **Minimum API Level** | Android 12L / API 32+ (Quest 3) |
| **Target Architectures** | **ARM64** only |
| **Scripting Backend** | IL2CPP |
| **Package Name** | Change from the URP template default if you want (e.g. `com.yourname.hapticfeedbackarm`) |

---

## 4. Build the APK

1. Open scene `Assets/Scenes/SampleScene.unity` (already in Build Settings).
2. **File → Build Profiles** (Unity 6) or **File → Build Settings**.
3. Select **Android** → **Switch Platform** if it is not already active (first switch can take a while).
4. **Build** (or **Build And Run** if `adb devices` already shows the Quest).

Suggested output path:

```text
~/projects/HapticFeedbackArm/Builds/HapticFeedbackArm.apk
```

Create the `Builds/` folder if needed.

---

## 5. Install on the Quest (adb)

With the headset connected and authorized:

```bash
# Confirm device
adb devices

# Install (reinstall keeps data if you use -r)
adb install -r ~/projects/HapticFeedbackArm/Builds/HapticFeedbackArm.apk
```

If Unity’s **Build And Run** worked, you can skip the manual `adb install`.

---

## 6. Run it on the headset

1. Put on the Quest.
2. Open the app library → filter **Unknown Sources** (or **Sources → Unknown sources**).
3. Launch **HapticFeedbackArm** (product name from Player Settings).
4. Grab a box with the **right** controller:
   - Head-locked HUD: `Force: X.X N`
5. Release → `0.0 N`.

There is no Unity Console on-device unless you use logcat:

```bash
adb logcat -s Unity
```

Look for lines like `[ElbowForce] … N`.

---

## 7. Iterate quickly

After code/scene changes:

```bash
# Rebuild APK in Unity, then:
adb install -r ~/projects/HapticFeedbackArm/Builds/HapticFeedbackArm.apk
adb shell am start -n com.UnityTechnologies.com.unity.template.urpblank/com.unity3d.player.UnityPlayerActivity
```

Replace the package name in that `am start` line if you changed **Player → Android → Package Name**. Check the installed package with:

```bash
adb shell pm list packages | grep -i haptic
# or
adb shell pm list packages | grep -i unity
```

---

## Troubleshooting

| Problem | What to try |
|---------|-------------|
| `adb devices` empty | Data cable, USB debugging allow prompt, udev rules, `adb kill-server && adb start-server` |
| `unauthorized` | Wear headset, Accept; revoke USB debugging authorizations in Quest developer settings and replug |
| Build fails (SDK/NDK) | Hub → add Android modules; Edit → Preferences → External Tools → refresh Android SDK/NDK/JDK paths |
| App missing in library | App Library → **Unknown Sources** |
| Black screen / no tracking | Confirm **Main Camera** exists under **XR Origin → Camera Offset**; OpenXR + Meta features enabled for Android |
| Grab does nothing | `RightHandGrabTorqueBridge` on right **Near-Far Interactor**; boxes have `XRGrabInteractable` + `BoxMass` |
| Install conflict | `adb uninstall <package.name>` then install again |

---

## Linux vs Windows (why this flow)

| | Linux | Windows |
|---|--------|---------|
| Quest Link / Air Link editor play | Not supported by Meta | Supported |
| Build APK + install via adb | Yes | Yes |
| Meta XR Simulator in Editor | Possible depending on package/version | Common for iteration |

For this MVP on Linux, **APK + adb** is the reliable path.
