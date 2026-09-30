"""Draws the Anklet of Bloodlust item icon (with --buff, the buff icon; with --store, the Thunderstore icon).

The item icon follows the vanilla band icons: a ray-traced 3D band with soft lighting,
a thin dark line, then a thick tier-colored (lunar blue) outline around the whole item.
"""
import math
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1]
SS = 4            # supersampling factor
N = 128 * SS      # working resolution

LUNAR_BLUE = (70, 150, 255)
OUTLINE_DARK = (26, 10, 18)


def drop_polygon(cx, top, w, h, steps=80):
    """Teardrop: pointed at the top, round at the bottom."""
    pts = []
    for i in range(steps + 1):
        t = math.pi * i / steps
        yy = top + h * (1 - math.cos(t)) / 2
        xx = w / 2 * math.sin(t) * (1 - math.cos(t)) / 2 * 1.3
        pts.append((cx + xx, yy))
    for i in range(steps, -1, -1):
        t = math.pi * i / steps
        yy = top + h * (1 - math.cos(t)) / 2
        xx = w / 2 * math.sin(t) * (1 - math.cos(t)) / 2 * 1.3
        pts.append((cx - xx, yy))
    return pts


def dilate(alpha, radius):
    img = Image.fromarray(alpha)
    for _ in range(radius // 2):
        img = img.filter(ImageFilter.MaxFilter(5))
    return np.array(img)


def rot_x(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def rot_z(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


# Band shape and camera
R_OUT, R_IN, HALF_H = 1.0, 0.8, 0.2       # a thick, short cylinder shell (object axis = y)
ELEVATION = math.radians(40)              # how far above the band the camera sits
SLANT = math.radians(14)                  # tilts the band on screen
VIEW = rot_z(SLANT) @ rot_x(-ELEVATION)   # object -> view (x right, y up, z away from camera)
SCALE = 36 * SS                           # pixels per unit
CX, CY = N * 0.5, 42 * SS                 # band center on the canvas


def project(p):
    v = VIEW @ np.asarray(p, float)
    return CX + v[0] * SCALE, CY - v[1] * SCALE, v[2]


def front_dir():
    """Object-space direction on the band's rim that faces the camera most."""
    cam = VIEW.T @ np.array([0, 0, -1.0])
    v = np.array([cam[0], 0, cam[2]])
    return v / np.linalg.norm(v)


def render_band():
    """Ray-traces the band (orthographic camera); returns an RGBA image."""
    ys, xs = np.mgrid[0:N, 0:N].astype(np.float64)
    vx, vy = (xs - CX) / SCALE, -(ys - CY) / SCALE
    inv = VIEW.T
    o = np.stack([vx, vy, np.full_like(vx, -10.0)], -1) @ inv.T
    d = inv @ np.array([0.0, 0.0, 1.0])
    ox, oy, oz = o[..., 0], o[..., 1], o[..., 2]
    dx, dy, dz = d

    best_t = np.full(ox.shape, np.inf)
    normal = np.zeros(ox.shape + (3,))
    part = np.zeros(ox.shape, np.int8)    # 1 outer wall, 2 inner wall, 3 top, 4 bottom

    def take(t, n, kind, valid):
        nonlocal best_t
        better = valid & (t > 0) & (t < best_t)
        best_t = np.where(better, t, best_t)
        normal[better] = n[better]
        part[better] = kind

    a = dx * dx + dz * dz
    for radius, sign, kind in ((R_OUT, 1, 1), (R_IN, -1, 2)):
        b = 2 * (ox * dx + oz * dz)
        c = ox * ox + oz * oz - radius * radius
        disc = b * b - 4 * a * c
        ok = disc >= 0
        sq = np.sqrt(np.where(ok, disc, 0))
        for t in ((-b - sq) / (2 * a), (-b + sq) / (2 * a)):
            px, py, pz = ox + t * dx, oy + t * dy, oz + t * dz
            n = np.stack([sign * px / radius, np.zeros_like(px), sign * pz / radius], -1)
            take(t, n, kind, ok & (np.abs(py) <= HALF_H))

    for cap_y, ny, kind in ((HALF_H, 1.0, 3), (-HALF_H, -1.0, 4)):
        t = (cap_y - oy) / dy
        px, pz = ox + t * dx, oz + t * dz
        r2 = px * px + pz * pz
        n = np.zeros(ox.shape + (3,))
        n[..., 1] = ny
        take(t, n, kind, (r2 <= R_OUT ** 2) & (r2 >= R_IN ** 2))

    hit = np.isfinite(best_t)
    # turn every normal toward the camera, then light in view space
    facing = np.sign(normal @ (-d) + 1e-9)[..., None]
    n_view = (normal * facing) @ VIEW.T
    light = np.array([-0.45, 0.75, -0.5])
    light /= np.linalg.norm(light)
    diffuse = np.clip(n_view @ light, 0, 1)
    half = light + np.array([0, 0, -1.0])
    half /= np.linalg.norm(half)
    spec = np.clip(n_view @ half, 0, 1) ** 40
    rim = (1 - np.clip(-n_view[..., 2], 0, 1)) ** 3

    base = np.array([150, 22, 34], float)          # crimson band
    shade = 0.28 + 0.8 * diffuse
    inner = (part == 2)[..., None]
    rgb = base * shade[..., None] * np.where(inner, 0.55, 1.0)
    rgb += np.array([255, 170, 170]) * (spec * 0.9)[..., None]
    rgb += np.array([255, 60, 70]) * (rim * 0.25)[..., None]

    # darker groove around the middle of the outer wall, like the vanilla bands
    p = o + np.where(hit, best_t, 0)[..., None] * d
    groove = (part == 1) & (np.abs(p[..., 1]) < 0.035)
    rgb[groove] *= 0.45

    out = np.zeros((N, N, 4), np.uint8)
    out[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    out[..., 3] = np.where(hit, 255, 0)
    return Image.fromarray(out, "RGBA")


def spike(draw, base, tip, half_width):
    (bx, by), (tx, ty) = base, tip
    dx, dy = tx - bx, ty - by
    n = math.hypot(dx, dy) or 1
    nx, ny = -dy / n * half_width, dx / n * half_width
    draw.polygon([(bx + nx, by + ny), (bx, by), (tx, ty)], fill=(245, 238, 222, 255))   # lit side
    draw.polygon([(bx, by), (bx - nx, by - ny), (tx, ty)], fill=(170, 156, 136, 255))   # shaded side


def item_icon(size=128):
    s = SS
    spikes = []
    for k in range(8):
        a = (k + 0.5) * math.pi * 2 / 8
        radial = np.array([math.cos(a), 0, math.sin(a)])
        bx, by, depth = project(radial * R_OUT)
        tx, ty, _ = project(radial * (R_OUT + 0.38) + np.array([0, -0.08, 0]))
        spikes.append((depth, (bx, by), (tx, ty)))

    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    for depth, base, tip in spikes:
        if depth > 0:                     # far side: behind the band
            spike(d, base, tip, 3.2 * s)
    img.alpha_composite(render_band())
    d = ImageDraw.Draw(img)
    for depth, base, tip in spikes:
        if depth <= 0:
            spike(d, base, tip, 3.2 * s)

    # the charm hangs straight down from the front of the band's lower edge
    fx, fy, _ = project(front_dir() * R_OUT + np.array([0, -HALF_H, 0]))
    d.ellipse([fx - 3.5 * s, fy - 1 * s, fx + 3.5 * s, fy + 11 * s], outline=(225, 215, 195, 255), width=2 * s)
    top = fy + 9 * s
    w, h = 22 * s, 32 * s
    d.polygon(drop_polygon(fx, top, w, h), fill=(110, 0, 14, 255))
    d.polygon(drop_polygon(fx - 0.5 * s, top + 5 * s, w * 0.78, h * 0.8), fill=(175, 8, 26, 255))
    d.polygon(drop_polygon(fx - 2 * s, top + 13 * s, w * 0.45, h * 0.5), fill=(225, 40, 52, 255))
    d.ellipse([fx - 6 * s, top + 16 * s, fx - 2 * s, top + 22 * s], fill=(255, 215, 215, 255))  # wet shine

    # outlines around the whole silhouette, drop included
    canvas = np.array(img)
    alpha = np.where(canvas[..., 3] > 100, 255, 0).astype(np.uint8)
    dark = dilate(alpha, 3 * s)
    blue = dilate(alpha, 7 * s)
    out = np.zeros_like(canvas)
    out[blue > 0] = (*LUNAR_BLUE, 255)
    out[dark > 0] = (*OUTLINE_DARK, 255)
    # see-through hole in the middle of the band: flood the background in from the corner,
    # anything left that is not item or dark line is the enclosed hole
    outside = Image.fromarray(np.where(alpha > 0, 255, 0).astype(np.uint8)).copy()
    ImageDraw.floodfill(outside, (0, 0), 128)
    hole = (np.array(outside) == 0) & (dark == 0)
    out[hole] = 0
    result = Image.fromarray(out, "RGBA")
    result.alpha_composite(img)
    return result.resize((size, size), Image.LANCZOS)


def buff_icon():
    """White silhouette; the game tints buff icons with the buff color."""
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    s = SS
    cx = N * 0.5
    d.polygon(drop_polygon(cx, 10 * s, 70 * s, 105 * s), fill=(255, 255, 255, 255))
    # a chevron cut into the drop suggests speed
    d.polygon([(cx - 22 * s, 80 * s), (cx, 62 * s), (cx + 22 * s, 80 * s),
               (cx + 22 * s, 92 * s), (cx, 74 * s), (cx - 22 * s, 92 * s)], fill=(0, 0, 0, 0))
    return img.resize((128, 128), Image.LANCZOS)


item_icon().save(f"{OUT}/texAnkletOfBloodlustIcon.png")
if "--store" in sys.argv:    # 256x256 icon for the Thunderstore package
    item_icon(256).save(f"{OUT}/../Thunderstore/icon.png")
if "--buff" in sys.argv:
    buff_icon().save(f"{OUT}/texBloodlustBuffIcon.png")
print("ok")
