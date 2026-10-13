using Xunit;

namespace DirHealth.Tests;

/// <summary>
/// Tests that replace the static hooks TableViewModel.PickSavePath / SetClipboardText.
/// xunit runs test classes in parallel; sharing a collection serialises them so one class
/// cannot swap the save path under another.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ExportHookCollection
{
    public const string Name = "Static export hooks";
}
