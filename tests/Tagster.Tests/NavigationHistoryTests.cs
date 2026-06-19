using Tagster.Core;

namespace Tagster.Tests;

public class NavigationHistoryTests
{
    [Fact]
    public void Navigate_pushes_back_and_clears_forward()
    {
        var history = new NavigationHistory();
        history.Navigate("A");
        history.Navigate("B");

        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);
        Assert.Equal("B", history.Current!.Path);
    }

    [Fact]
    public void Back_and_forward_restore_saved_scroll_offsets()
    {
        var history = new NavigationHistory();
        history.Navigate("A");
        history.SaveScrollOffset(10);
        history.Navigate("B");
        history.SaveScrollOffset(20);
        history.Navigate("C");
        history.SaveScrollOffset(30);

        var back1 = history.GoBack(); // -> B
        Assert.Equal("B", back1!.Path);
        Assert.Equal(20, back1.ScrollOffset);

        var back2 = history.GoBack(); // -> A
        Assert.Equal("A", back2!.Path);
        Assert.Equal(10, back2.ScrollOffset);

        var forward = history.GoForward(); // -> B
        Assert.Equal("B", forward!.Path);
        Assert.Equal(20, forward.ScrollOffset);
    }

    [Fact]
    public void Navigating_after_going_back_clears_forward()
    {
        var history = new NavigationHistory();
        history.Navigate("A");
        history.Navigate("B");
        history.GoBack(); // -> A, forward now holds B

        Assert.True(history.CanGoForward);

        history.Navigate("C");

        Assert.False(history.CanGoForward);
        Assert.Equal("C", history.Current!.Path);
    }

    [Fact]
    public void Back_at_the_start_returns_null()
    {
        var history = new NavigationHistory();
        history.Navigate("A");
        Assert.Null(history.GoBack());
    }
}
