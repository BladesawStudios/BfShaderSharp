using BfShaderSharp.Errors;
using BfShaderSharp.Lookup;

namespace BfShaderSharp.Tests.Lookup;

public class ProgramLookupTests
{
    private static ReadOnlyMemory<byte> Rows(params uint[] words)
    {
        byte[] bytes = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++) BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), words[i]);
        return bytes;
    }

    private static readonly LookupOption Fog = new("fog", 0, 0b1, 0, 0, ["off", "on"]);
    private static readonly LookupOption Skin = new("skin", 1, 0b11, 0, 1, ["none", "one", "two"]);

    // Programs by (fog, skin): 0=(off,none) 1=(on,one) 2=(on,two); rows are two words each.
    private static ProgramLookup Make() => new([Fog, Skin], Rows(0, 0, 1, 1, 1, 2), 2, 3);

    private static Dictionary<string, string> Choose(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void FindsTheProgramWithExactlyTheseChoices()
    {
        ProgramLookup lookup = Make();
        Assert.Equal(0, lookup.Find(Choose(("fog", "off"), ("skin", "none"))));
        Assert.Equal(2, lookup.Find(Choose(("fog", "on"), ("skin", "two"))));
    }

    [Fact]
    public void UnnamedOptionsTakeTheirDefaultAndPartialChoicesMatchTheFirstProgram()
    {
        ProgramLookup lookup = Make();
        Assert.Equal(1, lookup.Find(Choose(("fog", "on"))));
        Assert.Equal(2, lookup.Find(Choose(("skin", "two"))));
    }

    [Fact]
    public void UnknownOptionsAreIgnoredAndUnknownChoicesFindNothing()
    {
        ProgramLookup lookup = Make();
        Assert.Equal(0, lookup.Find(Choose(("weather", "rain"))));
        Assert.Equal(-1, lookup.Find(Choose(("fog", "thick"))));
    }

    [Fact]
    public void ListsTheChoicesOfAProgramByName()
    {
        Dictionary<string, string> choices = Make().ChoicesOf(2);
        Assert.Equal("on", choices["fog"]);
        Assert.Equal("two", choices["skin"]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Make().ChoicesOf(3));
    }

    [Fact]
    public void ARowThatNamesAMissingChoiceIsAFormatError()
    {
        ProgramLookup lookup = new([new LookupOption("skin", 0, 0b11, 0, 0, ["none", "one", "two"])], Rows(3), 1, 1);
        Assert.Throws<BfShaderFormatException>(() => lookup.ChoicesOf(0));
    }

    [Fact]
    public void RejectsKeyTablesThatDoNotMatchTheProgramsOrOptions()
    {
        Assert.Throws<BfShaderFormatException>(() => new ProgramLookup([Fog], Rows(0, 0, 0), 2, 2));
        Assert.Throws<BfShaderFormatException>(() => new ProgramLookup([Skin], Rows(0), 1, 1));
    }
}
