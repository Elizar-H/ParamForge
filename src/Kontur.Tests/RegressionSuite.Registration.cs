namespace Kontur.Tests;

internal static partial class RegressionSuite
{
    private static void RegisterTests()
    {
        RegisterAuditTests();
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
