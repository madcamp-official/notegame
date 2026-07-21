using KeyboardWanderer.Gameplay;
using KeyboardWanderer.World;
using UnityEngine;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// 스킬 이펙트 매핑을 게임 진행 없이 눈으로 확인하는 임시 개발 도구.
    /// 화면 좌상단 OnGUI 패널에서 스킬 버튼을 누르면 근처 적(없으면 시전자) 위에 해당 이펙트를 재생한다.
    /// 출시 빌드 전에 제거하거나 KeyboardWandererDemoController의 토글을 끄면 된다.
    /// </summary>
    public sealed class SkillEffectTester : MonoBehaviour
    {
        private static readonly (AbilityKind Skill, string Label)[] Buttons =
        {
            (AbilityKind.Copy, "COPY · 복제 (분신)"),
            (AbilityKind.Delete, "DELETE · 단일 공격 (fx_type)"),
            (AbilityKind.Connect, "CONNECT · 연결 (스파크)"),
            (AbilityKind.Restore, "RESTORE · 복원 (회복 오라)"),
            (AbilityKind.Undo, "UNDO · 2턴 역행 (마법진)"),
            (AbilityKind.Search, "SEARCH · 조사 (스캔 오라)"),
            (AbilityKind.SelectAll, "SELECT_ALL · 영역전개 (전체 공격)"),
        };

        private static readonly SkillFxType[] FxTypes =
        {
            SkillFxType.Physical, SkillFxType.Fire, SkillFxType.Ice,
            SkillFxType.Thunder, SkillFxType.Void, SkillFxType.Water, SkillFxType.Plant
        };

        private KeyboardWandererDemoController _controller;
        private SkillFxSize _size = SkillFxSize.Medium;
        private int _fxTypeIndex;
        private bool _expanded = true;
        private string _lastResult = "";

        public void Bind(KeyboardWandererDemoController controller)
        {
            _controller = controller;
        }

        private void OnGUI()
        {
            if (_controller == null)
                return;

            var area = new Rect(12f, 12f, 260f, _expanded ? 400f : 34f);
            GUILayout.BeginArea(area, GUI.skin.box);

            _expanded = GUILayout.Toggle(_expanded, _expanded ? "▼ 스킬 이펙트 테스터" : "▶ 스킬 이펙트 테스터");
            if (_expanded)
            {
                GUILayout.Space(4f);
                GUILayout.Label("fx_size: " + _size + "  (대성공→Large 등 판정 대응)");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀ 작게"))
                    _size = (SkillFxSize)Mathf.Max(0, (int)_size - 1);
                if (GUILayout.Button("크게 ▶"))
                    _size = (SkillFxSize)Mathf.Min((int)SkillFxSize.Screen, (int)_size + 1);
                GUILayout.EndHorizontal();

                GUILayout.Label("fx_type(DELETE 속성): " + FxTypes[_fxTypeIndex]);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("◀ 속성"))
                    _fxTypeIndex = (_fxTypeIndex - 1 + FxTypes.Length) % FxTypes.Length;
                if (GUILayout.Button("속성 ▶"))
                    _fxTypeIndex = (_fxTypeIndex + 1) % FxTypes.Length;
                GUILayout.EndHorizontal();

                GUILayout.Space(4f);
                for (int i = 0; i < Buttons.Length; i++)
                    if (GUILayout.Button(Buttons[i].Label))
                    {
                        int hits = _controller.DebugPreviewSkillEffect(Buttons[i].Skill, _size, FxTypes[_fxTypeIndex]);
                        _lastResult = Buttons[i].Skill + " → 대상 " + hits + "개에 재생";
                    }

                if (!string.IsNullOrEmpty(_lastResult))
                {
                    GUILayout.Space(4f);
                    GUILayout.Label(_lastResult);
                }
            }

            GUILayout.EndArea();
        }
    }
}
