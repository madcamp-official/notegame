using UnityEngine;

namespace KeyboardWanderer.Demo
{
    /// <summary>
    /// Story Panel 오브젝트가 튜토리얼 문구를 직렬화해 소유한다.
    /// 기획자는 코드를 열지 않고 Inspector에서 페이지를 수정할 수 있다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KeyboardWandererTutorialView : MonoBehaviour
    {
        [SerializeField] private KeyboardWandererDialogueView dialogueView;
        [SerializeField, TextArea(3, 8)] private string[] pages = CreateDefaultPages();

        public bool IsReady => dialogueView != null && dialogueView.IsReady && pages != null && pages.Length > 0;
        public int PageCount => pages == null ? 0 : pages.Length;

        public void Present(int page, string objective)
        {
            if (!IsReady)
                return;
            int index = Mathf.Clamp(page, 0, pages.Length - 1);
            string content = (pages[index] ?? string.Empty).Replace("{objective}",
                string.IsNullOrWhiteSpace(objective) ? "좌측 상단의 현재 목표" : objective);
            dialogueView.Present(true, "게임 방법 " + (index + 1) + "/" + pages.Length,
                content, index < pages.Length - 1 ? "다음 ▶" : "게임 시작", true);
        }

#if UNITY_EDITOR
        public void Configure(KeyboardWandererDialogueView dialogue)
        {
            dialogueView = dialogue;
            if (pages == null || pages.Length == 0)
                pages = CreateDefaultPages();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        private static string[] CreateDefaultPages()
        {
            return new[]
            {
                "당신은 넙죽이입니다. 붕괴한 코드리아를 탐험해 관리자 권한 3개를 되찾고, 루트 시스템까지 도달하는 것이 최종 목표입니다.\n지금 할 일은 좌측 상단의 ‘현재 목표’에서 확인할 수 있습니다.",
                "이동: W 버튼을 선택하고 지도에서 빈 타일을 누른 뒤, 우측 아래 실행 버튼을 누르세요.\n이동은 의미 턴과 D20을 사용하지 않습니다. 분홍 미니맵 표식이 현재 목표입니다.",
                "조사: Ctrl F를 선택하고 목표 인물이나 물건을 누른 뒤 실행하세요.\n조사·공격·연결 같은 키보드 기술은 의미 턴 1회와 D20 판정을 사용하며, 성공과 실패가 세계에 남습니다.",
                "주요 기술: Delete는 적 1명 공격, Ctrl A는 주변 적 전체 공격, Ctrl K는 두 대상 연결·대화, Ctrl Z는 최근 의미 턴 2개 되돌리기입니다.\n사용할 수 없을 때는 상단 선택 패널에 필요한 대상·거리·조건이 표시됩니다.",
                "첫 목표: {objective}\n행동 결과는 서버 규칙이 확정하고, 그 뒤의 짧은 장면과 대사는 AI가 표현합니다. AI가 응답하지 않아도 기본 이야기로 게임은 계속됩니다."
            };
        }
    }
}
