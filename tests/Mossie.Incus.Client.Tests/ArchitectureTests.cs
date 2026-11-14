using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client.Tests;

/// <summary>Tests for the architecture name/ID mapping mirroring Go's <c>shared/osarch</c>.</summary>
public sealed class ArchitectureTests
{
    [Theory]
    [InlineData(IncusArchitecture.IntelX86_32, "i686")]
    [InlineData(IncusArchitecture.IntelX86_64, "x86_64")]
    [InlineData(IncusArchitecture.ArmV6_32, "armv6l")]
    [InlineData(IncusArchitecture.ArmV7_32, "armv7l")]
    [InlineData(IncusArchitecture.ArmV8_32, "armv8l")]
    [InlineData(IncusArchitecture.ArmV8_64, "aarch64")]
    [InlineData(IncusArchitecture.PowerPc_32, "ppc")]
    [InlineData(IncusArchitecture.PowerPc_64, "ppc64")]
    [InlineData(IncusArchitecture.PowerPc_64Le, "ppc64le")]
    [InlineData(IncusArchitecture.S390X, "s390x")]
    [InlineData(IncusArchitecture.Mips_32, "mips")]
    [InlineData(IncusArchitecture.Mips_64, "mips64")]
    [InlineData(IncusArchitecture.RiscV_32, "riscv32")]
    [InlineData(IncusArchitecture.RiscV_64, "riscv64")]
    [InlineData(IncusArchitecture.LoongArch_64, "loongarch64")]
    public void Name_ReturnsCanonicalName(int id, string expected)
    {
        Assert.Equal(expected, IncusArchitecture.Name(id));
    }

    [Theory]
    [InlineData("x86_64", IncusArchitecture.IntelX86_64)]
    [InlineData("amd64", IncusArchitecture.IntelX86_64)]
    [InlineData("generic_64", IncusArchitecture.IntelX86_64)]
    [InlineData("aarch64", IncusArchitecture.ArmV8_64)]
    [InlineData("arm64", IncusArchitecture.ArmV8_64)]
    [InlineData("armhf", IncusArchitecture.ArmV7_32)]
    [InlineData("ppc64el", IncusArchitecture.PowerPc_64Le)]
    [InlineData("loong64", IncusArchitecture.LoongArch_64)]
    public void Id_AcceptsCanonicalNamesAndAliases(string name, int expected)
    {
        Assert.Equal(expected, IncusArchitecture.Id(name));
    }

    [Fact]
    public void UnknownArchitecture_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IncusArchitecture.Name(999));
        Assert.Throws<ArgumentException>(() => IncusArchitecture.Id("not-an-arch"));
    }

    [Fact]
    public void SupportedPersonalities_AreReported()
    {
        Assert.Contains(IncusArchitecture.ArmV7_32, IncusArchitecture.SupportedPersonalities(IncusArchitecture.ArmV8_64));
        Assert.Contains(IncusArchitecture.ArmV6_32, IncusArchitecture.SupportedPersonalities(IncusArchitecture.ArmV8_64));
        Assert.Empty(IncusArchitecture.SupportedPersonalities(IncusArchitecture.RiscV_64));
    }

    [Fact]
    public void LocalName_RoundTripsThroughId()
    {
        string local = IncusArchitecture.LocalName();

        Assert.False(string.IsNullOrWhiteSpace(local));
        Assert.Equal(IncusArchitecture.LocalId(), IncusArchitecture.Id(local));
    }
}
