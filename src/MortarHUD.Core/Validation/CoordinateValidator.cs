using MortarHUD.Core.Models;

namespace MortarHUD.Core.Validation;

/// <summary>
/// 按 TDD §16 逐项检查：X 存在、Y 存在、X 在范围内、Y 在范围内、格式合法、置信度达标。
/// </summary>
public sealed class CoordinateValidator : ICoordinateValidator
{
    private readonly CoordinateValidationOptions _options;

    public CoordinateValidator(CoordinateValidationOptions? options = null)
        => _options = options ?? CoordinateValidationOptions.Default;

    public CoordinateValidationResult Validate(CoordinateOcrResult ocrResult)
    {
        if (ocrResult is null)
        {
            return CoordinateValidationResult.Invalid("NULL_RESULT");
        }

        if (!ocrResult.Success)
        {
            return CoordinateValidationResult.Invalid(ocrResult.Error ?? "OCR_FAILED");
        }

        if (ocrResult.X is null && _options.RequireBothAxes)
        {
            return CoordinateValidationResult.Invalid("X_NOT_FOUND");
        }

        if (ocrResult.Y is null && _options.RequireBothAxes)
        {
            return CoordinateValidationResult.Invalid("Y_NOT_FOUND");
        }

        if (ocrResult.X is null || ocrResult.Y is null)
        {
            // RequireBothAxes=false 时，缺一轴也算不完整，无法形成地图坐标。
            return CoordinateValidationResult.Invalid("INCOMPLETE_COORDINATE");
        }

        if (!double.IsFinite(ocrResult.X.Value) || !double.IsFinite(ocrResult.Y.Value))
        {
            return CoordinateValidationResult.Invalid("NON_FINITE_COORDINATE");
        }

        if (ocrResult.Confidence < _options.MinimumConfidence)
        {
            return CoordinateValidationResult.Invalid("LOW_CONFIDENCE");
        }

        var x = ocrResult.X.Value;
        var y = ocrResult.Y.Value;

        if (x < _options.CoordinateMin || x > _options.CoordinateMax)
        {
            return CoordinateValidationResult.Invalid("X_OUT_OF_RANGE");
        }

        if (y < _options.CoordinateMin || y > _options.CoordinateMax)
        {
            return CoordinateValidationResult.Invalid("Y_OUT_OF_RANGE");
        }

        return CoordinateValidationResult.Valid(new MapCoordinate(x, y));
    }
}
