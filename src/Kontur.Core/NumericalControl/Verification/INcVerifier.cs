namespace Kontur.Core;

public interface INcVerifier
{
    VerificationResult Verify(string? nc, Parameters? parameters);
}
