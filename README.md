# ViewCraft 🎨

> **The Universal Studio Image Viewer & Digital Asset Inspector.**  
> An open-source, high-performance alternative to Adobe Bridge, IrfanView, and Honeyview &mdash; built as the third pillar of the **ArtCraft** suite alongside **LightCraft** and **PhotoCraft**.

---

## ⚡ Overview

ViewCraft is designed from the ground up for extreme speed, ultra-wide format compatibility, and deep asset inspection. Powered by **.NET 9**, hardware-accelerated **Direct3D rendering**, and **Magick.NET (ImageMagick)**, ViewCraft can open and inspect **over 200+ image formats** &mdash; from modern web and next-gen containers to RAW camera archives, layered Photoshop documents, medical scans, and retro computing formats.

---

## 🚀 Key Features

### 1. 🌐 Universal Format Support (200+ Formats)
- **Modern & Next-Gen**: AVIF, JPEG XL (`.jxl`), HEIC / HEIF, WebP, OpenEXR (`.exr`), Radiance HDR (`.hdr`).
- **Camera RAW & MIPI CSI-2 Dumps**: Canon (`.cr2`, `.cr3`), Nikon (`.nef`, `.nrw`), Sony (`.arw`, `.sr2`), Fujifilm (`.raf`), Olympus (`.orf`), Panasonic (`.rw2`), Pentax (`.pef`), Adobe (`.dng`), Hasselblad (`.3fr`), Leica, etc.
- **Embedded & Sensor RAW (MIPI CSI-2)**: Packed MIPI RAW10 (`.raw`, `.mipi`), Packed MIPI RAW12, RAW8, RAW16 (LE/BE) sensor dumps with automatic resolution detection and bilinear Bayer CFA demosaicing (RGGB, BGGR, GRBG, GBRG, Mono).
- **Adobe Creative Suite**: Multi-layer Photoshop (`.psd`, `.psb`), Illustrator (`.ai`), Vector (`.svg`, `.eps`, `.pdf`).
- **Standard Web & Desktop**: JPEG, PNG, APNG, GIF (animated), BMP, multi-page TIFF, BigTIFF (`.btf`), ICO, CUR.
- **Scientific & Medical**: DICOM (`.dcm`), FITS (`.fits`), MATLAB (`.mat`).
- **3D & Game Textures**: DDS (DXT1–5), Targa (`.tga`), KTX, PVR.
- **Legacy & Retro Computing**: PCX, PNM (PBM, PGM, PPM), SGI (`.rgb`), Sun Raster, Amiga IFF/ILBM, XBM, XPM, PICT, Palm OS.

### 2. 🔍 Deep Zoom & Pixel Inspector
- **Infinite Smooth Zoom**: 1% to 6400% (64x) with mouse-centered zooming.
- **Pixel Grid & Nearest-Neighbor**: Automatic crisp pixel grid at zoom ≥ 400% for pixel-level art and defect inspection.
- **Live Color Loupe**: Hover over any pixel to inspect:
  - Exact `(X, Y)` coordinate.
  - RGBA values `(0–255)`.
  - Hex color code `#RRGGBB`.
  - Live color swatch box.
- **Floating Mini-Map / Navigator**: Interactive overview thumbnail with draggable viewport rectangle.
- **Lock Zoom Mode**: Lock zoom level and pan coordinates while flipping between photos to compare sharpness across shots.

### 3. ☀️ Real-Time Non-Destructive Tone & Color Adjustments
- **Exposure**: -2.0 to +2.0 EV factor.
- **Contrast, Brightness, Saturation & Vibrance**: Real-time slider feedback.
- **White Balance**: Temperature (Cool ↔ Warm) and Tint (Green ↔ Magenta).
- **Tones**: Highlights and Shadows recovery.
- **Detail**: Sharpness / Unsharp Mask.
- **Filters**: Invert Colors, Grayscale, Sepia Tone.
- **Channel Isolation**: View Red-only, Green-only, Blue-only, or Alpha transparency mask independently.

### 4. 📊 Live 256-Bin RGB & Luminance Histogram
- Real-time 4-channel histogram graph with logarithmic curve smoothing.
- Toggle individual channels: Luminance, Red, Green, Blue.
- Statistical metrics: Mean Luma, Maximum bin count, Standard deviation.

### 5. 🎞️ Multi-Frame & Animation Timeline Player
- Animated GIF, APNG, Animated WebP, Multi-page TIFF, and DICOM slice series.
- Timeline scrubber with Play / Pause / Step Forward / Step Backward.
- Frame delay recognition and loop management.
- One-click **"Export All Frames"** to PNG sequence.

### 6. ⚖️ Split Wipe & Side-by-Side Comparison
- **Split Wipe Slider**: Drag interactive divider to wipe between Before (Original) and After (Adjusted).
- **Side-by-Side (2-Up)**: Dual viewports for direct side-by-side comparison.

### 7. ⚡ Multi-Threaded Batch Converter
- Batch convert hundreds of images in parallel across all CPU cores.
- Convert to WebP, JPEG, PNG, AVIF, TIFF, BMP, or ICO.
- Configurable quality slider, optional bounding box resizing, and prefix/suffix renaming.

### 8. 📷 Studio Metadata Inspector (Adobe Bridge Style)
- **Technical Specs**: Format, uncompressed dimensions, megapixels, file size, color space, bit depth, ICC profile.
- **Camera EXIF**: Camera Make/Model, Lens, Focal Length, 35mm Equiv, Aperture (f-stop), Shutter Speed, ISO, Exposure Bias, Metering Mode, Flash, Date Taken.
- **GPS Location**: Latitude, Longitude, Altitude, with direct clickable link to **Google Maps**.
- **IPTC & XMP**: Title, Caption, Copyright, Artist, Keywords.

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `←` / `PageUp` | Previous image in folder |
| `→` / `PageDown` | Next image in folder |
| `Home` / `End` | First / Last image |
| `+` / `-` / `Ctrl + Scroll` | Zoom In / Zoom Out |
| `0` | Fit to Window |
| `1` | 100% Actual Size (1:1) |
| `R` | Rotate 90° Clockwise |
| `Shift + R` | Rotate 90° Counter-Clockwise |
| `H` | Flip Horizontal |
| `V` | Flip Vertical |
| `C` | Open Split / Side-by-Side Comparison |
| `F` | Toggle Bottom Filmstrip |
| `I` | Toggle Studio Inspector Sidebar |
| `M` | Toggle Floating Mini-Map / Navigator |
| `F11` | Toggle Full Screen Mode |
| `Space` | Play/Pause Animation or Advance Slideshow |
| `Ctrl + O` | Open Image File |
| `Ctrl + Shift + O` | Open Folder |
| `Ctrl + S` | Save / Export As |
| `Ctrl + C` | Copy Image to Clipboard |
| `Delete` | Delete File to Recycle Bin |

---

## 🛠️ Building & Running

### Requirements
- **.NET 9.0 SDK** or higher
- Windows 10 / 11 (x64 / ARM64)

### Run from Terminal
```powershell
cd ViewCraft
dotnet run
```

### Publish Single-File Executable
```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

---

## 📄 License
MIT OR Apache-2.0 (Matching ArtCraft / LightCraft / PhotoCraft).
