using System.Collections;
using System.Collections.Generic;
using KeyboardWanderer.Demo;
using KeyboardWanderer.Networking;
using NUnit.Framework;
using UnityEngine;

namespace KeyboardWanderer.Tests.EditMode
{
    public sealed class SceneSequencePlayerTests
    {
        private GameObject _gameObject;
        private SceneSequencePlayer _player;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("SceneSequencePlayer test");
            _player = _gameObject.AddComponent<SceneSequencePlayer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void PlaysAuthoritativeActionsInServerOrder()
        {
            var observed = new List<string>();
            var sequence = new[]
            {
                new GameApiClient.SceneActionSnapshot { sequence = 1, type = "MOVE" },
                new GameApiClient.SceneActionSnapshot { sequence = 2, type = "ATTACK" },
                new GameApiClient.SceneActionSnapshot { sequence = 3, type = "DIALOGUE" }
            };

            IEnumerator playback = _player.Play(sequence, action => Record(action, observed));
            Drain(playback);

            CollectionAssert.AreEqual(new[] { "MOVE", "ATTACK", "DIALOGUE" }, observed);
            Assert.That(_player.IsPlaying, Is.False);
        }

        [Test]
        public void CancelReleasesTheInputLockState()
        {
            IEnumerator playback = _player.Play(
                new[] { new GameApiClient.SceneActionSnapshot { sequence = 1, type = "MOVE" } },
                action => Record(action, new List<string>()));
            Assert.That(playback.MoveNext(), Is.True);
            Assert.That(_player.IsPlaying, Is.True);

            _player.Cancel();

            Assert.That(_player.IsPlaying, Is.False);
        }

        private static IEnumerator Record(GameApiClient.SceneActionSnapshot action, ICollection<string> observed)
        {
            observed.Add(action.type);
            yield break;
        }

        private static void Drain(IEnumerator iterator)
        {
            while (iterator.MoveNext())
            {
                if (iterator.Current is IEnumerator nested)
                    Drain(nested);
            }
        }
    }
}
