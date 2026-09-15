namespace Inspection.Domain.Planning;

public enum CameraGroup { AB, CD }
public enum Camera { A, B, C, D }
public sealed record FaceDefinition(string FaceId, CameraGroup CameraGroup);
public sealed record CaptureTarget(string PartId, Camera Camera);
public sealed record FaceCapturePlan(string FaceId, bool RequiresFlipBefore,
    bool RequiresScanBefore, IReadOnlyList<CaptureTarget> Captures);
public sealed record OrdinaryTrayPlan(IReadOnlyList<FaceCapturePlan> Faces);

public static class OrdinaryTrayPlanner
{
    public static OrdinaryTrayPlan Build(IEnumerable<string> partIds, IEnumerable<FaceDefinition> faces)
    {
        ArgumentNullException.ThrowIfNull(partIds);
        ArgumentNullException.ThrowIfNull(faces);
        var parts = partIds.ToArray();
        var definitions = faces.ToArray();
        ValidateIdentifiers(parts, nameof(partIds));
        if (definitions.Any(face => face is null))
            throw new ArgumentException("Face definitions cannot contain null.", nameof(faces));
        ValidateIdentifiers(definitions.Select(face => face.FaceId).ToArray(), nameof(faces));

        var plans = new List<FaceCapturePlan>();
        foreach (var face in definitions)
        {
            Camera[] cameras = face.CameraGroup switch
            {
                CameraGroup.AB => [Camera.A, Camera.B],
                CameraGroup.CD => [Camera.C, Camera.D],
                _ => throw new ArgumentException("Camera group must be AB or CD.", nameof(faces))
            };
            var captures = cameras.SelectMany(camera => parts.Select(part => new CaptureTarget(part, camera))).ToArray();
            plans.Add(new FaceCapturePlan(face.FaceId, plans.Count > 0, true, Array.AsReadOnly(captures)));
        }
        return new OrdinaryTrayPlan(plans.AsReadOnly());
    }

    private static void ValidateIdentifiers(string[] identifiers, string parameterName)
    {
        if (identifiers.Length == 0 || identifiers.Any(id => string.IsNullOrWhiteSpace(id) || id != id.Trim()))
            throw new ArgumentException("At least one nonblank identifier without surrounding whitespace is required.", parameterName);
        if (identifiers.Distinct(StringComparer.Ordinal).Count() != identifiers.Length)
            throw new ArgumentException("Identifiers must be unique.", parameterName);
    }
}
