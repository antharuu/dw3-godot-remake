using Dw3.Disc;

namespace Dw3.Extractor;

/// <summary>
/// One step of the full extraction: the name of its scratch directory (and of the progress
/// event) and the function that runs it.
/// </summary>
/// <param name="Name">Scratch sub-directory and progress step name, for example <c>zones</c>.</param>
/// <param name="Run">Receives the disc opener and the scratch output directory; returns 0 on success
/// or the step's non-zero exit code.</param>
internal sealed record ExtractionDomain(string Name, Func<Func<IDiscSource>, string, int> Run);
