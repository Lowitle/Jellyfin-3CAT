using Jellyfin.Plugin.ThreeCat.Api;
using Xunit;

namespace Jellyfin.Plugin.ThreeCat.Tests;

/// <summary>
/// Tests of what the preview of the mapping says about the counter of 3Cat.
/// </summary>
/// <remarks>
/// The message is the only place the user learns why a series is not numbered with TMDB, and «Detectiu Conan»
/// spent a release failing in silence: the pass rejected the counter and the preview only said that the dates
/// were the ones of the dub.
/// </remarks>
public sealed class ThreeCatPlayControllerTests
{
    [Fact]
    public void CounterHint_SaysHowManyEpisodesTheProviderNeedsToReachTheCounter()
    {
        // «Detectiu Conan» announces 1.206 chapters and a provider may list only 300 of them.
        var hint = ThreeCatPlayController.CounterHint(1206, 300);

        Assert.Contains("anuncia 1206 capítulos", hint, StringComparison.Ordinal);
        Assert.Contains("el proveedor lista 300", hint, StringComparison.Ordinal);
        Assert.Contains("al menos 603", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void CounterHint_BlamesTheCounterAndNotTheProviderWhenTheProviderCoversIt()
    {
        // 1.150 episodes of a counter of 1.206: the provider covers it, so the counter is not the one of the
        // program for another reason.
        var hint = ThreeCatPlayController.CounterHint(1206, 1150);

        Assert.Contains("no se ha usado", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("el proveedor lista", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void CounterHint_SaysNothingWhenTheCatalogPublishesNoCounter()
    {
        Assert.Equal(string.Empty, ThreeCatPlayController.CounterHint(0, 1150));
    }
}
