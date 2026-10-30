namespace Kontur.Core;

public static class CncProfiles
{
    public const string Axioma = "axioma";
    public const string Sinumerik = "sinumerik840dsl";
    public static bool Supported(string profile) => profile is Axioma or Sinumerik;
    public static string Name(string profile) => profile switch
    {
        Axioma => "AxiOMA Control",
        Sinumerik => "SINUMERIK 840D sl",
        _ => throw new ArgumentException("Неизвестный профиль ЧПУ.")
    };
    public static INcPostprocessor Postprocessor(string profile) => profile switch
    {
        Axioma => new AxiomaPostprocessor(),
        Sinumerik => new SinumerikPostprocessor(),
        _ => throw new ArgumentException("Неизвестный профиль ЧПУ.")
    };
    public static INcVerifier Verifier(string profile) => profile switch
    {
        Axioma => new AxiomaVerifier(),
        Sinumerik => new SinumerikVerifier(),
        _ => throw new ArgumentException("Неизвестный профиль ЧПУ.")
    };
}
