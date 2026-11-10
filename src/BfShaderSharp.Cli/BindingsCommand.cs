using BfShaderSharp.Prepared;

namespace BfShaderSharp.Cli;

// bfshader bindings <file.bfsp> <model> <program>: the vertex attributes, the samplers and the uniform blocks of one program with the slot each
// stage binds them at (-1 where the stage does not use it).
internal static class BindingsCommand
{
    internal static int Run(string[] args)
    {
        if (args.Length < 4 || !int.TryParse(args[3], out int index)) return Fail("usage: bfshader bindings <file.bfsp> <model> <program>");
        using PreparedArchive archive = PreparedArchive.Open(args[1]);
        PreparedModel? model = archive.Model(args[2]);
        if (model is null || index < 0 || index >= model.ProgramCount) return Fail("no such model or program");
        PreparedProgram program = model.Program(index);
        Console.WriteLine($"{model.Name} program {index} (variation {program.Variation})");
        foreach (PreparedAttribute a in model.Attributes()) Console.WriteLine($"attribute\t{a.Name}\tlocation {a.Location}");
        foreach (PreparedSampler s in model.Samplers())
        {
            PreparedBinding b = program.Sampler(s.Index);
            Console.WriteLine($"sampler\t{s.Name}\t{s.Annotation}\tv{b.Vertex}/f{b.Fragment}");
        }

        foreach (PreparedBlock block in model.Blocks())
        {
            PreparedBinding b = program.UniformBlock(block.Index);
            Console.WriteLine($"block\t{block.Name}\ttype {block.Type}\tsize {block.ByteSize}\tv{b.Vertex}/f{b.Fragment}");
        }

        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
