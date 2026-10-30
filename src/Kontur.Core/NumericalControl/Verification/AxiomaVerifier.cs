namespace Kontur.Core;

public sealed class AxiomaVerifier : INcVerifier
{
    public VerificationResult Verify(string? nc, Parameters? parameters) => NcSubsetVerifier.Verify(nc, parameters, false);
}
