using KeyboardWanderer.Runtime;
using NUnit.Framework;
using UnityEngine;

/// <summary>Global SetUpFixture: applies to every test namespace in the PlayMode assembly.</summary>
[SetUpFixture]
public sealed class PlayModeTestStorageIsolationLifecycle
{
    [OneTimeSetUp]
    public void Setup()
    {
        if (!KeyboardWandererTestStorage.IsActive)
            KeyboardWandererTestStorage.Begin("play-mode");
    }

    [OneTimeTearDown]
    public void Cleanup()
    {
        // Settings may still own a debounced flush when the last Scene test ends.
        // Flush it while the test namespace is active; otherwise OnDisable during
        // PlayMode exit could write those test values to the real user preference.
        KeyboardWandererSettingsController[] settings =
            Object.FindObjectsByType<KeyboardWandererSettingsController>(
                FindObjectsInactive.Include);
        for (int i = 0; i < settings.Length; i++)
            if (settings[i] != null && settings[i].gameObject.scene.IsValid())
                settings[i].gameObject.SetActive(false);
        KeyboardWandererTestStorage.End();
    }
}
