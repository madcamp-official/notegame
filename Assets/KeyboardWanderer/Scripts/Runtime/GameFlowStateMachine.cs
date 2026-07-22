using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.Runtime
{
    public enum GameFlowPhase
    {
        Title,
        Settings,
        Paused,
        Ended,
        Tutorial,
        ResolvingChoice,
        Traveling,
        PresentingWorldAction,
        PresentingStory,
        AwaitingNarrativeChoice,
        AwaitingEncounterChoice,
        AwaitingChoice,
        WaitingForNarrative
    }

    public readonly struct GameFlowSignals
    {
        public readonly bool IsTitle;
        public readonly bool IsSettings;
        public readonly bool IsPlaying;
        public readonly bool IsRunActive;
        public readonly bool IsPaused;
        public readonly bool IsTutorial;
        public readonly bool IsResolving;
        public readonly bool IsTraveling;
        public readonly bool IsWorldActionPlaying;
        public readonly bool IsStoryVisible;
        public readonly bool IsAwaitingIntervention;
        public readonly bool HasEncounter;
        public readonly bool HasSealedNarrativeChoices;

        public GameFlowSignals(bool isTitle, bool isSettings, bool isPlaying, bool isRunActive,
            bool isPaused, bool isTutorial, bool isResolving, bool isTraveling,
            bool isWorldActionPlaying, bool isStoryVisible, bool isAwaitingIntervention, bool hasEncounter,
            bool hasSealedNarrativeChoices = false)
        {
            IsTitle = isTitle;
            IsSettings = isSettings;
            IsPlaying = isPlaying;
            IsRunActive = isRunActive;
            IsPaused = isPaused;
            IsTutorial = isTutorial;
            IsResolving = isResolving;
            IsTraveling = isTraveling;
            IsWorldActionPlaying = isWorldActionPlaying;
            IsStoryVisible = isStoryVisible;
            IsAwaitingIntervention = isAwaitingIntervention;
            HasEncounter = hasEncounter;
            HasSealedNarrativeChoices = hasSealedNarrativeChoices;
        }
    }

    /// <summary>
    /// UI, 이동, 대화, 서버 요청에 흩어진 상태를 하나의 우선순위 있는 게임 흐름으로 정규화한다.
    /// 실제 명령 허용 여부는 반드시 이 객체를 거쳐야 한다.
    /// </summary>
    public sealed class GameFlowStateMachine
    {
        public GameFlowPhase Phase { get; private set; } = GameFlowPhase.Title;

        public GameFlowPhase Refresh(GameFlowSignals signals)
        {
            if (signals.IsSettings) Phase = GameFlowPhase.Settings;
            else if (signals.IsTitle || !signals.IsPlaying) Phase = GameFlowPhase.Title;
            else if (!signals.IsRunActive) Phase = GameFlowPhase.Ended;
            else if (signals.IsPaused) Phase = GameFlowPhase.Paused;
            else if (signals.IsTutorial) Phase = GameFlowPhase.Tutorial;
            else if (signals.IsResolving) Phase = GameFlowPhase.ResolvingChoice;
            else if (signals.IsTraveling) Phase = GameFlowPhase.Traveling;
            else if (signals.IsWorldActionPlaying) Phase = GameFlowPhase.PresentingWorldAction;
            else if (signals.IsStoryVisible) Phase = GameFlowPhase.PresentingStory;
            else if (signals.IsAwaitingIntervention && signals.HasSealedNarrativeChoices) Phase = GameFlowPhase.AwaitingNarrativeChoice;
            else if (signals.IsAwaitingIntervention && signals.HasEncounter) Phase = GameFlowPhase.AwaitingEncounterChoice;
            else if (signals.IsAwaitingIntervention) Phase = GameFlowPhase.AwaitingChoice;
            else Phase = GameFlowPhase.WaitingForNarrative;
            return Phase;
        }

        public bool CanIssueGameplayCommand =>
            Phase == GameFlowPhase.AwaitingChoice || Phase == GameFlowPhase.AwaitingEncounterChoice ||
            Phase == GameFlowPhase.AwaitingNarrativeChoice;

        public bool CanSelectNarrativeChoice => Phase == GameFlowPhase.AwaitingNarrativeChoice;

        public bool CanIssueAbility(AbilityKind ability)
        {
            if (Phase == GameFlowPhase.AwaitingChoice) return true;
            if (Phase == GameFlowPhase.AwaitingNarrativeChoice) return ability == AbilityKind.Move;
            // WASD is also the player's explicit disengage action. During a normal
            // encounter it is submitted as an authoritative D20 MOVE, so success
            // closes the encounter and held input can continue into exploration.
            // The mandatory opening attack is guarded separately by the controller
            // and server and therefore cannot be bypassed through this permission.
            return Phase == GameFlowPhase.AwaitingEncounterChoice;
        }

        public string BlockReason(AbilityKind? ability = null)
        {
            if (ability.HasValue && CanIssueGameplayCommand && !CanIssueAbility(ability.Value))
                return "phase=" + Phase + " ability=" + ability.Value;
            return CanIssueGameplayCommand ? string.Empty : "phase=" + Phase;
        }
    }
}
