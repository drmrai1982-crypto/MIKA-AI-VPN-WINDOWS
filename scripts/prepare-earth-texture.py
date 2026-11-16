#!/usr/bin/env python3
"""
Prepare the offline MIKA AI VPN Earth texture.
- Brighten NASA Black Marble without washing out city lights.
- Overlay Natural Earth coastlines, country borders, and small-island outlines.
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


def download_geojson(destination: Path) -> dict:
    request = urllib.request.Request(
        NATURAL_EARTH_GEOJSON,
        headers={"User-Agent": "MIKA-AI-VPN-Earth-Texture-Builder/1.0"},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        payload = response.read()
    destination.write_bytes(payload)
    return json.loads(payload)


def iter_rings(geometry: dict):
    kind = geometry.get("type")
    coordinates = geometry.get("coordinates", [])
    if kind == "Polygon":
        for ring in coordinates:
            yield ring
    elif kind == "MultiPolygon":
        for polygon in coordinates:
            for ring in polygon:
                yield ring


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
        if abs(point[0] - current[-1][0]) > 2000:
            if len(current) > 1:
                segments.append(current)
            current = [point]
        else:
            current.append(point)
    if len(current) > 1:
        segments.append(current)
    return segments


def render_borders(image: Image.Image, geojson: dict) -> Image.Image:
    width, height = image.size
    glow = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    fine = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    glow_draw = ImageDraw.Draw(glow)
    fine_draw = ImageDraw.Draw(fine)

    for feature in geojson.get("features", []):
        geometry = feature.get("geometry") or {}
        for ring in iter_rings(geometry):
            points = project_ring(ring, width, height)
            for segment in split_dateline(points):
                if len(segment) < 2:
                    continue
                # Warm champagne-gold outer trace with a crisp pale-gold centre line.
                glow_draw.line(segment, fill=(216, 181, 106, 105), width=7, joint="curve")
                fine_draw.line(segment, fill=(255, 224, 158, 220), width=2, joint="curve")

                # Keep tiny islands visible at this global viewing scale.
                if len(segment) >= 4:
                    xs = [p[0] for p in segment]
                    ys = [p[1] for p in segment]
                    if max(xs) - min(xs) <= 8 and max(ys) - min(ys) <= 8:
                        cx = sum(xs) // len(xs)
                        cy = sum(ys) // len(ys)
                        fine_draw.ellipse((cx - 2, cy - 2, cx + 2, cy + 2),
                                          fill=(255, 224, 158, 225))

    composed = Image.alpha_composite(image.convert("RGBA"), glow)
    composed = Image.alpha_composite(composed, fine)
    return composed.convert("RGB")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--geojson-cache", type=Path, default=Path("ne_50m_admin_0_countries.geojson"))
    args = parser.parse_args()

    if not args.input.is_file():
        raise FileNotFoundError(f"NASA source texture not found: {args.input}")

    if args.geojson_cache.is_file():
        data = json.loads(args.geojson_cache.read_text(encoding="utf-8"))
    else:
        data = download_geojson(args.geojson_cache)

    with Image.open(args.input) as source:
        base = source.convert("RGB")
        if base.size != (13500, 6750):
            print(f"Note: NASA texture size is {base.size}; expected 13500x6750.", file=sys.stderr)
        # Preserve NASA's natural daytime colours while gently improving local contrast and detail.
        base = ImageEnhance.Brightness(base).enhance(1.55)
        base = ImageEnhance.Color(base).enhance(1.16)
        base = ImageEnhance.Contrast(base).enhance(1.07)
        result = render_borders(base, data)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        result.save(args.output, "JPEG", quality=94, optimize=True, progressive=True)

    print(f"Prepared {args.output} ({result.width}x{result.height}) with Natural Earth borders.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
