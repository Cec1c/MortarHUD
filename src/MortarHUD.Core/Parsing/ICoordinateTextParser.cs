namespace MortarHUD.Core.Parsing;

/// <summary>
/// 把 OCR 出来的自由文本解析成 X / Y 数值。
/// </summary>
/// <remarks>
/// 解析器是「可信边界」：它只负责格式层面的判断，
/// 数值范围与置信度判断交给 <see cref="Validation.ICoordinateValidator"/>。
/// </remarks>
public interface ICoordinateTextParser
{
    CoordinateParseResult Parse(string? rawText);
}
