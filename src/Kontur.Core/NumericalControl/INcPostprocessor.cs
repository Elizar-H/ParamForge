namespace Kontur.Core;

public interface INcPostprocessor
{
    PostprocessResult Process(MachiningPlan plan);
}
