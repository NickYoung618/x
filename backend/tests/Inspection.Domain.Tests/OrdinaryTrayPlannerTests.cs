using Inspection.Domain.Planning;

namespace Inspection.Domain.Tests;

public class OrdinaryTrayPlannerTests
{
    private static readonly FaceDefinition[] TwoFaces =
        [new("front", CameraGroup.AB), new("back", CameraGroup.AB)];

    [Fact]
    public void Batches_whole_tray_by_camera_and_preserves_identity_across_faces()
    {
        var plan = OrdinaryTrayPlanner.Build(["p1", "p2"], TwoFaces);
        Assert.Equal(8, plan.Faces.Sum(face => face.Captures.Count));
        Assert.Equal(["front", "back"], plan.Faces.Select(face => face.FaceId));
        foreach (var face in plan.Faces)
            Assert.Equal(["A:p1", "A:p2", "B:p1", "B:p2"],
                face.Captures.Select(c => $"{c.Camera}:{c.PartId}"));
        Assert.False(plan.Faces[0].RequiresFlipBefore);
        Assert.True(plan.Faces[1].RequiresFlipBefore);
        Assert.All(plan.Faces, face => Assert.True(face.RequiresScanBefore));
    }

    [Fact]
    public void Supports_more_parts_faces_and_cd_group_without_reordering_slots()
    {
        var plan = OrdinaryTrayPlanner.Build(["p3", "p1", "p2"],
            [new("f1", CameraGroup.CD), new("f2", CameraGroup.AB), new("f3", CameraGroup.CD)]);
        Assert.Equal(18, plan.Faces.Sum(f => f.Captures.Count));
        Assert.Equal(["C:p3", "C:p1", "C:p2", "D:p3", "D:p1", "D:p2"],
            plan.Faces[0].Captures.Select(c => $"{c.Camera}:{c.PartId}"));
        Assert.True(plan.Faces[2].RequiresFlipBefore);
    }

    [Fact]
    public void Input_mutation_cannot_change_published_plan()
    {
        var parts = new List<string> { "p1", "p2" };
        var faces = TwoFaces.ToList();
        var plan = OrdinaryTrayPlanner.Build(parts, faces);
        parts[0] = "other";
        faces.Clear();
        Assert.Equal(2, plan.Faces.Count);
        Assert.Equal("p1", plan.Faces[0].Captures[0].PartId);
        Assert.Throws<NotSupportedException>(() => ((IList<FaceCapturePlan>)plan.Faces).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<CaptureTarget>)plan.Faces[0].Captures).Clear());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" p1")]
    [InlineData("p1 ")]
    [InlineData(null)]
    public void Rejects_invalid_part_identifiers(string? invalid)
        => Assert.Throws<ArgumentException>(() => OrdinaryTrayPlanner.Build([invalid!], TwoFaces));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" f1")]
    [InlineData(null)]
    public void Rejects_invalid_face_identifiers(string? invalid)
        => Assert.Throws<ArgumentException>(() => OrdinaryTrayPlanner.Build(["p1"], [new(invalid!, CameraGroup.AB)]));

    [Fact]
    public void Rejects_empty_or_duplicate_parts_and_faces()
    {
        Assert.Throws<ArgumentException>(() => OrdinaryTrayPlanner.Build([], TwoFaces));
        Assert.Throws<ArgumentException>(() => OrdinaryTrayPlanner.Build(["p1", "p1"], TwoFaces));
        Assert.Throws<ArgumentException>(() => OrdinaryTrayPlanner.Build(["p1"], []));
        Assert.Throws<ArgumentException>(() => OrdinaryTrayPlanner.Build(["p1"], [TwoFaces[0], TwoFaces[0]]));
        Assert.Throws<ArgumentException>(() => OrdinaryTrayPlanner.Build(["p1"], [null!]));
    }

    [Fact]
    public void Rejects_undefined_camera_group()
        => Assert.Throws<ArgumentException>(() => OrdinaryTrayPlanner.Build(["p1"], [new("front", (CameraGroup)99)]));

    [Fact]
    public void Rejects_null_collections()
    {
        Assert.Throws<ArgumentNullException>(() => OrdinaryTrayPlanner.Build(null!, TwoFaces));
        Assert.Throws<ArgumentNullException>(() => OrdinaryTrayPlanner.Build(["p1"], null!));
    }
}
