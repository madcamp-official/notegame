using KeyboardWanderer.Runtime;
using NUnit.Framework;

/// <summary>Global SetUpFixture: applies to every test namespace in the EditMode assembly.</summary>
[SetUpFixture]
public sealed class EditModeTestStorageIsolationLifecycle
{
    [OneTimeSetUp]
    public void Setup()
    {
        if (!KeyboardWandererTestStorage.IsActive)
            KeyboardWandererTestStorage.Begin("edit-mode");
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        KeyboardWandererTestStorage.End();
    }
}
