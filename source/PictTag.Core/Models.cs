namespace PictTag.Core;

public record BoundingBox(int YMin, int XMin, int YMax, int XMax);

public record DetectedEntity(string Label, BoundingBox Box);

public record ImageAnalysisResult(List<DetectedEntity> Entities);
