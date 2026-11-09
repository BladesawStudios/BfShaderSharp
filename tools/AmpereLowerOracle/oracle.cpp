// Reference oracle: exposes the Eden/Fritchii Ampere-to-Maxwell lowering (header-only) as a C function so a C# port can be checked against it.
// Build: see build.sh. The headers are found through -I<Fritchii>/src and are not copied into this repository.
#include <cstring>
#include <string>
#include <vector>
#include "shader_recompiler/frontend/ampere/lower_to_maxwell.h"

using namespace Shader::Ampere;

extern "C" __declspec(dllexport) int bfs_lower(const uint8_t* code, uint32_t size, int geometry, int tensor, int tessellation,
                                               uint64_t address, uint64_t* out, uint32_t capacity, uint32_t* count,
                                               uint32_t* errorOffset, char* error, uint32_t errorCapacity) {
    std::vector<Instruction> native(size / sizeof(Instruction));
    std::memcpy(native.data(), code, native.size() * sizeof(Instruction));
    std::vector<u32> traps;
    for (u32 i = 0; i < native.size(); ++i)
        if (native[i].Bits(0, 12) == 0x95c) traps.push_back(i * sizeof(Instruction));
    LoweredProgram lowered = LowerToMaxwell(native, {.geometry = geometry != 0, .tensor = tensor != 0, .native_code_address = address,
                                                     .verified_traps = traps,
                                                     .tessellation_stage = static_cast<TessellationStage>(tessellation)});
    *errorOffset = lowered.error_offset;
    if (!lowered) {
        std::snprintf(error, errorCapacity, "%.*s", static_cast<int>(lowered.error.size()), lowered.error.data());
        *count = 0;
        return 1;
    }
    if (lowered.instructions.size() > capacity) return 2;
    std::memcpy(out, lowered.instructions.data(), lowered.instructions.size() * sizeof(u64));
    *count = static_cast<uint32_t>(lowered.instructions.size());
    return 0;
}
