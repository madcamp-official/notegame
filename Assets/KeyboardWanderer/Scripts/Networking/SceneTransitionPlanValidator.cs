using System.Collections.Generic;

namespace KeyboardWanderer.Networking
{
    /// <summary>Outcome of validating a <see cref="SceneTransitionPlan"/>: valid only when no errors were collected.</summary>
    public sealed class SceneTransitionPlanValidation
    {
        private static readonly IReadOnlyList<string> NoErrors = new string[0];

        public SceneTransitionPlanValidation(IReadOnlyList<string> errors)
        {
            Errors = errors ?? NoErrors;
        }

        public bool IsValid => Errors.Count == 0;
        public IReadOnlyList<string> Errors { get; }

        public string ErrorSummary => IsValid ? "OK" : string.Join("; ", Errors);
    }

    /// <summary>
    /// Transport-neutral validation of a resolved <see cref="SceneTransitionPlan"/> before the client
    /// applies it. Mirrors the architecture-independent checks from the LLM communication reference
    /// (required fields, echo/request identity match to drop stale responses, string length caps, and
    /// an optional id allowlist). Anything requiring the raw model envelope or compact indices is the
    /// producer's responsibility and is intentionally out of scope here.
    /// </summary>
    public static class SceneTransitionPlanValidator
    {
        // String caps from the compact-decision contract (reference §18.2).
        public const int MaxSceneGoalLength = 80;
        public const int MaxConflictLength = 80;
        public const int MaxSummaryLength = 120;
        public const int MaxChoiceLabelLength = 40;
        public const int MaxIntentTagLength = 30;

        /// <param name="plan">The deserialized plan to check.</param>
        /// <param name="expectedRequestId">If set, <c>plan.requestId</c> must equal it.</param>
        /// <param name="expectedEcho">If set, the plan's echo must match this request identity exactly.</param>
        /// <param name="allowedIds">If set, every id the plan references must be a member.</param>
        /// <param name="expectedChoiceCount">If greater than zero, the plan must offer exactly this many choices.</param>
        public static SceneTransitionPlanValidation Validate(
            SceneTransitionPlan plan,
            string expectedRequestId = null,
            SceneTransitionEcho expectedEcho = null,
            ISet<string> allowedIds = null,
            int expectedChoiceCount = 0)
        {
            var errors = new List<string>();

            if (plan == null)
            {
                errors.Add("plan is null");
                return new SceneTransitionPlanValidation(errors);
            }

            if (!string.IsNullOrEmpty(plan.status) && plan.status != "OK")
            {
                errors.Add($"status is '{plan.status}', expected 'OK'");
            }

            if (!string.IsNullOrEmpty(expectedRequestId) && plan.requestId != expectedRequestId)
            {
                errors.Add($"requestId '{plan.requestId}' does not match expected '{expectedRequestId}'");
            }

            ValidateEcho(plan.echo, expectedEcho, errors);
            ValidateSelection(plan.selection, errors);
            ValidateTransition(plan.transition, errors);
            ValidateScenePlan(plan.scenePlan, expectedChoiceCount, errors);

            if (allowedIds != null)
            {
                ValidateAllowlist(plan, allowedIds, errors);
            }

            return new SceneTransitionPlanValidation(errors);
        }

        private static void ValidateEcho(SceneTransitionEcho echo, SceneTransitionEcho expected, List<string> errors)
        {
            if (expected == null)
            {
                return;
            }

            if (echo == null)
            {
                errors.Add("echo is missing");
                return;
            }

            if (echo.runId != expected.runId)
            {
                errors.Add($"echo.runId '{echo.runId}' != expected '{expected.runId}'");
            }

            if (echo.turnId != expected.turnId)
            {
                errors.Add($"echo.turnId '{echo.turnId}' != expected '{expected.turnId}'");
            }

            if (echo.turnNo != expected.turnNo)
            {
                errors.Add($"echo.turnNo {echo.turnNo} != expected {expected.turnNo}");
            }

            if (echo.expectedRunVersion != expected.expectedRunVersion)
            {
                errors.Add($"echo.expectedRunVersion {echo.expectedRunVersion} != expected {expected.expectedRunVersion}");
            }

            if (!string.IsNullOrEmpty(expected.worldLayoutHash) && echo.worldLayoutHash != expected.worldLayoutHash)
            {
                errors.Add("echo.worldLayoutHash does not match request");
            }

            if (!string.IsNullOrEmpty(expected.contextHash) && echo.contextHash != expected.contextHash)
            {
                errors.Add("echo.contextHash does not match request");
            }
        }

        private static void ValidateSelection(SceneSelection selection, List<string> errors)
        {
            if (selection == null)
            {
                errors.Add("selection is missing");
                return;
            }

            RequireId(selection.destinationAreaId, "selection.destinationAreaId", errors);
            RequireId(selection.routeId, "selection.routeId", errors);
            RequireId(selection.entrySlotId, "selection.entrySlotId", errors);
            RequireId(selection.storyBeatId, "selection.storyBeatId", errors);
            RequireId(selection.sceneTemplateId, "selection.sceneTemplateId", errors);
        }

        private static void ValidateTransition(SceneTransition transition, List<string> errors)
        {
            if (transition == null)
            {
                errors.Add("transition is missing");
                return;
            }

            RequireId(transition.transitionStyleId, "transition.transitionStyleId", errors);
            RequireId(transition.bgmCueId, "transition.bgmCueId", errors);
            RequireLength(transition.summary, MaxSummaryLength, "transition.summary", errors);
        }

        private static void ValidateScenePlan(ScenePlanDetail scenePlan, int expectedChoiceCount, List<string> errors)
        {
            if (scenePlan == null)
            {
                errors.Add("scenePlan is missing");
                return;
            }

            RequireLength(scenePlan.sceneGoal, MaxSceneGoalLength, "scenePlan.sceneGoal", errors, required: true);
            RequireLength(scenePlan.conflict, MaxConflictLength, "scenePlan.conflict", errors, required: true);

            var choices = scenePlan.suggestedChoices;
            var count = choices?.Length ?? 0;
            if (count == 0)
            {
                errors.Add("scenePlan.suggestedChoices is empty");
            }
            else if (expectedChoiceCount > 0 && count != expectedChoiceCount)
            {
                errors.Add($"scenePlan.suggestedChoices has {count} entries, expected {expectedChoiceCount}");
            }

            if (choices == null)
            {
                return;
            }

            for (var i = 0; i < choices.Length; i++)
            {
                var choice = choices[i];
                if (choice == null)
                {
                    errors.Add($"scenePlan.suggestedChoices[{i}] is null");
                    continue;
                }

                RequireId(choice.choiceId, $"suggestedChoices[{i}].choiceId", errors);
                RequireLength(choice.label, MaxChoiceLabelLength, $"suggestedChoices[{i}].label", errors, required: true);
                RequireLength(choice.intentTag, MaxIntentTagLength, $"suggestedChoices[{i}].intentTag", errors, required: true);
            }
        }

        private static void ValidateAllowlist(SceneTransitionPlan plan, ISet<string> allowedIds, List<string> errors)
        {
            RequireAllowed(plan.selection?.destinationAreaId, "selection.destinationAreaId", allowedIds, errors);
            RequireAllowed(plan.selection?.routeId, "selection.routeId", allowedIds, errors);
            RequireAllowed(plan.selection?.entrySlotId, "selection.entrySlotId", allowedIds, errors);
            RequireAllowed(plan.selection?.storyBeatId, "selection.storyBeatId", allowedIds, errors);
            RequireAllowed(plan.selection?.sceneTemplateId, "selection.sceneTemplateId", allowedIds, errors);
            RequireAllowed(plan.transition?.transitionStyleId, "transition.transitionStyleId", allowedIds, errors);
            RequireAllowed(plan.transition?.bgmCueId, "transition.bgmCueId", allowedIds, errors);

            var sfx = plan.transition?.sfxCueIds;
            if (sfx != null)
            {
                for (var i = 0; i < sfx.Length; i++)
                {
                    RequireAllowed(sfx[i], $"transition.sfxCueIds[{i}]", allowedIds, errors);
                }
            }

            var reveals = plan.scenePlan?.revealIds;
            if (reveals != null)
            {
                for (var i = 0; i < reveals.Length; i++)
                {
                    RequireAllowed(reveals[i], $"scenePlan.revealIds[{i}]", allowedIds, errors);
                }
            }
        }

        private static void RequireId(string value, string field, List<string> errors)
        {
            if (string.IsNullOrEmpty(value))
            {
                errors.Add($"{field} is required");
            }
        }

        private static void RequireLength(string value, int max, string field, List<string> errors, bool required = false)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (required)
                {
                    errors.Add($"{field} is required");
                }

                return;
            }

            if (value.Length > max)
            {
                errors.Add($"{field} exceeds {max} characters ({value.Length})");
            }
        }

        private static void RequireAllowed(string value, string field, ISet<string> allowedIds, List<string> errors)
        {
            if (!string.IsNullOrEmpty(value) && !allowedIds.Contains(value))
            {
                errors.Add($"{field} '{value}' is not in the request allowlist");
            }
        }
    }
}
