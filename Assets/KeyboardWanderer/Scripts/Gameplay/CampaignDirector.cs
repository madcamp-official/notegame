using System;

namespace KeyboardWanderer.Gameplay
{
    public sealed class CampaignConstraints
    {
        public CampaignAct Act { get; }
        public int RemainingTurns { get; }
        public bool MustAdvanceMainPlot { get; }
        public bool AllowNewLongQuest { get; }
        public bool AllowUnrelatedNpcOrRegion { get; }
        public bool ForceEnding { get; }

        public CampaignConstraints(
            CampaignAct act,
            int remainingTurns,
            bool mustAdvanceMainPlot,
            bool allowNewLongQuest,
            bool allowUnrelatedNpcOrRegion,
            bool forceEnding)
        {
            Act = act;
            RemainingTurns = remainingTurns;
            MustAdvanceMainPlot = mustAdvanceMainPlot;
            AllowNewLongQuest = allowNewLongQuest;
            AllowUnrelatedNpcOrRegion = allowUnrelatedNpcOrRegion;
            ForceEnding = forceEnding;
        }
    }

    public static class CampaignDirector
    {
        public static CampaignConstraints Evaluate(int committedTurns, int turnLimit)
        {
            if (turnLimit < 30 || turnLimit > 50)
                throw new ArgumentOutOfRangeException(nameof(turnLimit), "Campaign turn limit must be between 30 and 50.");

            int remaining = Math.Max(0, turnLimit - committedTurns);
            double progress = turnLimit == 0 ? 1d : (double)committedTurns / turnLimit;
            CampaignAct act;
            if (remaining <= 3)
                act = CampaignAct.Ending;
            else if (remaining <= 8)
                act = CampaignAct.Convergence;
            else if (progress >= 0.5d)
                act = CampaignAct.Pressure;
            else if (progress >= 0.15d)
                act = CampaignAct.Exploration;
            else
                act = CampaignAct.Introduction;

            return new CampaignConstraints(
                act,
                remaining,
                remaining <= 10,
                remaining > 5,
                remaining > 3,
                remaining <= 1);
        }
    }
}
