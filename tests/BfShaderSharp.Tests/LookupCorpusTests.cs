using System.Diagnostics;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Lookup;
using BfShaderSharp.Tests.Corpus;
using Xunit.Abstractions;

namespace BfShaderSharp.Tests;

public class LookupCorpusTests(ITestOutputHelper output)
{
    [CorpusFact]
    public void EveryProgramIsFoundByItsOwnChoices()
    {
        foreach (string folder in GameFiles.Folders)
        foreach ((string name, byte[] data) in GameFiles.BfshaFiles(folder))
        foreach (ShaderModel model in BfshaFile.Read(data).Models())
        {
            Stopwatch clock = Stopwatch.StartNew();
            ProgramLookup lookup = model.CreateLookup();
            long build = clock.ElapsedMilliseconds;
            int checkedPrograms = 0, bindings = 0, variations = model.Bnsh.VariationCount;
            for (int program = 0; program < model.ProgramCount; program++)
            {
                Dictionary<string, string> options = lookup.ChoicesOf(program);
                int found = lookup.Find(options);
                Assert.True(found >= 0, $"{name} {model.Name}: program {program} not found");
                Assert.Equal(options, lookup.ChoicesOf(found));
                ShaderProgram p = model.Program(found);
                Assert.InRange(p.VariationIndex, 0, variations - 1);
                for (int i = 0; i < p.SamplerCount; i++) { _ = p.Sampler(i); bindings++; }
                checkedPrograms++;
            }

            output.WriteLine($"{name} {model.Name}: {checkedPrograms} programs, {bindings} sampler bindings, lookup built in {build} ms, all in {clock.ElapsedMilliseconds} ms");
        }
    }
}
