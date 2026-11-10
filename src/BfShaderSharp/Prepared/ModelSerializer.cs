using BfShaderSharp.Binary;
using BfShaderSharp.Errors;
using BfShaderSharp.Lookup;

namespace BfShaderSharp.Prepared;

/// <summary>Appends the tables of one model to the table buffer and returns the position of its model record.</summary>
/// <remarks>
/// Each table is written before the model record that points at it, so no offset is patched afterwards. Tables start on
/// 8-byte boundaries. Blob references are renumbered to the file order of the blobs. Strings are interned in the shared pool.
/// </remarks>
internal sealed class ModelSerializer(ByteSink sink, StringPool strings, int[] blobPosition)
{
    internal List<long> ModelOffsets { get; } = [];

    internal void Write(PreparedModelData model)
    {
        int programs = model.Programs.Count;
        long options = WriteOptions(model.Options);
        long keyRows = Table(model.KeyRows.Span);
        long variations = WriteVariations(model.Variations);
        long programTable = WriteProgramVariations(model.Programs);
        long bindings = WriteBindings(model);
        long samplers = WriteSamplers(model.Samplers);
        long attributes = WriteAttributes(model.Attributes);
        long blocks = WriteBlocks(model.Blocks);
        sink.Align(8);
        ModelOffsets.Add(sink.Length);
        sink.U32((uint)strings.Intern(model.Name));
        sink.U32((uint)programs);
        sink.U32((uint)model.RowWords);
        sink.U32((uint)model.Options.Count);
        sink.U32((uint)model.Variations.Count);
        sink.U32((uint)model.Samplers.Count);
        sink.U32((uint)model.Attributes.Count);
        sink.U32((uint)model.Blocks.Count);
        foreach (long offset in new[] { options, keyRows, variations, programTable, bindings, samplers, attributes, blocks }) sink.U64((ulong)offset);
    }

    private int Blob(int number)
    {
        if (number == PreparedLayout.NoBlob) return number;
        if ((uint)number >= (uint)blobPosition.Length) throw new BfShaderFormatException($"Blob {number} is not in the pool");
        return blobPosition[number];
    }

    private long Table(ReadOnlySpan<byte> bytes)
    {
        sink.Align(8);
        long at = sink.Length;
        sink.Bytes(bytes);
        return at;
    }

    private long WriteOptions(IReadOnlyList<LookupOption> options)
    {
        long[] choices = new long[options.Count];
        for (int i = 0; i < options.Count; i++)
        {
            sink.Align(8);
            choices[i] = sink.Length;
            foreach (string choice in options[i].Choices) sink.U32((uint)strings.Intern(choice));
        }

        sink.Align(8);
        long at = sink.Length;
        for (int i = 0; i < options.Count; i++)
        {
            LookupOption option = options[i];
            if (option.Word > ushort.MaxValue || option.Shift > byte.MaxValue) throw new BfShaderFormatException($"Option {option.Name} does not fit the prepared layout");
            sink.U32((uint)strings.Intern(option.Name));
            sink.U32(option.Mask);
            sink.U16((ushort)option.Word);
            sink.U8((byte)option.Shift);
            sink.U8(0);
            sink.I32(option.DefaultChoice);
            sink.U32((uint)option.Choices.Count);
            sink.U32(0);
            sink.U64((ulong)choices[i]);
        }

        return at;
    }

    private long WriteVariations(IReadOnlyList<int[]> variations)
    {
        sink.Align(8);
        long at = sink.Length;
        foreach (int[] stages in variations)
        {
            if (stages.Length != PreparedLayout.StageCount) throw new BfShaderFormatException("A variation must name one blob for each of the eight stages");
            foreach (int blob in stages) sink.I32(Blob(blob));
        }

        return at;
    }

    private long WriteProgramVariations(IReadOnlyList<PreparedProgramData> programs)
    {
        sink.Align(8);
        long at = sink.Length;
        foreach (PreparedProgramData program in programs) sink.U32((uint)program.Variation);
        return at;
    }

    private long WriteBindings(PreparedModelData model)
    {
        sink.Align(8);
        long at = sink.Length;
        int slots = model.Samplers.Count + model.Blocks.Count;
        foreach (PreparedProgramData program in model.Programs)
        {
            if (program.Bindings.Count != slots) throw new BfShaderFormatException("A program must bind every sampler and block of its model");
            foreach (PreparedBinding binding in program.Bindings)
            {
                sink.U16((ushort)binding.Vertex);
                sink.U16((ushort)binding.Fragment);
            }
        }

        return at;
    }

    private long WriteSamplers(IReadOnlyList<PreparedSampler> samplers)
    {
        sink.Align(8);
        long at = sink.Length;
        foreach (PreparedSampler sampler in samplers)
        {
            sink.U32((uint)strings.Intern(sampler.Name));
            sink.U32((uint)strings.Intern(sampler.Annotation));
            sink.U32((uint)sampler.Index);
            sink.U32(0);
        }

        return at;
    }

    private long WriteAttributes(IReadOnlyList<PreparedAttribute> attributes)
    {
        sink.Align(8);
        long at = sink.Length;
        foreach (PreparedAttribute attribute in attributes)
        {
            sink.U32((uint)strings.Intern(attribute.Name));
            sink.I32(attribute.Index);
            sink.I32(attribute.Location);
            sink.U32(0);
        }

        return at;
    }

    private long WriteBlocks(IReadOnlyList<PreparedBlock> blocks)
    {
        long[] uniforms = new long[blocks.Count];
        for (int i = 0; i < blocks.Count; i++)
        {
            sink.Align(8);
            uniforms[i] = sink.Length;
            foreach (PreparedUniform uniform in blocks[i].Uniforms)
            {
                sink.U32((uint)strings.Intern(uniform.Name));
                sink.I32(uniform.Index);
                sink.U32((uint)uniform.Offset);
                sink.U32((uint)uniform.BlockIndex);
            }
        }

        sink.Align(8);
        long at = sink.Length;
        for (int i = 0; i < blocks.Count; i++)
        {
            PreparedBlock block = blocks[i];
            sink.U32((uint)strings.Intern(block.Name));
            sink.U16((ushort)block.Index);
            sink.U16((ushort)block.Type);
            sink.U32((uint)block.ByteSize);
            sink.U32((uint)block.Uniforms.Count);
            sink.U64((ulong)uniforms[i]);
            sink.I32(Blob(block.DefaultBuffer));
            sink.U32(0);
        }

        return at;
    }
}
