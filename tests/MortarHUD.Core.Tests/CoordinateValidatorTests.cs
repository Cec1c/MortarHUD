using MortarHUD.Core.Models;
using MortarHUD.Core.Validation;
using Xunit;

namespace MortarHUD.Core.Tests;

/// <summary>校验测试（TDD §16）。</summary>
public class CoordinateValidatorTests
{
    private static readonly CoordinateValidator Validator = new();

    private static CoordinateOcrResult Result(double? x, double? y, double confidence = 0.9)
        => new()
        {
            Success = true,
            X = x,
            Y = y,
            Confidence = confidence,
            RawText = "y109.78 x98.09",
        };

    [Fact]
    public void Validate_ValidCoordinate_Passes()
    {
        var validation = Validator.Validate(Result(98.09, 109.78));

        Assert.True(validation.IsValid);
        Assert.Equal(new MapCoordinate(98.09, 109.78), validation.Coordinate);
    }

    [Fact]
    public void Validate_OcrFailure_IsRejected()
    {
        var validation = Validator.Validate(CoordinateOcrResult.Failed("X_NOT_FOUND"));

        Assert.False(validation.IsValid);
        Assert.Equal("X_NOT_FOUND", validation.Error);
    }

    [Theory]
    [InlineData(null, 109.78, "X_NOT_FOUND")]
    [InlineData(98.09, null, "Y_NOT_FOUND")]
    public void Validate_MissingAxis_IsRejected(double? x, double? y, string expectedError)
    {
        var validation = Validator.Validate(Result(x, y));

        Assert.False(validation.IsValid);
        Assert.Equal(expectedError, validation.Error);
    }

    [Theory]
    [InlineData(-1.0, 100.0, "X_OUT_OF_RANGE")]
    [InlineData(100.0, -1.0, "Y_OUT_OF_RANGE")]
    [InlineData(500.0, 100.0, "X_OUT_OF_RANGE")]
    [InlineData(100.0, 500.0, "Y_OUT_OF_RANGE")]
    public void Validate_OutOfRange_IsRejected(double x, double y, string expectedError)
    {
        var validation = Validator.Validate(Result(x, y));

        Assert.False(validation.IsValid);
        Assert.Equal(expectedError, validation.Error);
    }

    [Fact]
    public void Validate_LowConfidence_IsRejected()
    {
        var validation = Validator.Validate(Result(98.09, 109.78, confidence: 0.10));

        Assert.False(validation.IsValid);
        Assert.Equal("LOW_CONFIDENCE", validation.Error);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_NonFinite_IsRejected(double value)
    {
        var validation = Validator.Validate(Result(value, 100.0));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Validate_CustomRange_IsRespected()
    {
        var validator = new CoordinateValidator(new CoordinateValidationOptions
        {
            CoordinateMin = 90,
            CoordinateMax = 120,
        });

        Assert.True(validator.Validate(Result(98.09, 109.78)).IsValid);
        Assert.False(validator.Validate(Result(50.0, 100.0)).IsValid);
    }
}
