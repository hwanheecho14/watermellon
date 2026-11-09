using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.EventSystems;

namespace Watermelon3D
{
    /// <summary>
    /// 상단 과일 스폰, 마우스/터치 Raycast X좌표 추적,
    /// 수직 가이드라인(LineRenderer), 클릭 낙하 및 쿨타임을 담당하는 스크립트.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class FruitSpawner : MonoBehaviour
    {
        [Header("스폰 설정")]
        [Tooltip("과일이 대기하는 상단 Y 좌표")]
        public float spawnY = 3.8f;

        [Tooltip("박스 내부 이동 가능 좌우 폭(반경)")]
        public float boxHalfWidth = 2.85f;

        [Tooltip("과일 낙하 후 다음 과일 생성까지의 쿨타임(초)")]
        public float spawnCooldown = 0.55f;

        [Tooltip("초반 스폰 가능한 최대 과일 단계 (0~4: 체리~감)")]
        public int maxSpawnLevel = 4;

        [Header("가이드라인 설정")]
        public float floorY = -3.8f;
        public LayerMask guideRaycastMask = ~0;

        private LineRenderer guideLine;
        private Fruit currentFruit;
        private int nextFruitLevel = 0;
        private float cooldownTimer = 0f;
        private float currentTargetX = 0f;
        private Camera mainCam;

        // Z=0 평면 (마우스/터치 Raycast 추적용)
        private Plane playPlane = new Plane(Vector3.back, Vector3.zero);

        private void Awake()
        {
            guideLine = GetComponent<LineRenderer>();
            SetupGuideLine();
        }

        private void Start()
        {
            mainCam = Camera.main;
            nextFruitLevel = Random.Range(0, maxSpawnLevel + 1);
            SpawnReadyFruit();
        }

        private void SetupGuideLine()
        {
            guideLine.positionCount = 2;
            guideLine.startWidth = 0.045f;
            guideLine.endWidth = 0.045f;
            guideLine.useWorldSpace = true;
            guideLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            guideLine.receiveShadows = false;
        }

        private void Update()
        {
            if (GameManager.Instance == null || GameManager.Instance.IsGameOver)
            {
                if (guideLine.enabled) guideLine.enabled = false;
                return;
            }

            // 쿨타임 진행
            if (currentFruit == null)
            {
                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f)
                {
                    SpawnReadyFruit();
                }
                return;
            }

            // UI 버튼 위를 클릭 중일 때는 조작 제외
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                UpdateGuideLine();
                return;
            }

            // 1. 마우스/터치 위치를 Raycast로 추적하여 상단 스폰 X 좌표 결정
            UpdatePointerPosition();

            // 2. 현재 과일 위치에서 바닥을 향해 수직으로 내려오는 가이드라인 업데이트
            UpdateGuideLine();

            // 3. 클릭(또는 터치 릴리즈) 시 과일 낙하
            if (IsDropTriggered())
            {
                DropCurrentFruit();
            }
        }

        private void UpdatePointerPosition()
        {
            if (mainCam == null) mainCam = Camera.main;
            if (mainCam == null) return;

            Vector2 screenPos = GetPointerScreenPosition(out bool hasPointer);
            if (!hasPointer) return;

            Ray ray = mainCam.ScreenPointToRay(screenPos);
            float hitX = currentTargetX;

            // Physics.Raycast 우선 확인 후 평면(Z=0) Raycast로 정확한 X좌표 산출
            if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f))
            {
                hitX = hitInfo.point.x;
            }
            else if (playPlane.Raycast(ray, out float enter))
            {
                Vector3 worldPoint = ray.GetPoint(enter);
                hitX = worldPoint.x;
            }

            // 현재 과일의 반지름만큼 벽 안쪽으로 Clamp하여 벽에 끼지 않게 제한
            float radius = currentFruit != null ? currentFruit.transform.localScale.x * 0.5f : 0.3f;
            float limitX = Mathf.Max(0.1f, boxHalfWidth - radius - 0.05f);
            currentTargetX = Mathf.Clamp(hitX, -limitX, limitX);

            Vector3 spawnPos = new Vector3(currentTargetX, spawnY, 0f);
            transform.position = spawnPos;
            if (currentFruit != null)
            {
                currentFruit.transform.position = spawnPos;
            }
        }

        private void UpdateGuideLine()
        {
            if (currentFruit == null)
            {
                guideLine.enabled = false;
                return;
            }

            guideLine.enabled = true;
            Vector3 startPos = currentFruit.transform.position;
            float endY = floorY;

            // 수직 아래 방향으로 Raycast하여 바닥 또는 가장 위의 과일 표면까지 가이드라인 표시
            if (Physics.Raycast(startPos, Vector3.down, out RaycastHit hit, 20f, guideRaycastMask, QueryTriggerInteraction.Ignore))
            {
                endY = hit.point.y;
            }

            guideLine.SetPosition(0, new Vector3(startPos.x, startPos.y, 0f));
            guideLine.SetPosition(1, new Vector3(startPos.x, endY, 0f));
        }

        private void SpawnReadyFruit()
        {
            if (GameManager.Instance == null || GameManager.Instance.IsGameOver) return;

            int spawnLevel = nextFruitLevel;
            nextFruitLevel = Random.Range(0, maxSpawnLevel + 1);

            // 우측 상단 '다음 과일(Next)' UI 연동
            GameManager.Instance.UpdateNextFruitUI(nextFruitLevel);

            float radius = GameManager.Instance.GetFruitDiameter(spawnLevel) * 0.5f;
            float limitX = Mathf.Max(0.1f, boxHalfWidth - radius - 0.05f);
            currentTargetX = Mathf.Clamp(currentTargetX, -limitX, limitX);

            Vector3 spawnPos = new Vector3(currentTargetX, spawnY, 0f);
            currentFruit = GameManager.Instance.CreateFruitInstance(spawnLevel, spawnPos, isMerged: false);

            if (currentFruit != null)
            {
                currentFruit.PrepareForSpawn();
                guideLine.enabled = true;
                UpdateGuideLine();
            }
        }

        private void DropCurrentFruit()
        {
            if (currentFruit == null) return;

            currentFruit.Drop();
            currentFruit = null;
            guideLine.enabled = false;
            cooldownTimer = spawnCooldown;
        }

        /// <summary>
        /// 신규 Input System과 구 Input Manager 모두 호환되는 포인터 위치 읽기
        /// </summary>
        private Vector2 GetPointerScreenPosition(out bool valid)
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                valid = true;
                return Touchscreen.current.primaryTouch.position.ReadValue();
            }
            if (Mouse.current != null)
            {
                valid = true;
                return Mouse.current.position.ReadValue();
            }
#else
            if (Input.touchCount > 0)
            {
                valid = true;
                return Input.GetTouch(0).position;
            }
            valid = true;
            return Input.mousePosition;
#endif
            valid = false;
            return Vector2.zero;
        }

        private bool IsDropTriggered()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                return true;
            }
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                return true;
            }
            return false;
#else
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Ended)
            {
                return true;
            }
            return Input.GetMouseButtonDown(0);
#endif
        }
    }
}
