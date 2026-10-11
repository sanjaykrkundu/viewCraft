import os
from PIL import Image, ImageDraw

out_dir = os.path.join(os.path.dirname(__file__), "SampleImages")
os.makedirs(out_dir, exist_ok=True)

# 1. High-res gradient JPEG
img_grad = Image.new("RGB", (1920, 1080))
draw = ImageDraw.Draw(img_grad)
for y in range(1080):
    r = int(255 * (y / 1080.0))
    g = int(200 * (1.0 - (y / 1080.0)))
    b = int(180 + 75 * (y / 1080.0))
    draw.line([(0, y), (1920, y)], fill=(r, g, b))
draw.text((100, 100), "ViewCraft Universal Viewer Sample - Rec.709 Gradient", fill=(255, 255, 255))
img_grad.save(os.path.join(out_dir, "01_gradient.jpg"), quality=95)

# 2. Transparent PNG with geometric shapes
img_png = Image.new("RGBA", (1200, 1200), (0, 0, 0, 0))
draw_png = ImageDraw.Draw(img_png)
draw_png.ellipse([100, 100, 1100, 1100], fill=(245, 158, 11, 220), outline=(255, 255, 255, 255), width=8)
draw_png.rectangle([300, 300, 900, 900], fill=(59, 130, 246, 200), outline=(0, 0, 0, 255), width=6)
img_png.save(os.path.join(out_dir, "02_shapes_alpha.png"))

# 3. Animated GIF
frames = []
for i in range(16):
    f = Image.new("RGB", (400, 400), (24, 24, 27))
    d = ImageDraw.Draw(f)
    angle_offset = i * 20
    d.arc([50, 50, 350, 350], start=angle_offset, end=angle_offset + 120, fill=(245, 158, 11), width=16)
    d.text((150, 190), f"Frame {i+1}/16", fill=(255, 255, 255))
    frames.append(f)
frames[0].save(os.path.join(out_dir, "03_animation.gif"), save_all=True, append_images=frames[1:], duration=60, loop=0)

# 4. WebP Lossless
img_webp = Image.new("RGBA", (800, 600), (36, 36, 40, 255))
draw_webp = ImageDraw.Draw(img_webp)
draw_webp.line([(0, 0), (800, 600)], fill=(34, 197, 94), width=4)
draw_webp.line([(0, 600), (800, 0)], fill=(239, 68, 68), width=4)
img_webp.save(os.path.join(out_dir, "04_vector_lines.webp"), lossless=True)

# 5. Multi-page TIFF
t_frames = [
    Image.new("RGB", (600, 600), (220, 50, 50)),
    Image.new("RGB", (600, 600), (50, 220, 50)),
    Image.new("RGB", (600, 600), (50, 50, 220))
]
t_frames[0].save(os.path.join(out_dir, "05_multipage.tiff"), save_all=True, append_images=t_frames[1:])

# 6. Windows BMP
img_bmp = Image.new("RGB", (512, 512), (70, 70, 80))
img_bmp.save(os.path.join(out_dir, "06_texture.bmp"))

# 7. Scalable Vector Graphics (SVG)
svg_content = '''<svg xmlns="http://www.w3.org/2000/svg" width="800" height="600" viewBox="0 0 800 600">
  <rect width="800" height="600" fill="#18181b"/>
  <circle cx="400" cy="300" r="180" fill="#f59e0b"/>
  <text x="400" y="310" font-size="32" font-family="Arial" fill="#18181b" text-anchor="middle" font-weight="bold">ViewCraft Vector SVG</text>
</svg>'''
with open(os.path.join(out_dir, "07_vector.svg"), "w", encoding="utf-8") as f:
    f.write(svg_content)

print(f"Generated sample files in {out_dir}")
