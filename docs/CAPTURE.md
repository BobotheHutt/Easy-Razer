# Recording what Synapse sends (one-time, per device)

Easy Razer writes keybinds into the Tartarus Pro and Naga Trinity's own memory. Razer doesn't publish
the commands for that, so we copy them from Synapse once. This takes about 30 minutes and only uses free tools.
Until it's done, Easy Razer runs keybinds in **dry-run mode**: it writes what it *would* send to the log
and never touches the device.

You need Synapse installed for this step, so do it **before** uninstalling Synapse.

## 1. Install the tools

1. Download Wireshark from <https://www.wireshark.org/download.html> and run the installer.
2. When the installer asks, tick **Install USBPcap**. Finish the install and **restart Windows**.

## 2. Get ready

1. Exit Easy Razer if it's running (tray icon → Exit).
2. Open Synapse and select the **Naga Trinity**. Attach the **12-button side plate**.
3. Have Notepad open to write down what you do, in order.

## 3. Record the Naga Trinity

1. Open Wireshark. You'll see one or more `USBPcap1`, `USBPcap2`… entries.
   Hold Ctrl and click all of them, then press the blue shark fin (Start).
2. In Synapse, do these **one at a time, waiting about 5 seconds between each**, and write each one
   in Notepad as you go:
   1. Side button 1 → keyboard key **K**
   2. Side button 1 → keyboard key **K** with **Ctrl** (modifier)
   3. Side button 1 → mouse **Middle click**
   4. Side button 1 → mouse **Button 4 (back)**
   5. Side button 1 → mouse **Scroll up**
   6. Side button 1 → **Disable**
   7. Side button 1 → back to default (Synapse's reset button for that key)
   8. Then bind each button below to a different letter, in this order, writing each down:
      side 2 → A, side 3 → B, side 4 → C, side 5 → D, side 6 → E, side 7 → F, side 8 → G,
      side 9 → H, side 10 → I, side 11 → J, side 12 → L, right click → M, wheel click → N,
      front button under the wheel → O, back button under the wheel → P
   9. Front button under the wheel → back to **DPI up** (Synapse's sensitivity option), then the
      back one → **DPI down**
   10. Put your **auto left-click toggle macro** back on the front button under the wheel, and the
       **auto right-click** one on the back button (set to toggle, as you normally use them).
       Then press each one once to start it and once to stop it.
   11. If Synapse lets you change the macro's click delay, change it once (e.g. 50 ms → 100 ms)
       and write down both values.
   12. Switch Synapse to a different profile, then back.
   13. Use Synapse's "reset to default" for the whole device, if it has one.
3. Press the red square (Stop) in Wireshark.
4. **File → Save As**, name it `naga-trinity.pcapng`.

## 4. Record the Tartarus Pro

Same as above, with the Tartarus Pro selected in Synapse:

1. Start a new capture (all `USBPcap` interfaces).
2. Key 01 → **K**, then **Ctrl+K**, then mouse **Middle click**, then **Disable**, then reset to default.
3. Then each key to a different letter, in order, writing it down. Use letters for 02–20 (skip K), then
   numbers for the thumbpad up/down/left/right and the scroll wheel up/down/click.
4. Switch Synapse profile and back.
5. Stop, save as `tartarus-pro.pcapng`.

## 5. Send them over

On the GitHub repo page: **Add file → Upload files**, drag in both `.pcapng` files plus your Notepad
notes (save them as `capture-notes.txt`), put them in a folder called `captures/`, and commit.

The captures only contain the USB traffic to your devices during those minutes. If anything else was
plugged in and active (for example you typed in another window), that could be in there too. Keep
your hands off other devices while recording.

## What happens next

From the captures we read the exact command, the button ids and how keys/modifiers/mouse actions are
encoded, and fill them in `src/EasyRazer/Devices/Razer/`. After that you turn off dry-run per device in
**Settings → Devices**.
