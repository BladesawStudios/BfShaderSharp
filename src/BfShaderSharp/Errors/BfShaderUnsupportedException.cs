namespace BfShaderSharp.Errors;

/// <summary>The error thrown when a file is well formed but uses a version or feature the library does not read.</summary>
/// <remarks>It derives from <see cref="BfShaderFormatException"/>, so one catch covers every file the library cannot use.</remarks>
public sealed class BfShaderUnsupportedException(string message, long? offset = null) : BfShaderFormatException(message, offset);
