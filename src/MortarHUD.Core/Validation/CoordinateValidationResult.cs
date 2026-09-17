using MortarHUD.Core.Models;

namespace MortarHUD.Core.Validation;

public sealed record CoordinateValidationResult
{
    public bool IsValid { get; init; }

    /// <summary>校验通过时的坐标；否则为 null。</summary>
    public MapCoordinate? Coordinate { get; init; }

    public string? Error { get; init; }

    public static CoordinateValidationResult Valid(MapCoordinate coordinate)
        => new() { IsValid = true, Coordinate = coordinate };

    public static CoordinateValidationResult Invalid(string error)
        => new() { IsValid = false, Error = error };
}
