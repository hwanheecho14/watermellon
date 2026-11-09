using System;
using UnityEngine;

namespace Watermelon3D
{
    /// <summary>
    /// 11단계 과일 진화 정보를 담는 데이터 구조체
    /// </summary>
    [Serializable]
    public class FruitData
    {
        [Tooltip("과일 단계 (0: 체리 ~ 10: 수박)")]
        public int levelIndex;

        [Tooltip("과일 이름")]
        public string fruitName;

        [Tooltip("3D 구체 스케일 (지름)")]
        public float diameter;

        [Tooltip("과일 대표 색상")]
        public Color color;

        [Tooltip("과일 질량 (Rigidbody Mass)")]
        public float mass;

        [Tooltip("이 단계로 병합 성공 시 획득하는 점수")]
        public int mergeScore;

        [Tooltip("이 단계로 병합 성공 시 획득하는 코인 (특정 크기 이상부터 부여)")]
        public int rewardCoins;

        [Tooltip("생성할 과일 프리팹 (없을 경우 런타임/에디터에서 자동 생성)")]
        public GameObject prefab;
    }
}
