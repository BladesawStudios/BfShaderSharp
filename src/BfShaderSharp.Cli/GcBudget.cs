using System.Diagnostics;

namespace BfShaderSharp.Cli;

/// <summary>Runs the process again with a large generation 0 budget, which the runtime only accepts as an environment variable.</summary>
/// <remarks>Preparing allocates heavily across every core. With the default budget the collector runs constantly; a 512 MB budget cut a full prepare by about 40%.</remarks>
internal static class GcBudget
{
    private const string Variable = "DOTNET_GCgen0size";
    private const string Value = "0x20000000";

    /// <summary>Starts a child with the budget set unless this process already has one. Returns false when the caller should just run.</summary>
    public static bool Relaunch(string[] args, out int exitCode)
    {
        exitCode = 0;
        string? path = Environment.ProcessPath;
        if (path is null || Environment.GetEnvironmentVariable(Variable) is not null) return false;

        ProcessStartInfo start = new(path) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.Environment[Variable] = Value;

        try
        {
            using Process? child = Process.Start(start);
            if (child is null) return false;
            child.WaitForExit();
            exitCode = child.ExitCode;
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
