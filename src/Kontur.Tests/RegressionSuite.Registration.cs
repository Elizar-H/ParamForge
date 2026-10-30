namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterTests()
    {
        RegisterStandardToleranceAndControllerTests();

        RegisterWordBorderTests();

        RegisterStockSizingTests();

        RegisterBlankAndMetadataTests();

        RegisterParameterSchemaTests();
        RegisterParameterValidationTests();
        RegisterProcessSetupTests();
        RegisterMachiningPlanTests();
        RegisterVerifierTests();

        RegisterFileTests();

    }
}
