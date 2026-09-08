using System;
using System.Runtime.InteropServices;

namespace RimWorldAccess
{
    /// <summary>
    /// Steers Prism's bundled simdutf away from its AVX-512 ("icelake") kernel
    /// on Windows. In the shipped Windows prism.dll from v0.17.0 onward that
    /// kernel rejects well-formed UTF-8, so any utterance containing multi-byte
    /// characters is refused as invalid on AVX-512-capable machines.
    /// </summary>
    public static class SimdutfKernelWorkaround
    {
        public const string VariableName = "SIMDUTF_FORCE_IMPLEMENTATION";
        public const string ForcedValue = "haswell";

        // IsProcessorFeaturePresent codes (winnt.h).
        private const int PF_AVX2_INSTRUCTIONS_AVAILABLE = 40;
        private const int PF_AVX512F_INSTRUCTIONS_AVAILABLE = 41;

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsProcessorFeaturePresent(int processorFeature);

        /// <summary>
        /// Decides whether to pin the kernel, given the platform, the CPU's
        /// reported features, and any value the player already set.
        /// </summary>
        public static bool ShouldForce(bool isWindows, bool hasAvx2, bool hasAvx512, string existingValue)
        {
            if (!isWindows || !string.IsNullOrEmpty(existingValue))
            {
                return false;
            }

            // AVX-512F is a superset signal: it admits a few CPUs that lack the
            // VBMI2 the broken kernel needs, but simdutf picks haswell on those
            // anyway, so pinning is a no-op there rather than a behavior change.
            // Requiring AVX2 as well keeps the pinned kernel executable even if
            // the AVX-512 probe ever misreports - forcing a kernel the CPU
            // cannot run aborts the process with an illegal instruction.
            return hasAvx2 && hasAvx512;
        }

        /// <summary>
        /// Applies the workaround. Must run before prism.dll is loaded: the
        /// library links the CRT statically and snapshots the environment block
        /// at load time, so a later assignment never reaches it.
        /// </summary>
        /// <returns>A description of what was pinned, or null if it was not needed.</returns>
        public static string Apply(bool isWindows)
        {
            bool hasAvx2 = false;
            bool hasAvx512 = false;
            if (isWindows)
            {
                try
                {
                    hasAvx2 = IsProcessorFeaturePresent(PF_AVX2_INSTRUCTIONS_AVAILABLE);
                    hasAvx512 = IsProcessorFeaturePresent(PF_AVX512F_INSTRUCTIONS_AVAILABLE);
                }
                catch (DllNotFoundException)
                {
                    return null;
                }
                catch (EntryPointNotFoundException)
                {
                    return null;
                }
            }

            if (!ShouldForce(isWindows, hasAvx2, hasAvx512, Environment.GetEnvironmentVariable(VariableName)))
            {
                return null;
            }

            Environment.SetEnvironmentVariable(VariableName, ForcedValue);
            return VariableName + "=" + ForcedValue;
        }
    }
}
