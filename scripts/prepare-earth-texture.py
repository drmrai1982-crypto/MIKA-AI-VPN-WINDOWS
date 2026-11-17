#!/usr/bin/env python3
"""
Prepare the offline MIKA AI VPN daytime Earth texture.
- Downsample NASA's 21600x10800 source to a GPU-friendly 8192x4096 texture.
- Gently enhance daytime colour and detail.
- Overlay Natural Earth country borders, coastlines, and small-island outlines.
The final image keeps the same 2:1 equirectangular projection as the source.
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.request
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance

NATURAL_EARTH_GEOJSON = (
    "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/"
    "master/geojson/ne_50m_admin_0_countries.geojson"
)
MAX_TEXTURE_WIDTH = 8192
MAX_SOURCE_PIXELS = 250_000_000


def download_geojson(destination: Path) -> dict:
    request = urllib.request.Request(
        NATURAL_EARTH_GEOJSON,
        headers={"User-Agent": "MIKA-AI-VPN-Earth-Texture-Builder/1.0"},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        payload = response.read()
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(payload)
    return json.loads(payload)


def iter_rings(geometry: dict):
    kind = geometry.get("type")
    coordinates = geometry.get("coordinates", [])
    if kind == "Polygon":
        yield from coordinates
    elif kind == "MultiPolygon":
        for polygon in coordinates:
            yield from polygon


def project_ring(ring, width: int, height: int):
    points = []
    for coordinate in ring:
        lon, lat = coordinate[:2]
        x = round((float(lon) + 180.0) / 360.0 * (width - 1))
        y = round((90.0 - max(-90.0, min(90.0, float(lat)))) / 180.0 * (height - 1))
        points.append((x, y))
    return points


def split_dateline(points):
    """Split rings at the antimeridian to avoid drawing a line across the whole map."""
    if len(points) < 2:
        return []
    segments = []
    current = [points[0]]
    for point in points[1:]:
        if abs(point[0] - current[-1][0]) > width_for_dateline:
            if len(current) > 1:
                segments.append(current)
            current = [point]
        else:
            current.append(point)
    if len(current) > 1:
        segments.append(current)
    return segments


# The source is a known NASA Blue Marble asset. Disable Pillow's generic pixel
# warning, then enforce a strict project-specific upper bound and resize before
# allocating any RGBA overlay layers.
Image.MAX_IMAGE_PIXELS = None


def render_borders(image: Image.Image, geojson: dict) -> Image.Image:
    width, height = image.size
    glow = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    fine = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    glow_draw = ImageDraw.Draw(glow)
    fine_draw = ImageDraw.Draw(fine)

    # Split at half the texture width (the antimeridian).
    global width_for_dateline
    width_for_dateline = width // 2

    for feature in geojson.get("features", []):
        geometry = feature.get("geometry") or {}
        for ring in iter_rings(geometry):
            points = project_ring(ring, width, height)
            for segment in split_dateline(points):
                if len(segment) < 2:
                    continue
                # Warm champagne-gold outer trace with a crisp pale-gold centre line.
                glow_draw.line(segment, fill=(216, 181, 106, 95), width=5, joint="curve")
                fine_draw.line(segment, fill=(255, 224, 158, 215), width=2, joint="curve")

                # Keep small islands visible at this global viewing scale.
                if len(segment) >= 4:
                    xs = [p[0] for p in segment]
                    ys = [p[1] for p in segment]
                    if max(xs) - min(xs) <= 8 and max(ys) - min(ys) <= 8:
                        cx = sum(xs) // len(xs)
                        cy = sum(ys) // len(ys)
                        fine_draw.ellipse(
                            (cx - 2, cy - 2, cx + 2, cy + 2),
                            fill=(255, 224, 158, 225),
                        )

    composed = Image.alpha_composite(image.convert("RGBA"), glow)
    composed = Image.alpha_composite(composed, fine)
    return composed.convert("RGB")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument(
        "--geojson-cache",
        type=Path,
        default=Path("ne_50m_admin_0_countries.geojson"),
    )
    args = parser.parse_args()

    if not args.input.is_file():
        raise FileNotFoundError(f"NASA source texture not found: {args.input}")

    if args.geojson_cache.is_file():
        data = json.loads(args.geojson_cache.read_text(encoding="utf-8"))
    else:
        data = download_geojson(args.geojson_cache)

    with Image.open(args.input) as source:
        width, height = source.size
        if width * height > MAX_SOURCE_PIXELS:
            raise ValueError(
                f"Source image is unexpectedly large ({width}x{height}); "
                f"limit is {MAX_SOURCE_PIXELS:,} pixels."
            )
        if width < 2 or height < 2 or abs((width / height) - 2.0) > 0.02:
            raise ValueError(f"Expected a 2:1 equirectangular image, got {width}x{height}.")

        base = source.convert("RGB")
        if width > MAX_TEXTURE_WIDTH:
            target_size = (MAX_TEXTURE_WIDTH, MAX_TEXTURE_WIDTH // 2)
            base = base.resize(target_size, Image.Resampling.LANCZOS)

    # Preserve NASA's natural daytime colours; avoid the excessive brightness
    # previously used for night imagery.
    base = ImageEnhance.Brightness(base).enhance(1.04)
    base = ImageEnhance.Color(base).enhance(1.10)
    base = ImageEnhance.Contrast(base).enhance(1.12)
    base = ImageEnhance.Sharpness(base).enhance(1.18)

    result = render_borders(base, data)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    result.save(args.output, "JPEG", quality=93, optimize=True, progressive=True)

    print(f"Prepared {args.output} ({result.width}x{result.height}) with Natural Earth borders.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
