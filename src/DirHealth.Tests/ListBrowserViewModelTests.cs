using DirHealth.Desktop.ViewModels;
using Xunit;

namespace DirHealth.Tests;

public class ListBrowserViewModelTests
{
    private sealed class Fake : ListBrowserViewModel
    {
        public bool Empty;
        protected override bool IsEmpty => Empty;
        public void Begin()          => BeginLoad();
        public void End()            => EndLoad();
        public void Fail(string msg) => SetError(msg);
    }

    [Fact]
    public void Initial_ShowsPlaceholderOnly()
    {
        var vm = new Fake();
        Assert.True(vm.ShowPlaceholder);
        Assert.False(vm.ShowEmptyState);
        Assert.False(vm.HasError);
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public void Loading_HidesPlaceholderAndEmpty()
    {
        var vm = new Fake();
        vm.Begin();
        Assert.True(vm.IsLoading);
        Assert.False(vm.ShowPlaceholder);
        Assert.False(vm.ShowEmptyState);
    }

    [Fact]
    public void LoadedWithData_ShowsNeither()
    {
        var vm = new Fake { Empty = false };
        vm.Begin();
        vm.End();
        Assert.False(vm.IsLoading);
        Assert.False(vm.ShowPlaceholder);
        Assert.False(vm.ShowEmptyState);
    }

    [Fact]
    public void LoadedEmpty_ShowsEmptyState()
    {
        var vm = new Fake { Empty = true };
        vm.Begin();
        vm.End();
        Assert.True(vm.ShowEmptyState);
        Assert.False(vm.ShowPlaceholder);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void Error_ShowsErrorAndNothingElse()
    {
        var vm = new Fake { Empty = true };
        vm.Begin();
        vm.Fail("boom");
        Assert.True(vm.HasError);
        Assert.Equal("boom", vm.ErrorMessage);
        Assert.False(vm.IsLoading);
        Assert.False(vm.ShowEmptyState);
        Assert.False(vm.ShowPlaceholder);
    }
}
