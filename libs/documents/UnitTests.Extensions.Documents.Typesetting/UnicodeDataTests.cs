using System.IO;

namespace UnitTests.Extensions.Documents.Typesetting;

/// <summary>The committed Unicode property table must be exactly what the committed Unicode data files say.</summary>
public class UnicodeDataTests
{
    [Fact]
    public void The_committed_property_table_matches_the_unicode_data_files()
    {
        var expected = UnicodeDataGenerator.Generate();
        if (Environment.GetEnvironmentVariable("UPDATE_GENERATED") == "1")
        {
            File.WriteAllText(UnicodeDataGenerator.GeneratedFile(), expected);
        }

        File.ReadAllText(UnicodeDataGenerator.GeneratedFile()).ReplaceLineEndings("\n").Should().Be(
            expected, "the table is generated from assets/unicode; run the tests with UPDATE_GENERATED=1 after changing them");
    }

    [Fact]
    public void The_committed_normalization_table_matches_the_unicode_data_files()
    {
        var expected = UnicodeDataGenerator.GenerateNormalization();
        if (Environment.GetEnvironmentVariable("UPDATE_GENERATED") == "1")
        {
            File.WriteAllText(UnicodeDataGenerator.GeneratedNormalizationFile(), expected);
        }

        File.ReadAllText(UnicodeDataGenerator.GeneratedNormalizationFile()).ReplaceLineEndings("\n").Should().Be(
            expected, "the table is generated from assets/unicode; run the tests with UPDATE_GENERATED=1 after changing them");
    }
}
