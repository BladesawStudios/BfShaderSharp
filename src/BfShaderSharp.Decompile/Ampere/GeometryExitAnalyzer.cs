namespace BfShaderSharp.Decompile.Ampere;

/// <summary>Checks that a native geometry program has the exit-handler shape the lowering can replace.</summary>
/// <remarks>
/// The program must start with the five-instruction setup that installs the handler, end with the three-instruction
/// handler, and have a body whose exits and branches stay inside it. The setup state is then followed until it is dead,
/// because the lowering drops the setup instructions; any body instruction that reads it, or any instruction the
/// analysis cannot account for before the first exit, rejects the program. Ported from the Eden emulator
/// (GPL-3.0-or-later).
/// </remarks>
public sealed class GeometryExitAnalyzer(AmpereInstruction[] code)
{
    public const int SetupInstructions = 5;
    private const uint ModeMask = 0xc00001ffU;
    private readonly GeometryExitEnvelope _result = new();
    private int _handler;

    public GeometryExitEnvelope Analyze()
    {
        int count = code.Length;
        if (count < SetupInstructions + 4) return Fail(count, "native exit envelope truncated");
        uint target = code[1].W1;
        if (!Is(0, 0x734e, 0, 0) || !Is(1, 0x37802, target, 0xf00) || !Is(2, 0x7210, 3, 0x07f1e0ff) ||
            !(Is(3, 0x01017210, 0xff, 0x007fe4ff) || Is(3, 0xff017224, 0xff, 0x000e0401)) || !Is(4, 0x7357, 0, 0))
            return Fail(0, "native exit handler installation");
        _handler = (int)(target / 16);
        if (target % 16 != 0 || _handler <= SetupInstructions + 1 || _handler + 3 != count) return Fail(1, "native exit handler location");
        if (!Is(_handler, 0x00ff7324, 0xff, 0) || !Is(_handler + 1, 0x7957, 0, 0) || !Is(_handler + 2, 0x794d, 0, 0x03800000))
            return Fail(_handler, "native exit handler body");
        if (!(IsExit(_handler - 1) && code[_handler - 1].Bits(12, 4) == 7) && !IsReturn(_handler - 1))
            return Fail(_handler - 1, "native exit handler reached by fallthrough");
        if (!CheckBody() || !CheckSetupIsDead()) return _result;
        _result.HandlerOffset = target;
        return _result;
    }

    private GeometryExitEnvelope Fail(int index, string reason)
    {
        _result.Fail(index, reason);
        return _result;
    }

    private bool Reject(int index, string reason)
    {
        _result.Fail(index, reason);
        return false;
    }

    private bool Is(int index, uint word0, uint word1, uint word2) =>
        code[index].W0 == word0 && code[index].W1 == word1 && code[index].W2 == word2 && (code[index].W3 & ModeMask) == 0;

    private bool IsExit(int index) =>
        (code[index].W0 & ~0xf000U) == 0x94d && code[index].W1 == 0 && code[index].W2 == 0x03800000 &&
        (code[index].W3 & ModeMask) == 0 && code[index].Bits(12, 4) != 15;

    private bool IsReturn(int index)
    {
        AmpereInstruction i = code[index];
        return (i.W0 & 0x00ffffffU) == 0x00007950 && i.Bits(24, 8) < 249 && (i.W2 & ~0x3ffffU) == 0x03c00000 &&
               (i.W3 & ModeMask) == 0 && (long)(index + 1) * 16 + RelativeTarget(i) == 0;
    }

    private static long RelativeTarget(AmpereInstruction i) => (long)((i.W1 | ((ulong)(i.W2 & 0x3ffff) << 32)) << 14) >> 14;

    private bool CheckBody()
    {
        for (int index = SetupInstructions; index < _handler; index++)
        {
            AmpereInstruction i = code[index];
            uint opcode = i.Opcode, family = opcode & 0x1ff;
            if (family == 0x14e || (family >= 0x154 && family <= 0x157)) return Reject(index, "native exit handler state access in body");
            if (opcode == 0x94d)
            {
                if (!IsExit(index)) return Reject(index, "native body EXIT mode");
                _result.Exits.Add(((uint)(index * 16), i.Bits(12, 4)));
                continue;
            }

            if (IsReturn(index))
            {
                _result.HasReturns = true;
                continue;
            }

            if (family < 0x140 || family >= 0x160 || opcode == 0x941 || opcode == 0x942 || opcode == 0x948) continue;
            bool nonIncrementingCall = opcode == 0x944 && (i.W2 & 0x400000) != 0;
            if (opcode != 0x945 && opcode != 0x947 && !nonIncrementingCall) return Reject(index, "native control flow in exit envelope body");
            long relative = opcode == 0x945 ? (int)i.W1 : RelativeTarget(i);
            long destination = (long)index * 16 + 16 + relative;
            if (destination % 16 != 0 || destination < SetupInstructions * 16L || destination >= _handler * 16L)
                return Reject(index, "native branch leaves exit envelope body");
        }

        return true;
    }

    private bool CheckSetupIsDead()
    {
        uint liveRegisters = 0b1011, livePredicates = 0b1;
        for (int index = SetupInstructions; liveRegisters != 0 || livePredicates != 0; index++)
        {
            if (index >= code.Length) return Reject(index, "native instruction before exit setup state is dead");
            AccessSet access = new();
            uint guard = code[index].Bits(12, 4);
            access.ReadPredicate(guard & 7);
            if (IsExit(index))
            {
                if (guard != 7) return Reject(index, "native instruction before exit setup state is dead");
                // The handler finalizes the output through R0; the rest of the setup state may stay live when the body ends without reading it.
                if ((liveRegisters & 1) != 0 || (access.ReadPredicates & livePredicates) != 0) return Reject(index, "native exit handler reads exit setup state");
                return true;
            }

            if (!access.Classify(code[index])) return Reject(index, "native instruction before exit setup state is dead");
            if ((access.Reads & liveRegisters) != 0 || (access.ReadPredicates & livePredicates) != 0) return Reject(index, "native body reads exit setup state");
            if (guard == 7)
            {
                liveRegisters &= ~access.Writes;
                livePredicates &= ~access.WritePredicates;
            }
        }

        return true;
    }
}
