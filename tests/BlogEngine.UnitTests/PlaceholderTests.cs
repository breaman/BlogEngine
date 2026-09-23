using System.Reflection;

using BlogEngine.Shared.Common;

namespace BlogEngine.UnitTests;

/// <summary>
/// Smoke tests proving the unit test project is wired up: TUnit discovers and runs tests, and
/// Verify can write and compare snapshots. Replace with real tests as shared utilities land.
/// </summary>
public class PlaceholderTests
{
    /// <summary>Every column length is a usable, positive size.</summary>
    [Test]
    public async Task FieldLengths_AreAllPositive()
    {
        var lengths = typeof(FieldLengths)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToList();

        await Assert.That(lengths).IsNotEmpty();
        await Assert.That(lengths).All(length => length > 0);
    }

    /// <summary>Proves Verify.TUnit is configured and the verified snapshot is committed.</summary>
    [Test]
    public Task Verify_MatchesCommittedSnapshot()
    {
        return Verify(new { FieldLengths.MetaTitle, FieldLengths.MetaDescription });
    }
}
