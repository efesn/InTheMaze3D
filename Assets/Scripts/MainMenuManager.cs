using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MainMenuManager : MonoBehaviour
{
    private enum MenuState
    {
        MainMenu,
        Instructions,
        FinishScreen,
        Starting,
        Restarting,
        Hidden
    }

    [Header("Script References")]
    [SerializeField] private MazeGameSystem gameSystem;
    [SerializeField] private MazeScoreSystem scoreSystem;
    [SerializeField] private MazeBestTimeManager bestTimeManager;
    [SerializeField] private bool autoFindReferences = true;

    [Header("Scene Behavior")]
    [SerializeField] private bool showMenuOnStart = true;
    [SerializeField] private bool forceGameSystemManualStart = true;
    [SerializeField] private bool reloadActiveSceneIfNoGameSystem = false;
    [SerializeField] private string optionalMazeSceneName = "";
    [SerializeField, Min(0.05f)] private float gameSystemWaitInterval = 0.15f;
    [SerializeField, Min(0f)] private float gameSystemStartTimeout = 10f;

    [Header("Finish Screen")]
    [SerializeField] private bool showFinishScreenWhenGameEnds = true;
    [SerializeField] private string finishTitle = "Maze Completed";
    [SerializeField] private string finishMessage = "You reached the exit.";
    [SerializeField] private string mainMenuButtonLabel = "Main Menu";

    [Header("Generated UI")]
    [SerializeField] private bool createUiIfMissing = true;
    [SerializeField] private string canvasObjectName = "Main Menu Canvas";
    [SerializeField] private string menuPanelObjectName = "Main Menu Panel";
    [SerializeField] private string instructionsPanelObjectName = "Instructions Panel";
    [SerializeField] private string finishPanelObjectName = "Finish Panel";
    [SerializeField] private int sortingOrder = 1000;

    [Header("Existing UI References")]
    [SerializeField] private Canvas menuCanvas;
    [SerializeField] private RectTransform menuPanel;
    [SerializeField] private RectTransform instructionsPanel;
    [SerializeField] private RectTransform finishPanel;

    [SerializeField] private Button startGameButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button instructionsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button closeInstructionsButton;
    [SerializeField] private Button finishMainMenuButton;
    [SerializeField] private Button finishRestartButton;
    [SerializeField] private Button finishQuitButton;

    [SerializeField] private Text titleText;
    [SerializeField] private Text instructionsText;
    [SerializeField] private Text statusText;
    [SerializeField] private Text finishTitleText;
    [SerializeField] private Text finishMessageText;
    [SerializeField] private Text finishStatsText;

    [Header("Menu Text")]
    [SerializeField] private string gameTitle = "In the Maze";
    [SerializeField] private string startButtonLabel = "Start Game";
    [SerializeField] private string restartButtonLabel = "Restart";
    [SerializeField] private string instructionsButtonLabel = "Instructions";
    [SerializeField] private string quitButtonLabel = "Quit";
    [SerializeField] private string closeInstructionsButtonLabel = "Back";
    [SerializeField] private string instructionsMessage =
        "Use the arrow keys to move the white token through the maze. Reach the exit on the right side and try to find the shortest route.";

    [Header("Visual Settings")]
    [SerializeField] private Color backgroundColor = new Color(0.04f, 0.06f, 0.08f, 0.86f);
    [SerializeField] private Color panelColor = new Color(0.10f, 0.12f, 0.16f, 0.94f);
    [SerializeField] private Color titleColor = Color.white;
    [SerializeField] private Color bodyTextColor = new Color(0.92f, 0.95f, 1f, 1f);
    [SerializeField] private Color successColor = new Color(0.55f, 1f, 0.45f, 1f);
    [SerializeField] private Color buttonNormalColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private Color buttonHighlightedColor = new Color(1f, 0.86f, 0.22f, 1f);
    [SerializeField] private Color buttonPressedColor = new Color(0.95f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color buttonTextColor = new Color(0.05f, 0.06f, 0.08f, 1f);
    [SerializeField, Min(8)] private int titleFontSize = 52;
    [SerializeField, Min(8)] private int buttonFontSize = 24;
    [SerializeField, Min(8)] private int bodyFontSize = 22;
    [SerializeField, Min(8)] private int statsFontSize = 20;

    [Header("Debug")]
    [SerializeField] private bool printDebugMessages = true;

    [SerializeField, HideInInspector] private string status = "main menu";

    private MenuState currentMenuState = MenuState.MainMenu;
    private MazeGameSystem.MazeGameState lastObservedGameState;
    private Coroutine startGameCoroutine;
    private Coroutine restartGameCoroutine;

    public string Status => status;
    public string CurrentMenuState => currentMenuState.ToString();
    public bool IsMenuVisible => menuCanvas != null && menuCanvas.gameObject.activeSelf;

    private void Awake()
    {
        ResolveReferences();

        if (forceGameSystemManualStart)
        {
            ForceGameSystemToManualStart();
        }

        if (createUiIfMissing)
        {
            CreateMissingUi();
        }

        BindButtons();
        SetMenuVisible(showMenuOnStart);
        ShowMainMenu();
    }

    private void Start()
    {
        ResolveReferences();

        if (gameSystem != null)
        {
            lastObservedGameState = gameSystem.CurrentState;
        }

        if (showMenuOnStart)
        {
            SetMenuVisible(true);
            ShowMainMenu();
        }
    }

    private void Update()
    {
        ResolveReferences();

        if (showFinishScreenWhenGameEnds && gameSystem != null)
        {
            MazeGameSystem.MazeGameState currentGameState = gameSystem.CurrentState;

            if (currentGameState == MazeGameSystem.MazeGameState.Finished &&
                lastObservedGameState != MazeGameSystem.MazeGameState.Finished)
            {
                ShowFinishScreen();
            }

            lastObservedGameState = currentGameState;
        }

        if (currentMenuState == MenuState.FinishScreen)
        {
            UpdateFinishScreenText();
        }
    }

    private void OnValidate()
    {
        gameSystemWaitInterval = Mathf.Max(0.05f, gameSystemWaitInterval);
        gameSystemStartTimeout = Mathf.Max(0f, gameSystemStartTimeout);
        titleFontSize = Mathf.Max(8, titleFontSize);
        buttonFontSize = Mathf.Max(8, buttonFontSize);
        bodyFontSize = Mathf.Max(8, bodyFontSize);
        statsFontSize = Mathf.Max(8, statsFontSize);
    }

    public void StartGame()
    {
        if (startGameCoroutine != null)
        {
            StopCoroutine(startGameCoroutine);
        }

        startGameCoroutine = StartCoroutine(StartGameFlow());
    }

    public void RestartGame()
    {
        if (restartGameCoroutine != null)
        {
            StopCoroutine(restartGameCoroutine);
        }

        restartGameCoroutine = StartCoroutine(RestartGameFlow());
    }

    public void ShowInstructions()
    {
        currentMenuState = MenuState.Instructions;
        status = "instructions";

        SetMenuVisible(true);
        SetOnlyPanelActive(instructionsPanel);
        UpdateStatusText("");
    }

    public void ShowMainMenu()
    {
        currentMenuState = MenuState.MainMenu;
        status = "main menu";

        SetMenuVisible(true);
        SetOnlyPanelActive(menuPanel);
        UpdateStatusText("");
    }

    public void ShowFinishScreen()
    {
        currentMenuState = MenuState.FinishScreen;
        status = "finish screen";

        SetMenuVisible(true);
        SetOnlyPanelActive(finishPanel);
        UpdateFinishScreenText();
    }

    public void QuitGame()
    {
        status = "quitting";

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void SetMenuVisible(bool visible)
    {
        if (menuCanvas != null)
        {
            menuCanvas.gameObject.SetActive(visible);
        }

        if (!visible)
        {
            currentMenuState = MenuState.Hidden;
            status = "hidden";
        }
    }

    private IEnumerator StartGameFlow()
    {
        currentMenuState = MenuState.Starting;
        status = "starting";
        UpdateStatusText("Starting...");

        ResolveReferences();

        if (gameSystem == null)
        {
            HandleMissingGameSystem();
            yield break;
        }

        ForceGameSystemToManualStart();

        float elapsed = 0f;
        while (!CanGameSystemStart())
        {
            if (gameSystemStartTimeout > 0f && elapsed >= gameSystemStartTimeout)
            {
                UpdateStatusText("Game is still preparing...");
                if (printDebugMessages)
                {
                    Debug.LogWarning("MainMenuManager waited for MazeGameSystem, but it did not become ready before the timeout.");
                }

                yield break;
            }

            yield return new WaitForSeconds(gameSystemWaitInterval);
            elapsed += gameSystemWaitInterval;
        }

        SetMenuVisible(false);
        gameSystem.StartGame();

        currentMenuState = MenuState.Hidden;
        status = "game started";

        if (printDebugMessages)
        {
            Debug.Log("Main menu started the game.");
        }
    }

    private IEnumerator RestartGameFlow()
    {
        currentMenuState = MenuState.Restarting;
        status = "restarting";
        UpdateStatusText("Restarting...");

        ResolveReferences();

        if (gameSystem == null)
        {
            HandleMissingGameSystem();
            yield break;
        }

        SetMenuVisible(false);
        gameSystem.RestartGame();

        float elapsed = 0f;
        while (!CanGameSystemStart())
        {
            if (gameSystemStartTimeout > 0f && elapsed >= gameSystemStartTimeout)
            {
                ShowMainMenu();
                UpdateStatusText("Restart is still preparing...");
                if (printDebugMessages)
                {
                    Debug.LogWarning("MainMenuManager waited after restart, but MazeGameSystem did not become ready before the timeout.");
                }

                yield break;
            }

            yield return new WaitForSeconds(gameSystemWaitInterval);
            elapsed += gameSystemWaitInterval;
        }

        gameSystem.StartGame();
        SetMenuVisible(false);

        currentMenuState = MenuState.Hidden;
        status = "game restarted";

        if (printDebugMessages)
        {
            Debug.Log("Main menu restarted the game.");
        }
    }

    private void HandleMissingGameSystem()
    {
        if (!string.IsNullOrWhiteSpace(optionalMazeSceneName))
        {
            SceneManager.LoadScene(optionalMazeSceneName);
            return;
        }

        if (reloadActiveSceneIfNoGameSystem)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

        UpdateStatusText("Game system not found.");

        if (printDebugMessages)
        {
            Debug.LogWarning("MainMenuManager could not find MazeGameSystem.");
        }
    }

    private bool CanGameSystemStart()
    {
        if (gameSystem == null)
        {
            return false;
        }

        return gameSystem.CurrentState == MazeGameSystem.MazeGameState.ReadyToStart ||
               gameSystem.CurrentState == MazeGameSystem.MazeGameState.Finished;
    }

    private void ResolveReferences()
    {
        if (!autoFindReferences)
        {
            return;
        }

        if (gameSystem == null)
        {
            gameSystem = FindFirstObjectByType<MazeGameSystem>();
        }

        if (scoreSystem == null)
        {
            scoreSystem = FindFirstObjectByType<MazeScoreSystem>();
        }

        if (bestTimeManager == null)
        {
            bestTimeManager = FindFirstObjectByType<MazeBestTimeManager>();
        }

        if (menuCanvas == null)
        {
            GameObject existingCanvas = GameObject.Find(canvasObjectName);
            if (existingCanvas != null)
            {
                menuCanvas = existingCanvas.GetComponent<Canvas>();
            }
        }

        if (menuPanel == null)
        {
            GameObject existingMenuPanel = GameObject.Find(menuPanelObjectName);
            if (existingMenuPanel != null)
            {
                menuPanel = existingMenuPanel.GetComponent<RectTransform>();
            }
        }

        if (instructionsPanel == null)
        {
            GameObject existingInstructionsPanel = GameObject.Find(instructionsPanelObjectName);
            if (existingInstructionsPanel != null)
            {
                instructionsPanel = existingInstructionsPanel.GetComponent<RectTransform>();
            }
        }

        if (finishPanel == null)
        {
            GameObject existingFinishPanel = GameObject.Find(finishPanelObjectName);
            if (existingFinishPanel != null)
            {
                finishPanel = existingFinishPanel.GetComponent<RectTransform>();
            }
        }
    }

    private void ForceGameSystemToManualStart()
    {
        if (!forceGameSystemManualStart || gameSystem == null)
        {
            return;
        }

        FieldInfo startAutomaticallyField = typeof(MazeGameSystem).GetField(
            "startAutomatically",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (startAutomaticallyField != null)
        {
            startAutomaticallyField.SetValue(gameSystem, false);
        }
    }

    private void CreateMissingUi()
    {
        EnsureEventSystem();
        EnsureCanvas();
        EnsureMenuPanel();
        EnsureInstructionsPanel();
        EnsureFinishPanel();
        EnsureMenuButtons();
        EnsureInstructionsContent();
        EnsureFinishContent();
    }

    private void EnsureEventSystem()
    {
        EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
        if (eventSystem != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private void EnsureCanvas()
    {
        if (menuCanvas != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject(canvasObjectName);
        menuCanvas = canvasObject.AddComponent<Canvas>();
        menuCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        menuCanvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject backgroundObject = new GameObject("Main Menu Background");
        backgroundObject.transform.SetParent(canvasObject.transform, false);

        RectTransform backgroundRect = backgroundObject.AddComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        Image backgroundImage = backgroundObject.AddComponent<Image>();
        backgroundImage.color = backgroundColor;
    }

    private void EnsureMenuPanel()
    {
        if (menuPanel != null)
        {
            return;
        }

        GameObject panelObject = CreatePanel(menuPanelObjectName, new Vector2(540f, 570f));
        menuPanel = panelObject.GetComponent<RectTransform>();

        titleText = CreateText("Title", menuPanel, gameTitle, titleFontSize, titleColor,
            TextAnchor.MiddleCenter, new Vector2(0f, 195f), new Vector2(480f, 90f));

        statusText = CreateText("Status Text", menuPanel, "", 18, bodyTextColor,
            TextAnchor.MiddleCenter, new Vector2(0f, -240f), new Vector2(480f, 40f));
    }

    private void EnsureInstructionsPanel()
    {
        if (instructionsPanel != null)
        {
            return;
        }

        GameObject panelObject = CreatePanel(instructionsPanelObjectName, new Vector2(700f, 440f));
        instructionsPanel = panelObject.GetComponent<RectTransform>();
        instructionsPanel.gameObject.SetActive(false);
    }

    private void EnsureFinishPanel()
    {
        if (finishPanel != null)
        {
            return;
        }

        GameObject panelObject = CreatePanel(finishPanelObjectName, new Vector2(720f, 600f));
        finishPanel = panelObject.GetComponent<RectTransform>();
        finishPanel.gameObject.SetActive(false);
    }

    private void EnsureMenuButtons()
    {
        if (startGameButton == null)
        {
            startGameButton = CreateButton("Start Game Button", menuPanel, startButtonLabel, new Vector2(0f, 85f));
        }

        if (restartButton == null)
        {
            restartButton = CreateButton("Restart Button", menuPanel, restartButtonLabel, new Vector2(0f, 15f));
        }

        if (instructionsButton == null)
        {
            instructionsButton = CreateButton("Instructions Button", menuPanel, instructionsButtonLabel, new Vector2(0f, -55f));
        }

        if (quitButton == null)
        {
            quitButton = CreateButton("Quit Button", menuPanel, quitButtonLabel, new Vector2(0f, -125f));
        }
    }

    private void EnsureInstructionsContent()
    {
        if (instructionsText == null)
        {
            CreateText("Instructions Title", instructionsPanel, "Instructions", 38, titleColor,
                TextAnchor.MiddleCenter, new Vector2(0f, 145f), new Vector2(600f, 60f));

            instructionsText = CreateText("Instructions Text", instructionsPanel, instructionsMessage,
                bodyFontSize, bodyTextColor, TextAnchor.MiddleCenter, new Vector2(0f, 35f), new Vector2(590f, 150f));
        }

        if (closeInstructionsButton == null)
        {
            closeInstructionsButton = CreateButton("Close Instructions Button", instructionsPanel,
                closeInstructionsButtonLabel, new Vector2(0f, -145f));
        }
    }

    private void EnsureFinishContent()
    {
        if (finishTitleText == null)
        {
            finishTitleText = CreateText("Finish Title", finishPanel, finishTitle, 44, successColor,
                TextAnchor.MiddleCenter, new Vector2(0f, 225f), new Vector2(620f, 70f));
        }

        if (finishMessageText == null)
        {
            finishMessageText = CreateText("Finish Message", finishPanel, finishMessage, bodyFontSize, bodyTextColor,
                TextAnchor.MiddleCenter, new Vector2(0f, 165f), new Vector2(620f, 44f));
        }

        if (finishStatsText == null)
        {
            finishStatsText = CreateText("Finish Stats", finishPanel, "", statsFontSize, Color.white,
                TextAnchor.MiddleCenter, new Vector2(0f, 55f), new Vector2(620f, 180f));
        }

        if (finishMainMenuButton == null)
        {
            finishMainMenuButton = CreateButton("Finish Main Menu Button", finishPanel,
                mainMenuButtonLabel, new Vector2(0f, -75f));
        }

        if (finishRestartButton == null)
        {
            finishRestartButton = CreateButton("Finish Restart Button", finishPanel,
                restartButtonLabel, new Vector2(0f, -145f));
        }

        if (finishQuitButton == null)
        {
            finishQuitButton = CreateButton("Finish Quit Button", finishPanel,
                quitButtonLabel, new Vector2(0f, -215f));
        }
    }

    private GameObject CreatePanel(string objectName, Vector2 size)
    {
        GameObject panelObject = new GameObject(objectName);
        panelObject.transform.SetParent(menuCanvas.transform, false);

        RectTransform rectTransform = panelObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.sizeDelta = size;

        Image panelImage = panelObject.AddComponent<Image>();
        panelImage.color = panelColor;

        return panelObject;
    }

    private Button CreateButton(string objectName, RectTransform parent, string label, Vector2 anchoredPosition)
    {
        GameObject buttonObject = new GameObject(objectName);
        buttonObject.transform.SetParent(parent, false);

        RectTransform rectTransform = buttonObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(340f, 56f);

        Image image = buttonObject.AddComponent<Image>();
        image.color = buttonNormalColor;

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = buttonNormalColor;
        colors.highlightedColor = buttonHighlightedColor;
        colors.selectedColor = buttonHighlightedColor;
        colors.pressedColor = buttonPressedColor;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        CreateText(objectName + " Label", rectTransform, label, buttonFontSize, buttonTextColor,
            TextAnchor.MiddleCenter, Vector2.zero, new Vector2(320f, 50f));

        return button;
    }

    private Text CreateText(
        string objectName,
        RectTransform parent,
        string content,
        int fontSize,
        Color color,
        TextAnchor alignment,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = size;

        Text text = textObject.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        return text;
    }

    private void BindButtons()
    {
        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveListener(StartGame);
            startGameButton.onClick.AddListener(StartGame);
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(RestartGame);
            restartButton.onClick.AddListener(RestartGame);
        }

        if (instructionsButton != null)
        {
            instructionsButton.onClick.RemoveListener(ShowInstructions);
            instructionsButton.onClick.AddListener(ShowInstructions);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(QuitGame);
            quitButton.onClick.AddListener(QuitGame);
        }

        if (closeInstructionsButton != null)
        {
            closeInstructionsButton.onClick.RemoveListener(ShowMainMenu);
            closeInstructionsButton.onClick.AddListener(ShowMainMenu);
        }

        if (finishMainMenuButton != null)
        {
            finishMainMenuButton.onClick.RemoveListener(ShowMainMenu);
            finishMainMenuButton.onClick.AddListener(ShowMainMenu);
        }

        if (finishRestartButton != null)
        {
            finishRestartButton.onClick.RemoveListener(RestartGame);
            finishRestartButton.onClick.AddListener(RestartGame);
        }

        if (finishQuitButton != null)
        {
            finishQuitButton.onClick.RemoveListener(QuitGame);
            finishQuitButton.onClick.AddListener(QuitGame);
        }
    }

    private void SetOnlyPanelActive(RectTransform activePanel)
    {
        if (menuPanel != null)
        {
            menuPanel.gameObject.SetActive(activePanel == menuPanel);
        }

        if (instructionsPanel != null)
        {
            instructionsPanel.gameObject.SetActive(activePanel == instructionsPanel);
        }

        if (finishPanel != null)
        {
            finishPanel.gameObject.SetActive(activePanel == finishPanel);
        }
    }

    private void UpdateStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void UpdateFinishScreenText()
    {
        if (finishTitleText != null)
        {
            finishTitleText.text = finishTitle;
        }

        if (finishMessageText != null)
        {
            finishMessageText.text = finishMessage;
        }

        if (finishStatsText != null)
        {
            finishStatsText.text = BuildFinishStatsText();
        }
    }

    private string BuildFinishStatsText()
    {
        float completionTime = gameSystem != null ? gameSystem.FinalCompletionTime : 0f;

        string bestTimeText = "Best Time: --:--.--";
        string newBestText = "";

        string bestTimeKey = "InTheMaze_BestCompletionTime";
        float bestTimeVal = PlayerPrefs.GetFloat(bestTimeKey, -1f);

        if (bestTimeManager != null && bestTimeManager.HasBestTime)
        {
            bestTimeVal = bestTimeManager.BestTimeSeconds;
            if (bestTimeManager.IsNewBestTime)
            {
                newBestText = "\nNew Best Time!";
            }
        }

        if (bestTimeVal > 0f)
        {
            bestTimeText = "Best Time: " + FormatTime(bestTimeVal);
            if (Mathf.Approximately(completionTime, bestTimeVal) || completionTime <= bestTimeVal)
            {
                newBestText = "\nNew Best Time!";
            }
        }

        string scoreText = "Score: --";
        string efficiencyText = "Route Efficiency: --";
        string optimalText = "Optimal Route: --";
        string distanceText = "Player Distance: --";

        if (scoreSystem != null)
        {
            scoreText = "Score: " + Mathf.RoundToInt(scoreSystem.FinalScore);
            efficiencyText = "Route Efficiency: " + (scoreSystem.FinalRouteEfficiency * 100f).ToString("0.0") + "%";
            optimalText = "Optimal Route: " + scoreSystem.OptimalRouteSteps + " steps";
            distanceText = "Player Distance: " + scoreSystem.PlayerTravelDistance.ToString("0.0") + " units";
        }

        return "Completion Time: " + FormatTime(completionTime) + "\n" +
               bestTimeText + newBestText + "\n" +
               scoreText + "\n" +
               efficiencyText + "\n" +
               optimalText + "\n" +
               distanceText;
    }

    private string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);

        int minutes = Mathf.FloorToInt(seconds / 60f);
        int wholeSeconds = Mathf.FloorToInt(seconds % 60f);
        int hundredths = Mathf.FloorToInt((seconds - Mathf.Floor(seconds)) * 100f);

        return $"{minutes:00}:{wholeSeconds:00}.{hundredths:00}";
    }
}