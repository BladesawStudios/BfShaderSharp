namespace BfShaderSharp.Preparation;

/// <summary>How the text of a prepared stage reads.</summary>
/// <remarks>Raw is what the translator writes, with its own names for inputs, samplers and blocks. Reflected is rewritten with the names and slots of the stage's reflection and its constants as literals (see <c>ReflectedGlsl</c>), the text a renderer built on ShaderLibrary's extractor patches.</remarks>
public enum StageText
{
    Raw,
    Reflected,
}
