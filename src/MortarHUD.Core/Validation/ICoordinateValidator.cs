using MortarHUD.Core.Models;

namespace MortarHUD.Core.Validation;

/// <summary>
/// 判定一次 OCR 结果是否可信到可以拿来当炮位/目标。
/// 这是「错误数据不许静默流下去」的最后一道闸门。
/// </summary>
public interface ICoordinateValidator
{
    CoordinateValidationResult Validate(CoordinateOcrResult ocrResult);
    CoordinateValidationResult ValidateCoordinates(CoordinateOcrResult ocrResult);
}
