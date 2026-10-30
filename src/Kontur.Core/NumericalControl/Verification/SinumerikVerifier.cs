namespace Kontur.Core;

public sealed class SinumerikVerifier : INcVerifier
{
    public VerificationResult Verify(string? nc, Parameters? parameters) => NcSubsetVerifier.Verify(nc, parameters, true);
}
