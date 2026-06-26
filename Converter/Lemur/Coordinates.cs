using ImageMagick;

namespace Converter.Lemur;

// Explicit coordinate-space types. The converter juggles four 2D spaces that all look like a
// bare (double, double) but are NOT interchangeable; silently mixing them produced the burg
// clustering bug (canvas burg positions compared against geo cell polygons). Keeping each space
// a distinct type makes a cross-space mistake a COMPILE error instead of a silent wrong answer.
//
// Conversions that need the map's scale live in Helper (GeoToImage / GeoToWorld / CanvasToWorld /
// CanvasToGeo / GeoToCanvas), each named <SourceSpace>To<DestSpace>. The pure Image<->World vertical
// flip lives here (no map needed).

/// <summary>Azgaar geographic coordinate (longitude, latitude). Cell GeoDataCoordinates live here.</summary>
public readonly record struct GeoPoint(double Lon, double Lat);

/// <summary>Azgaar canvas/SVG pixel coordinate (azBurg.x/y; scaled by info.width/height). Burg positions live here.</summary>
public readonly record struct CanvasPoint(double X, double Y);

/// <summary>CK3 map IMAGE pixel: origin top-left, Y increases DOWNWARD (north = top). Used when rasterizing PNG/DDS.</summary>
public readonly record struct ImagePixel(double X, double Y)
{
    /// <summary>Flip to CK3 world space: Z = MapHeight - Y. The two differ only on the vertical axis.</summary>
    public WorldPixel ToWorld() => new(X, Entities.Map.MapHeight - Y);

    /// <summary>
    /// Bridge to Magick.NET's <c>PointD</c> (the "D" is Double — a double-precision X/Y point), which its
    /// untyped draw API (.Polygon/.Line) consumes. Only image space should ever be rasterized, hence it lives here.
    /// </summary>
    public PointD ToMagickPoint() => new(X, Y);
}

/// <summary>CK3 WORLD pixel: X east, Z increases NORTHWARD (north = high Z). Used for map_object locator positions.</summary>
public readonly record struct WorldPixel(double X, double Z)
{
    /// <summary>Flip to image space: Y = MapHeight - Z. The two differ only on the vertical axis.</summary>
    public ImagePixel ToImage() => new(X, Entities.Map.MapHeight - Z);
}
