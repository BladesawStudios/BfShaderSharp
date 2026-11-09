using BfShaderSharp.Bfsha;
using BfShaderSharp.Tests.Corpus;
using Xunit.Abstractions;

namespace BfShaderSharp.Tests;

public class ModelTablesCorpusTests(ITestOutputHelper output)
{
    [CorpusFact]
    public void SamplersAttributesAndUniformBlocksAreConsistent()
    {
        foreach (string folder in GameFiles.Folders)
        foreach ((string name, byte[] data) in GameFiles.BfshaFiles(folder))
        foreach (ShaderModel model in BfshaFile.Read(data).Models())
        {
            foreach (ShaderSampler sampler in model.Samplers.All()) Assert.False(string.IsNullOrEmpty(sampler.Name));
            foreach (ShaderAttribute attribute in model.Attributes.All()) Assert.InRange(attribute.Location, -1, 31);
            int uniforms = 0;
            foreach (ShaderUniformBlock block in model.UniformBlocks.All())
            {
                Assert.Equal(block.UniformCount, block.Uniforms.Count);
                Assert.Equal(block.ByteSize, block.DefaultBuffer.Length == 0 ? block.ByteSize : block.DefaultBuffer.Length);
                foreach (ShaderUniform uniform in block.Uniforms.All())
                {
                    Assert.InRange(uniform.Offset, 0, Math.Max(block.ByteSize - 1, 0));
                    uniforms++;
                }
            }

            if (model.ProgramCount > 1000 || name.StartsWith("Terrain"))
                output.WriteLine($"{model.Name}: {model.Samplers.Count} samplers, {model.Attributes.Count} attributes, {model.UniformBlocks.Count} blocks, {uniforms} uniforms; first block {(model.UniformBlocks.Count > 0 ? model.UniformBlocks[0].Name : "-")}, first sampler {(model.Samplers.Count > 0 ? model.Samplers[0] : default)}");
        }
    }
}
