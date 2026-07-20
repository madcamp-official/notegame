using UnityEngine;

namespace KeyboardWanderer.Demo
{
    public sealed class KeyboardWandererEntityView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer actorRenderer;
        [SerializeField] private SpriteRenderer healthBack;
        [SerializeField] private SpriteRenderer healthFill;
        [SerializeField] private TextMesh finaleLabel;

        public SpriteRenderer ActorRenderer => actorRenderer;
        public GameObject HealthBack => healthBack != null ? healthBack.gameObject : null;
        public GameObject HealthFill => healthFill != null ? healthFill.gameObject : null;
        public GameObject FinaleLabel => finaleLabel != null ? finaleLabel.gameObject : null;
        public TextMesh FinaleLabelText => finaleLabel;

        public void Configure(
            SpriteRenderer actor,
            SpriteRenderer healthBackground,
            SpriteRenderer healthForeground,
            TextMesh label)
        {
            actorRenderer = actor;
            healthBack = healthBackground;
            healthFill = healthForeground;
            finaleLabel = label;
        }

        public void Prepare(Sprite whiteSprite, bool hostile)
        {
            if (healthBack != null)
            {
                healthBack.sprite = whiteSprite;
                healthBack.gameObject.SetActive(hostile);
            }
            if (healthFill != null)
            {
                healthFill.sprite = whiteSprite;
                healthFill.gameObject.SetActive(hostile);
            }
            if (finaleLabel != null)
                finaleLabel.gameObject.SetActive(false);
        }
    }
}
