// Talk.ReadTags: the [TITLE:x] tag sets how the village addresses you; stray tags never reach the player.
using Hearsay;

public static class TagTests
{
    public static void TestTitleTagSetsTitleAndIsRemoved()
    {
        var v = new VillageState();
        string reply = Talk.ReadTags("Very well. [TITLE: Chief] I'll tell the others.", null, v);
        Assert.Equal("Chief", v.Title, "title");
        Assert.Equal("a villager", v.TitleSetBy, "first title records who asked");
        Assert.Equal("Very well.  I'll tell the others.".Replace("  ", " "), reply.Replace("  ", " "), "reply without the tag");
    }

    public static void TestTitleTagIsCleanedAndChangeable()
    {
        var v = new VillageState { Title = "Chief", TitleSetBy = "Osmund" };
        Talk.ReadTags("[title: \"Captain.\"]", null, v);
        Assert.Equal("Captain", v.Title, "quotes and dots trimmed, case-insensitive tag");
        Assert.Equal("Osmund", v.TitleSetBy, "changing the title keeps who first asked");
    }

    public static void TestStrayTagsDropped()
    {
        var v = new VillageState();
        Assert.Equal("Fine weather.", Talk.ReadTags("Fine weather. [MOOD:happy]", null, v), "unknown tag removed");
        Assert.Equal(null, v.Title, "no title from other tags");
    }
}
