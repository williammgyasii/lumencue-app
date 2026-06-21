# Importing your lower-third designs

You can design your lower thirds (and announcement graphics) in Photoshop — or any tool — and
import them straight into the app. The app lays your graphic onto the screen and puts the live
text (scripture heading, verse, reference) on top, exactly where you want it.

## How to export from Photoshop (do this once per design)

1. **Work at 1920 × 1080.** That's the resolution we send to your screen / ATEM. Designing at this
   size keeps everything crisp. (Smaller files still work, but they get upscaled and look softer —
   the sample we tested was 1024 × 576 and lost sharpness.)
2. **Keep the area around your design clean.** The app fills everything *around* your graphic with
   chroma **green** so your ATEM can key it out over the live camera. So:
   - **Best:** export a **transparent PNG** (no background layer) — the green shows through cleanly.
   - **Or:** put your design on a solid **green** background that matches the ATEM key.
   - **Avoid** a black (or any opaque) background — the ATEM keys *green*, not black, so a black
     box would sit on top of your live shot.
3. **Don't "trim to layer bounds."** Export the **full 1920 × 1080 canvas** so the design stays in
   the position you placed it. (If Photoshop trims off the transparent margins, the app will still
   place it full-width at the bottom, but it can't know exactly where on the frame you wanted it.)
4. Export as **PNG** (preferred, supports transparency) or JPG.

## Importing in the app

1. Open **Theme Studio**.
2. Click **Import design…** and pick your exported file.
3. The app creates a new theme that:
   - places your graphic **full-width, anchored to the bottom**, keeping its proportions (no shrink),
   - uses a **green ATEM-key** background, and
   - adds **heading / verse / reference** text boxes on top.
4. Drag and resize those text boxes onto your design, set fonts/colours, and **Save**.
5. Assign the theme to Scripture (or whichever content type) and you're live.

## Two kinds of designs

- **Scripture lower thirds** — your designed band/look; the app overlays the live scripture text.
  Position the text boxes inside your band.
- **Announcement lower thirds** — fully designed graphics where the text is already baked in by
  your designer. Import the same way; just hide the text boxes if you don't need the app's text.

## Quick checklist

- [ ] 1920 × 1080
- [ ] Transparent PNG (or green background) — never opaque black
- [ ] Full canvas exported (not trimmed)
- [ ] Imported via Theme Studio → text boxes positioned → saved
