using System;
using KeyboardWanderer.Core;
using KeyboardWanderer.Gameplay;

namespace KeyboardWanderer.Demo
{
    [Flags]
    public enum PresentationChange
    {
        None = 0,
        Screen = 1 << 0,
        Hud = 1 << 1,
        Dialogue = 1 << 2,
        Minimap = 1 << 3,
        Selection = 1 << 4,
        All = Screen | Hud | Dialogue | Minimap | Selection
    }

    /// <summary>
    /// The normalized, read-only state consumed by authored scene presenters.
    /// Network DTOs and save models deliberately do not cross this boundary.
    /// </summary>
    public readonly struct RunPresentationState : IEquatable<RunPresentationState>
    {
        public readonly long Version;
        public readonly int Turn;
        public readonly string LayoutHash;
        public readonly GridCoord PlayerPosition;
        public readonly GridCoord? SelectedCoord;
        public readonly Guid? SelectedTarget;
        public readonly AbilityKind Ability;
        public readonly int Screen;
        public readonly int DialoguePage;
        public readonly string DialogueSignature;
        public readonly bool Paused;
        public readonly bool Pending;
        public readonly bool Walking;

        public RunPresentationState(long version, int turn, string layoutHash, GridCoord playerPosition,
            GridCoord? selectedCoord, Guid? selectedTarget, AbilityKind ability, int screen,
            int dialoguePage, string dialogueSignature, bool paused, bool pending, bool walking)
        {
            Version = version;
            Turn = turn;
            LayoutHash = layoutHash ?? string.Empty;
            PlayerPosition = playerPosition;
            SelectedCoord = selectedCoord;
            SelectedTarget = selectedTarget;
            Ability = ability;
            Screen = screen;
            DialoguePage = dialoguePage;
            DialogueSignature = dialogueSignature ?? string.Empty;
            Paused = paused;
            Pending = pending;
            Walking = walking;
        }

        public bool Equals(RunPresentationState other)
        {
            return Version == other.Version && Turn == other.Turn && LayoutHash == other.LayoutHash &&
                   PlayerPosition.Equals(other.PlayerPosition) && Nullable.Equals(SelectedCoord, other.SelectedCoord) &&
                   Nullable.Equals(SelectedTarget, other.SelectedTarget) && Ability == other.Ability &&
                   Screen == other.Screen && DialoguePage == other.DialoguePage &&
                   DialogueSignature == other.DialogueSignature && Paused == other.Paused &&
                   Pending == other.Pending && Walking == other.Walking;
        }

        public override bool Equals(object obj) => obj is RunPresentationState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Version, Turn, LayoutHash, PlayerPosition,
            SelectedCoord, SelectedTarget, Ability, HashCode.Combine(Screen, DialoguePage, DialogueSignature,
                Paused, Pending, Walking));
    }

    /// <summary>Owns presentation invalidation independently from MonoBehaviour lifecycle.</summary>
    public sealed class RunCoordinator
    {
        private bool _hasState;
        private RunPresentationState _state;

        public event Action<RunPresentationState, PresentationChange> PresentationChanged;
        public RunPresentationState State => _state;

        public void Publish(RunPresentationState next, PresentationChange requested = PresentationChange.None)
        {
            PresentationChange changes = requested | Compare(_hasState ? _state : default, next, !_hasState);
            _state = next;
            _hasState = true;
            if (changes != PresentationChange.None)
                PresentationChanged?.Invoke(next, changes);
        }

        public void Invalidate(PresentationChange changes)
        {
            if (_hasState && changes != PresentationChange.None)
                PresentationChanged?.Invoke(_state, changes);
        }

        private static PresentationChange Compare(RunPresentationState previous, RunPresentationState next, bool first)
        {
            if (first) return PresentationChange.All;
            PresentationChange changes = PresentationChange.None;
            if (previous.Screen != next.Screen || previous.Paused != next.Paused)
                changes |= PresentationChange.Screen;
            if (previous.Version != next.Version || previous.Turn != next.Turn || previous.Ability != next.Ability ||
                previous.Pending != next.Pending || previous.Walking != next.Walking)
                changes |= PresentationChange.Hud;
            if (previous.DialoguePage != next.DialoguePage || previous.DialogueSignature != next.DialogueSignature)
                changes |= PresentationChange.Dialogue;
            if (previous.LayoutHash != next.LayoutHash || !previous.PlayerPosition.Equals(next.PlayerPosition) ||
                !Nullable.Equals(previous.SelectedCoord, next.SelectedCoord) ||
                !Nullable.Equals(previous.SelectedTarget, next.SelectedTarget))
                changes |= PresentationChange.Minimap;
            if (!Nullable.Equals(previous.SelectedCoord, next.SelectedCoord) ||
                !Nullable.Equals(previous.SelectedTarget, next.SelectedTarget) || previous.Ability != next.Ability)
                changes |= PresentationChange.Selection;
            return changes;
        }
    }
}
