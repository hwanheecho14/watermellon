using System.Collections.Generic;
using UnityEngine;

namespace Watermelon3D
{
    /// <summary>
    /// 박스 상단 한계선(Limit Line) 트리거 스크립트.
    /// 낙하 완료된 과일이 이 트리거 영역을 2초 이상 지속적으로 침범하면 게임 오버를 발생시킵니다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class LimitLine : MonoBehaviour
    {
        [Tooltip("게임 오버까지 필요한 연속 침범 시간(초)")]
        public float requiredStayTime = 2.0f;

        [Tooltip("방금 떨어뜨린 과일이 통과할 때는 무시하기 위한 최소 경과 시간(초)")]
        public float dropGracePeriod = 0.85f;

        [Tooltip("위험 상태일 때 표시할 시각적 렌더러 (선택)")]
        public Renderer warningLineRenderer;

        // 트리거 내부에 머물고 있는 과일별 침범 누적 시간
        private readonly Dictionary<Fruit, float> stayTimers = new Dictionary<Fruit, float>();
        private Color normalColor = new Color(1f, 0.25f, 0.25f, 0.25f);
        private Color dangerColor = new Color(1f, 0.1f, 0.1f, 0.85f);

        private void Awake()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            box.isTrigger = true;

            if (warningLineRenderer == null)
            {
                warningLineRenderer = GetComponent<Renderer>();
            }
        }

        private void OnTriggerStay(Collider other)
        {
            if (GameManager.Instance == null || GameManager.Instance.IsGameOver) return;

            Fruit fruit = other.GetComponent<Fruit>();
            if (fruit == null || !fruit.isDropped) return;

            // 방금 스폰 위치에서 떨어지기 시작한 과일은 통과 중이므로 유예 시간 부여
            if (!fruit.hasTouchedSurface && (Time.time - fruit.dropTime) < dropGracePeriod)
            {
                return;
            }

            if (!stayTimers.ContainsKey(fruit))
            {
                stayTimers[fruit] = 0f;
            }

            stayTimers[fruit] += Time.deltaTime;
            float currentStay = stayTimers[fruit];

            // 경고선 깜빡임 연출
            if (warningLineRenderer != null && currentStay > 0.3f)
            {
                float pulse = Mathf.PingPong(Time.time * 6f, 1f);
                warningLineRenderer.material.color = Color.Lerp(normalColor, dangerColor, pulse);
            }

            // 2초 이상 지속적으로 침범 시 게임 오버 처리
            if (currentStay >= requiredStayTime)
            {
                GameManager.Instance.TriggerGameOver();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            Fruit fruit = other.GetComponent<Fruit>();
            if (fruit != null && stayTimers.ContainsKey(fruit))
            {
                stayTimers.Remove(fruit);
            }

            if (stayTimers.Count == 0 && warningLineRenderer != null)
            {
                warningLineRenderer.material.color = normalColor;
            }
        }

        private void LateUpdate()
        {
            // 파괴된(병합된) 과일 키 정리 (가비지 최소화)
            if (stayTimers.Count > 0)
            {
                List<Fruit> toRemove = null;
                foreach (var kvp in stayTimers)
                {
                    if (kvp.Key == null)
                    {
                        if (toRemove == null) toRemove = new List<Fruit>();
                        toRemove.Add(kvp.Key);
                    }
                }

                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        stayTimers.Remove(toRemove[i]);
                    }
                }

                if (stayTimers.Count == 0 && warningLineRenderer != null)
                {
                    warningLineRenderer.material.color = normalColor;
                }
            }
        }
    }
}
