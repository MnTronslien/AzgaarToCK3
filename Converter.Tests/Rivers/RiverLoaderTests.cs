using Converter.Lemur.Deserialization;
using Converter.Lemur.Entities;
using Converter.Lemur.Rivers;
using Xunit;

namespace Converter.Tests.Rivers;

/// <summary>
/// Regression tests for <see cref="RiverLoader.MergeControlPoints"/>.
///
/// Issue #32: a malformed rivers GeoJSON feature (null feature, or a feature whose
/// <c>properties</c>/<c>geometry</c> is null) used to throw an unhandled
/// NullReferenceException while merging control points. Because the rivers step runs
/// before any file writer and the output directory is wiped at the start of a run,
/// the crash left an empty mod folder. These tests pin the guard that skips such
/// features instead of crashing.
/// </summary>
public class RiverLoaderTests
{
    private static RiverFeature ValidFeature(int id) => new(
        type: "Feature",
        geometry: new RiverGeometry("LineString", new[] { new[] { 1f, 1f }, new[] { 2f, 2f } }),
        properties: new RiverFeatureProperties(
            id: id, source: 0, mouth: 0, parent: 0, basin: 0,
            widthFactor: 1f, sourceWidth: 0.1f, discharge: 10f, name: "Test", type: "River"));

    [Fact]
    public void MergeControlPoints_MergesValidFeatures()
    {
        var rivers = new List<River> { new() { Id = 1 }, new() { Id = 2 } };
        var geo = new RiverGeoJson("FeatureCollection", new[] { ValidFeature(1), ValidFeature(2) });

        var (merged, skipped) = RiverLoader.MergeControlPoints(rivers, geo);

        Assert.Equal(2, merged);
        Assert.Equal(0, skipped);
        Assert.All(rivers, r => Assert.NotNull(r.ControlPoints));
    }

    [Fact]
    public void MergeControlPoints_SkipsFeatureWithNullProperties_DoesNotThrow()
    {
        var rivers = new List<River> { new() { Id = 1 } };
        var malformed = new RiverFeature(
            type: "Feature",
            geometry: new RiverGeometry("LineString", new[] { new[] { 1f, 1f } }),
            properties: null!);
        var geo = new RiverGeoJson("FeatureCollection", new[] { malformed, ValidFeature(1) });

        var (merged, skipped) = RiverLoader.MergeControlPoints(rivers, geo);

        Assert.Equal(1, merged);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void MergeControlPoints_SkipsFeatureWithNullGeometry_DoesNotThrow()
    {
        var rivers = new List<River> { new() { Id = 1 } };
        var malformed = new RiverFeature("Feature", geometry: null!,
            properties: new RiverFeatureProperties(1, 0, 0, 0, 0, 1f, 0.1f, 10f, "Test", "River"));
        var geo = new RiverGeoJson("FeatureCollection", new[] { malformed, ValidFeature(1) });

        var (merged, skipped) = RiverLoader.MergeControlPoints(rivers, geo);

        Assert.Equal(1, merged);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void MergeControlPoints_SkipsNullFeature_DoesNotThrow()
    {
        var rivers = new List<River> { new() { Id = 1 } };
        var geo = new RiverGeoJson("FeatureCollection", new[] { null!, ValidFeature(1) });

        var (merged, skipped) = RiverLoader.MergeControlPoints(rivers, geo);

        Assert.Equal(1, merged);
        Assert.Equal(1, skipped);
    }
}
