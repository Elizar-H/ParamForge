namespace Kontur.Core;
/// <summary>Выбор независимого проверяющего модуля по профилю ЧПУ.</summary>
public static class NcVerifier
{
    public const string Version = "2.4.0";
    public const int MaximumCharacters = NcSubsetVerifier.MaximumCharacters;
    public const int MaximumLines = NcSubsetVerifier.MaximumLines;
    public const int MaximumErrors = NcSubsetVerifier.MaximumErrors;
    public static VerificationResult Verify(string? nc, Parameters? parameters)
    {
        if (parameters is not null && !CncProfiles.Supported(parameters.Controller))
        {
            return new(false, ["Неизвестный профиль ЧПУ."], [], [], 0);
        }

        return CncProfiles.Verifier(parameters?.Controller ?? CncProfiles.Axioma).Verify(nc, parameters);
    }
}
