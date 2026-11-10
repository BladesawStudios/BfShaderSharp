using BfShaderSharp.Lookup;
using BfShaderSharp.Prepared;

namespace BfShaderSharp.Cli;

// bfshader options <file.bfsp> <model> [<program>]: the options of a shading model with their choices, or with a program number the choice that program was
// compiled with for each option.
internal static class OptionsCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 3) return Fail("usage: bfshader options <file.bfsp> <model> [<program>]");
        using PreparedArchive archive = PreparedArchive.Open(args[1]);
        ProgramLookup? lookup = archive.Model(args[2])?.CreateLookup();
        if (lookup is null) return Fail("no such model");
        if (args.Length > 3)
        {
            if (!int.TryParse(args[3], out int program) || program < 0 || program >= lookup.ProgramCount) return Fail("no such program");
            foreach (var (name, choice) in lookup.ChoicesOf(program).OrderBy(c => c.Key, StringComparer.Ordinal)) Console.WriteLine($"{name}	{choice}");
            return 0;
        }

        foreach (LookupOption option in lookup.Options.OrderBy(o => o.Name, StringComparer.Ordinal))
            Console.WriteLine($"{option.Name}	default {option.Choices[option.DefaultChoice]}	{string.Join(" ", option.Choices)}");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
