using UnityEngine;

namespace KeyboardWanderer.Demo
{
    public sealed class KeyboardWandererEntityView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer actorRenderer;
        [SerializeField] private SpriteRenderer healthBack;
        [SerializeField] private SpriteRenderer healthFill;
        [SerializeField] private TextMesh finaleLabel;

        private bool _actorDefaultsCaptured;
        private Vector3 _actorLocalPosition;
        private Quaternion _actorLocalRotation;
        private Vector3 _actorLocalScale;

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
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            if (actorRenderer != null)
            {
                CaptureActorDefaults();
                Animator animator = actorRenderer.GetComponent<Animator>();
                if (animator != null)
                {
                    // Pooled views may previously have represented an entirely different
                    // asset. Leave no controller or animator state for the next entity.
                    animator.enabled = false;
                    animator.runtimeAnimatorController = null;
                }
                actorRenderer.transform.localPosition = _actorLocalPosition;
                actorRenderer.transform.localRotation = _actorLocalRotation;
                actorRenderer.transform.localScale = _actorLocalScale;
                actorRenderer.sprite = null;
                actorRenderer.color = Color.white;
                actorRenderer.flipX = false;
                actorRenderer.flipY = false;
                actorRenderer.enabled = true;
            }
            if (healthBack != null)
            {
                healthBack.sprite = whiteSprite;
                healthBack.color = Color.white;
                healthBack.enabled = true;
                healthBack.gameObject.SetActive(hostile);
            }
            if (healthFill != null)
            {
                healthFill.sprite = whiteSprite;
                healthFill.color = Color.white;
                healthFill.enabled = true;
                healthFill.gameObject.SetActive(hostile);
            }
            if (finaleLabel != null)
            {
                finaleLabel.text = string.Empty;
                finaleLabel.gameObject.SetActive(false);
            }
        }

        private void CaptureActorDefaults()
        {
            if (_actorDefaultsCaptured || actorRenderer == null)
                return;
            _actorDefaultsCaptured = true;
            Transform actorTransform = actorRenderer.transform;
            _actorLocalPosition = actorTransform.localPosition;
            _actorLocalRotation = actorTransform.localRotation;
            _actorLocalScale = actorTransform.localScale;
        }
    }
}
