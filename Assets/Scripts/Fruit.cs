using System.Collections;
using UnityEngine;

namespace Watermelon3D
{
    /// <summary>
    /// 3D 구체 과일 객체 스크립트.
    /// 3D SphereCollider와 Rigidbody를 가지며, Z축 이동과 X/Y축 회전을 고정하여
    /// 2D 평면(X, Y) 위에서 원작처럼 직관적으로 굴러가고 쌓이도록 제어합니다.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public class Fruit : MonoBehaviour
    {
        [Header("과일 상태")]
        public int levelIndex;
        public bool isDropped = false;
        public bool hasMerged = false;
        public bool hasTouchedSurface = false;
        public float dropTime = 0f;

        private Rigidbody rb;
        private SphereCollider sphereCollider;

        public Rigidbody Rb => rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            sphereCollider = GetComponent<SphereCollider>();

            // 2D 평면(X, Y축) 물리 제한:
            // Z축 위치를 고정하고 X, Y축 회전을 고정하여 Z축 회전(좌우 구르기)만 허용합니다.
            rb.constraints = RigidbodyConstraints.FreezePositionZ
                           | RigidbodyConstraints.FreezeRotationX
                           | RigidbodyConstraints.FreezeRotationY;

            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        /// <summary>
        /// 스폰 대기 상태 설정 (클릭 전에는 상단에 매달려 있음)
        /// </summary>
        public void PrepareForSpawn()
        {
            isDropped = false;
            hasMerged = false;
            hasTouchedSurface = false;

            if (rb == null) rb = GetComponent<Rigidbody>();
            if (sphereCollider == null) sphereCollider = GetComponent<SphereCollider>();

            rb.useGravity = false;
            rb.isKinematic = true;
            sphereCollider.enabled = false;
        }

        /// <summary>
        /// 플레이어가 클릭/터치했을 때 중력을 켜고 낙하 시작
        /// </summary>
        public void Drop()
        {
            isDropped = true;
            dropTime = Time.time;

            sphereCollider.enabled = true;
            rb.isKinematic = false;
            rb.useGravity = true;

            // Z=0 평면 유지
            Vector3 pos = transform.position;
            pos.z = 0f;
            transform.position = pos;
        }

        /// <summary>
        /// 병합으로 새로 태어난 과일 초기화 (즉시 물리 활성화 + 팝업 연출)
        /// </summary>
        public void ActivateAsMergedFruit(Vector3 targetScale)
        {
            isDropped = true;
            hasTouchedSurface = true;
            dropTime = Time.time - 1f;

            sphereCollider.enabled = true;
            rb.isKinematic = false;
            rb.useGravity = true;

            StartCoroutine(PopScaleRoutine(targetScale));
        }

        private IEnumerator PopScaleRoutine(Vector3 targetScale)
        {
            transform.localScale = targetScale * 0.35f;
            float elapsed = 0f;
            const float duration = 0.14f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // 통통 튀는 오버슈트 보간
                float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.15f;
                transform.localScale = Vector3.Lerp(targetScale * 0.35f, targetScale, t) * overshoot;
                yield return null;
            }

            transform.localScale = targetScale;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!isDropped || hasMerged) return;

            hasTouchedSurface = true;

            // 동일한 과일 컴포넌트 확인
            Fruit other = collision.gameObject.GetComponent<Fruit>();
            if (other == null || !other.isDropped || other.hasMerged) return;

            // 같은 단계(ID)의 과일끼리 충돌했을 때 병합
            if (other.levelIndex == this.levelIndex)
            {
                if (GameManager.Instance == null) return;

                // 최대 단계(수박) 이상이면 더 이상 합쳐지지 않음 (또는 수박끼리 합쳐서 보너스 처리 가능)
                if (this.levelIndex >= GameManager.Instance.MaxFruitLevel)
                {
                    return;
                }

                // 두 객체 중 InstanceID가 작은 쪽에서만 병합을 1회 실행하여 중복 생성 방지
                if (this.GetInstanceID() < other.GetInstanceID())
                {
                    this.hasMerged = true;
                    other.hasMerged = true;

                    // 두 과일의 중심 지점 계산 (Vector3.Lerp 사용)
                    Vector3 mergeCenter = Vector3.Lerp(this.transform.position, other.transform.position, 0.5f);
                    mergeCenter.z = 0f;

                    int nextLevel = this.levelIndex + 1;

                    // 두 기존 과일 파괴 후 한 단계 더 큰 과일 생성
                    Destroy(other.gameObject);
                    Destroy(this.gameObject);

                    GameManager.Instance.MergeFruits(mergeCenter, nextLevel);
                }
            }
        }
    }
}
