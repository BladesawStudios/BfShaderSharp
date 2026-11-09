namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Finds the direct branches and calls of a native program and decides whether its control flow is the bounded kind the lowering can follow.</summary>
/// <remarks>
/// Control flow is bounded when every call sets up its return address in a register just before it, every call target
/// is entered only by calls and ends in a return through that register, and no other indirect control flow exists.
/// The scan records the entry points, the branch edges, the return-address register of each call, and which returns
/// are accounted for. It also marks the instructions skipped by a forward branch that no edge enters, which the
/// lowering omits. Ported from the Eden emulator (GPL-3.0-or-later).
/// </remarks>
internal sealed class ControlFlowScan
{
    private const uint ModeMask = 0xc00001ffU;
    private readonly AmpereInstruction[] _code;
    private readonly bool _usesUniformBank;
    private readonly uint _highUniformBase;

    public ControlFlowScan(AmpereInstruction[] code, bool geometryStage, bool usesUniformBank, uint highUniformBase)
    {
        _code = code;
        _usesUniformBank = usesUniformBank;
        _highUniformBase = highUniformBase;
        ControlEntries = new bool[code.Length];
        DeadInstructions = new bool[code.Length];
        CheckedReturns = new bool[code.Length];
        BoundedLinks = Enumerable.Repeat(uint.MaxValue, code.Length).ToArray();
        DirectTargets = Enumerable.Repeat(uint.MaxValue, code.Length).ToArray();
        BoundedControl = !geometryStage;
        ControlEntries[0] = true;
        FindEdges();
        FindReturnLinks();
        CheckCalls();
        CheckReturns();
        MarkSkippedRanges();
    }

    public bool[] ControlEntries { get; }

    public bool[] DeadInstructions { get; }

    public bool[] CheckedReturns { get; }

    public uint[] BoundedLinks { get; }

    public uint[] DirectTargets { get; }

    public List<(uint Source, uint Destination)> ControlEdges { get; } = [];

    public bool BoundedControl { get; private set; }

    private static long Relative(AmpereInstruction i, bool dropLowBits)
    {
        ulong raw = (i.W1 & (dropLowBits ? ~3U : ~0U)) | ((ulong)(i.W2 & 0x3ffff) << 32);
        return (long)(raw << 14) >> 14;
    }

    private void FindEdges()
    {
        for (uint index = 0; index < _code.Length; index++)
        {
            AmpereInstruction i = _code[index];
            uint opcode = i.Opcode;
            if (opcode == 0x944 || opcode == 0x945 || opcode == 0x947)
            {
                if (opcode == 0x944 && index == 0) BoundedControl = false;
                uint mode = i.W2 & ~0x3ffffU;
                if (mode != (opcode == 0x944 ? 0x03c00000U : 0x03800000U) && !(opcode == 0x947 && mode == 0x0b800000)) BoundedControl = false;
                long target = (long)index * 16 + 16 + Relative(i, opcode == 0x947);
                if (target < 0 || target % 16 != 0 || (ulong)(target / 16) >= (ulong)_code.Length)
                {
                    BoundedControl = false;
                }
                else
                {
                    DirectTargets[index] = (uint)(target / 16);
                    ControlEntries[DirectTargets[index]] = true;
                    ControlEdges.Add((index, DirectTargets[index]));
                }

                if (opcode == 0x944 && index + 1 < _code.Length)
                {
                    ControlEntries[index + 1] = true;
                    ControlEdges.Add((index, index + 1));
                }
            }
            else if ((opcode & 0x1ff) >= 0x140 && (opcode & 0x1ff) <= 0x15f &&
                     opcode != 0x941 && opcode != 0x942 && opcode != 0x948 && opcode != 0x348 && opcode != 0x94d && opcode != 0x950 && opcode != 0x95b)
            {
                BoundedControl = false;
            }
        }
    }

    private bool OrdinaryOperands(AmpereInstruction i)
    {
        foreach (int bit in new[] { 16, 24, 32, 64 })
        {
            if (bit == 32 && i.Bits(9, 3) != 1) continue;
            uint register = i.Bits(bit, 8);
            if (register >= (_usesUniformBank ? _highUniformBase : 246U) && register != 255) return false;
        }

        return true;
    }

    // Each call must be preceded by an instruction that loads the return address into a register no later instruction overwrote.
    private void FindReturnLinks()
    {
        for (int index = 1; BoundedControl && index < _code.Length; index++)
        {
            if (_code[index].Opcode != 0x944) continue;
            if (_code[index].W0 != 0x7944)
            {
                BoundedControl = false;
                break;
            }

            bool[] overwritten = new bool[256];
            int setup = index;
            for (int prior = index; prior-- > 0;)
            {
                AmpereInstruction i = _code[prior];
                uint destination = i.Bits(16, 8);
                if ((i.W0 & ~0x00ff0000U) == 0x00007802 && i.W1 == (uint)(index + 1) * 16 && i.W2 == 0xf00 &&
                    (i.W3 & ModeMask) == 0 && OrdinaryOperands(i) && destination < 246 && !overwritten[destination])
                {
                    BoundedLinks[index] = destination;
                    setup = prior;
                    break;
                }

                uint? count = OpcodeFacts.WriteCount(i);
                if (count is null || !OrdinaryOperands(i) || destination + count > 255) break;
                for (uint component = 0; component < count; component++) overwritten[destination + component] = true;
            }

            if (BoundedLinks[index] == uint.MaxValue)
            {
                BoundedControl = false;
                break;
            }

            for (int entry = setup + 1; entry <= index; entry++)
                if (ControlEntries[entry]) BoundedControl = false;
        }
    }

    private void CheckCalls()
    {
        for (int index = 0; BoundedControl && index < _code.Length; index++)
        {
            if (_code[index].Opcode != 0x944) continue;
            uint link = BoundedLinks[index], target = DirectTargets[index];
            if (target == 0)
            {
                BoundedControl = false;
                break;
            }

            AmpereInstruction previous = _code[target - 1];
            uint previousOpcode = previous.Opcode;
            if (previous.Bits(12, 4) != 7 || previous.Bits(87, 4) != 7 || (previousOpcode != 0x947 && previousOpcode != 0x94d && previousOpcode != 0x950))
            {
                BoundedControl = false;
                break;
            }

            foreach (var (source, destination) in ControlEdges)
                if (destination == target && _code[source].Opcode != 0x944) BoundedControl = false;
            if (!ReturnsThroughLink(target, link)) BoundedControl = false;
        }
    }

    // Follows the straight-line code from a call target to its RET, tracking which registers still hold the return address.
    private bool ReturnsThroughLink(uint target, uint link)
    {
        bool[] aliases = new bool[256];
        aliases[link] = true;
        for (uint pc = target; pc < _code.Length; pc++)
        {
            AmpereInstruction i = _code[pc];
            if (pc != target && ControlEntries[pc]) break;
            if (i.Opcode == 0x950)
            {
                bool returned = (i.W0 & 0x00ffffffU) == 0x00007950 && (i.W2 & ~0x3ffffU) == 0x03c00000 && (i.W3 & ModeMask) == 0 &&
                                (long)pc * 16 + 16 + Relative(i, false) == 0 && aliases[i.Bits(24, 8)];
                if (returned) CheckedReturns[pc] = true;
                return returned;
            }

            uint? count = OpcodeFacts.WriteCount(i);
            uint destination = i.Bits(16, 8);
            if (count is null || !OrdinaryOperands(i) || destination + count > 255 || (count != 0 && destination <= link && link < destination + count)) break;
            bool copy = CopiesAlias(i, aliases);
            for (uint component = 0; component < count; component++) aliases[destination + component] = component == 0 && copy;
        }

        return false;
    }

    private static bool CopiesAlias(AmpereInstruction i, bool[] aliases)
    {
        bool copy = false;
        if (i.Bits(12, 4) != 7) return copy;
        if ((i.W0 & 0xff00ffffU) == 0x00007202U && i.W1 < 256 && i.W2 == 0xf00) copy = aliases[i.W1];
        if ((i.W0 & 0xff00ffffU) == 0xff007224U && i.W1 == 0xff && (i.W2 & ~0xffU) == 0x078e0000) copy = aliases[i.Bits(64, 8)];
        return copy;
    }

    private void CheckReturns()
    {
        for (int index = 0; BoundedControl && index < _code.Length; index++)
            if (_code[index].Opcode == 0x950 && !CheckedReturns[index]) BoundedControl = false;
    }

    // A forward unconditional branch over a call that nothing else enters skips code the lowering can drop.
    private void MarkSkippedRanges()
    {
        for (uint index = 0; BoundedControl && index < _code.Length; index++)
        {
            AmpereInstruction i = _code[index];
            uint target = DirectTargets[index];
            if (i.Opcode != 0x947 || i.Bits(12, 4) != 7 || i.Bits(32, 2) == 2 || i.Bits(16, 8) != 0 || i.Bits(87, 4) != 7 || target <= index ||
                target == uint.MaxValue || (i.W3 & ModeMask) != 0 || (i.Bits(91, 1) != 0 ? i.Bits(24, 8) >= 64 : i.Bits(24, 8) != 0))
                continue;
            bool skippedCall = false;
            for (uint pc = index + 1; pc < target; pc++) skippedCall |= _code[pc].Opcode == 0x944;
            if (!skippedCall) continue;
            bool entered = false;
            foreach (var (source, destination) in ControlEdges)
                entered |= index < destination && destination < target && !(index < source && source < target);
            if (!entered)
                for (uint pc = index + 1; pc < target; pc++) DeadInstructions[pc] = true;
        }
    }
}
