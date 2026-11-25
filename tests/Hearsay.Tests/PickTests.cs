// Book.Pick and Scene conditions: the most specific line wins, recent lines are avoided, missing placeholders drop a line.
using System.Collections.Generic;
using System.Linq;
using Hearsay;

public static class PickTests
{
    static Line L(string say, params string[] when) => new Line { Say = { say }, When = when.ToList() };

    public static void TestMostSpecificLineWins()
    {
        var lines = new List<Line> { L("general"), L("ouch", "wounded") };
        Assert.Equal("ouch", Book.Pick(lines, new Scene { Wounded = true }, null).text, "wounded villager");
        Assert.Equal("general", Book.Pick(lines, new Scene { Wounded = false }, null).text, "healthy villager");
    }

    public static void TestPriorityAddsToScore()
    {
        var lines = new List<Line> { L("specific", "wounded"), new Line { Say = { "boosted" }, P = 2 } };
        Assert.Equal("boosted", Book.Pick(lines, new Scene { Wounded = true }, null).text, "P=2 beats one condition");
    }

    public static void TestRecentLinesAreAvoided()
    {
        var lines = new List<Line> { new Line { Say = { "one", "two" } } };
        var first = Book.Pick(lines, new Scene(), null);
        for (int i = 0; i < 20; i++)
        {
            var next = Book.Pick(lines, new Scene(), new HashSet<string> { first.key });
            Assert.True(next.text != first.text, "picked the line said just before");
        }
        var again = Book.Pick(lines, new Scene(), new HashSet<string> { Book.Pick(new List<Line> { L("one") }, new Scene(), null).key, Book.Pick(new List<Line> { L("two") }, new Scene(), null).key });
        Assert.True(again.text != null, "when everything was said recently, one is still picked");
    }

    public static void TestMissingPlaceholderDropsLine()
    {
        var lines = new List<Line> { L("Hello, {title}.", "alone"), L("Hello.") };
        Assert.Equal("Hello.", Book.Pick(lines, new Scene { Pop = 1 }, null).text, "no title yet");
        Assert.Equal("Hello, Chief.", Book.Pick(lines, new Scene { Pop = 1, Title = "Chief" }, null).text, "title set");
        Assert.Equal("Hello, Chief.", new Scene { Title = "chief" }.Fill("Hello, {Title}."), "capitalised placeholder");
    }

    public static void TestComparisonsNegationAndGroups()
    {
        var s = new Scene { Mood = 20, Hour = 23, Book = new Book() };
        s.Book.Groups["sad"] = "mood<30";
        Assert.True(s.Test("mood<30") && !s.Test("mood>=30") && s.Test("mood=20"), "mood comparisons");
        Assert.True(s.Test("night") && !s.Test("!night"), "night and its negation");
        Assert.True(s.Test("@sad") && !s.Test("@nosuchgroup"), "named groups");
        Assert.True(s.All(new[] { "mood<30", "night" }) && !s.All(new[] { "mood<30", "!night" }), "All needs every condition");
    }

    public static void TestUnknownConditionWarnsAndFails()
    {
        Env.Warnings.Clear();
        Assert.True(!new Scene().Test("nosuchflag"), "unknown condition must not hold");
        Assert.True(Env.Warnings.Any(w => w.Contains("nosuchflag")), "unknown condition must log a warning");
    }

    public static void TestComposeJoinsParts()
    {
        var parts = new List<List<Line>> { new List<Line> { L("Morning.") }, new List<Line> { L("{title} is here.") }, new List<Line> { L("Busy day.") } };
        Assert.Equal("Morning. Busy day.", Book.Compose(parts, new Scene(), null).text, "a part with a missing placeholder is skipped");
    }
}
