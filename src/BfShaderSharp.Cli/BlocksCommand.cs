using BfShaderSharp.Prepared;

namespace BfShaderSharp.Cli;

// bfshader blocks: lists the uniform blocks of prepared files, one line for each block name with its size and the models that declare it,
// with --models the model names too, and with --uniforms the variables of the block (their offsets are stored as byte offset + 1).
internal static class BlocksCommand
{
    internal static int Run(string[] args)
    {
        bool uniforms = args.Contains("--uniforms");
        Dictionary<string, BlockUse> blocks = [];
        foreach (string path in args.Skip(1).Where(a => !a.StartsWith("--")))
        {
            PreparedArchive archive = PreparedArchive.Read(File.ReadAllBytes(path));
            string file = Path.GetFileName(path);
            foreach (PreparedModel model in archive.Models())
            foreach (PreparedBlock block in model.Blocks())
            {
                if (!blocks.TryGetValue(block.Name, out BlockUse? use)) blocks[block.Name] = use = new BlockUse();
                use.Add(file, model.Name, block);
                for (int program = 0; program < model.ProgramCount; program++)
                    use.Slots.Add(model.Program(program).UniformBlock(block.Index));
            }
        }

        foreach ((string name, BlockUse use) in blocks.OrderBy(b => b.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{name}\ttype {string.Join("/", use.Types.Order())}\tsize {string.Join("/", use.Sizes.Order())}\tslots {Slots(use)}	{use.Models} models in {string.Join(",", use.Files.Order())}");
            if (args.Contains("--models")) Console.WriteLine($"    models: {string.Join(", ", use.ModelNames)}");
            if (!uniforms) continue;
            foreach (PreparedUniform u in use.Uniforms.Values.OrderBy(u => u.Offset)) Console.WriteLine($"    +0x{u.Offset:x}\t{u.Name}");
        }

        Console.WriteLine($"{blocks.Count} distinct blocks");
        return 0;
    }

    private static string Slots(BlockUse use) =>
        string.Join(" ", use.Slots.Where(b => b.Vertex >= 0 || b.Fragment >= 0).Select(b => $"v{b.Vertex}/f{b.Fragment}").Distinct().Order());

    private sealed class BlockUse
    {
        public SortedSet<string> ModelNames { get; } = [];
        public HashSet<int> Types { get; } = [];
        public HashSet<int> Sizes { get; } = [];
        public HashSet<string> Files { get; } = [];
        public HashSet<PreparedBinding> Slots { get; } = [];
        public Dictionary<string, PreparedUniform> Uniforms { get; } = [];
        public int Models { get; private set; }

        public void Add(string file, string model, PreparedBlock block)
        {
            Models++;
            ModelNames.Add(model);
            Types.Add(block.Type);
            Sizes.Add(block.ByteSize);
            Files.Add(file);
            foreach (PreparedUniform u in block.Uniforms) Uniforms.TryAdd($"{u.Offset}:{u.Name}", u);
        }
    }
}
