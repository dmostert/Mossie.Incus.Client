using System.Runtime.InteropServices;

namespace Mossie.Incus.Client.Api;

/// <summary>
/// Architecture identifiers and name mapping mirroring the Go <c>shared/osarch</c> package.
/// </summary>
public static class IncusArchitecture
{
    /// <summary>Unknown architecture.</summary>
    public const int Unknown = 0;

    /// <summary>32-bit Intel x86.</summary>
    public const int IntelX86_32 = 1;

    /// <summary>64-bit Intel x86.</summary>
    public const int IntelX86_64 = 2;

    /// <summary>32-bit ARMv7 little-endian.</summary>
    public const int ArmV7_32 = 3;

    /// <summary>64-bit ARMv8 little-endian.</summary>
    public const int ArmV8_64 = 4;

    /// <summary>32-bit PowerPC big-endian.</summary>
    public const int PowerPc_32 = 5;

    /// <summary>64-bit PowerPC big-endian.</summary>
    public const int PowerPc_64 = 6;

    /// <summary>64-bit PowerPC little-endian.</summary>
    public const int PowerPc_64Le = 7;

    /// <summary>64-bit s390 big-endian.</summary>
    public const int S390X = 8;

    /// <summary>32-bit MIPS.</summary>
    public const int Mips_32 = 9;

    /// <summary>64-bit MIPS.</summary>
    public const int Mips_64 = 10;

    /// <summary>32-bit RISC-V little-endian.</summary>
    public const int RiscV_32 = 11;

    /// <summary>64-bit RISC-V little-endian.</summary>
    public const int RiscV_64 = 12;

    /// <summary>32-bit ARMv6 little-endian.</summary>
    public const int ArmV6_32 = 13;

    /// <summary>32-bit ARMv8 little-endian.</summary>
    public const int ArmV8_32 = 14;

    /// <summary>64-bit LoongArch.</summary>
    public const int LoongArch_64 = 15;

    /// <summary>Default architecture name used when detection fails.</summary>
    public const string Default = "x86_64";

    private static readonly Dictionary<int, string> Names = new()
    {
        [IntelX86_32] = "i686",
        [IntelX86_64] = "x86_64",
        [ArmV6_32] = "armv6l",
        [ArmV7_32] = "armv7l",
        [ArmV8_32] = "armv8l",
        [ArmV8_64] = "aarch64",
        [PowerPc_32] = "ppc",
        [PowerPc_64] = "ppc64",
        [PowerPc_64Le] = "ppc64le",
        [S390X] = "s390x",
        [Mips_32] = "mips",
        [Mips_64] = "mips64",
        [RiscV_32] = "riscv32",
        [RiscV_64] = "riscv64",
        [LoongArch_64] = "loongarch64",
    };

    private static readonly Dictionary<int, string[]> Aliases = new()
    {
        [IntelX86_32] = ["i386", "i586", "386", "x86", "generic_32"],
        [IntelX86_64] = ["amd64", "generic_64", "x86_64_v1", "x86_64_v2", "x86_64_v3", "x86_64_v4"],
        [ArmV6_32] = ["armel", "arm"],
        [ArmV7_32] = ["armhf", "armhfp", "armv7a_hardfp", "armv7", "armv7a_vfpv3_hardfp"],
        [ArmV8_64] = ["arm64", "arm64_generic"],
        [PowerPc_32] = ["powerpc"],
        [PowerPc_64] = ["powerpc64", "ppc64"],
        [PowerPc_64Le] = ["ppc64el"],
        [Mips_32] = ["mipsel", "mipsle"],
        [Mips_64] = ["mips64el", "mips64le"],
        [LoongArch_64] = ["loong64"],
    };

    private static readonly Dictionary<int, int[]> Personalities = new()
    {
        [IntelX86_32] = [],
        [IntelX86_64] = [IntelX86_32],
        [ArmV6_32] = [],
        [ArmV7_32] = [ArmV6_32],
        [ArmV8_32] = [ArmV6_32, ArmV7_32],
        [ArmV8_64] = [ArmV6_32, ArmV7_32, ArmV8_32],
        [PowerPc_32] = [],
        [PowerPc_64] = [PowerPc_32],
        [PowerPc_64Le] = [],
        [S390X] = [],
        [Mips_32] = [],
        [Mips_64] = [Mips_32],
        [RiscV_32] = [],
        [RiscV_64] = [],
        [LoongArch_64] = [],
    };

    /// <summary>Converts an architecture ID to its canonical name.</summary>
    public static string Name(int architecture)
    {
        if (Names.TryGetValue(architecture, out string? name))
            return name;

        throw new ArgumentOutOfRangeException(nameof(architecture), architecture, "Architecture isn't supported");
    }

    /// <summary>Converts an architecture name or alias to its ID.</summary>
    public static int Id(string architecture)
    {
        foreach ((int id, string name) in Names)
        {
            if (name == architecture)
                return id;
        }

        foreach ((int id, string[] aliases) in Aliases)
        {
            if (aliases.Contains(architecture))
                return id;
        }

        throw new ArgumentException($"Architecture isn't supported: {architecture}", nameof(architecture));
    }

    /// <summary>Returns the personality architecture IDs supported by an architecture.</summary>
    public static IReadOnlyList<int> SupportedPersonalities(int architecture)
        => Personalities.TryGetValue(architecture, out int[]? values) ? values : [];

    /// <summary>Returns the name of the local architecture.</summary>
    public static string LocalName()
    {
        (int id, string fallback) = RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X86 => (IntelX86_32, "i686"),
            System.Runtime.InteropServices.Architecture.X64 => (IntelX86_64, "x86_64"),
            System.Runtime.InteropServices.Architecture.Arm => (ArmV7_32, "armv7l"),
            System.Runtime.InteropServices.Architecture.Arm64 => (ArmV8_64, "aarch64"),
            System.Runtime.InteropServices.Architecture.Ppc64le => (PowerPc_64Le, "ppc64le"),
            System.Runtime.InteropServices.Architecture.S390x => (S390X, "s390x"),
#if NET9_0_OR_GREATER
            System.Runtime.InteropServices.Architecture.RiscV64 => (RiscV_64, "riscv64"),
            System.Runtime.InteropServices.Architecture.LoongArch64 => (LoongArch_64, "loongarch64"),
#endif
            _ => (Unknown, Default),
        };

        return id == Unknown ? fallback : Name(id);
    }

    /// <summary>Returns the local architecture ID.</summary>
    public static int LocalId()
    {
        try
        {
            return Id(LocalName());
        }
        catch (ArgumentException)
        {
            return Unknown;
        }
    }
}
