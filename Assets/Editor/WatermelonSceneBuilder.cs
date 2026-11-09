#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Watermelon3D.Editor
{
    /// <summary>
    /// 유니티 에디터 로드 시 자동으로:
    /// 1) Unity MCP 서버 및 Antigravity 설정/브릿지 자동 연결
    /// 2) 3D 수박(머지) 게임의 11단계 과일 프리팹, Physics Material, 투명 3면 박스+바닥,
    ///    상단 Limit Line, 스포너(LineRenderer), 전체 UI(Next 과일, 점수, 코인 상점, 하단 11단계 진화표)를
    ///    현재 씬에 원클릭/자동 구성합니다.
    /// </summary>
    [InitializeOnLoad]
    public static class WatermelonSceneBuilder
    {
        private const string AutoSetupSessionKey = "Watermelon3D_AutoSetupDone";

        static WatermelonSceneBuilder()
        {
            EditorApplication.delayCall += OnEditorReady;
        }

        private static void OnEditorReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += OnEditorReady;
                return;
            }

            // 1. MCP 자동 설정 및 브릿지 시작
            TryAutoConfigureAndStartMcp();

            // 2. 현재 씬에 아직 수박 게임이 세팅되지 않았다면 자동으로 씬 구성
            if (!SessionState.GetBool(AutoSetupSessionKey, false))
            {
                SessionState.SetBool(AutoSetupSessionKey, true);
                if (UnityEngine.Object.FindFirstObjectByType<GameManager>() == null)
                {
                    BuildWatermelonScene();
                }
            }
        }

        [MenuItem("Tools/3D Watermelon Game/1. 씬 & 프리팹 자동 세팅 실행")]
        public static void BuildWatermelonSceneMenu()
        {
            BuildWatermelonScene();
        }

        [MenuItem("Tools/3D Watermelon Game/2. Unity MCP 자동 연결 재시도")]
        public static void RetryMcpConnectionMenu()
        {
            TryAutoConfigureAndStartMcp();
        }

        private static async void TryAutoConfigureAndStartMcp()
        {
            try
            {
                EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", true);
                EditorPrefs.SetBool("MCPForUnity.HttpServerLaunchConfirmed", true);
                EditorPrefs.SetBool("MCPForUnity.SetupCompleted", true);

                // 감지된 모든 클라이언트(Antigravity 포함) 자동 설정
                MCPServiceLocator.Client.ConfigureAllDetectedClients();

                // ~/.gemini/config/mcp_config.json 설정을 ~/.gemini/antigravity/mcp_config.json 에도 동기화
                SyncAntigravityMcpConfig();

                // 로컬 HTTP 서버 및 브릿지 시작
                if (!MCPServiceLocator.Server.IsLocalHttpServerReachable())
                {
                    MCPServiceLocator.Server.StartLocalHttpServer(quiet: true);
                }

                if (!MCPServiceLocator.Bridge.IsRunning)
                {
                    await MCPServiceLocator.Bridge.StartAsync();
                }

                Debug.Log("[Watermelon3D] Unity MCP 자동 설정 및 브릿지 시작 요청이 완료되었습니다.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Watermelon3D] MCP 자동 연결 중 경미한 예외 (무시 가능): {ex.Message}");
            }
        }

        private static void SyncAntigravityMcpConfig()
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string configPathNew = Path.Combine(home, ".gemini", "config", "mcp_config.json");
                string configPathLegacy = Path.Combine(home, ".gemini", "antigravity", "mcp_config.json");

                if (File.Exists(configPathNew) && Directory.Exists(Path.GetDirectoryName(configPathLegacy)))
                {
                    string jsonContent = File.ReadAllText(configPathNew);
                    File.WriteAllText(configPathLegacy, jsonContent);
                }
            }
            catch
            {
                // 파일 접근 제한 시 무시
            }
        }

        public static void BuildWatermelonScene()
        {
            if (Application.isPlaying) return;

            Debug.Log("[Watermelon3D] 3D 수박 게임 씬 및 프리팹 자동 구성을 시작합니다...");

            // 폴더 생성
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Prefabs");

            // 기존 기본 Sphere 제거 및 이전 생성 오브젝트 정리
            CleanupExistingObjects();

            // 카메라 세팅
            SetupCameraAndLight();

            // Physics Material 생성 (통통 튀는 탄성 설정)
#if UNITY_6000_0_OR_NEWER
            PhysicsMaterial bouncyMat = CreateOrLoadPhysicsMaterial();
#else
            PhysicMaterial bouncyMat = CreateOrLoadPhysicsMaterial();
#endif

            // GameManager 생성 및 기본 11단계 과일 데이터 초기화
            GameObject gmObj = new GameObject("WatermelonGameManager");
            GameManager gm = gmObj.AddComponent<GameManager>();
            gm.EnsureDefaultEvolutionList();
            gm.bouncyMaterial = bouncyMat;

            // 11단계 과일 프리팹 및 머티리얼 생성
            CreateFruitPrefabs(gm, bouncyMat);

            // 투명 상자 벽(BoxCollider 3면 + 바닥 + 뒷면 시각 가이드) 및 상단 LimitLine 생성
            CreateBoxAndLimitLine(bouncyMat);

            // 상단 과일 스포너 + 수직 가이드라인(LineRenderer) 생성
            CreateFruitSpawner();

            // 전체 UI (점수, 코인, 상점 스킬, 우측 상단 Next 과일, 하단 11단계 진화 순서, 게임오버 창) 생성
            CreateGameUI(gm);

            // 씬 저장
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("✅ [Watermelon3D] 3D 수박 게임 세팅 완료! 상단 ▶(Play) 버튼을 누르면 바로 플레이할 수 있습니다.");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
                string name = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
                {
                    AssetDatabase.CreateFolder(parent, name);
                }
            }
        }

        private static void CleanupExistingObjects()
        {
            string[] namesToClean =
            {
                "Sphere",
                "WatermelonGameManager",
                "BoxContainer",
                "FruitSpawner",
                "FruitsContainer",
                "WatermelonCanvas",
                "EventSystem"
            };

            for (int i = 0; i < namesToClean.Length; i++)
            {
                GameObject obj = GameObject.Find(namesToClean[i]);
                while (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                    obj = GameObject.Find(namesToClean[i]);
                }
            }
        }

        private static void SetupCameraAndLight()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, -0.15f, -11.2f);
                cam.transform.rotation = Quaternion.identity;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.96f, 0.93f, 0.86f); // 따뜻한 크림색 배경 (눈의 피로 및 부하 감소)
                cam.fieldOfView = 48f;
            }

            Light dirLight = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (dirLight != null)
            {
                dirLight.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
                dirLight.intensity = 1.1f;
                // 컴퓨터 부하를 줄이기 위해 그림자 연산을 Soft 대신 가벼운 설정 또는 Off 가능
                dirLight.shadows = LightShadows.Hard;
            }
        }

#if UNITY_6000_0_OR_NEWER
        private static PhysicsMaterial CreateOrLoadPhysicsMaterial()
        {
            const string assetPath = "Assets/Materials/FruitBouncy.physicMaterial";
            PhysicsMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(assetPath);
            if (mat == null)
            {
                mat = new PhysicsMaterial("FruitBouncy")
                {
                    bounciness = 0.25f,
                    dynamicFriction = 0.18f,
                    staticFriction = 0.18f,
                    bounceCombine = PhysicsMaterialCombine.Average,
                    frictionCombine = PhysicsMaterialCombine.Minimum
                };
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            return mat;
        }
#else
        private static PhysicMaterial CreateOrLoadPhysicsMaterial()
        {
            const string assetPath = "Assets/Materials/FruitBouncy.physicMaterial";
            PhysicMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(assetPath);
            if (mat == null)
            {
                mat = new PhysicMaterial("FruitBouncy")
                {
                    bounciness = 0.25f,
                    dynamicFriction = 0.18f,
                    staticFriction = 0.18f,
                    bounceCombine = PhysicMaterialCombine.Average,
                    frictionCombine = PhysicMaterialCombine.Minimum
                };
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            return mat;
        }
#endif

        private static Material CreateOrLoadColorMaterial(string matName, Color color, float glossiness = 0.6f, bool transparent = false)
        {
            string assetPath = $"Assets/Materials/{matName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, assetPath);
            }

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

            EditorUtility.SetDirty(mat);
            return mat;
        }

#if UNITY_6000_0_OR_NEWER
        private static void CreateFruitPrefabs(GameManager gm, PhysicsMaterial bouncyMat)
#else
        private static void CreateFruitPrefabs(GameManager gm, PhysicMaterial bouncyMat)
#endif
        {
            for (int i = 0; i < gm.fruitEvolutionList.Count; i++)
            {
                FruitData data = gm.fruitEvolutionList[i];
                Material fruitMat = CreateOrLoadColorMaterial($"FruitMat_{i:D2}", data.color, 0.65f, false);

                string prefabPath = $"Assets/Prefabs/Fruit_{i:D2}.prefab";
                GameObject tempObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                tempObj.name = $"Fruit_{i:D2}_{data.fruitName}";
                tempObj.transform.localScale = Vector3.one * data.diameter;

                Renderer rend = tempObj.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = fruitMat;

                SphereCollider col = tempObj.GetComponent<SphereCollider>();
                col.sharedMaterial = bouncyMat;

                Rigidbody rb = tempObj.AddComponent<Rigidbody>();
                rb.mass = data.mass;
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.08f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                // 2D 평면(X, Y축) 물리 이동 고정 및 X/Y 회전 고정
                rb.constraints = RigidbodyConstraints.FreezePositionZ
                               | RigidbodyConstraints.FreezeRotationX
                               | RigidbodyConstraints.FreezeRotationY;

                Fruit fruitComp = tempObj.AddComponent<Fruit>();
                fruitComp.levelIndex = i;

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempObj, prefabPath);
                UnityEngine.Object.DestroyImmediate(tempObj);

                data.prefab = savedPrefab;
            }
        }

#if UNITY_6000_0_OR_NEWER
        private static void CreateBoxAndLimitLine(PhysicsMaterial bouncyMat)
#else
        private static void CreateBoxAndLimitLine(PhysicMaterial bouncyMat)
#endif
        {
            GameObject boxRoot = new GameObject("BoxContainer");

            Material wallGlassMat = CreateOrLoadColorMaterial("BoxGlassWall", new Color(0.55f, 0.72f, 0.85f, 0.28f), 0.8f, true);
            Material floorWoodMat = CreateOrLoadColorMaterial("BoxFloorWood", new Color(0.76f, 0.56f, 0.36f, 1f), 0.3f, false);
            Material backBoardMat = CreateOrLoadColorMaterial("BoxBackBoard", new Color(0.90f, 0.85f, 0.76f, 1f), 0.1f, false);
            Material limitLineMat = CreateOrLoadColorMaterial("LimitLineMat", new Color(1f, 0.22f, 0.22f, 0.35f), 0.2f, true);

            // 1. 바닥 (Floor) - BoxCollider
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "BottomWall_Floor";
            floor.transform.SetParent(boxRoot.transform);
            floor.transform.position = new Vector3(0f, -4.05f, 0f);
            floor.transform.localScale = new Vector3(6.6f, 0.45f, 3.6f);
            floor.GetComponent<Renderer>().sharedMaterial = floorWoodMat;
            floor.GetComponent<BoxCollider>().sharedMaterial = bouncyMat;

            // 2. 좌측 벽 (LeftWall) - BoxCollider
            GameObject leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftWall.name = "LeftWall";
            leftWall.transform.SetParent(boxRoot.transform);
            leftWall.transform.position = new Vector3(-3.12f, -0.35f, 0f);
            leftWall.transform.localScale = new Vector3(0.35f, 7.6f, 3.6f);
            leftWall.GetComponent<Renderer>().sharedMaterial = wallGlassMat;
            leftWall.GetComponent<BoxCollider>().sharedMaterial = bouncyMat;

            // 3. 우측 벽 (RightWall) - BoxCollider
            GameObject rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightWall.name = "RightWall";
            rightWall.transform.SetParent(boxRoot.transform);
            rightWall.transform.position = new Vector3(3.12f, -0.35f, 0f);
            rightWall.transform.localScale = new Vector3(0.35f, 7.6f, 3.6f);
            rightWall.GetComponent<Renderer>().sharedMaterial = wallGlassMat;
            rightWall.GetComponent<BoxCollider>().sharedMaterial = bouncyMat;

            // 4. 뒷면 시각 배경판 (콜라이더 제거하여 마우스 Raycast 방해 방지)
            GameObject backBoard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backBoard.name = "BackVisualPanel";
            backBoard.transform.SetParent(boxRoot.transform);
            backBoard.transform.position = new Vector3(0f, -0.35f, 1.85f);
            backBoard.transform.localScale = new Vector3(6.6f, 7.6f, 0.1f);
            backBoard.GetComponent<Renderer>().sharedMaterial = backBoardMat;
            UnityEngine.Object.DestroyImmediate(backBoard.GetComponent<BoxCollider>());

            // 5. 상단 한계선 (Limit Line) - BoxCollider (isTrigger = true), 2초 이상 침범 시 게임오버
            GameObject limitObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            limitObj.name = "LimitLine";
            limitObj.transform.SetParent(boxRoot.transform);
            limitObj.transform.position = new Vector3(0f, 2.85f, 0f);
            limitObj.transform.localScale = new Vector3(5.88f, 0.12f, 2.5f);
            Renderer limitRend = limitObj.GetComponent<Renderer>();
            limitRend.sharedMaterial = limitLineMat;

            BoxCollider limitCol = limitObj.GetComponent<BoxCollider>();
            limitCol.isTrigger = true;
            // 상단 한계선 위로 쌓인 과일도 놓치지 않도록 트리거 높이를 위쪽으로 여유 있게 확장
            limitCol.size = new Vector3(1f, 8f, 1f);
            limitCol.center = new Vector3(0f, 3.5f, 0f);

            LimitLine limitScript = limitObj.AddComponent<LimitLine>();
            limitScript.requiredStayTime = 2.0f;
            limitScript.warningLineRenderer = limitRend;
        }

        private static void CreateFruitSpawner()
        {
            GameObject spawnerObj = new GameObject("FruitSpawner");
            spawnerObj.transform.position = new Vector3(0f, 3.85f, 0f);

            LineRenderer lr = spawnerObj.AddComponent<LineRenderer>();
            Material guideMat = CreateOrLoadColorMaterial("GuideLineMat", new Color(1f, 1f, 1f, 0.65f), 0.1f, true);
            lr.sharedMaterial = guideMat;
            lr.startColor = new Color(1f, 1f, 1f, 0.75f);
            lr.endColor = new Color(1f, 1f, 1f, 0.25f);

            FruitSpawner spawner = spawnerObj.AddComponent<FruitSpawner>();
            spawner.spawnY = 3.85f;
            spawner.boxHalfWidth = 2.92f;
            spawner.spawnCooldown = 0.50f;
            spawner.maxSpawnLevel = 4;
            spawner.floorY = -3.82f;
        }

        private static void CreateGameUI(GameManager gm)
        {
            // EventSystem 생성 (신규 Input System 호환)
            GameObject esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            esObj.AddComponent<InputSystemUIInputModule>();
#else
            esObj.AddComponent<StandaloneInputModule>();
#endif

            // Canvas 생성
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

            // 1. 좌측 상단: 점수 / 최고점수 / 코인 & 상점 패널
            GameObject leftPanel = CreatePanel(canvasObj.transform, "LeftHudPanel",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f), new Vector2(360f, 320f),
                new Color(0.18f, 0.15f, 0.12f, 0.82f));

            gm.scoreText = CreateText(leftPanel.transform, "ScoreText", "점수: 0", 32, TextAnchor.MiddleLeft,
                new Vector2(20f, -18f), new Vector2(320f, 44f), Color.white, defaultFont);

            gm.bestScoreText = CreateText(leftPanel.transform, "BestScoreText", "최고: 0", 24, TextAnchor.MiddleLeft,
                new Vector2(20f, -64f), new Vector2(320f, 36f), new Color(0.95f, 0.85f, 0.45f), defaultFont);

            gm.coinText = CreateText(leftPanel.transform, "CoinText", "🪙 코인: 10", 26, TextAnchor.MiddleLeft,
                new Vector2(20f, -106f), new Vector2(320f, 38f), new Color(1f, 0.90f, 0.25f), defaultFont);

            // 상점 버튼 1: 작은 과일 제거
            Button removeBtn = CreateButton(leftPanel.transform, "ShopRemoveBtn",
                "🛒 작은 과일 제거 (5 코인)", 20,
                new Vector2(20f, -158f), new Vector2(320f, 58f),
                new Color(0.88f, 0.36f, 0.25f, 1f), defaultFont);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(removeBtn.onClick, gm.UseCoinRemoveSmallestFruit);

            // 상점 버튼 2: 박스 흔들기
            Button shakeBtn = CreateButton(leftPanel.transform, "ShopShakeBtn",
                "🛒 박스 흔들기 (3 코인)", 20,
                new Vector2(20f, -228f), new Vector2(320f, 58f),
                new Color(0.25f, 0.62f, 0.85f, 1f), defaultFont);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(shakeBtn.onClick, gm.UseCoinShakeBox);

            // 2. 우측 상단: 다음 과일(NEXT) 미리보기 패널
            GameObject rightPanel = CreatePanel(canvasObj.transform, "NextFruitPanel",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -20f), new Vector2(260f, 220f),
                new Color(0.18f, 0.15f, 0.12f, 0.82f));
            RectTransform rtRight = rightPanel.GetComponent<RectTransform>();
            rtRight.pivot = new Vector2(1f, 1f);

            CreateText(rightPanel.transform, "NextTitle", "NEXT (다음 과일)", 22, TextAnchor.MiddleCenter,
                new Vector2(10f, -14f), new Vector2(240f, 34f), new Color(0.95f, 0.90f, 0.75f), defaultFont);

            GameObject iconObj = new GameObject("NextFruitIcon");
            iconObj.transform.SetParent(rightPanel.transform, false);
            RectTransform iconRt = iconObj.AddComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0.5f, 0.5f);
            iconRt.anchorMax = new Vector2(0.5f, 0.5f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.anchoredPosition = new Vector2(0f, -2f);
            iconRt.sizeDelta = new Vector2(82f, 82f);
            Image iconImg = iconObj.AddComponent<Image>();
            iconImg.color = gm.fruitEvolutionList[0].color;
            gm.nextFruitIconImage = iconImg;

            gm.nextFruitNameText = CreateText(rightPanel.transform, "NextFruitName", "1. 체리", 24, TextAnchor.MiddleCenter,
                new Vector2(10f, -162f), new Vector2(240f, 40f), Color.white, defaultFont);

            // 3. 상단 중앙: 상태/보상 알림 텍스트
            gm.statusNoticeText = CreateText(canvasObj.transform, "StatusNoticeText",
                "마우스로 조준하고 클릭하여 과일을 떨어뜨리세요! (5단계 '감' 이상 합체 시 코인 획득)", 22, TextAnchor.MiddleCenter,
                new Vector2(0f, -18f), new Vector2(960f, 42f), new Color(0.22f, 0.18f, 0.12f), defaultFont);
            RectTransform noticeRt = gm.statusNoticeText.GetComponent<RectTransform>();
            noticeRt.anchorMin = new Vector2(0.5f, 1f);
            noticeRt.anchorMax = new Vector2(0.5f, 1f);
            noticeRt.pivot = new Vector2(0.5f, 1f);

            // 4. 하단 바: 11단계 진화 순서 (Evolution Bar)
            GameObject bottomPanel = CreatePanel(canvasObj.transform, "EvolutionBottomBar",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(1360f, 86f),
                new Color(0.18f, 0.15f, 0.12f, 0.86f));
            RectTransform bottomRt = bottomPanel.GetComponent<RectTransform>();
            bottomRt.pivot = new Vector2(0.5f, 0f);

            float startX = 22f;
            float stepX = 121f;
            for (int i = 0; i < gm.fruitEvolutionList.Count; i++)
            {
                FruitData f = gm.fruitEvolutionList[i];

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

                string arrow = (i < gm.fruitEvolutionList.Count - 1) ? " →" : "";
                CreateText(bottomPanel.transform, $"EvoLabel_{i}", $"{f.fruitName}{arrow}", 16, TextAnchor.MiddleLeft,
                    new Vector2(startX - 6f + i * stepX, -52f), new Vector2(118f, 28f), Color.white, defaultFont);
            }

            // 5. 게임 오버 패널
            GameObject goPanel = CreatePanel(canvasObj.transform, "GameOverPanel",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 360f),
                new Color(0.10f, 0.08f, 0.08f, 0.94f));
            RectTransform goRt = goPanel.GetComponent<RectTransform>();
            goRt.pivot = new Vector2(0.5f, 0.5f);

            gm.finalScoreText = CreateText(goPanel.transform, "FinalScoreText",
                "게임 오버!\n최종 점수: 0점", 32, TextAnchor.MiddleCenter,
                new Vector2(30f, -35f), new Vector2(500f, 170f), Color.white, defaultFont);

            Button restartBtn = CreateButton(goPanel.transform, "RestartButton",
                "🔄 다시 시작하기", 26,
                new Vector2(130f, -245f), new Vector2(300f, 72f),
                new Color(0.22f, 0.72f, 0.32f, 1f), defaultFont);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(restartBtn.onClick, gm.RestartGame);

            goPanel.SetActive(false);
            gm.gameOverPanel = goPanel;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, Color bgColor)
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

        private static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor align, Vector2 anchoredPos, Vector2 size, Color color, Font font)
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

        private static Button CreateButton(Transform parent, string name, string label, int fontSize, Vector2 anchoredPos, Vector2 size, Color bgColor, Font font)
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

            Text txt = CreateText(btnObj.transform, "Label", label, fontSize, TextAnchor.MiddleCenter,
                Vector2.zero, size, Color.white, font);
            txt.raycastTarget = false;

            return btn;
        }
    }
}
#endif
