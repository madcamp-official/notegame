using KeyboardWanderer.Editor.Validation;
using NUnit.Framework;
using UnityEngine;

namespace KeyboardWanderer.Tests.EditMode
{
    public sealed class AssetIntegrityTests
    {
        [Test]
        public void Assets_HaveExactlyOneMetaAndUniqueGuids()
        {
            var problems = UnityAssetIntegrityValidator.CollectProblems(Application.dataPath);
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
