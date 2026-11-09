using BfShaderSharp.Bfsha;
using BfShaderSharp.Tests.Corpus;
using Xunit.Abstractions;
namespace BfShaderSharp.Tests;
public class TmpProbe(ITestOutputHelper output)
{
    [CorpusFact]
    public void Probe()
    {
        foreach (string folder in GameFiles.Folders)
        foreach ((string name, byte[] data) in GameFiles.BfshaFiles(folder, 1))
        foreach (ShaderModel model in BfshaFile.Read(data).Models().Take(1))
        foreach (ShaderUniformBlock block in model.UniformBlocks.All())
        {
            output.WriteLine($"{name}/{model.Name}/{block.Name} type {block.Type} idx {block.Index} size {block.ByteSize} n {block.UniformCount} default {block.DefaultBuffer.Length}");
            foreach (ShaderUniform u in block.Uniforms.All().Take(8)) output.WriteLine($"   {u.Name} index {u.Index} offset {u.Offset} blk {u.BlockIndex}");
        }
    }
}
