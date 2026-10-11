import os
import numpy as np

out_dir = r"C:\Users\sanja\Desktop\ai\adobe-alt\ViewCraft\SampleImages"
os.makedirs(out_dir, exist_ok=True)

# Generate a synthetic 1920x1080 color chart in RGB, then convert to YUV
width = 1920
height = 1080

# Create color bars
bar_width = width // 8
r = np.zeros((height, width), dtype=np.uint8)
g = np.zeros((height, width), dtype=np.uint8)
b = np.zeros((height, width), dtype=np.uint8)

colors = [
    (255, 255, 255), # White
    (255, 255, 0),   # Yellow
    (0, 255, 255),   # Cyan
    (0, 255, 0),     # Green
    (255, 0, 255),   # Magenta
    (255, 0, 0),     # Red
    (0, 0, 255),     # Blue
    (0, 0, 0)        # Black
]

for i, (cr, cg, cb) in enumerate(colors):
    x0 = i * bar_width
    x1 = (i + 1) * bar_width if i < 7 else width
    r[:, x0:x1] = cr
    g[:, x0:x1] = cg
    b[:, x0:x1] = cb

# BT.709 conversion
y_plane = np.clip(0.2126 * r + 0.7152 * g + 0.0722 * b, 0, 255).astype(np.uint8)
u_plane = np.clip((b.astype(float) - y_plane.astype(float)) / 1.8556 + 128, 0, 255).astype(np.uint8)
v_plane = np.clip((r.astype(float) - y_plane.astype(float)) / 1.5748 + 128, 0, 255).astype(np.uint8)

# Subsample U and V for 4:2:0
u_sub = u_plane[0::2, 0::2]
v_sub = v_plane[0::2, 0::2]

# 1. NV12 (Y + interleaved UV)
uv_interleaved = np.empty((height // 2, width), dtype=np.uint8)
uv_interleaved[:, 0::2] = u_sub
uv_interleaved[:, 1::2] = v_sub

nv12_data = np.concatenate([y_plane.flatten(), uv_interleaved.flatten()])
with open(os.path.join(out_dir, "test_hd_1080p.nv12"), "wb") as f:
    f.write(nv12_data.tobytes())
print("Wrote test_hd_1080p.nv12:", len(nv12_data), "bytes")

# 2. NV21 (Y + interleaved VU)
vu_interleaved = np.empty((height // 2, width), dtype=np.uint8)
vu_interleaved[:, 0::2] = v_sub
vu_interleaved[:, 1::2] = u_sub

nv21_data = np.concatenate([y_plane.flatten(), vu_interleaved.flatten()])
with open(os.path.join(out_dir, "test_android_camera.nv21"), "wb") as f:
    f.write(nv21_data.tobytes())
print("Wrote test_android_camera.nv21:", len(nv21_data), "bytes")

# 3. Qualcomm CamX 64-byte aligned NV12
q_stride = (width + 63) & ~63
q_scanlines = (height + 31) & ~31
q_y = np.zeros((q_scanlines, q_stride), dtype=np.uint8)
q_y[:height, :width] = y_plane

q_uv = np.zeros((q_scanlines // 2, q_stride), dtype=np.uint8)
q_uv[:height // 2, :width] = uv_interleaved

q_nv12_data = np.concatenate([q_y.flatten(), q_uv.flatten()])
with open(os.path.join(out_dir, "test_qualcomm_camx_aligned.nv12"), "wb") as f:
    f.write(q_nv12_data.tobytes())
print("Wrote test_qualcomm_camx_aligned.nv12:", len(q_nv12_data), "bytes")

# 4. YUYV 4:2:2 Packed (1280x720)
w720, h720 = 1280, 720
y720 = y_plane[:h720, :w720]
u720 = u_plane[:h720, :w720]
v720 = v_plane[:h720, :w720]

yuyv = np.empty((h720, w720 * 2), dtype=np.uint8)
yuyv[:, 0::4] = y720[:, 0::2]
yuyv[:, 1::4] = u720[:, 0::2]
yuyv[:, 2::4] = y720[:, 1::2]
yuyv[:, 3::4] = v720[:, 0::2]

with open(os.path.join(out_dir, "test_webcam_frame.yuy2"), "wb") as f:
    f.write(yuyv.tobytes())
print("Wrote test_webcam_frame.yuy2:", yuyv.size, "bytes")

# 5. P010 (10-bit HDR Semi-Planar, 1920x1080)
# 10-bit words in uint16
y10 = (y_plane.astype(np.uint16) * 4) # 0..1020
u10_sub = (u_sub.astype(np.uint16) * 4)
v10_sub = (v_sub.astype(np.uint16) * 4)

uv10_interleaved = np.empty((height // 2, width), dtype=np.uint16)
uv10_interleaved[:, 0::2] = u10_sub
uv10_interleaved[:, 1::2] = v10_sub

p010_data = np.concatenate([y10.flatten(), uv10_interleaved.flatten()])
with open(os.path.join(out_dir, "test_hdr10_mobile.p010"), "wb") as f:
    f.write(p010_data.tobytes())
print("Wrote test_hdr10_mobile.p010:", p010_data.nbytes, "bytes")

print("All synthetic camera frames generated successfully!")
