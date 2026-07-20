using System;
using System.Collections.Generic;
using UnityEngine;

#pragma warning disable 0649 // JsonUtility populates serialized DTO fields through reflection.

namespace KeyboardWanderer.Networking
{
    /// <summary>
    /// Presentation-layer scene transition plan the client consumes to stage the next scene
    /// (destination, transition style, audio cues, reveals, and suggested choices).
    ///
    /// This is deliberately transport-neutral: it models the resolved v1.0 contract described in
    /// the LLM communication reference, in which every index has already been restored to a real
    /// game id before Unity sees it. Whichever producer emits it — the authoritative Game Service
    /// or a direct prototype bridge — the client treats the payload as untrusted data and validates
    /// it with <see cref="SceneTransitionPlanValidator"/> before applying anything.
    ///
    /// Field names use the same camelCase convention as the rest of <c>GameApiClient</c> so that
    /// <see cref="JsonUtility.FromJson{T}"/> maps them directly.
    /// </summary>
    [Serializable]
    public sealed class SceneTransitionPlan
    {
        public string protocolVersion;
        public string schemaVersion;
        public string requestType;
        public string requestId;
        public string status;
        public SceneTransitionEcho echo;
        public SceneSelection selection;
        public SceneTransition transition;
        public ScenePlanDetail scenePlan;
        public SceneUsage usage;
    }

    /// <summary>Copy of the request identity, used to reject stale or mismatched responses.</summary>
    [Serializable]
    public sealed class SceneTransitionEcho
    {
        public string runId;
        public string turnId;
        public int turnNo;
        public long expectedRunVersion;
        public string worldLayoutHash;
        public string contextHash;
    }

    /// <summary>The chosen destination and story ids, already restored from compact indices.</summary>
    [Serializable]
    public sealed class SceneSelection
    {
        public string destinationAreaId;
        public string routeId;
        public string entrySlotId;
        public string storyBeatId;
        public string sceneTemplateId;
    }

    /// <summary>How to present the transition: style, audio, camera, and short narration.</summary>
    [Serializable]
    public sealed class SceneTransition
    {
        public string transitionStyleId;
        public string bgmCueId;
        public string[] sfxCueIds;
        public string cameraCue;
        public string summary;
        public string body;
    }

    /// <summary>The scene's dramatic frame and the choices offered to the player.</summary>
    [Serializable]
    public sealed class ScenePlanDetail
    {
        public string sceneGoal;
        public string conflict;
        public string[] revealIds;
        public SuggestedChoice[] suggestedChoices;
    }

    /// <summary>A single player-facing choice with a machine-readable intent tag.</summary>
    [Serializable]
    public sealed class SuggestedChoice
    {
        public string choiceId;
        public string label;
        public string intentTag;
    }

    /// <summary>Non-authoritative telemetry about the model call that produced this plan.</summary>
    [Serializable]
    public sealed class SceneUsage
    {
        public string modelProfile;
        public string modelId;
        public int inputTokens;
        public int outputTokens;
        public int latencyMs;
        public string finishReason;
    }
}
