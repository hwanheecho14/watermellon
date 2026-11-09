using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Watermelon3D
{
    /// <summary>
    /// 3D 수박 게임 전체 매니저.
    /// - 11단계 과일 진화 데이터(배열/리스트) 관리
    /// - 병합 시 중심점(Vector3.Lerp) 생성 및 주변 과일 폭발력(AddExplosionForce) 부여
    /// - 점수(Score) 및 재화(Coin) 경제/상점 시스템 관리
    /// - 컴퓨터 부하 방지를 위한 60FPS 프레임 제한 및 GPU Instancing 적용
    /// - 씬에 오브젝트가 없을 경우 런타임에도 즉시 박스/스포너/UI 자동 구축 보장
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("11단계 과일 진화 리스트")]
        public List<FruitData> fruitEvolutionList = new List<FruitData>();

        [Header("물리 및 병합 폭발력 설정")]
#if UNITY_6000_0_OR_NEWER
        public PhysicsMaterial bouncyMaterial;
#else
        public PhysicMaterial bouncyMaterial;
#endif
        [Tooltip("병합 시 주변 과일을 들썩이게 하는 반경")]
        public float mergeExplosionRadius = 2.2f;

        [Tooltip("병합 시 주변 과일에 가하는 폭발력 세기")]
        public float mergeExplosionForce = 135f;

        [Tooltip("위쪽 방향으로 들썩이게 하는 보정값 (Upwards Modifier)")]
        public float mergeUpwardsModifier = 0.85f;

        [Header("재화(Coin) 경제 및 상점 설정")]
        [Tooltip("코인을 획득하기 시작하는 최소 병합 결과 단계 (기본 4: 5단계 '감' 이상부터 코인 획득)")]
        public int minLevelForCoinReward = 4;

        [Tooltip("상점 아이템 1: 가장 작은 과일 제거 비용")]
        public int removeSmallFruitCost = 5;

        [Tooltip("상점 아이템 2: 박스 흔들기(정리) 비용")]
        public int shakeBoxCost = 3;

        [Header("UI 참조")]
        public Text scoreText;
        public Text bestScoreText;
        public Text coinText;
        public Text nextFruitNameText;
        public Image nextFruitIconImage;
        public Text statusNoticeText;
        public GameObject gameOverPanel;
        public Text finalScoreText;

        private int currentScore = 0;
        private int bestScore = 0;
        private int currentCoins = 0;
        private bool isGameOver = false;

        // 런타임 머티리얼 캐시 (드로우콜 및 메모리 절약)
        private readonly Dictionary<int, Material> cachedFruitMaterials = new Dictionary<int, Material>();
        private Transform fruitsContainer;

        public bool IsGameOver => isGameOver;
        public int MaxFruitLevel => fruitEvolutionList.Count - 1;

        /// <summary>
        /// 플레이 버튼을 눌렀을 때 씬에 매니저가 없더라도 자동으로 전체 게임 환경을 구성합니다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrapOnPlay()
        {
            if (FindFirstObjectByType<GameManager>() == null)
            {
                GameObject gmObj = new GameObject("WatermelonGameManager");
                gmObj.AddComponent<GameManager>();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 컴퓨터 과부하/발열 방지를 위해 60프레임으로 제한
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;

            EnsureDefaultEvolutionList();
            EnsurePhysicsMaterial();

            GameObject containerObj = GameObject.Find("FruitsContainer");
            if (containerObj == null)
            {
                containerObj = new GameObject("FruitsContainer");
            }
            fruitsContainer = containerObj.transform;

            bestScore = PlayerPrefs.GetInt("Watermelon3D_BestScore", 0);
            currentCoins = PlayerPrefs.GetInt("Watermelon3D_Coins", 10); // 초기 테스트용 10코인 지급

            // 씬에 박스, 스포너, UI 등이 누락되어 있다면 즉시 자동 생성
            EnsureRuntimeSceneObjects();
        }

        private void Start()
        {
            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(false);
            }
            RefreshAllUI();
        }

        /// <summary>
        /// 11단계 과일 진화 데이터가 비어 있을 경우 기본 11단계(체리~수박)로 자동 초기화
        /// </summary>
        public void EnsureDefaultEvolutionList()
        {
            if (fruitEvolutionList != null && fruitEvolutionList.Count == 11) return;

            fruitEvolutionList = new List<FruitData>
            {
                new FruitData { levelIndex = 0,  fruitName = "1. 체리",     diameter = 0.48f, color = new Color(0.90f, 0.12f, 0.22f), mass = 0.6f, mergeScore = 2,    rewardCoins = 0 },
                new FruitData { levelIndex = 1,  fruitName = "2. 딸기",     diameter = 0.65f, color = new Color(1.00f, 0.32f, 0.42f), mass = 0.8f, mergeScore = 4,    rewardCoins = 0 },
                new FruitData { levelIndex = 2,  fruitName = "3. 포도",     diameter = 0.85f, color = new Color(0.58f, 0.22f, 0.85f), mass = 1.0f, mergeScore = 8,    rewardCoins = 0 },
                new FruitData { levelIndex = 3,  fruitName = "4. 한라봉",   diameter = 1.05f, color = new Color(1.00f, 0.65f, 0.10f), mass = 1.2f, mergeScore = 16,   rewardCoins = 0 },
                new FruitData { levelIndex = 4,  fruitName = "5. 감",       diameter = 1.30f, color = new Color(0.95f, 0.42f, 0.08f), mass = 1.5f, mergeScore = 32,   rewardCoins = 1 },
                new FruitData { levelIndex = 5,  fruitName = "6. 사과",     diameter = 1.58f, color = new Color(0.88f, 0.15f, 0.15f), mass = 1.8f, mergeScore = 64,   rewardCoins = 2 },
                new FruitData { levelIndex = 6,  fruitName = "7. 배",       diameter = 1.88f, color = new Color(0.88f, 0.90f, 0.28f), mass = 2.2f, mergeScore = 128,  rewardCoins = 3 },
                new FruitData { levelIndex = 7,  fruitName = "8. 복숭아",   diameter = 2.20f, color = new Color(1.00f, 0.72f, 0.78f), mass = 2.6f, mergeScore = 256,  rewardCoins = 5 },
                new FruitData { levelIndex = 8,  fruitName = "9. 파인애플", diameter = 2.55f, color = new Color(0.98f, 0.84f, 0.12f), mass = 3.2f, mergeScore = 512,  rewardCoins = 8 },
                new FruitData { levelIndex = 9,  fruitName = "10. 멜론",    diameter = 2.95f, color = new Color(0.52f, 0.88f, 0.40f), mass = 3.8f, mergeScore = 1024, rewardCoins = 15 },
                new FruitData { levelIndex = 10, fruitName = "11. 수박",    diameter = 3.40f, color = new Color(0.12f, 0.62f, 0.22f), mass = 4.6f, mergeScore = 2048, rewardCoins = 30 }
            };
        }

        private void EnsurePhysicsMaterial()
        {
            if (bouncyMaterial != null) return;

#if UNITY_6000_0_OR_NEWER
            bouncyMaterial = new PhysicsMaterial("FruitBouncyMaterial")
            {
                bounciness = 0.25f,
                dynamicFriction = 0.18f,
                staticFriction = 0.18f,
                bounceCombine = PhysicsMaterialCombine.Average,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };
#else
            bouncyMaterial = new PhysicMaterial("FruitBouncyMaterial")
            {
                bounciness = 0.25f,
                dynamicFriction = 0.18f,
                staticFriction = 0.18f,
                bounceCombine = PhysicMaterialCombine.Average,
                frictionCombine = PhysicMaterialCombine.Minimum
            };
#endif
        }

        private void EnsureRuntimeSceneObjects()
        {
            // 기본 샘플 Sphere가 남아있다면 제거
            GameObject defaultSphere = GameObject.Find("Sphere");
            if (defaultSphere != null && defaultSphere.GetComponent<Fruit>() == null)
            {
                Destroy(defaultSphere);
            }

            // 카메라 위치 및 배경색 보정
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, -0.15f, -11.2f);
                cam.transform.rotation = Quaternion.identity;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.96f, 0.93f, 0.86f);
                cam.fieldOfView = 48f;
            }

            // 3면 투명 상자 + 바닥 + 상단 LimitLine이 없으면 생성
            if (GameObject.Find("BoxContainer") == null)
            {
                BuildRuntimeBoxAndLimitLine();
            }

            // UI가 없으면 생성
            if (scoreText == null && GameObject.Find("WatermelonCanvas") == null)
            {
                BuildRuntimeUI();
            }

            // 과일 스포너가 없으면 생성
            if (FindFirstObjectByType<FruitSpawner>() == null)
            {
                BuildRuntimeSpawner();
            }
        }

        private void BuildRuntimeBoxAndLimitLine()
        {
            GameObject boxRoot = new GameObject("BoxContainer");

            Material wallGlassMat = CreateRuntimeMaterial(new Color(0.55f, 0.72f, 0.85f, 0.28f), 0.8f, true);
            Material floorWoodMat = CreateRuntimeMaterial(new Color(0.76f, 0.56f, 0.36f, 1f), 0.3f, false);
            Material backBoardMat = CreateRuntimeMaterial(new Color(0.90f, 0.85f, 0.76f, 1f), 0.1f, false);
            Material limitLineMat = CreateRuntimeMaterial(new Color(1f, 0.22f, 0.22f, 0.35f), 0.2f, true);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "BottomWall_Floor";
            floor.transform.SetParent(boxRoot.transform);
            floor.transform.position = new Vector3(0f, -4.05f, 0f);
            floor.transform.localScale = new Vector3(6.6f, 0.45f, 3.6f);
            floor.GetComponent<Renderer>().sharedMaterial = floorWoodMat;
            floor.GetComponent<BoxCollider>().sharedMaterial = bouncyMaterial;

            GameObject leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftWall.name = "LeftWall";
            leftWall.transform.SetParent(boxRoot.transform);
            leftWall.transform.position = new Vector3(-3.12f, -0.35f, 0f);
            leftWall.transform.localScale = new Vector3(0.35f, 7.6f, 3.6f);
            leftWall.GetComponent<Renderer>().sharedMaterial = wallGlassMat;
            leftWall.GetComponent<BoxCollider>().sharedMaterial = bouncyMaterial;

            GameObject rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightWall.name = "RightWall";
            rightWall.transform.SetParent(boxRoot.transform);
            rightWall.transform.position = new Vector3(3.12f, -0.35f, 0f);
            rightWall.transform.localScale = new Vector3(0.35f, 7.6f, 3.6f);
            rightWall.GetComponent<Renderer>().sharedMaterial = wallGlassMat;
            rightWall.GetComponent<BoxCollider>().sharedMaterial = bouncyMaterial;

            GameObject backBoard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backBoard.name = "BackVisualPanel";
            backBoard.transform.SetParent(boxRoot.transform);
            backBoard.transform.position = new Vector3(0f, -0.35f, 1.85f);
            backBoard.transform.localScale = new Vector3(6.6f, 7.6f, 0.1f);
            backBoard.GetComponent<Renderer>().sharedMaterial = backBoardMat;
            Destroy(backBoard.GetComponent<BoxCollider>());

            GameObject limitObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            limitObj.name = "LimitLine";
            limitObj.transform.SetParent(boxRoot.transform);
            limitObj.transform.position = new Vector3(0f, 2.85f, 0f);
            limitObj.transform.localScale = new Vector3(5.88f, 0.12f, 2.5f);
            Renderer limitRend = limitObj.GetComponent<Renderer>();
            limitRend.sharedMaterial = limitLineMat;

            BoxCollider limitCol = limitObj.GetComponent<BoxCollider>();
            limitCol.isTrigger = true;
            limitCol.size = new Vector3(1f, 8f, 1f);
            limitCol.center = new Vector3(0f, 3.5f, 0f);

            LimitLine limitScript = limitObj.AddComponent<LimitLine>();
            limitScript.requiredStayTime = 2.0f;
            limitScript.warningLineRenderer = limitRend;
        }

        private void BuildRuntimeSpawner()
        {
            GameObject spawnerObj = new GameObject("FruitSpawner");
            spawnerObj.transform.position = new Vector3(0f, 3.85f, 0f);

            LineRenderer lr = spawnerObj.AddComponent<LineRenderer>();
            lr.sharedMaterial = CreateRuntimeMaterial(new Color(1f, 1f, 1f, 0.65f), 0.1f, true);
            lr.startColor = new Color(1f, 1f, 1f, 0.75f);
            lr.endColor = new Color(1f, 1f, 1f, 0.25f);

            FruitSpawner spawner = spawnerObj.AddComponent<FruitSpawner>();
            spawner.spawnY = 3.85f;
            spawner.boxHalfWidth = 2.92f;
            spawner.spawnCooldown = 0.50f;
            spawner.maxSpawnLevel = 4;
            spawner.floorY = -3.82f;
        }

        private void BuildRuntimeUI()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                esObj.AddComponent<InputSystemUIInputModule>();
#else
                esObj.AddComponent<StandaloneInputModule>();
#endif
            }

            GameObject canvasObj = new GameObject("WatermelonCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject leftPanel = CreateUiPanel(canvasObj.transform, "LeftHudPanel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(360f, 320f),
                new Color(0.18f, 0.15f, 0.12f, 0.82f));

            scoreText = CreateUiText(leftPanel.transform, "ScoreText", "점수: 0", 32, TextAnchor.MiddleLeft,
                new Vector2(20f, -18f), new Vector2(320f, 44f), Color.white, defaultFont);

            bestScoreText = CreateUiText(leftPanel.transform, "BestScoreText", "최고: 0", 24, TextAnchor.MiddleLeft,
                new Vector2(20f, -64f), new Vector2(320f, 36f), new Color(0.95f, 0.85f, 0.45f), defaultFont);

            coinText = CreateUiText(leftPanel.transform, "CoinText", "🪙 코인: 10", 26, TextAnchor.MiddleLeft,
                new Vector2(20f, -106f), new Vector2(320f, 38f), new Color(1f, 0.90f, 0.25f), defaultFont);

            Button removeBtn = CreateUiButton(leftPanel.transform, "ShopRemoveBtn",
                "🛒 작은 과일 제거 (5 코인)", 20,
                new Vector2(20f, -158f), new Vector2(320f, 58f),
                new Color(0.88f, 0.36f, 0.25f, 1f), defaultFont);
            removeBtn.onClick.AddListener(UseCoinRemoveSmallestFruit);

            Button shakeBtn = CreateUiButton(leftPanel.transform, "ShopShakeBtn",
                "🛒 박스 흔들기 (3 코인)", 20,
                new Vector2(20f, -228f), new Vector2(320f, 58f),
                new Color(0.25f, 0.62f, 0.85f, 1f), defaultFont);
            shakeBtn.onClick.AddListener(UseCoinShakeBox);

            GameObject rightPanel = CreateUiPanel(canvasObj.transform, "NextFruitPanel",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(260f, 220f),
                new Color(0.18f, 0.15f, 0.12f, 0.82f));
            rightPanel.GetComponent<RectTransform>().pivot = new Vector2(1f, 1f);

            CreateUiText(rightPanel.transform, "NextTitle", "NEXT (다음 과일)", 22, TextAnchor.MiddleCenter,
                new Vector2(10f, -14f), new Vector2(240f, 34f), new Color(0.95f, 0.90f, 0.75f), defaultFont);

            GameObject iconObj = new GameObject("NextFruitIcon");
            iconObj.transform.SetParent(rightPanel.transform, false);
            RectTransform iconRt = iconObj.AddComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = new Vector2(0f, -2f);
            iconRt.sizeDelta = new Vector2(82f, 82f);
            nextFruitIconImage = iconObj.AddComponent<Image>();
            nextFruitIconImage.color = fruitEvolutionList[0].color;

            nextFruitNameText = CreateUiText(rightPanel.transform, "NextFruitName", "1. 체리", 24, TextAnchor.MiddleCenter,
                new Vector2(10f, -162f), new Vector2(240f, 40f), Color.white, defaultFont);

            statusNoticeText = CreateUiText(canvasObj.transform, "StatusNoticeText",
                "마우스로 조준하고 클릭하여 과일을 떨어뜨리세요! (5단계 '감' 이상 합체 시 코인 획득)", 22, TextAnchor.MiddleCenter,
                new Vector2(0f, -18f), new Vector2(960f, 42f), new Color(0.22f, 0.18f, 0.12f), defaultFont);
            RectTransform noticeRt = statusNoticeText.GetComponent<RectTransform>();
            noticeRt.anchorMin = new Vector2(0.5f, 1f);
            noticeRt.anchorMax = new Vector2(0.5f, 1f);
            noticeRt.pivot = new Vector2(0.5f, 1f);

            GameObject bottomPanel = CreateUiPanel(canvasObj.transform, "EvolutionBottomBar",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(1360f, 86f),
                new Color(0.18f, 0.15f, 0.12f, 0.86f));
            bottomPanel.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0f);

            float startX = 22f;
            float stepX = 121f;
            for (int i = 0; i < fruitEvolutionList.Count; i++)
            {
                FruitData f = fruitEvolutionList[i];
                GameObject badge = new GameObject($"Evo_{i}");
                badge.transform.SetParent(bottomPanel.transform, false);
                RectTransform bRt = badge.AddComponent<RectTransform>();
                bRt.anchorMin = new Vector2(0f, 0.5f);
                bRt.anchorMax = new Vector2(0f, 0.5f);
                bRt.pivot = new Vector2(0f, 0.5f);
                bRt.anchoredPosition = new Vector2(startX + i * stepX, 10f);
                bRt.sizeDelta = new Vector2(36f, 36f);
                Image bImg = badge.AddComponent<Image>();
                bImg.color = f.color;

                string arrow = (i < fruitEvolutionList.Count - 1) ? " →" : "";
                CreateUiText(bottomPanel.transform, $"EvoLabel_{i}", $"{f.fruitName}{arrow}", 16, TextAnchor.MiddleLeft,
                    new Vector2(startX - 6f + i * stepX, -52f), new Vector2(118f, 28f), Color.white, defaultFont);
            }

            GameObject goPanel = CreateUiPanel(canvasObj.transform, "GameOverPanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 360f),
                new Color(0.10f, 0.08f, 0.08f, 0.94f));
            goPanel.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 0.5f);

            finalScoreText = CreateUiText(goPanel.transform, "FinalScoreText",
                "게임 오버!\n최종 점수: 0점", 32, TextAnchor.MiddleCenter,
                new Vector2(30f, -35f), new Vector2(500f, 170f), Color.white, defaultFont);

            Button restartBtn = CreateUiButton(goPanel.transform, "RestartButton",
                "🔄 다시 시작하기", 26,
                new Vector2(130f, -245f), new Vector2(300f, 72f),
                new Color(0.22f, 0.72f, 0.32f, 1f), defaultFont);
            restartBtn.onClick.AddListener(RestartGame);

            goPanel.SetActive(false);
            gameOverPanel = goPanel;
        }

        private static Material CreateRuntimeMaterial(Color color, float glossiness, bool transparent)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            Material mat = new Material(shader);
            mat.color = color;
            mat.SetFloat("_Glossiness", glossiness);
            mat.enableInstancing = true;

            if (transparent)
            {
                mat.SetFloat("_Mode", 3);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
            return mat;
        }

        private static GameObject CreateUiPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color bgColor)
        {
            GameObject panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            Image img = panel.AddComponent<Image>();
            img.color = bgColor;
            return panel;
        }

        private static Text CreateUiText(Transform parent, string name, string content, int fontSize, TextAnchor align, Vector2 anchoredPos, Vector2 size, Color color, Font font)
        {
            GameObject txtObj = new GameObject(name);
            txtObj.transform.SetParent(parent, false);
            RectTransform rt = txtObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            Text txt = txtObj.AddComponent<Text>();
            txt.font = font;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.alignment = align;
            txt.color = color;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
        }

        private static Button CreateUiButton(Transform parent, string name, string label, int fontSize, Vector2 anchoredPos, Vector2 size, Color bgColor, Font font)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            RectTransform rt = btnObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            Image img = btnObj.AddComponent<Image>();
            img.color = bgColor;
            Button btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;
            Text txt = CreateUiText(btnObj.transform, "Label", label, fontSize, TextAnchor.MiddleCenter,
                Vector2.zero, size, Color.white, font);
            txt.raycastTarget = false;
            return btn;
        }

        public float GetFruitDiameter(int levelIndex)
        {
            int idx = Mathf.Clamp(levelIndex, 0, fruitEvolutionList.Count - 1);
            return fruitEvolutionList[idx].diameter;
        }

        /// <summary>
        /// 지정한 단계와 위치에 3D 과일 객체를 생성합니다.
        /// </summary>
        public Fruit CreateFruitInstance(int levelIndex, Vector3 position, bool isMerged)
        {
            int idx = Mathf.Clamp(levelIndex, 0, fruitEvolutionList.Count - 1);
            FruitData data = fruitEvolutionList[idx];

            GameObject fruitObj;
            if (data.prefab != null)
            {
                fruitObj = Instantiate(data.prefab, position, Quaternion.identity, fruitsContainer);
            }
            else
            {
                fruitObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fruitObj.transform.SetParent(fruitsContainer);
                fruitObj.transform.position = position;
            }

            fruitObj.name = $"Fruit_{idx}_{data.fruitName}";
            Vector3 targetScale = Vector3.one * data.diameter;
            fruitObj.transform.localScale = targetScale;

            Renderer rend = fruitObj.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = GetOrCreateFruitMaterial(idx, data.color);
            }

            SphereCollider col = fruitObj.GetComponent<SphereCollider>();
            if (col == null) col = fruitObj.AddComponent<SphereCollider>();
            col.sharedMaterial = bouncyMaterial;

            Rigidbody rb = fruitObj.GetComponent<Rigidbody>();
            if (rb == null) rb = fruitObj.AddComponent<Rigidbody>();
            rb.mass = data.mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.08f;

            Fruit fruit = fruitObj.GetComponent<Fruit>();
            if (fruit == null) fruit = fruitObj.AddComponent<Fruit>();
            fruit.levelIndex = idx;

            if (isMerged)
            {
                fruit.ActivateAsMergedFruit(targetScale);
            }

            return fruit;
        }

        private Material GetOrCreateFruitMaterial(int levelIndex, Color color)
        {
            if (cachedFruitMaterials.TryGetValue(levelIndex, out Material mat) && mat != null)
            {
                return mat;
            }

            mat = CreateRuntimeMaterial(color, 0.65f, false);
            cachedFruitMaterials[levelIndex] = mat;
            return mat;
        }

        /// <summary>
        /// 두 과일이 충돌하여 병합될 때 호출됩니다.
        /// 1. 충돌 중심점(mergeCenter, Vector3.Lerp)에 한 단계 큰 과일 생성
        /// 2. 주변 과일들에 위쪽 방향 폭발력(AddExplosionForce) 적용
        /// 3. 점수 및 재화(Coin) 획득 반영
        /// </summary>
        public void MergeFruits(Vector3 mergeCenter, int newLevelIndex)
        {
            if (isGameOver) return;

            int idx = Mathf.Clamp(newLevelIndex, 0, fruitEvolutionList.Count - 1);
            FruitData data = fruitEvolutionList[idx];

            Fruit mergedFruit = CreateFruitInstance(idx, mergeCenter, isMerged: true);

            if (mergedFruit != null && mergedFruit.Rb != null)
            {
                mergedFruit.Rb.AddForce(Vector3.up * 2.2f, ForceMode.Impulse);
            }

            ApplyMergePopImpulse(mergeCenter, mergedFruit);
            AddScore(data.mergeScore);

            if (idx >= minLevelForCoinReward && data.rewardCoins > 0)
            {
                AddCoins(data.rewardCoins);
                ShowNotice($"🎉 {data.fruitName} 합체 성공! +{data.mergeScore}점 / +{data.rewardCoins} 코인 획득!");
            }
            else
            {
                ShowNotice($"{data.fruitName} 합체! (+{data.mergeScore}점)");
            }
        }

        private void ApplyMergePopImpulse(Vector3 center, Fruit mergedFruit)
        {
            Collider[] nearby = Physics.OverlapSphere(center, mergeExplosionRadius);
            for (int i = 0; i < nearby.Length; i++)
            {
                Fruit otherFruit = nearby[i].GetComponent<Fruit>();
                if (otherFruit == null || otherFruit == mergedFruit || !otherFruit.isDropped) continue;

                if (otherFruit.Rb != null)
                {
                    otherFruit.Rb.AddExplosionForce(
                        mergeExplosionForce,
                        center,
                        mergeExplosionRadius,
                        mergeUpwardsModifier,
                        ForceMode.Impulse);
                }
            }
        }

        public void AddScore(int amount)
        {
            currentScore += amount;
            if (currentScore > bestScore)
            {
                bestScore = currentScore;
                PlayerPrefs.SetInt("Watermelon3D_BestScore", bestScore);
            }
            RefreshAllUI();
        }

        public void AddCoins(int amount)
        {
            currentCoins += amount;
            PlayerPrefs.SetInt("Watermelon3D_Coins", currentCoins);
            RefreshAllUI();
        }

        /// <summary>
        /// [상점 기능 1] 코인을 소비하여 상자 안의 가장 작은 과일 1개를 제거합니다.
        /// </summary>
        public void UseCoinRemoveSmallestFruit()
        {
            if (isGameOver) return;

            if (currentCoins < removeSmallFruitCost)
            {
                ShowNotice($"코인이 부족합니다! (필요: {removeSmallFruitCost} 코인)");
                return;
            }

            if (fruitsContainer == null) return;

            Fruit[] allFruits = fruitsContainer.GetComponentsInChildren<Fruit>();
            Fruit smallest = null;
            for (int i = 0; i < allFruits.Length; i++)
            {
                if (!allFruits[i].isDropped) continue;
                if (smallest == null || allFruits[i].levelIndex < smallest.levelIndex)
                {
                    smallest = allFruits[i];
                }
            }

            if (smallest == null)
            {
                ShowNotice("제거할 과일이 박스에 없습니다.");
                return;
            }

            AddCoins(-removeSmallFruitCost);
            string removedName = fruitEvolutionList[smallest.levelIndex].fruitName;
            Destroy(smallest.gameObject);
            ShowNotice($"🛒 [상점] {removedName} 제거 완료! (-{removeSmallFruitCost} 코인)");
        }

        /// <summary>
        /// [상점 기능 2] 코인을 소비하여 박스 안의 과일들을 위/좌우로 흔들어 틈새를 정리합니다.
        /// </summary>
        public void UseCoinShakeBox()
        {
            if (isGameOver) return;

            if (currentCoins < shakeBoxCost)
            {
                ShowNotice($"코인이 부족합니다! (필요: {shakeBoxCost} 코인)");
                return;
            }

            if (fruitsContainer == null) return;

            Fruit[] allFruits = fruitsContainer.GetComponentsInChildren<Fruit>();
            int count = 0;
            for (int i = 0; i < allFruits.Length; i++)
            {
                if (!allFruits[i].isDropped || allFruits[i].Rb == null) continue;
                Vector3 impulse = new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(3.2f, 5.0f), 0f);
                allFruits[i].Rb.AddForce(impulse, ForceMode.Impulse);
                count++;
            }

            if (count == 0)
            {
                ShowNotice("흔들 과일이 박스에 없습니다.");
                return;
            }

            AddCoins(-shakeBoxCost);
            ShowNotice($"🛒 [상점] 박스 흔들기 발동! (-{shakeBoxCost} 코인)");
        }

        public void UpdateNextFruitUI(int nextLevelIndex)
        {
            int idx = Mathf.Clamp(nextLevelIndex, 0, fruitEvolutionList.Count - 1);
            FruitData data = fruitEvolutionList[idx];

            if (nextFruitNameText != null)
            {
                nextFruitNameText.text = data.fruitName;
            }
            if (nextFruitIconImage != null)
            {
                nextFruitIconImage.color = data.color;
            }
        }

        private void ShowNotice(string message)
        {
            if (statusNoticeText != null)
            {
                statusNoticeText.text = message;
            }
        }

        private void RefreshAllUI()
        {
            if (scoreText != null) scoreText.text = $"점수: {currentScore:N0}";
            if (bestScoreText != null) bestScoreText.text = $"최고: {bestScore:N0}";
            if (coinText != null) coinText.text = $"🪙 코인: {currentCoins:N0}";
        }

        public void TriggerGameOver()
        {
            if (isGameOver) return;
            isGameOver = true;

            PlayerPrefs.Save();

            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(true);
            }
            if (finalScoreText != null)
            {
                finalScoreText.text = $"게임 오버!\n최종 점수: {currentScore:N0}점\n보유 코인: {currentCoins:N0}개";
            }
            ShowNotice("⚠️ 상단 한계선을 2초 이상 넘어 게임 오버되었습니다.");
        }

        public void RestartGame()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
