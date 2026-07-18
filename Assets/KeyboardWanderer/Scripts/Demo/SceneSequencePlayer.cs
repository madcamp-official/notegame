using System;
using System.Collections;
using KeyboardWanderer.Networking;
using UnityEngine;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Plays an authoritative server scene one action at a time. The server owns
    /// the result; this component owns only presentation timing and ordering.
    /// </summary>
    public sealed class SceneSequencePlayer : MonoBehaviour
    {
        public bool IsPlaying { get; private set; }

        public IEnumerator Play(
            GameApiClient.SceneActionSnapshot[] sequence,
            Func<GameApiClient.SceneActionSnapshot, IEnumerator> playAction)
        {
            if (IsPlaying || sequence == null || sequence.Length == 0)
                yield break;

            IsPlaying = true;
            for (int i = 0; i < sequence.Length; i++)
            {
                GameApiClient.SceneActionSnapshot action = sequence[i];
                if (action == null)
                    continue;
                IEnumerator presentation = playAction?.Invoke(action);
                if (presentation != null)
                    yield return presentation;
            }
            IsPlaying = false;
        }

        public void Cancel()
        {
            StopAllCoroutines();
            IsPlaying = false;
        }

        private void OnDisable()
        {
            IsPlaying = false;
        }
    }
}
