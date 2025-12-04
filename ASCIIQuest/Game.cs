// Game.cs
using System.Text;
using System.Threading; 
using System.Collections.Generic; 
using System.Linq; 
using System.Text.Json; // <--- 이 줄을 추가하세요

public class Game
{
    // 맵 생성 설정
    private int MapWidth; 
    private int MapHeight;

    public ConsoleColor customBlinkColor = ConsoleColor.Black; 
    
    // [신규] 턴 딜레이 상수 (0.5초) - 이 값을 조절해 딜레이를 변경하세요.
    private const int BATTLE_TURN_DELAY_MS = 300; 
    private DateTime turnDelayEndTime = DateTime.MinValue;

    private DateTime lastMonsterMoveTime = DateTime.MinValue;
    private const int MONSTER_MOVE_INTERVAL_MS = 1000; // 1초마다 이동
    private bool isPlayerTurn = true;

    private bool isMonsterTurnInProgress = false;

    private GameState returnStateFromMenu = GameState.World;

    private Monster? currentMapMonsterReference = null;

    private DateTime battleAnimationStartTime;

    // 게임 상태
    internal enum GameState
    {
        Intro,
        MainMenu,         // [신규] 메인 메뉴
        HowToPlay,        // [신규] 조작법
        StageIntro,
        World,  // 맵 탐험
        Battle_Intro,
        Battle, // 전투 메인 메뉴
        Battle_TurnDelay, // [신규] 턴 종료 후 딜레이
        Battle_SkillSelect, // 전투 스킬 선택
        Battle_ItemMenu,  // 전투 아이템 (메인)
        Battle_ItemSubMenu, // 전투 아이템 (서브: 등급 선택)
        LootDrop,         // "장비 비교" 창
        LevelUp, //레벨 업 창
        LootSummary,      // "획득 아이템 목록" 창
        Inventory,        // 인벤토리 확인 창
        CharacterStat,
        GameOver, // 게임 오버
        GameEnd,
        EndingStory,
        Pause,
        Battle_Animation, // [신규] 전투 애니메이션 재생
        Chest_Confirm,    // 1. 열기 확인 창
        Chest_Opening,    // 2. 열기 애니메이션
        Multiplayer_Nick,       // 닉네임 입력
        Multiplayer_Lobby,      // 방 목록 (로비)
        Multiplayer_CreateRoom, // 방 만들기
        Multiplayer_RoomWait,   // 방 대기실
        Multiplayer_Lobby_ExitConfirm,  // 로비에서 나가기 확인
        Multiplayer_Room_LeaveConfirm,  // 방에서 나가기 확인
        Multiplayer_PasswordInput,       // 비밀번호 입력 창
        Multiplayer_DirectIpConnect, // [신규] IP 직접 접속 창
        Multiplayer_DirectConnect_Wait, // [신규] IP 접속 후 방 정보 대기 중
        Multiplayer_FullRoomWarning,
        // [Phase 2: 인게임 신규 상태 추가]
        Multiplayer_ClassSelect,
        Multiplayer_Nick_ExitConfirm,        // 닉네임 입력 중 나가기 확인
        Multiplayer_ClassSelect_ExitConfirm, // 직업 선택 중 나가기 확인
        Multiplayer_Countdown, // [신규] 카운트다운 상태
        Multiplayer_World,
        Multiplayer_Battle,
        Multiplayer_BattleResultWait, // [신규] 전투 결과 확인 후 대기 상태
    }

    // 화면 셀
    internal struct ScreenCell
    {
        public char Char;
        public ConsoleColor FgColor;
        public ConsoleColor BgColor;
        public static readonly ScreenCell Empty = new(' ', ConsoleColor.Gray, ConsoleColor.Black);
        public ScreenCell(char c, ConsoleColor fg = ConsoleColor.Gray, ConsoleColor bg = ConsoleColor.Black)
        {
            Char = c; FgColor = fg; BgColor = bg;
        }
        public static readonly ScreenCell Null = new('\0', ConsoleColor.Gray, ConsoleColor.Black);
    }

    // 방과 복도를 관리하기 위한 사각형 구조체
    internal struct Rectangle
    {
        public int X, Y, Width, Height;
        public Rectangle(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
        public int Left => X;
        public int Right => X + Width;
        public int Top => Y;
        public int Bottom => Y + Height;
        public (int x, int y) Center => (X + Width / 2, Y + Height / 2);
        public bool Intersects(Rectangle other)
        {
            return (Left <= other.Right && Right >= other.Left &&
                    Top <= other.Bottom && Bottom >= other.Top);
        }
        public bool Contains(int x, int y)
        {
            return (x > Left && x < Right && y > Top && y < Bottom);
        }
    }

    private const int PORTAL_DETECTION_RANGE_SQ = 9;

    private Player otherPlayer;              // 상대방 플레이어 (파란색 캐릭터)
    public Player OtherPlayer => otherPlayer;
    private bool iSelectedClass = false;     // 나 직업 골랐니?
    private bool otherSelectedClass = false; // 상대 직업 골랐니?
    
    private bool isMyBattleTurn = false;     // 지금 내 턴이니?

    private int battleTurnCount = 0;       // 현재 라운드에서 행동한 플레이어 수
    private bool myFleeRequest = false;    // 내가 후퇴를 눌렀는지
    private bool otherFleeRequest = false; // 상대가 후퇴를 눌렀는지

    private bool isOtherPlayerFinishedBattleResult = false;

    private bool isGameChatting = false; // 현재 채팅 입력 중인가?
    private string gameChatInput = "";   // 입력 중인 내용

    private string directIpInput = "";       // 입력한 IP 문자열
    private string directIpError = "";       // 접속 실패 메시지
    private int directIpBtnIndex = 0;        // 0: 접속, 1: 취소
    private RoomInfo myHostingRoom = null;
    private bool isEnteringIp = true;        // 포커스 (텍스트 vs 버튼)

    private DateTime lastBattleActionTime = DateTime.Now;

    // [Game.cs] 변수 선언부
    private int currentBattleMapX; // [신규] 전투 중인 몬스터의 맵 X좌표
    private int currentBattleMapY; // [신규] 전투 중인 몬스터의 맵 Y좌표

    private int introPageIndex = 0;
    private string currentIntroFullText = ""; // 현재 페이지의 완성된 텍스트
    private string currentIntroBuffer = "";   // 현재 화면에 타이핑된 텍스트
    private int introCharIndex = 0;           // 타이핑 진행도
    private DateTime lastIntroTypeTime = DateTime.MinValue;
    private const int INTRO_TYPE_SPEED_MS = 20; // 타이핑 속도 (낮을수록 빠름)
    
   private readonly string[][] introStoryPages = new string[][]
    {
        // Page 1: 프롤로그 (변경 없음)
        new string[] {
            "--- 프롤로그: 균열과 오류의 시대 ---",
            "",
            "이곳은 모든 지식이 저장된 거대한 디지털 차원,",
            "'코드의 기록 보관소(The Code Archives)'입니다.",
            "",
            "하지만 어느 날, '태초의 오류(The Primal Bug)'가",
            "심층 코드에 침투하며 세계에 균열이 발생했습니다.",
            "",
            "데이터는 왜곡되었고, 미처리 데이터 덩어리들이",
            "몬스터가 되어 배회하는 혼란의 영역...",
            "",
            "우리는 이곳을 'ASCII 미궁'이라 부릅니다."
        },
        // Page 2: 플레이어의 소명 (문맥 다듬음)
        new string[] {
            "--- 싱크로넷의 마지막 수호자들 ---",
            "",
            "당신은 세계의 질서를 수호하는 고대 길드,",
            "'싱크로넷(Synchronet)'의 일원입니다.",
            "",
            "당신의 임무는 명확합니다.",
            "",
            "미궁 깊은 곳을 탐험하여 시스템을 좀먹는",
            "치명적인 오류들을 제거하고 기록 보관소를 지키는 것.",
            "",
            "시스템을 정화할 준비가 되셨습니까?"
        },
        // [수정] Page 3: 목표 (최종 보스: 태초의 오류로 변경)
        new string[] {
            "--- 최종 목표 ---",
            "",
            "미궁은 깊어질수록 코드가 뒤섞이고 위험해집니다.",
            "",
            "그 깊은 끝에는 모든 혼란의 원흉,",
            "'태초의 오류(The Primal Bug)'가 실체화되어 기다리고 있습니다.",
            "",
            "동료와 협동하여 이 강력한 버그를 처치하고,",
            "손상된 코드를 복구하여 세계의 질서를 되찾으십시오.",
            "",
            "이제, 접속을 시작합니다..."
        }
    };

    // [신규] 멀티플레이 직업 선택 UI 제어 변수
    private int mpClassSelectedIndex = 0;   // 커서 위치 (0:전사, 1:마법사, 2:도적)
    private bool mpIsConfirming = false;    // 확인 팝업 활성화 여부
    private bool mpConfirmChoice = true;    // 확인 팝업 (예/아니요) 커서

    // [신규] 카운트다운 관련 변수
    private DateTime countdownStartTime;
    private int currentCountdownNumber = 5;
    private bool isCountdownStarted = false;

    private int createRoomBtnIndex = 0; // 0: 생성, 1: 취소
    private bool isFocusOnButtons = false; // 포커스가 버튼에 있는지(true), 텍스트 입력인지(false)
    private int passwordBtnIndex = 0; // 0: 확인, 1: 취소
    // [신규] 멀티플레이용 변수
    private string playerNickname = "";
    private string roomTitleInput = "";
    private string roomPwInput = "";
    private bool isEnteringTitle = true; // 방 만들기에서 제목 입력 중인지 여부

    private string currentRoomTitle = "";

    // 로비 UI용 변수
    private List<RoomInfo> roomList = new List<RoomInfo>(); // (실제로는 서버에서 받아야 함)
    private int lobbySelectIndex = 0; // 0: 방 리스트, 1: 참가/생성/새로고침 버튼 영역
    private int roomListIndex = 0;    // 방 목록 내 커서
    private int lobbyBtnIndex = 0;    // 0:참가, 1:생성, 2:새로고침

    // 대기실 변수
    private string otherPlayerNickname = "???";
    private bool isOtherPlayerReady = false;

    // [신규] 에러 메시지 저장용 변수
    private string nicknameError = "";
    private string createRoomError = "";

    private string myPlayerId = Guid.NewGuid().ToString();

    // [신규] 팝업 및 비밀번호 처리용 변수
    private int popupIndex = 0;           // 예/아니요 선택 인덱스
    private RoomInfo pendingJoinRoom = null; // 입장을 시도하려는 방 정보 임시 저장
    private string joinPasswordInput = "";   // 입력한 비밀번호
    private string joinPasswordError = "";   // 비밀번호 틀림 메시지
    private bool isEnteringJoinPw = true;    // 비밀번호 입력 중인지(true), 버튼 선택 중인지(false)

    private string currentRoomPassword = "";

    // [신규] 채팅 관련 변수
    private List<string> chatLog = new List<string>(); // 채팅 기록
    private string chatInput = "";                     // 현재 입력 중인 내용
    private bool isChatting = false;                   // 채팅 입력 모드 여부

    // [신규] 조작법 창이 어디서 호출되었는지 기억하는 변수
    private GameState returnStateFromHelp = GameState.MainMenu;

    // [신규] 클리어 타임 측정 변수
    private DateTime gameStartTime;
    private TimeSpan gameClearTime;

    // [신규] 엔딩 스토리 관련 변수 (인트로와 유사)
    private int endingPageIndex = 0;
    private string currentEndingFullText = "";
    private string currentEndingBuffer = "";
    private int endingCharIndex = 0;
    private DateTime lastEndingTypeTime = DateTime.MinValue;
    
    // 엔딩 스토리 텍스트
    private readonly string[][] endingStoryPages = new string[][]
    {
        // Page 1: 승리
        new string[] {
            "--- 시스템 정화 완료 ---",
            "",
            "치열한 사투 끝에, '태초의 오류'는 소멸했습니다.",
            "",
            "그와 함께 미궁을 뒤덮고 있던 왜곡된 데이터들이",
            "본래의 순수한 코드로 분해되어 흩어집니다.",
            "",
            "무너져가던 '코드의 기록 보관소'에",
            "다시금 질서의 빛이 돌아오고 있습니다."
        },
        // Page 2: 복구
        new string[] {
            "--- 임무 완수 ---",
            "",
            "싱크로넷의 수호자여, 당신의 활약으로",
            "세계는 붕괴의 위기에서 벗어났습니다.",
            "",
            "이제 관리자 권한이 복구되고,",
            "모든 시스템이 정상 가동을 시작합니다.",
            "",
            "당신의 이름은 이 디지털 차원의 역사에",
            "영원히 '디버거(The Debugger)'로 기록될 것입니다."
        },
        // Page 3: 로그아웃
        new string[] {
            "--- 연결 종료 ---",
            "",
            "이제 편안히 휴식을 취하십시오.",
            "",
            "하지만 기억하십시오.",
            "언젠가 또 다른 오류가 이 세계를 위협할 때,",
            "",
            "우리는 다시 당신을 호출할 것입니다.",
            "",
            "시스템 로그아웃..."
        }
    };

    private DateTime battleIntroStartTime;
    private bool isBossEncounter = false; // 보스전 여부 (색상 결정용)
    private int currentBattleIntroDuration = 2500;
    private DateTime stageIntroStartTime;
    private const int STAGE_INTRO_DURATION_MS = 3000; // 3초간 표시

    private string[] currentIntroTextArt = null!;

    private ConsoleColor currentIntroColor = ConsoleColor.Yellow;

    private List<(int x, int y, int delay)> introWindows = new List<(int, int, int)>();

    // 게임 데이터
    public Player player { get; private set; }    
    private List<Monster> monsters = new List<Monster>();
    private char[,] map = null!;
    private List<(string Text, ConsoleColor Color)> logMessages = new List<(string, ConsoleColor)>();
    private List<Rectangle> rooms = new List<Rectangle>(); 
    private Rectangle bossRoom; 
    private List<Trap> traps = null!;
    private List<Chest> chests = null!; // [신규]

    private int currentStage = 1; // 1, 2, 3
    private (int x, int y) portalPosition = (-1, -1); // (-1, -1) = 포탈 없음

    // 상태 관리 변수
    private GameState currentState = GameState.MainMenu; // [수정] 시작 상태를 World -> MainMenu로
    private Monster? currentBattleMonster = null;

    public Monster CurrentBattleMonster => currentBattleMonster;
    private Random rand = new Random(); 
    
    // [신규] 1번 요청 (함정 전투)
    private bool isTrapBattle = false; 
    
    // [신규] 3번 요청 (몬스터 추적)
    private const int MONSTER_AGGRO_RANGE_SQUARED = 49; // (10*10 타일)
    
    // [신규] 4번 요청 (크리티컬/MISS)
    private bool lastAttackWasCrit = false;
    private bool lastAttackWasMiss = false;
    private const int ANIM_BLINK_DURATION_MS = 150; 
    private const int ANIM_TOTAL_BLINKS = 4; 
    private int currentIntroBlinkInterval = 200;
    private int currentBlinkCount = 0; 
    private DateTime nextBlinkTime = DateTime.MinValue;
    private Action? animationCallback = null; 
    private object? currentAnimationTarget = null; 

    // [신규] "야매" 오버레이 제어용 변수
    private bool showHitOverlay = false;       // true일 때 오버레이를 그림
    private string currentAsciiDamageString = ""; // 아스키 아트로 변환할 문자열 (예: "-100", "+50", "M")

    // [신규] 아이템 창이 열리기 직전의 상태 (World or Battle)
    private GameState stateBeforeLoot = GameState.World; 
    
    private Equipment? currentLootEquipment = null; 
    private List<Item> currentLootList = new List<Item>(); 
    private Queue<Equipment> equipmentDropQueue = new Queue<Equipment>(); 
    
    private ConsumableType currentItemSubMenu = ConsumableType.HealthPotion;

    private GameState stateBeforePause;  // 일시정지 직전 상태 저장
    private bool gameRunning = true;     // RunGameLoop의 실행 여부
    private bool needsRestart = false;   // true가 되면 Program.cs의 루프가 재시작

    // 화면 버퍼
    private ScreenCell[,] screenBuffer = null!; 
    private int screenWidth;
    private int screenHeight;
    
    // 월드 레이아웃 변수
    private int worldMapHeight, worldLogY, worldLogHeight, worldInfoX, worldLogWidth, worldInfoWidth;
    
    // 전투 레이아웃 변수
    private int logWindowHeight_BATTLE = 10, playerStatusHeight_BATTLE = 8, battleArtHeight_BATTLE, logWindowY_BATTLE, playerStatusY_BATTLE;

    // 회피율 상수
    private const double PLAYER_DEX_EVASION_SCALER = 0.0075; // (DEX 1당 0.5%)
    private const double MONSTER_EVASION_CHANCE = 0.05;

    // [신규] 몬스터 레벨 스케일링 배율 (플레이어 레벨 1당 20%씩 강해짐)
    private const double MONSTER_SCALING_FACTOR = 0.10;

    private const double BOSS_SCALING_FACTOR = 0.05;

    //방어력 공식을 위한 상수 (방어력 K=50일 때 50% 감소)
    private const int DEFENSE_CONSTANT = 30;
    

    // [신규] 상자 열기 연출용 변수
    private Chest? currentTargetChest = null;         // 현재 열려고 하는 상자
    private DateTime chestAnimStartTime = DateTime.MinValue; // 애니메이션 시작 시간
    private const int CHEST_ANIM_FLASH_MS = 200;  // 0.2초간 노랗게 빛남
    private const int CHEST_ANIM_TOTAL_MS = 600;  // 0.6초 후 결과 창으로

    private int mainMenuIndex = 0; // 0=시작, 1=조작법, 2=종료
    private int gameEndMenuIndex = 0; // 0=메인화면, 1=재시작, 2=종료
    private int gameOverMenuIndex = 0;
    private bool isTitleBright = false; // 타이틀 깜빡임
    private DateTime nextTitleBlink = DateTime.MinValue;
    private const int TITLE_BLINK_DURATION_MS = 1000; // 1초 (긴 깜빡임)

    // [신규] 방향키 메뉴 선택을 위한 인덱스 변수
    private int battleMenuIndex = 0;   // 0=공격, 1=스킬, 2=아이템, 3=후퇴
    private int pauseMenuIndex = 0;    // 0=재시작, 1=종료, 2=계속하기
    private int lootDropIndex = 0;     // 0=예, 1=아니요
    private int chestConfirmIndex = 0; // 0=열기, 1=열지 않기

    private int skillMenuIndex = 0;    // 0~3 (Q,W,E,R)
    private int itemMenuIndex = 0;     // 0=HP, 1=MP
    private int itemSubMenuIndex = 0;  // 0~N (아이템 등급별)

    private Rectangle startSafeRoomRect;

    private const int SAFE_SPAWN_RADIUS_SQUARED = 64;


    // (Start, ChooseClass, InitConsole... 변경 없음)
    #region Game_Initialization
    public bool Start()
    {
        NetworkManager.Instance.Close(); 
        InitializeConsole();

        gameRunning = true;
        needsRestart = false;
        logMessages.Clear();

        otherPlayer = null;
        otherPlayerNickname = "???";
        otherSelectedClass = false;
        isOtherPlayerReady = false;
        myFleeRequest = false;
        otherFleeRequest = false;

        currentBattleMonster = null; 
        monsters.Clear();
        
        // [핵심 수정] 스테이지 정보 초기화 (재시작 시 1스테이지부터)
        currentStage = 1;

        currentState = GameState.MainMenu;
        mainMenuIndex = 0;

        RunGameLoop();

        return needsRestart;
    }
    // [신규] 맵/플레이어 데이터를 '초기화'하는 헬퍼
    private void InitializeGameData()
    {
        // (Start()에서 이동됨)
        monsters = new List<Monster>();
        rooms = new List<Rectangle>();
        traps = new List<Trap>();
        chests = new List<Chest>();
        currentLootList = new List<Item>();
    }

    // [신규] 다음 스테이지로 '전환'하는 헬퍼
    // [수정] seed 매개변수 추가 (null이면 랜덤, 멀티플레이면 호스트가 준 시드 사용)
    private void TransitionToStage(int stage, int? seed = null, int width = 0, int height = 0)
    {
        currentStage = stage;
        portalPosition = (-1, -1); 

        InitializeGameData(); 

        // [핵심 수정] 맵 크기 결정 로직
        if (width > 0 && height > 0)
        {
            // [게스트] 호스트가 보내준 크기 강제 적용 (동기화)
            MapWidth = width;
            MapHeight = height;
        }
        else
        {
            // [호스트/싱글] 내 화면 크기에 맞춰 설정
            MapWidth = screenWidth - 2;
            MapHeight = worldMapHeight - 2;
        }

        // 맵 생성 (이제 MapWidth/Height가 호스트와 동일하므로 똑같은 맵이 생성됨)
        InitializeMap(seed);

        // [신규] 부활 및 완전 회복 로직
        // (죽은 상태로 스테이지를 넘어가면 부활하고, 살아있어도 체력을 회복)
        if (player.IsDead)
        {
            AddLog("새로운 지역의 기운으로 부활했습니다!", ConsoleColor.Green);
        }
        player.HP = player.MaxHP;
        player.MP = player.MaxMP;

        // [신규] 내 정보 갱신 전송 (멀티플레이 동기화)
        // (부활했거나 체력이 찼으므로 동료에게 알려야 함)
        if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
        {
            SendMyPlayerInfo();
        }

        // [신규] 플레이어 스폰 위치 재확인 (항상 안전한 시작 방 중앙)
        if (rooms.Count > 0) 
        { 
            (player.X, player.Y) = rooms[0].Center; 
        }
        else 
        { 
            (player.X, player.Y) = (MapWidth / 2, MapHeight / 2); 
        }

        AddLog($"Stage {currentStage}에 입장했습니다.");
        
        // 스테이지 인트로 재생 시작
        currentState = GameState.StageIntro;
        stageIntroStartTime = DateTime.Now;
    }
    
    private PlayerClass? ChooseClass()
    {
        var classInfos = new[] {
            new { 
                Name = "Warrior (시스템 방어자)", 
                Desc = "높은 체력과 방어력을 가진 탱커(STR/DEF)", 
                Skills = "주요 스킬: 파워 스트라이크, 방패 치기, 처형" 
            },
            new { 
                Name = "Wizard (버그 수정자)", 
                Desc = "강력한 주문으로 적의 방어를 무시하여 공격하는 폭발형 딜러(INT)", 
                Skills = "주요 스킬: 파이어볼, 힐, 메테오" 
            },
            new { 
                Name = "Rogue (정보 수집가)", 
                Desc = "회피와 치명타, 그리고 지속 피해가 높은 지속형 운 딜러(DEX)", 
                Skills = "주요 스킬: 백스탭, 독 찌르기, 파열" 
            }
        };

        int selectedIndex = 0; 
        
        // 상태 관리
        bool isConfirmingClass = false; // 직업 선택 확인 창
        bool confirmChoice = true;      // (예/아니요 커서)
        
        // [신규] 2번 요청: 메인으로 돌아가기 확인 창
        bool isConfirmingExit = false;  
        bool confirmExit = true;        // (예/아니요 커서)

        while (true)
        {
            // --- 렌더링 ---
            ClearBuffer();
            
            string[] titleArt = AsciiArt.GetChooseClassTitleArt();
            int titleHeight = titleArt.Length;
            int titleWidth = 0;
            foreach(string line in titleArt) { titleWidth = Math.Max(titleWidth, GetDisplayWidth(line)); }
            
            int titleX = screenWidth / 2 - titleWidth / 2;
            int titleY = 1; 
            
            for(int i=0; i<titleHeight; i++)
            {
                DrawTextToBuffer(titleX, titleY + i, titleArt[i], ConsoleColor.White, ConsoleColor.Black, true);
            }

            int boxWidth = Math.Max(35, screenWidth / 3); 
            int totalWidth = boxWidth * 3;
            int startX = screenWidth / 2 - totalWidth / 2;
            int artHeight = 20; 
            int descHeight = 6; 
            int boxHeight = artHeight + descHeight + 3; 
            int boxY = titleY + titleHeight + 2;

            for (int i = 0; i < 3; i++)
            {
                bool isSelected = (i == selectedIndex);
                ConsoleColor boxColor = isSelected ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
                
                int boxX = startX + (i * boxWidth);
                int currentBoxWidth = (i == 2) ? (screenWidth - boxX - 1) : boxWidth;
                
                DrawBox(boxX, boxY, currentBoxWidth, boxHeight, "CLASS"); 

                PlayerClass currentClass = (PlayerClass)i;
                string[] art = AsciiArt.GetPlayerArt(currentClass);

                int artActualMaxWidth = 0;
                foreach(string line in art) { artActualMaxWidth = Math.Max(artActualMaxWidth, GetDisplayWidth(line)); }
                
                int baseArtStartX = boxX + (currentBoxWidth / 2) - (artActualMaxWidth / 2);
                int baseArtStartY = boxY + 2; 

                (float percentX, float percentY) = AsciiArt.GetClassSelectOffset(currentClass);
                int offsetX = (int)(currentBoxWidth * percentX);
                int offsetY = (int)(artHeight * percentY);

                int artStartX = baseArtStartX + offsetX;
                int artStartY = baseArtStartY + offsetY;
            
                ConsoleColor artColor = ConsoleColor.Green; 
                if ((PlayerClass)i == PlayerClass.Wizard) artColor = ConsoleColor.Cyan;
                else if ((PlayerClass)i == PlayerClass.Rogue) artColor = ConsoleColor.Yellow;

                for(int j=0; j<art.Length; j++) {
                    if (artStartY + j < boxY + artHeight) 
                        DrawTextToBuffer(artStartX, artStartY + j, art[j], artColor, ConsoleColor.Black, true);
                }

                int descY = boxY + artHeight + 1;
                DrawTextToBuffer(boxX + 2, descY, classInfos[i].Name, boxColor);
                DrawTextToBuffer(boxX + 2, descY + 2, classInfos[i].Desc, ConsoleColor.White);
                DrawTextToBuffer(boxX + 2, descY + 3, classInfos[i].Skills, ConsoleColor.DarkGray);

                if (isSelected) {
                    string selectText = "[ Enter: 선택 ]";
                    DrawTextToBuffer(boxX + (boxWidth / 2) - (GetDisplayWidth(selectText) / 2), boxY + boxHeight - 2, selectText, ConsoleColor.Yellow);
                }
            }
            
            DrawTextToBuffer(screenWidth / 2 - 20, boxY + boxHeight + 1, "[←/→] 이동  [Enter] 선택  [ESC] 뒤로가기", ConsoleColor.White);

            // [팝업 그리기]
            if (isConfirmingClass)
            {
                DrawClassConfirmation(classInfos[selectedIndex].Name, confirmChoice);
            }
            else if (isConfirmingExit)
            {
                // [신규] 메인 복귀 확인 창 (재사용 가능한 헬퍼가 없으므로 직접 그리기 or 헬퍼 추가)
                // 기존 DrawClassConfirmation을 약간 변형하여 사용하거나, 유사하게 그립니다.
                DrawExitConfirmation(confirmExit);
            }

            PrintBufferToConsole();

            // --- 입력 처리 ---
            ConsoleKeyInfo key = Console.ReadKey(true);

            // 1. 메인 복귀 확인 창 입력
            if (isConfirmingExit)
            {
                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.RightArrow:
                        confirmExit = !confirmExit; 
                        break;
                    case ConsoleKey.Enter:
                        if (confirmExit) return null; // [중요] null 반환 -> 취소
                        else isConfirmingExit = false;
                        break;
                    case ConsoleKey.Y: return null;
                    case ConsoleKey.N: case ConsoleKey.Escape: isConfirmingExit = false; break;
                }
            }
            // 2. 직업 선택 확인 창 입력
            else if (isConfirmingClass) 
            {
                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.RightArrow:
                        confirmChoice = !confirmChoice; 
                        break; 
                    case ConsoleKey.Enter:
                        if (confirmChoice) return (PlayerClass)selectedIndex;
                        else isConfirmingClass = false;
                        break;
                    case ConsoleKey.Y: return (PlayerClass)selectedIndex; 
                    case ConsoleKey.N: case ConsoleKey.Escape: isConfirmingClass = false; break;
                }
            }
            // 3. 기본 선택 화면 입력
            else 
            {
                if (key.Key == ConsoleKey.LeftArrow) selectedIndex = (selectedIndex - 1 + 3) % 3; 
                else if (key.Key == ConsoleKey.RightArrow) selectedIndex = (selectedIndex + 1) % 3; 
                else if (key.Key == ConsoleKey.D1) selectedIndex = 0;
                else if (key.Key == ConsoleKey.D2) selectedIndex = 1;
                else if (key.Key == ConsoleKey.D3) selectedIndex = 2;
                
                else if (key.Key == ConsoleKey.Enter)
                {
                    isConfirmingClass = true;
                    confirmChoice = true; 
                }
                // [신규] ESC 누르면 종료 확인 창 띄움
                else if (key.Key == ConsoleKey.Escape)
                {
                    isConfirmingExit = true;
                    confirmExit = true;
                }
            }
        } 
    }
    private void InitializeConsole()
    {
        Console.CursorVisible = false;
        Console.Clear(); 
        UpdateScreenSize(true); 
    }
    #endregion

    // (맵 생성 로직... 변경 없음)
    #region Map_Generation
   private void InitializeMap(int? fixedSeed = null)
    {
        // 1. 시드 설정 (멀티플레이는 공통 시드, 싱글은 랜덤)
        if (fixedSeed.HasValue) rand = new Random(fixedSeed.Value);
        else rand = new Random();

        // 2. 데이터 초기화
        map = new char[MapWidth, MapHeight]; 
        rooms.Clear();
        monsters.Clear();
        traps.Clear(); 
        chests.Clear(); 
        
        // 맵 전체를 벽으로 채움
        for (int y = 0; y < MapHeight; y++)
            for (int x = 0; x < MapWidth; x++) map[x, y] = '█';

        // 3. 스테이지별 난이도/개수 설정
        int maxRooms, minRoomSize, maxRoomSize;
        int monsterCount, fieldBossCount, damageTraps, battleTraps, chestCount;
        double lRoomChance = 0.25;

        switch (currentStage)
        {
            case 2: // Stage 2: 데이터 동굴
                maxRooms = 40; minRoomSize = 6; maxRoomSize = 12;
                monsterCount = 25; fieldBossCount = 2; damageTraps = 20; battleTraps = 15; chestCount = 7;
                lRoomChance = 0.50; 
                break;
            case 3: // Stage 3: 커널 코어
                maxRooms = 20; minRoomSize = 15; maxRoomSize = 25;
                monsterCount = 15; fieldBossCount = 3; damageTraps = 10; battleTraps = 20; chestCount = 10;
                lRoomChance = 0.10; 
                break;
            case 1: // Stage 1: ASCII 미궁
            default:
                maxRooms = 30; minRoomSize = 10; maxRoomSize = 20;
                monsterCount = 20; fieldBossCount = 2; damageTraps = 15; battleTraps = 10; chestCount = 5;
                lRoomChance = 0.25;
                break;
        }

        // ====================================================
        // [1] 안전한 시작 방 생성 (Start Room)
        // ====================================================
        // 항상 맵의 좌측 상단(2,2)에 고정적으로 생성합니다.
        int startRoomSize = 7; 
        Rectangle startRoom = new Rectangle(2, 2, startRoomSize, startRoomSize);
        
        // [!!! 핵심 수정 !!!] 이 줄이 빠져 있어서 0,0에 스폰되는 것입니다.
        // 생성된 방 정보를 변수에 저장해야 나중에 참조할 수 있습니다.
        startSafeRoomRect = startRoom; 

        CreateRoom(startRoom, rand, false); // 장애물 없이 생성
        rooms.Add(startRoom);

        // ====================================================
        // [2] 보스 방 생성 (Boss Room)
        // ====================================================
        // 맵의 우측 하단 구석에 생성합니다.
        int bossRoomW = Math.Min(25, MapWidth / 4);
        int bossRoomH = Math.Min(15, MapHeight / 2);
        int bossRoomX = MapWidth - bossRoomW - 3; 
        int bossRoomY = MapHeight - bossRoomH - 3;
        
        bossRoom = new Rectangle(bossRoomX, bossRoomY, bossRoomW, bossRoomH);
        
        // (혹시 모를 겹침 방지)
        if (!bossRoom.Intersects(startRoom))
        {
            CreateRoom(bossRoom, rand, false);
            rooms.Add(bossRoom);
        }

        // ====================================================
        // [3] 일반 방 생성 (Random Generation)
        // ====================================================
        for (int i = 0; i < maxRooms - 2; i++) 
        {
            Rectangle newRoom;
            Rectangle attachedRoom = new Rectangle(0,0,0,0); 
            bool isLShaped = false; 
            bool overlap;
            int attempts = 0; 
            
            do {
                attempts++;
                overlap = false;
                isLShaped = false;

                if (rand.NextDouble() < lRoomChance) 
                {
                    isLShaped = true;
                    int w1 = rand.Next(minRoomSize, maxRoomSize); 
                    int h1 = rand.Next(minRoomSize, maxRoomSize);
                    int x1 = rand.Next(1, MapWidth - w1 - 1);
                    int y1 = rand.Next(1, MapHeight - h1 - 1);
                    newRoom = new Rectangle(x1, y1, w1, h1);

                    int w2, h2, x2, y2;
                    if (rand.Next(0, 2) == 0) { w2 = rand.Next(minRoomSize / 2, maxRoomSize); h2 = h1; x2 = (rand.Next(0, 2) == 0) ? (x1 - w2) : (x1 + w1); y2 = y1; }
                    else { w2 = w1; h2 = rand.Next(minRoomSize / 2, maxRoomSize); x2 = x1; y2 = (rand.Next(0, 2) == 0) ? (y1 - h2) : (y1 + h1); }
                    attachedRoom = new Rectangle(x2, y2, w2, h2);

                    if (attachedRoom.Left < 1 || attachedRoom.Right >= MapWidth - 1 || attachedRoom.Top < 1 || attachedRoom.Bottom >= MapHeight - 1) { overlap = true; continue; }
                    if (rooms.Any(r => r.Intersects(newRoom) || r.Intersects(attachedRoom))) { overlap = true; continue; }
                }
                else 
                {
                    int w = rand.Next(minRoomSize, maxRoomSize + 1);
                    int h_variation = (int)(w * 0.5);
                    int h = rand.Next(Math.Max(minRoomSize, w - h_variation), Math.Min(maxRoomSize, w + h_variation) + 1);
                    int x = rand.Next(1, MapWidth - w - 1);
                    int y = rand.Next(1, MapHeight - h - 1);
                    newRoom = new Rectangle(x, y, w, h);
                    if (rooms.Any(r => r.Intersects(newRoom))) { overlap = true; continue; }
                }

            } while (overlap && attempts < 100);

            if (!overlap)
            {
                CreateRoom(newRoom, rand, true);
                rooms.Add(newRoom);
                if (isLShaped)
                {
                    CreateRoom(attachedRoom, rand, true);
                    CreateHorizontalTunnel(newRoom.Center.x, attachedRoom.Center.x, newRoom.Center.y);
                    CreateVerticalTunnel(newRoom.Center.y, attachedRoom.Center.y, attachedRoom.Center.x);
                }
            }
        }

        // ====================================================
        // [4] 방 정렬 및 터널 연결
        // ====================================================
        // 시작 방(0번)을 유지한 채 나머지 방들을 X좌표 순으로 정렬하여 자연스럽게 연결
        var safeRoom = rooms[0]; 
        var otherRooms = rooms.Skip(1).OrderBy(r => r.Center.x).ToList();
        
        rooms.Clear();
        rooms.Add(safeRoom);
        rooms.AddRange(otherRooms);

        for (int i = 1; i < rooms.Count; i++) {
            var (prevX, prevY) = rooms[i - 1].Center;
            var (currX, currY) = rooms[i].Center;
            CreateHorizontalTunnel(prevX, currX, prevY);
            CreateVerticalTunnel(prevY, currY, currX);
        }
        
        // ====================================================
        // [5] 플레이어 위치 설정 (가장 중요!)
        // ====================================================
        // 몬스터/함정 스폰 메서드(GetRandomEmptyPosition)가 
        // 플레이어와의 거리를 계산해야 하므로, 스폰보다 '먼저' 위치를 잡아야 합니다.
        
        (player.X, player.Y) = safeRoom.Center;
        
        // 멀티플레이 동기화를 위해 otherPlayer 위치도 미리 설정
        if (otherPlayer != null)
        {
            // 호스트 바로 옆(X+1)에 배치하여 겹침 방지 및 거리 체크에 포함시킴
            (otherPlayer.X, otherPlayer.Y) = (player.X + 1, player.Y);
        }

        // ====================================================
        // [6] 오브젝트 스폰 (이제 안전하게 생성됨)
        // ====================================================
        
        // 메인 보스 스폰 (보스 방 중앙)
        (int bossX, int bossY) = bossRoom.Center;
        monsters.Add(MonsterDB.CreateBoss(bossX, bossY, currentStage));
        
        // 함정, 상자, 필드보스, 일반 몬스터 스폰
        // (이 메서드들 내부에서 GetRandomEmptyPosition을 호출하며, 
        //  거기서 player.X, Y와 거리를 체크하여 안전 구역을 확보함)
        SpawnTraps(TrapType.Damage, '^', damageTraps);
        SpawnTraps(TrapType.Battle, '*', battleTraps);
        
        SpawnChests(chestCount);
        
        SpawnFieldBosses(fieldBossCount); 
        SpawnMonsters(monsterCount - fieldBossCount);
    }
    private void CreateRoom(Rectangle room, Random rand, bool addObstacles) 
    {
        // 1. 방의 바닥을 파냅니다.
        for (int y = room.Y + 1; y < room.Bottom; y++){
            for (int x = room.X + 1; x < room.Right; x++){
                // [수정] 맵 경계 검사를 더 안전하게 변경
                if(x > 0 && x < MapWidth && y > 0 && y < MapHeight)
                    map[x, y] = '.';
            }
        }

        // --- [핵심 수정] ---
        // 장애물을 생성하기 전에, 방이 너무 작아서
        // rand.Next(min, max) 호출 시 min >= max가 되는 오류를 방지합니다.
        // (L자 방의 팔 부분은 최소 5, 기둥/벽은 3+2+2=7이 필요)
        if (addObstacles && (room.Width < 7 || room.Height < 7))
        {
            addObstacles = false; // 방이 너무 작으면 장애물 생성 취소
        }
        // --- [끝] ---

        // [수정] addObstacles가 여전히 true일 때만 장애물 생성
        if (addObstacles)
        {
            // 방 크기에 따라 기둥 개수 결정 (50타일당 1개)
            int obstacleCount = (room.Width * room.Height) / 50;
            
            for (int i = 0; i < obstacleCount; i++)
            {
                // 3x3 크기 기둥 또는 1xN 벽
                if (rand.Next(0, 2) == 0) // 기둥
                {
                    int pillarX = rand.Next(room.Left + 2, room.Right - 2);
                    int pillarY = rand.Next(room.Top + 2, room.Bottom - 2);
                    
                    // 기둥 중앙 + 십자(+) 모양 4칸 (경로 보장)
                    if (pillarX > 0 && pillarX < MapWidth && pillarY > 0 && pillarY < MapHeight)
                    {
                        // [수정] 기둥이 맵 밖으로 나가지 않게
                        map[pillarX, pillarY] = '█';
                        if (pillarX + 1 < MapWidth) map[pillarX + 1, pillarY] = '█';
                        if (pillarY + 1 < MapHeight) map[pillarX, pillarY + 1] = '█';
                    }
                }
                else // 가로 또는 세로 벽
                {
                    int lineLength = rand.Next(3, Math.Max(4, room.Width / 3));
                    int lineX, lineY;

                    if (rand.Next(0, 2) == 0) // 가로 벽
                    {
                        lineX = rand.Next(room.Left + 2, room.Right - lineLength - 1);
                        lineY = rand.Next(room.Top + 2, room.Bottom - 2);
                        
                        for (int x = 0; x < lineLength; x++)
                            // [수정] 맵 경계 전체 확인
                            if (lineX + x < MapWidth && lineX + x > 0 && lineY > 0 && lineY < MapHeight) 
                                map[lineX + x, lineY] = '█';
                    }
                    else // 세로 벽
                    {
                        lineX = rand.Next(room.Left + 2, room.Right - 2);
                        lineY = rand.Next(room.Top + 2, room.Bottom - lineLength - 1);

                        for (int y = 0; y < lineLength; y++)
                            // [수정] 맵 경계 전체 확인
                            if (lineY + y < MapHeight && lineY + y > 0 && lineX > 0 && lineX < MapWidth) 
                                map[lineX, lineY + y] = '█';
                    }
                }
            }
        }
    }
    private void CreateHorizontalTunnel(int x1, int x2, int y) {
        for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++){
            if (x > 0 && x < MapWidth - 1 && y > 1 && y < MapHeight - 2){
                // 터널을 조금 더 두껍게 (3칸)
                map[x, y - 1] = '.'; map[x, y] = '.'; map[x, y + 1] = '.';
            }
        }
    }
    private void CreateVerticalTunnel(int y1, int y2, int x) {
        for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++){
            if (x > 1 && x < MapWidth - 2 && y > 0 && y < MapHeight - 1){
                // 터널을 조금 더 두껍게 (3칸)
                map[x - 1, y] = '.'; map[x, y] = '.'; map[x + 1, y] = '.';
            }
        }
    }
   private (int x, int y) GetRandomEmptyPosition(bool allowInBossRoom = false)
    {
        int x, y;
        int attempts = 0;
        
        do
        {
            x = rand.Next(1, MapWidth - 1);
            y = rand.Next(1, MapHeight - 1);
            attempts++;
            if (attempts > 500) return (1, 1);
            
            // 1. 보스 방 제외
            if (!allowInBossRoom && bossRoom.Contains(x, y)) { continue; }
            
            // [핵심 수정] 2. 안전 구역(시작 방) 절대 차단
            if (IsSafeZone(x, y))
            {
                continue; 
            }
            
            // 3. 플레이어 거리 체크 (추가 안전장치)
            int distSq = (player.X - x)*(player.X - x) + (player.Y - y)*(player.Y - y);
            if (distSq < 16) // 최소 4칸 거리 유지
            {
                continue;
            }

        }
        while (map[x, y] != '.' || 
               (monsters != null && monsters.Any(m => m.X == x && m.Y == y)) ||
               (traps != null && traps.Any(t => t.X == x && t.Y == y)) ||
               (chests != null && chests.Any(c => c.X == x && c.Y == y)));
               
        return (x, y);
    }

    private (int x, int y)? GetRandomCornerInRoom(Rectangle room)
    {
        List<(int x, int y)> corners = new List<(int x, int y)>();

        // 4개의 모서리 좌표
        (int x, int y)[] cornerPoints = new[]
        {
            (room.Left + 1, room.Top + 1),
            (room.Right - 1, room.Top + 1),
            (room.Left + 1, room.Bottom - 1),
            (room.Right - 1, room.Bottom - 1)
        };

        foreach (var (x, y) in cornerPoints)
        {
            // 좌표가 맵 바닥(.)이고, 주변 두 면이 벽(█)인지 확인
            if (map[x, y] == '.')
            {
                if ((map[x - 1, y] == '█' && map[x, y - 1] == '█') || // 좌상단
                    (map[x + 1, y] == '█' && map[x, y - 1] == '█') || // 우상단
                    (map[x - 1, y] == '█' && map[x, y + 1] == '█') || // 좌하단
                    (map[x + 1, y] == '█' && map[x, y + 1] == '█'))   // 우하단
                {
                    corners.Add((x, y));
                }
            }
        }

        if (corners.Count == 0) return null; // 구석을 못 찾음
        return corners[rand.Next(corners.Count)]; // 찾은 구석 중 랜덤 반환
    }
    
    private void SpawnTraps(TrapType type, char icon, int count) {
        for (int i = 0; i < count; i++){
            var (x, y) = GetRandomEmptyPosition(allowInBossRoom: false);
            traps.Add(new Trap(x, y, type, icon)); 
            map[x, y] = icon; 
        }
    }
    private void SpawnMonsters(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var (x, y) = GetRandomEmptyPosition(allowInBossRoom: false);
            // [수정]
            monsters.Add(MonsterDB.CreateRandomMonster(x, y, rand, currentStage));
        }
    }
    
    private void SpawnFieldBosses(int count)
    {
        // 1. 현재 스테이지의 필드 보스 ID 가져오기
        string fieldBossId = MonsterDB.GetFieldBossIdForStage(currentStage);
        
        for (int i = 0; i < count; i++)
        {
            // 2. 보스 방이 아닌 곳에 스폰
            var (x, y) = GetRandomEmptyPosition(allowInBossRoom: false);
            if (x == 1 && y == 1) continue; // 스폰 실패

            // 3. 몬스터 리스트에 추가
            monsters.Add(MonsterDB.CreateMonster(fieldBossId, x, y));
        }
    }

    // [신규] 상자 스폰
   private void SpawnChests(int count)
    {
        int chestsSpawned = 0;
        
        foreach (var room in rooms)
        {
            if (chestsSpawned >= count) break;
            if (room.Equals(bossRoom)) continue;

            // [핵심 수정] 방의 어떤 부분이라도 안전 구역에 걸치면 생략
            // (시작 방은 (2,2)에 고정되어 있으므로, 이 방은 통째로 스킵됨)
            if (IsSafeZone(room.X, room.Y))
            {
                continue;
            }

            // (이하 로직 유지)
            if (rand.NextDouble() < 0.5)
            {
                var corner = GetRandomCornerInRoom(room);
                if (corner.HasValue)
                {
                    var (x, y) = corner.Value;
                    
                    // [이중 체크] 생성하려는 좌표가 안전 구역인지 한 번 더 확인
                    if (IsSafeZone(x, y)) continue;

                    if (map[x, y] == '.' && !chests.Any(c => c.X == x && c.Y == y))
                    {
                        Chest chest = new Chest(x, y);
                        chests.Add(chest);
                        map[x, y] = chest.Icon;
                        chestsSpawned++;
                    }
                }
            }
        }
        
        // 남은 상자는 랜덤 스폰 (GetRandomEmptyPosition이 IsSafeZone을 체크하므로 안전)
        int remainingChests = count - chestsSpawned;
        for (int i = 0; i < remainingChests; i++)
        {
             var (x, y) = GetRandomEmptyPosition(allowInBossRoom: false);
             if (x == 1 && y == 1) continue; 
             
             Chest chest = new Chest(x, y);
             chests.Add(chest);
             map[x, y] = chest.Icon;
        }
    }
    #endregion

    // [수정] 게임 루프
    #region Game_Loop
    // [Game.cs] RunGameLoop 메서드 전체

    private void RunGameLoop()
    {
        // [수정] 'gameRunning = true' 제거 (Start()에서 초기화됨)
        bool needsRender = true; 

        while (gameRunning)
        {
            // ============================================================
            // 1. [Failsafe] 멀티플레이 상태 강제 복구 (싱글로 튕김 방지)
            // ============================================================
            if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
            {
                if (currentState == GameState.World)
                {
                    currentState = GameState.Multiplayer_World;
                    needsRender = true;
                }
                else if (currentState == GameState.Battle)
                {
                    currentState = GameState.Multiplayer_Battle;
                    needsRender = true;
                }
            }

            // ============================================================
            // [신규] 전투 턴 멈춤 방지 (Watchdog) - 호스트 또는 솔로 플레이어
            // ============================================================
            
            // [핵심 수정] 조건 변경: Host만이 아니라 'SoloMode'인 게스트도 감시해야 함
            bool isFightingSolo = (otherPlayer == null || otherPlayer.IsDead);
            bool canControlMonster = NetworkManager.Instance.IsHost;

            if (canControlMonster && currentState == GameState.Multiplayer_Battle)
            {
                // 1. 필요한 행동 횟수 계산
                int requiredActions = 2;
                if (player.IsDead) requiredActions--;
                if (otherPlayer != null && otherPlayer.IsDead) requiredActions--;
                if (isFightingSolo) requiredActions = 1;
                if (requiredActions < 1) requiredActions = 1;

                // 2. 멈춤 감지
                if (battleTurnCount >= requiredActions)
                {
                    TimeSpan idleTime = DateTime.Now - lastBattleActionTime;
                    if (idleTime.TotalSeconds > 3.0)
                    {
                        ProcessMultiplayerMonsterTurn();
                        lastBattleActionTime = DateTime.Now; 
                    }
                }
            }

            // ============================================================
            // 2. 네트워크 패킷 처리 (매 프레임)
            // ============================================================
            ProcessNetworkPackets();

            // 데이터 변경 신호가 있으면 렌더링 요청
            if (NetworkManager.Instance.IsDirty)
            {
                needsRender = true;
                NetworkManager.Instance.IsDirty = false;
            }

            if (currentState == GameState.Battle_Animation || 
                currentState == GameState.Battle_Intro ||
                currentState == GameState.Intro ||
                currentState == GameState.EndingStory ||
                currentState == GameState.Chest_Opening ||
                currentState == GameState.GameOver) // 깜빡임 효과 있는 상태들
            {
                needsRender = true;
            }

            // ============================================================
            // 3. 몬스터 이동 로직 (호스트 또는 싱글플레이)
            // ============================================================
            // [핵심 수정] 싱글플레이(!IsConnected)일 때도 몬스터가 스스로 움직이게 함
            if (NetworkManager.Instance.IsHost || !NetworkManager.Instance.IsConnected)
            {
                bool isWorldActive = (currentState == GameState.Multiplayer_World || 
                                      currentState == GameState.World || // [추가] 싱글 월드
                                      returnStateFromMenu == GameState.Multiplayer_World ||
                                      returnStateFromMenu == GameState.World || // [추가] 싱글 월드 복귀
                                      currentState == GameState.Inventory ||
                                      currentState == GameState.CharacterStat ||
                                      currentState == GameState.Pause || 
                                      currentState == GameState.HowToPlay);

                if (isWorldActive && currentBattleMonster == null)
                {
                    if ((DateTime.Now - lastMonsterMoveTime).TotalMilliseconds >= MONSTER_MOVE_INTERVAL_MS)
                    {
                        ProcessMonsterTurn_World(); 
                        lastMonsterMoveTime = DateTime.Now;
                        needsRender = true;
                    }
                }
            }
            
            // 화면 크기 변경 감지
            if (UpdateScreenSize(false)) { needsRender = true; }

            // ============================================================
            // 4. 애니메이션 및 시간 기반 상태 처리
            // ============================================================
            if (currentState == GameState.MainMenu)
            {
                ProcessMainMenuAnimation();
                needsRender = true; 
            }
            else if (currentState == GameState.StageIntro)
            {
                ProcessStageIntroAnimation();
                needsRender = true; 
            }
            else if (currentState == GameState.GameEnd)
            {
                ProcessMainMenuAnimation(); // 메인메뉴의 깜빡임 로직 재사용
                needsRender = true;
            }
            else if (currentState == GameState.EndingStory)
            {
                ProcessEndingStoryAnimation();
                needsRender = true;
            }
            else if (currentState == GameState.Battle_Intro)
            {
                ProcessBattleIntroAnimation();
                needsRender = true; 
            }
            else if (currentState == GameState.Intro)
            {
                ProcessIntroAnimation();
                needsRender = true; 
            }
            else if (currentState == GameState.Battle_Animation)
            {
                ProcessBattleAnimation();
                needsRender = true;
            }
            else if (currentState == GameState.Chest_Opening)
            {
                ProcessChestOpeningAnimation();
                needsRender = true;
            }
            else if (currentState == GameState.Battle_TurnDelay)
            {
                ProcessBattleTurnDelay(); 
                needsRender = true; // [중요] 딜레이 후 화면 갱신
            }
            else if (currentState == GameState.GameOver)
            {
                needsRender = true; // 깜빡임 효과
            }
            else if (currentState == GameState.Multiplayer_ClassSelect)
            {
                needsRender = true; // 대기 화면 갱신
            }
            else if (currentState == GameState.Multiplayer_Countdown)
            {
                ProcessCountdownLogic();
                needsRender = true;
            }

            // ============================================================
            // 5. [핵심 수정] 게임 오버 체크 (키 입력 없이 즉시 판정)
            // ============================================================
            bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;
            bool amIDead = (player != null && player.HP <= 0);
            bool isOtherDead = (otherPlayer != null && otherPlayer.HP <= 0);

            // 싱글: 나 죽으면 끝 / 멀티: 둘 다 죽으면 끝
            bool shouldGameOver = isMulti ? (amIDead && isOtherDead) : amIDead;

            if (shouldGameOver && 
                currentState != GameState.GameOver &&
                currentState != GameState.MainMenu && 
                currentState != GameState.Intro &&    
                currentState != GameState.HowToPlay &&
                currentState != GameState.Multiplayer_Lobby) // 로비 등 제외
            {
                currentState = GameState.GameOver;
                gameOverMenuIndex = 0; 
                AddLog("모두 쓰러졌습니다...");
                needsRender = true; // 즉시 화면 갱신
            }

            // ============================================================
            // 6. 키 입력 처리
            // ============================================================
            if (Console.KeyAvailable)
            {
                try { Console.SetCursorPosition(0, 0); } catch { }
                
                ConsoleKeyInfo key = Console.ReadKey(true);
                needsRender = true;

                // [6-1] 채팅 입력 중 처리 (최우선)
                if (isGameChatting)
                {
                    ProcessGameChatInput(key);
                    continue; // 게임 조작 건너뜀
                }

                // [6-2] 'T' 키 전역 감지 (채팅 시작)
                bool canChatState = (currentState == GameState.Multiplayer_World ||
                                     currentState == GameState.Multiplayer_Battle ||
                                     currentState == GameState.Battle_SkillSelect || 
                                     currentState == GameState.Battle_ItemMenu ||    
                                     currentState == GameState.Battle_ItemSubMenu || 
                                     currentState == GameState.Battle_TurnDelay ||
                                     currentState == GameState.Multiplayer_BattleResultWait ||
                                     currentState == GameState.LevelUp ||
                                     currentState == GameState.LootDrop ||
                                     currentState == GameState.LootSummary);

                if (isMulti && canChatState && (key.Key == ConsoleKey.T || char.ToUpper(key.KeyChar) == 'T'))
                {
                    isGameChatting = true;
                    gameChatInput = "";
                    continue; 
                }

                // [6-3] 전역 ESC 처리 (일시정지)
               if (key.Key == ConsoleKey.Escape)
                {
                    // 예외 상태들 (각 상태별 핸들러에서 ESC를 처리해야 하는 경우)
                    if (currentState == GameState.MainMenu || 
                        currentState == GameState.EndingStory ||
                        currentState == GameState.GameOver ||
                        currentState == GameState.Pause ||
                        currentState.ToString().StartsWith("Multiplayer_Lobby") ||
                        currentState == GameState.Multiplayer_RoomWait ||
                        currentState == GameState.Multiplayer_CreateRoom ||
                        currentState == GameState.Multiplayer_Nick ||
                        currentState == GameState.Multiplayer_ClassSelect || 
                        currentState == GameState.Multiplayer_Nick_ExitConfirm ||
                        currentState == GameState.Multiplayer_ClassSelect_ExitConfirm ||
                        
                        // [핵심 수정] IP 입력창과 비밀번호 창 예외 추가
                        currentState == GameState.Multiplayer_DirectIpConnect ||
                        currentState == GameState.Multiplayer_PasswordInput)
                    {
                        // Pass (각 상태별 핸들러로)
                    }
                    else 
                    {
                        // 렌더링용 변수(stateBeforePause)도 함께 갱신
                        stateBeforePause = currentState; 
                        
                        returnStateFromMenu = currentState;
                        currentState = GameState.Pause;
                        pauseMenuIndex = 0; 
                        continue; 
                    }
                }

                // [6-4] 각 상태별 입력 핸들러 호출
                switch (currentState)
                {
                    // [싱글플레이]
                    case GameState.MainMenu:        ProcessMainMenuInput(key); break;
                    case GameState.HowToPlay:       ProcessHowToPlayInput(key); break;
                    case GameState.World:           ProcessWorldInput(key); break;
                    case GameState.Battle:          ProcessBattleInput(key); break;
                    case GameState.Battle_SkillSelect: ProcessSkillSelectInput(key); break;
                    case GameState.Battle_ItemMenu:    ProcessItemMenuInput(key); break;
                    case GameState.Battle_ItemSubMenu: ProcessItemSubMenuInput(key); break;
                    case GameState.LevelUp:         ProcessLevelUpInput(key); break;
                    case GameState.LootDrop:        ProcessLootDropInput(key); break;
                    case GameState.LootSummary:     ProcessLootSummaryInput(key); break;
                    case GameState.Inventory:       ProcessInventoryInput(key); break;
                    case GameState.CharacterStat:   ProcessStatWindowInput(key); break;
                    case GameState.Chest_Confirm:   ProcessChestConfirmInput(key); break;
                    case GameState.Intro:           ProcessIntroInput(key); break;
                    case GameState.EndingStory:     ProcessEndingStoryInput(key); break;
                    case GameState.Pause:           ProcessPauseInput(key); break;
                    case GameState.GameOver:        ProcessGameOverInput(key); break;
                    case GameState.GameEnd:         ProcessGameEndInput(key); break;
                    case GameState.Multiplayer_ClassSelect: ProcessMultiplayerClassSelectInput(key); break;
                    case GameState.Multiplayer_DirectIpConnect: ProcessDirectIpInput(key); break; // [신규]
                    case GameState.Multiplayer_Countdown:
                        // 입력 무시
                        break;
                    case GameState.Multiplayer_World:       ProcessWorldInput(key); break;
                    case GameState.Multiplayer_Battle:      ProcessMultiplayerBattleInput(key); break;

                    // [멀티플레이]
                    case GameState.Multiplayer_Nick:            ProcessNicknameInput(key); break;
                    case GameState.Multiplayer_Lobby:           ProcessLobbyInput(key); break;
                    case GameState.Multiplayer_CreateRoom:      ProcessCreateRoomInput(key); break;
                    case GameState.Multiplayer_RoomWait:        ProcessRoomWaitInput(key); break;
                    case GameState.Multiplayer_PasswordInput:   ProcessPasswordInputWindow(key); break;
                    case GameState.Multiplayer_FullRoomWarning: ProcessFullRoomWarningInput(key); break;
                    
                    // [팝업창]
                    case GameState.Multiplayer_Lobby_ExitConfirm: ProcessLobbyExitConfirmInput(key); break;
                    case GameState.Multiplayer_Room_LeaveConfirm: ProcessRoomLeaveConfirmInput(key); break;
                    case GameState.Multiplayer_Nick_ExitConfirm: ProcessNickExitConfirmInput(key); break;
                    case GameState.Multiplayer_ClassSelect_ExitConfirm: ProcessClassSelectExitConfirmInput(key); break;
                }

                // 입력 버퍼 비우기 (빠른 연타 방지)
                while (Console.KeyAvailable)
                {
                    Console.ReadKey(true);
                }
            }

            // ============================================================
            // 7. 화면 그리기
            // ============================================================
            if (needsRender)
            {
                Render(); 
                PrintBufferToConsole(); 
                needsRender = false; 
            }
            if (currentState == GameState.Battle_Animation || currentState == GameState.Battle_Intro)
            {
                Thread.Sleep(5); // 아주 짧게 대기 (부드러운 연출)
            }
            else
            {
                Thread.Sleep(16); // 평소에는 60fps 유지
            }
        }
    }
    #endregion
    
    // [수정] 렌더링
    #region Rendering_Layout
    private bool UpdateScreenSize(bool force) 
    {
        if (force || screenWidth != Console.WindowWidth || screenHeight != Console.WindowHeight)
        {
            screenWidth = Math.Max(1, Console.WindowWidth);
            screenHeight = Math.Max(1, Console.WindowHeight); 
            Console.Clear(); 
            screenBuffer = new ScreenCell[screenHeight, screenWidth];
            worldMapHeight = (screenHeight * 2) / 3; 
            worldLogY = worldMapHeight;
            worldLogHeight = screenHeight - worldLogY;
            worldInfoX = (screenWidth * 3) / 5; 
            worldLogWidth = worldInfoX;
            worldInfoWidth = screenWidth - worldInfoX;
            logWindowY_BATTLE = screenHeight - logWindowHeight_BATTLE;
            playerStatusY_BATTLE = screenHeight - logWindowHeight_BATTLE - playerStatusHeight_BATTLE;
            battleArtHeight_BATTLE = playerStatusY_BATTLE; 
            return true; 
        }
        return false; 
    }

    // [수정] 렌더링 총괄
    private void Render()
    {
        ClearBuffer();

        switch (currentState)
        {
            case GameState.MainMenu:
                DrawMainMenu();
                break;
            case GameState.Multiplayer_Nick:       // 1. 닉네임 입력 화면
                DrawNicknameInput();
                break;
            case GameState.Multiplayer_Nick_ExitConfirm:
                DrawNicknameInput(); 
                DrawNickExitConfirm(); 
                break;
            case GameState.Multiplayer_Lobby:      // 2. 로비(방 목록) 화면
                DrawMultiplayerLobby();
                break;
            case GameState.Multiplayer_CreateRoom: // 3. 방 만들기 화면
                DrawCreateRoom();
                break;
            case GameState.Multiplayer_RoomWait:   // 4. 대기실 화면
                DrawRoomWait();
                break;
            case GameState.Multiplayer_Lobby_ExitConfirm:
                DrawMultiplayerLobby(); // 배경으로 로비를 먼저 그림
                DrawLobbyExitConfirm(); // 위에 팝업을 덮어씌움
                break;

            case GameState.Multiplayer_Room_LeaveConfirm:
                DrawRoomWait();        // 배경으로 대기실을 먼저 그림
                DrawRoomLeaveConfirm(); // 위에 팝업을 덮어씌움
                break;

            case GameState.Multiplayer_PasswordInput:
                DrawMultiplayerLobby(); // 배경으로 로비
                DrawPasswordInputWindow(); // 위에 비밀번호 창
                break;
            case GameState.Multiplayer_FullRoomWarning:
                DrawMultiplayerLobby(); // 배경으로 로비를 먼저 그림
                DrawFullRoomWarning();  // 위에 경고창 덮어씌움
                break;
            case GameState.Multiplayer_ClassSelect: DrawMultiplayerClassSelect(); break;
            case GameState.Multiplayer_ClassSelect_ExitConfirm:
                DrawMultiplayerClassSelect(); 
                DrawClassSelectExitConfirm();
                break;
            case GameState.Multiplayer_Countdown: // [신규]
                DrawMultiplayerCountdown(); 
                break;
            case GameState.Multiplayer_World:       DrawWorldLayout(); break; // 기존 World 그리기 재사용
            case GameState.Multiplayer_Battle:      DrawBattleLayout(); break; // 기존 Battle 그리기 재사용
            case GameState.Multiplayer_BattleResultWait: // [추가]
                DrawBattleResultWait();
                break;
            case GameState.Multiplayer_DirectIpConnect:
                DrawMultiplayerLobby(); // 배경으로 로비 유지
                DrawDirectIpWindow();   // 위에 팝업
                break;
            case GameState.Multiplayer_DirectConnect_Wait: DrawDirectConnectWait(); break;
            case GameState.HowToPlay:
                DrawHowToPlayWindow();
                break;
            case GameState.StageIntro:
                DrawStageIntroWindow();
                break;
            case GameState.World:
                DrawWorldLayout();
                break;
            case GameState.Intro:
                DrawIntroWindow();
                break;
            case GameState.EndingStory:
                DrawEndingStoryWindow();
                break;
            case GameState.Battle_Intro: // [신규]
                DrawBattleIntroWindow();
                break;
            case GameState.Battle:
            case GameState.Battle_SkillSelect:
            case GameState.Battle_ItemMenu:
            case GameState.Battle_ItemSubMenu:
            case GameState.Battle_Animation: // [수정] 애니메이션 상태도 기본 BattleLayout을 먼저 그리도록 추가
            case GameState.Battle_TurnDelay: // [신규] 딜레이 중에도 전투 레이아웃 유지
                DrawBattleLayout();
                break;

            // --- [핵심 수정] ---
            case GameState.LevelUp:
            case GameState.LootDrop:
            case GameState.LootSummary:
                // 아이템 창이 열리기 전 상태에 따라 배경을 다르게 그림
                if (stateBeforeLoot == GameState.Battle)
                {
                    DrawBattleLayout(); // 전투 배경
                }
                else // (stateBeforeLoot == GameState.World)
                {
                    DrawWorldLayout(); // 월드 배경
                }

                if (currentState == GameState.LevelUp) // [신규]
                {
                    DrawLevelUpWindow();
                }
                // 팝업 창을 덧그림
                else if (currentState == GameState.LootDrop)
                {
                    DrawLootDropWindow();
                }
                else // GameState.LootSummary
                {
                    DrawLootSummaryWindow();
                }
                break;
            // --- [끝] ---

            case GameState.Inventory:
                DrawWorldLayout();
                DrawInventoryWindow();
                break;
            case GameState.CharacterStat:
                DrawWorldLayout(); // 월드 배경
                DrawCharacterStatWindow(); // 스탯 창 띄우기
                break;
            case GameState.Chest_Confirm:
            case GameState.Chest_Opening:
                DrawWorldLayout(); // 월드 배경
                DrawChestWindow(); // 상자 창 띄우기 (새로 추가할 메서드)
                break;
            case GameState.GameOver:
                DrawGameOverWindow(); 
                break;
            case GameState.GameEnd:
                DrawGameEndWindow(); // (새로 추가할 메서드)
                break;
            case GameState.Pause:
                // 일시정지 직전 상태에 따라 배경을 그림
                if (stateBeforePause == GameState.World ||
                    stateBeforePause == GameState.Multiplayer_World || // [추가] 멀티 월드도 포함
                    stateBeforePause == GameState.Inventory)
                {
                    DrawWorldLayout();
                }
                // [핵심 수정] 인트로 중에 일시정지 했다면 인트로를 배경으로 그림
                else if (stateBeforePause == GameState.Intro)
                {
                    DrawIntroWindow();
                }
                else // (Battle 또는 하위 메뉴)
                {
                    DrawBattleLayout();
                }

                // 배경 위에 일시정지 창을 덧그림
                DrawPauseWindow();
                break;
        } 
    }

    private void ClearBuffer() 
    {
        for (int y = 0; y < screenHeight; y++) {
            for (int x = 0; x < screenWidth; x++) {
                screenBuffer[y, x] = ScreenCell.Empty;
            }
        }
    }

    // --- 레이아웃 그리기 함수들 ---
    private void DrawWorldLayout()
    {
        DrawBox(0, 0, screenWidth, worldMapHeight, "ASCII 미궁");
        DrawBox(0, worldLogY, worldLogWidth, worldLogHeight, "Log");
        DrawBox(worldInfoX, worldLogY, worldInfoWidth, worldLogHeight, "플레이어 정보");
        DrawMapToBuffer_Scrolling(screenWidth, worldMapHeight); 
        DrawInfoToBuffer(worldInfoX + 2, worldLogY + 2); 
        DrawLogRegion(0, worldLogY, worldLogWidth, worldLogHeight); 
    }

    private void DrawBattleLayout()
    {
        if (currentBattleMonster == null && 
            currentState != GameState.LootDrop && 
            currentState != GameState.LootSummary) return;

        // [핵심 수정] 상태가 변해도 네트워크 연결이 되어있으면 멀티플레이로 간주
        bool isMultiMode = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;

        // 1. 상단 타이틀 박스
        DrawBox(0, 0, screenWidth, battleArtHeight_BATTLE, "");
        
        // 타이틀 텍스트
        string title = isMultiMode ? " MULTI BATTLE " : " BATTLE STAGE ";
        DrawTextToBuffer(2, 0, title, ConsoleColor.Cyan);

        // 턴 표시 (중앙)
        string turnText;
        ConsoleColor turnColor;

        if (isMultiMode)
        {
            if (isMyBattleTurn) { turnText = "MY TURN"; turnColor = ConsoleColor.Green; }
            else { turnText = "WAITING..."; turnColor = ConsoleColor.Yellow; }
        }
        else // 싱글플레이
        {
            turnText = isPlayerTurn ? "PLAYER'S TURN" : "MONSTER'S TURN";
            turnColor = isPlayerTurn ? ConsoleColor.Green : ConsoleColor.Red;
        }

        string formattedTurnText = $"-- {turnText} --";
        int textX = screenWidth / 2 - GetDisplayWidth(formattedTurnText) / 2;
        DrawTextToBuffer(textX, 0, formattedTurnText, turnColor);

        // 2. 아트워크 그리기
        if (currentState == GameState.Battle || 
            currentState == GameState.Multiplayer_Battle || 
            currentState == GameState.Battle_Animation ||
            currentState == GameState.Battle_TurnDelay ||
            currentState == GameState.Battle_SkillSelect ||
            currentState == GameState.Battle_ItemMenu ||    // [확인] 이 줄이 있어야 함
            currentState == GameState.Battle_ItemSubMenu || // [확인] 이 줄이 있어야 함
            currentState == GameState.Battle_Intro)
        {
            DrawBattleArt();
        }

        // 3. 하단 UI
        DrawBox(0, playerStatusY_BATTLE, screenWidth, playerStatusHeight_BATTLE, "Status & Menu");
        DrawBattlePlayerStatus(); 
        
        DrawBox(0, logWindowY_BATTLE, screenWidth, logWindowHeight_BATTLE, "Battle Log");
        DrawLogRegion(0, logWindowY_BATTLE, screenWidth, logWindowHeight_BATTLE);
    }
    #endregion
    
    // (전투 아트 그리기... 변경 없음)
    #region Battle_Art
    private void DrawBattleArt()
    {
        if (currentBattleMonster == null) return;

        int centerY = battleArtHeight_BATTLE / 2;   
        
        bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;

        // [핵심 수정] 동료를 그릴지 여부 결정
        // 변경: !IsWaitingAtPortal (동료가 죽었어도 포탈을 탄 게 아니라면 그려야 함)
        bool drawOtherPlayer = isMulti && otherPlayer != null;

        // 1. 각 개체의 아스키 아트 너비(Width) 계산
        string[] p1Art = AsciiArt.GetPlayerArt(player.Class);
        int p1Width = 0;
        foreach (string line in p1Art) p1Width = Math.Max(p1Width, GetDisplayWidth(line));

        string[] mArt = AsciiArt.GetMonsterArt(currentBattleMonster.MonsterId);
        int mWidth = 0;
        foreach (string line in mArt) mWidth = Math.Max(mWidth, GetDisplayWidth(line));

        int p1CenterX;
        int p2CenterX = 0;

        // 2. 플레이어 위치 계산
        if (drawOtherPlayer)
        {
            // [멀티플레이 2인 모드 (동료 생존 or 사망)]
            
            // 플레이어 1 (나) 위치
            int p1LeftX = 3;
            p1CenterX = p1LeftX + (p1Width / 2); 

            // 플레이어 2 (동료) 너비 계산
            string[] p2Art = AsciiArt.GetPlayerArt(otherPlayer.Class);
            int p2Width = 0;
            foreach (string line in p2Art) p2Width = Math.Max(p2Width, GetDisplayWidth(line));

            // 플레이어 2 위치 (P1 끝나는 지점에서 6칸 띄움)
            int p2LeftX = p1LeftX + p1Width + 6; 
            p2CenterX = p2LeftX + (p2Width / 2);

            // [안전장치] 몬스터 영역 침범 방지
            int monsterSafeZone = screenWidth - mWidth - 6; 
            if (p2LeftX + p2Width > monsterSafeZone)
            {
                p2LeftX = monsterSafeZone - p2Width;
                p2CenterX = p2LeftX + (p2Width / 2);
            }
        }
        else
        {
            // [싱글/솔로 모드 (나 혼자)]
            p1CenterX = screenWidth / 4; 
        }

        // 3. [플레이어 1 (나)] 그리기
        // [수정] 죽었으면 회색, 살았으면 흰색
        ConsoleColor p1Color = player.IsDead ? ConsoleColor.DarkGray : ConsoleColor.White;
        
        // 피격 연출 색상 오버라이드
        if (currentState == GameState.Battle_Animation && showHitOverlay && currentAnimationTarget == player)
        {
            if (!string.IsNullOrEmpty(currentAsciiDamageString)) p1Color = ConsoleColor.White;
            else if (customBlinkColor != ConsoleColor.Black) p1Color = customBlinkColor;
        }
        DrawSingleEntityArt(player, p1CenterX, centerY, p1Color);

        // 4. [플레이어 2 (동료)] 그리기
        if (drawOtherPlayer)
        {
            // [수정] 죽었으면 회색, 살았으면 하늘색
            ConsoleColor p2Color = otherPlayer.IsDead ? ConsoleColor.DarkGray : ConsoleColor.Cyan;

            // 동료 피격 연출 (필요시)
            if (currentState == GameState.Battle_Animation && showHitOverlay && currentAnimationTarget == otherPlayer)
            {
                 // (동료 피격 색상 로직이 필요하다면 여기에 추가)
            }

            DrawSingleEntityArt(otherPlayer, p2CenterX, centerY, p2Color);
        }

        // 5. [몬스터] 위치 계산 및 그리기
        int mLeftX = (screenWidth * 2) / 3;
        int mCenterX = mLeftX + (mWidth / 2);

        Monster monster = currentBattleMonster;
        ConsoleColor monsterColor = ConsoleColor.Red;
        if (monster.MonsterId == "mimic") monsterColor = ConsoleColor.Yellow;

        DrawSingleEntityArt(monster, mCenterX, centerY, monsterColor, true);
    }

    // [헬퍼] 단일 개체(플레이어/몬스터) 아트 및 데미지 그리기
    private void DrawSingleEntityArt(object entity, int centerX, int centerY, ConsoleColor baseColor, bool isMonster = false)
    {
        string[] art;
        int artOffsetY = 0;
        int artOffsetX = 0;
        string nameInfo = "";
        int currentHP = 0, maxHP = 0;

        bool isMultiMode = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;

        if (entity is Player p)
        {
            art = AsciiArt.GetPlayerArt(p.Class);
            artOffsetY = p.ArtOffsetY;
            artOffsetX = isMultiMode ? 0 : p.ArtOffsetX;
            nameInfo = $"LV.{p.Level} {p.Class}";
            currentHP = p.HP; maxHP = p.MaxHP;
            if (p.IsDead) baseColor = ConsoleColor.DarkGray;
        }
        else if (entity is Monster mob) 
        {
            art = AsciiArt.GetMonsterArt(mob.MonsterId);
            artOffsetY = mob.ArtOffsetY;
            artOffsetX = mob.ArtOffsetX;
            nameInfo = mob.Name;
            currentHP = mob.HP; maxHP = mob.MaxHP;
            if (mob.MonsterId == "mimic") baseColor = ConsoleColor.Yellow;
        }
        else return;

        // --- 1. 아트 크기 및 그리기 좌표 계산 ---
        int artHeight = art.Length;
        int maxWidth = 0;
        foreach (string line in art) maxWidth = Math.Max(maxWidth, GetDisplayWidth(line));
        
        // drawX, drawY는 아스키 아트의 "좌측 상단" 모서리 좌표입니다.
        int drawX = centerX - (maxWidth / 2) + artOffsetX;
        int drawY = centerY - (artHeight / 2) + artOffsetY;

        // 오른쪽 테두리 침범 방지 (Clamping)
        if (drawX + maxWidth > screenWidth - 2)
        {
            drawX = screenWidth - maxWidth - 2;
        }

        int actualArtCenterX = drawX + (maxWidth / 2);

        ConsoleColor finalColor = baseColor;

        if (currentState == GameState.Battle_Animation && currentAnimationTarget == entity)
        {
            // 경과 시간 계산
            double elapsedMs = (DateTime.Now - battleAnimationStartTime).TotalMilliseconds;
            
            // 100ms 주기로 깜빡임 (0~99: ON, 100~199: OFF ...)
            // 짝수 구간일 때만 오버레이 색상(빨강/흰색 등) 적용
            bool isHighlight = ((int)(elapsedMs / ANIM_BLINK_DURATION_MS)) % 2 == 0;

            if (isHighlight)
            {
                finalColor = (customBlinkColor != ConsoleColor.Black) ? customBlinkColor : ConsoleColor.White;
            }
        }

        for (int i = 0; i < artHeight; i++)
        {
            DrawTextToBuffer(drawX, drawY + i, art[i], finalColor, ConsoleColor.Black, true);
        }

        // --- 2. 데미지/회복 숫자 ---
        if ((currentState == GameState.Battle_Animation || currentState == GameState.Battle_TurnDelay) &&
             currentAnimationTarget == entity && !string.IsNullOrEmpty(currentAsciiDamageString))
        {
            int dmgX, dmgY;

            if (entity is Player)
            {
                // 플레이어: 머리 위 중앙
                dmgX = actualArtCenterX; // [수정] centerX -> actualArtCenterX               
                dmgY = drawY - 4; 
            }
            else
            {
                // [핵심 수정] 몬스터 데미지 표시 위치 개선 (싱글플레이 코드 스타일 참고)
                // 몬스터의 경우, 아스키 아트의 왼쪽 상단(drawX, drawY)을 기준으로 하되,
                // 너무 딱 붙지 않도록 왼쪽으로 여유를 두고(-4), 위쪽으로도 살짝 올립니다(-2).
                
                // 이전 코드 참고: 
                // int dmgX = monsterBlockStartX - 2; 
                // int dmgY = monsterArtY + (monsterArt.Length / 2) - 2; 
                
                // 사용자 요청 반영: 왼쪽 중앙보다 살짝 위 -> 머리 부분 왼쪽 옆
                
                // 1. 아트의 높이 중앙
                int artCenterY = drawY + (artHeight / 2);
                
                // 2. 왼쪽으로 확실히 이동 (아트 시작점 - 6칸)
                dmgX = drawX - 2; 
                
                // 3. 중앙보다 살짝 위로 (2칸 위)
                dmgY = artCenterY - 2;
            }

            // 화면 밖 방지
            if (dmgY < 2) dmgY = 2; 
            if (dmgX < 2) dmgX = 2;

            ConsoleColor numColor = ConsoleColor.White;
            if (currentAsciiDamageString.StartsWith("+")) numColor = ConsoleColor.Green; 
            else if (currentAsciiDamageString.StartsWith("-")) numColor = ConsoleColor.Red; 
            else if (currentAsciiDamageString.StartsWith("M")) numColor = ConsoleColor.Cyan; 

            // [수정] padding을 -1 (오른쪽 정렬)로 설정하여 숫자가 길어져도 왼쪽으로 늘어나게 함
            // (몬스터 몸체 쪽으로 침범하지 않도록)
            DrawAsciiNumber(dmgX, dmgY, currentAsciiDamageString, numColor, -1);
        }

        // --- 3. 하단 정보 (HP바 등) ---
        if (isMonster && entity is Monster mobInfo)
        {
            int infoY = drawY + artHeight + 1;
            
            string hpText = $"{mobInfo.Name} (HP:{mobInfo.HP}/{mobInfo.MaxHP})"; 
            
            // [핵심 수정] centerX 대신 actualArtCenterX 사용
            DrawTextToBuffer(actualArtCenterX - (GetDisplayWidth(hpText)/2), infoY, hpText, ConsoleColor.Yellow);
            DrawBarToBuffer(actualArtCenterX - 10, infoY + 1, mobInfo.HP, mobInfo.MaxHP, 20, ConsoleColor.Red);

            // 상태이상 아이콘 위치도 수정
            int iconX = actualArtCenterX + 12;
            foreach (var status in mobInfo.StatusOrder)
            {
                string icon = "";
                ConsoleColor iconColor = ConsoleColor.White;
                switch (status)
                {
                    case StatType.StunChance: icon = "@"; break;
                    case StatType.PoisonStabDamage: icon = "P"; iconColor = ConsoleColor.Magenta; break;
                    case StatType.StrongPoison: icon = "X"; iconColor = ConsoleColor.Red; break;
                    case StatType.BleedChance: icon = "♦"; iconColor = ConsoleColor.DarkRed; break;
                    case StatType.AtkDebuff: icon = "▼"; iconColor = ConsoleColor.Blue; break;
                }
                if (icon != "") { DrawTextToBuffer(iconX, infoY + 1, icon, iconColor); iconX += 2; }
            }
        }
        else if (isMultiMode && entity is Player otherP && otherP != player)
        {
            int infoY = drawY + artHeight + 1;
            
            string friendInfo = $"{otherPlayerNickname} ({otherP.Class}) (HP:{otherP.HP}/{otherP.MaxHP})";
            DrawTextToBuffer(centerX - (GetDisplayWidth(friendInfo)/2), infoY, friendInfo, ConsoleColor.Cyan);
            
            // HP 바
            DrawBarToBuffer(centerX - 6, infoY + 1, otherP.HP, otherP.MaxHP, 12, ConsoleColor.Green);
            
            // [수정] 동료 마나바 제거 (요청 사항 반영)
        }
    }
    #endregion
    
    // [수정] 전투 UI
    #region Battle_UI_GameOver
    private ConsoleColor GetColorForRarity(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Legendary => ConsoleColor.Magenta,
            ItemRarity.Unique => ConsoleColor.Yellow,
            ItemRarity.Rare => ConsoleColor.Cyan,
            _ => ConsoleColor.White
        };
    }

   private void DrawBattlePlayerStatus()
    {
        int x = 2;
        int y = playerStatusY_BATTLE + 2; 

        // --- 왼쪽: 전투 메뉴 ---
        // [핵심 수정] Multiplayer_Battle 상태 추가
       if (currentState == GameState.Battle || 
            currentState == GameState.Multiplayer_Battle || 
            currentState == GameState.Battle_Animation ||
            currentState == GameState.Battle_TurnDelay)
        {
            // 메뉴 텍스트
            DrawTextToBuffer(x, y,     (battleMenuIndex == 0 ? "► 1. 기본 공격" : "  1. 기본 공격"), 
                             battleMenuIndex == 0 ? ConsoleColor.Yellow : ConsoleColor.White);
            
            // 멀티플레이 시 스킬/아이템은 아직 미구현이라 회색 처리하거나 그대로 둠
            // (여기선 그대로 둠)
            DrawTextToBuffer(x, y + 1, (battleMenuIndex == 1 ? "► 2. 스킬" : "  2. 스킬"), 
                             battleMenuIndex == 1 ? ConsoleColor.Yellow : ConsoleColor.White);
            DrawTextToBuffer(x, y + 2, (battleMenuIndex == 2 ? "► 3. 아이템" : "  3. 아이템"), 
                             battleMenuIndex == 2 ? ConsoleColor.Yellow : ConsoleColor.White); 
            DrawTextToBuffer(x, y + 3, (battleMenuIndex == 3 ? "► 4. 후퇴" : "  4. 후퇴"), 
                             battleMenuIndex == 3 ? ConsoleColor.Yellow : ConsoleColor.White);
        }
        else if (currentState == GameState.Battle_SkillSelect)
        {
            DrawTextToBuffer(x, y + 5, "[B] 뒤로가기", ConsoleColor.Yellow);
            
            Action<int, string, Skill, ConsoleColor> DrawSkill = 
                (index, key, skill, defaultColor) =>
            {
                string prefix = (skillMenuIndex == index) ? "►" : " ";
                bool mpAvailable = player.MP >= skill.MpCost;
                bool cooldownReady = skill.CurrentCooldown <= 0; // [신규] 쿨타임 확인

                ConsoleColor color;
                string statusText = "";

                // [신규] 쿨타임 텍스트 및 색상 처리
                if (!cooldownReady)
                {
                    statusText = $"(쿨타임 {skill.CurrentCooldown}턴)";
                    color = ConsoleColor.DarkGray; // 쿨타임 중이면 회색
                }
                else if (!mpAvailable)
                {
                    statusText = "(MP 부족)";
                    color = ConsoleColor.DarkGray;
                }
                else
                {
                    statusText = $"(MP {skill.MpCost})";
                    if (skillMenuIndex == index)
                        color = (skill.IsUltimate) ? ConsoleColor.Magenta : ConsoleColor.Yellow; // 궁극기는 보라색 강조
                    else
                        color = defaultColor;
                }

                DrawTextToBuffer(x, y + index, $"{prefix} [{key}] {skill.Name.PadRight(10)} {statusText}", color);
            };

            if (player.Skills.Count > 0) DrawSkill(0, "Q", player.Skills[0], ConsoleColor.White);
            if (player.Skills.Count > 1) DrawSkill(1, "W", player.Skills[1], ConsoleColor.White);
            if (player.Skills.Count > 2) DrawSkill(2, "E", player.Skills[2], ConsoleColor.White);
            if (player.Skills.Count > 3) DrawSkill(3, "R", player.Skills[3], ConsoleColor.Cyan);
        }
        else if (currentState == GameState.Battle_ItemMenu)
        {
            // [수정] itemMenuIndex를 기반으로 하이라이트
            DrawTextToBuffer(x, y + 5, "[B] 뒤로가기", ConsoleColor.Yellow);
            
            int hpPotions = player.ConsumableInventory.Count(i => i.CType == ConsumableType.HealthPotion);
            int mpPotions = player.ConsumableInventory.Count(i => i.CType == ConsumableType.ManaPotion);

            string hpPrefix = (itemMenuIndex == 0) ? "►" : " ";
            ConsoleColor hpColor = (itemMenuIndex == 0) ? ConsoleColor.Yellow : (hpPotions > 0 ? ConsoleColor.White : ConsoleColor.DarkGray);
            DrawTextToBuffer(x, y, $"{hpPrefix} [1] HP 회복 물약 (x{hpPotions})", hpColor);

            string mpPrefix = (itemMenuIndex == 1) ? "►" : " ";
            ConsoleColor mpColor = (itemMenuIndex == 1) ? ConsoleColor.Yellow : (mpPotions > 0 ? ConsoleColor.White : ConsoleColor.DarkGray);
            DrawTextToBuffer(x, y + 1, $"{mpPrefix} [2] MP 회복 물약 (x{mpPotions})", mpColor);
        }
        else if (currentState == GameState.Battle_ItemSubMenu)
        {
            // [수정] itemSubMenuIndex를 기반으로 하이라이트
            var distinctItemGroups = player.ConsumableInventory
                .Where(item => item.CType == currentItemSubMenu) 
                .GroupBy(item => item.Name) 
                .Select(group => new { 
                    Item = group.First(),
                    Count = group.Count()  
                })
                .OrderBy(g => g.Item.Rarity) 
                .ToList();

            int i = 0;
            for (i = 0; i < distinctItemGroups.Count; i++)
            {
                if (y + i >= playerStatusY_BATTLE + playerStatusHeight_BATTLE - 2) break; 
                var group = distinctItemGroups[i];
                
                string prefix = (itemSubMenuIndex == i) ? "►" : " ";
                ConsoleColor color = (itemSubMenuIndex == i) ? ConsoleColor.Yellow : GetColorForRarity(group.Item.Rarity);
                
                DrawTextToBuffer(x, y + i, $"{prefix} [{i + 1}] {group.Item.Name} (x{group.Count})", color);
            }
            
            DrawTextToBuffer(x, y + i + 1, "[B] 뒤로가기", ConsoleColor.Yellow);
        }

        // --- 오른쪽: 플레이어 스탯 (UI 정렬) --- (변경 없음)
        int statX = screenWidth / 2;
        int barWidth = 15;
        int barStartX = statX + 5; 
        string hpLabel  = "HP:".PadRight(4);
        string mpLabel  = "MP:".PadRight(4);
        string strLabel = "STR:".PadRight(4);
        string atkLabel = "ATK:".PadRight(4);
        DrawTextToBuffer(statX, y, hpLabel, ConsoleColor.White);
        DrawBarToBuffer(barStartX, y, player.HP, player.MaxHP, barWidth, ConsoleColor.Red);
        DrawTextToBuffer(barStartX + barWidth + 2, y, $"{player.HP} / {player.MaxHP}", ConsoleColor.White);
        DrawTextToBuffer(statX, y + 1, mpLabel, ConsoleColor.White);
        DrawBarToBuffer(barStartX, y + 1, player.MP, player.MaxMP, barWidth, ConsoleColor.Blue);
        DrawTextToBuffer(barStartX + barWidth + 2, y + 1, $"{player.MP} / {player.MaxMP}", ConsoleColor.White);
        DrawTextToBuffer(statX, y + 3, $"{strLabel} {player.STR} | INT: {player.INT} | DEX: {player.DEX}", ConsoleColor.Yellow);
        DrawTextToBuffer(statX, y + 4, $"{atkLabel} {player.ATK} | DEF: {player.DEF}", ConsoleColor.White);
    }
    private void DrawGameOverLayout()
    {
        string msg = "GAME OVER";
        DrawTextToBuffer(screenWidth/2 - msg.Length/2, screenHeight/2, msg, ConsoleColor.Red);
        string msg2 = "아무 키나 눌러 종료합니다.";
        DrawTextToBuffer(screenWidth/2 - msg2.Length/2, screenHeight/2 + 1, msg2, ConsoleColor.Gray);
        DrawBox(0, logWindowY_BATTLE, screenWidth, logWindowHeight_BATTLE, "Final Log");
        DrawLogRegion(0, logWindowY_BATTLE, screenWidth, logWindowHeight_BATTLE);
    }
    #endregion
    
    // [수정] 장비 비교 창, 아이템 요약 창
    #region Loot_Window
    private void DrawLootDropWindow()
    {
        if (currentLootEquipment == null) return;
        Equipment newItem = currentLootEquipment;
        player.EquippedGear.TryGetValue(newItem.Slot, out Equipment? oldItem);
        int modCount = newItem.Modifiers.Count + (oldItem?.Modifiers.Count ?? 1);
        int width = 45;
        int height = 15 + modCount; 
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;
        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black); // [신규] 배경
        DrawBox(startX, startY, width, height, "장비 획득! (비교)");
        for (int y = startY + 1; y < startY + height - 1; y++) {
            for (int x = startX + 1; x < startX + width - 1; x++) {
                DrawToBuffer(x, y, ' ', ConsoleColor.Gray, ConsoleColor.Black);
            }
        }
        
        int yDraw = startY + 2;
        DrawTextToBuffer(startX + 2, yDraw++, "--- [기존 장비] ---", ConsoleColor.Gray);
        if (oldItem == null) {
            DrawTextToBuffer(startX + 4, yDraw++, $"({newItem.Slot}) 착용 중인 장비 없음", ConsoleColor.DarkGray);
            yDraw++;
        }
        else {
            DrawTextToBuffer(startX + 4, yDraw++, oldItem.Name, GetColorForRarity(oldItem.Rarity));
            foreach (var mod in oldItem.Modifiers) {
                DrawTextToBuffer(startX + 6, yDraw++, mod.GetDescription(), ConsoleColor.Gray);
            }
            yDraw++;
        }
        DrawTextToBuffer(startX + 2, yDraw++, "--- [새 장비] ---", ConsoleColor.Cyan);
        DrawTextToBuffer(startX + 4, yDraw++, newItem.Name, GetColorForRarity(newItem.Rarity));
        foreach (var mod in newItem.Modifiers) {
            DrawTextToBuffer(startX + 6, yDraw++, mod.GetDescription(), ConsoleColor.White);
        }

        // [핵심 수정] Y/N 버튼 하이라이트
        yDraw = startY + height - 3;
        DrawTextToBuffer(startX + 2, yDraw++, "-------------------------------------------", ConsoleColor.DarkGray);
        
        string yes = " [Y] 교체하기 ";
        string no = " [N] 버리기 ";
        
        // (lootDropIndex == 0) ? Yes Highlight
        DrawTextToBuffer(startX + 4, yDraw, yes, 
                         lootDropIndex == 0 ? ConsoleColor.Black : ConsoleColor.White, 
                         lootDropIndex == 0 ? ConsoleColor.Green : ConsoleColor.Black);

        DrawTextToBuffer(startX + 4 + GetDisplayWidth(yes) + 2, yDraw, no, 
                         lootDropIndex == 1 ? ConsoleColor.Black : ConsoleColor.White, 
                         lootDropIndex == 1 ? ConsoleColor.Red : ConsoleColor.Black);
    }
    
    private void DrawLootSummaryWindow()
    {
        if (currentLootList.Count == 0) return;
        int width = 40;
        int height = 5 + currentLootList.Count;
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;
        DrawBox(startX, startY, width, height, "획득한 아이템");

        // 창 내부 클리어
        for (int y = startY + 1; y < startY + height - 1; y++) {
            for (int x = startX + 1; x < startX + width - 1; x++) {
                DrawToBuffer(x, y, ' ', ConsoleColor.Gray, ConsoleColor.Black);
            }
        }
        
        int yDraw = startY + 2;
        foreach (var item in currentLootList)
        {
            ConsoleColor color = GetColorForRarity(item.Rarity);
            DrawTextToBuffer(startX + 4, yDraw++, item.Name, color); 
        }
        yDraw = startY + height - 2;
        DrawTextToBuffer(startX + 2, yDraw, "계속하려면 [Enter]를 누르세요.", ConsoleColor.Yellow);
    }
    #endregion
    
    // [수정] 인벤토리 창
    #region Inventory_Window
    // Game.cs의 DrawInventoryWindow 메서드를 아래 코드로 통째로 교체하세요.

    private void DrawInventoryWindow()
    {
        // --- 1. Define Layout ---
        
        // [수정] 고정 너비 160 대신, 화면 너비의 90%를 사용
        int width = (int)(screenWidth * 0.9); 
        // [신규] 화면이 너무 작아도 최소 100의 너비는 확보
        width = Math.Max(width, 100); 

        int startX = (screenWidth - width) / 2;
        int startY = 3; 

        // [수정] 장비 열 너비를 동적으로 계산 ( (전체너비 - 좌우패딩 4) / 5열 )
        int equipColWidth = (width - 4) / 5;
        // [수정] 장비 열의 시작 X좌표
        int equipSectionX = startX + 2; 

        var columns = new (EquipmentSlot Slot, int X)[] 
        {
            (EquipmentSlot.Weapon, equipSectionX),
            (EquipmentSlot.Head,   equipSectionX + equipColWidth),     
            (EquipmentSlot.Armor,  equipSectionX + (equipColWidth * 2)), 
            (EquipmentSlot.Gloves, equipSectionX + (equipColWidth * 3)), 
            (EquipmentSlot.Boots,  equipSectionX + (equipColWidth * 4))  
        };
        
        // --- 2. Find Max Height (장비) ---
        int maxEquipLines = 0;
        var equipCache = new Dictionary<EquipmentSlot, Equipment?>();
        foreach (var (slot, x) in columns)
        {
            player.EquippedGear.TryGetValue(slot, out Equipment? equip);
            equipCache[slot] = equip;
            
            int linesForThisSlot = 2; // 1(슬롯이름) + 1(아이템이름)
            if (equip != null)
            {
                linesForThisSlot += equip.Modifiers.Count; // 옵션 개수
            }
            maxEquipLines = Math.Max(maxEquipLines, linesForThisSlot);
        }

        // --- 3. Find Consumable Data ---
        var consumableGroups = player.ConsumableInventory
            .GroupBy(item => item.Name)
            .Select(group => new { Name = group.Key, Rarity = group.First().Rarity, Count = group.Count() })
            .OrderBy(g => g.Rarity)
            .ToList();
        
        int consumableTitleLines = 1;
        int consumablePaddingLines = 1; 
        
        // (사용자 코드 원본: 5)
        int maxConsumableRows = 5; 
        int numCols = 3; 
        
        int numRowsNeeded = (int)Math.Ceiling(consumableGroups.Count / (double)numCols); 
        if (numRowsNeeded == 0) numRowsNeeded = 1; // (비어있음) 텍스트 1줄
        
        int consumableListHeight = Math.Min(numRowsNeeded, maxConsumableRows); 

        // --- 4. Calculate Final Window Height & Draw ---
        int equipmentSectionHeight = 1 + maxEquipLines + 2; // 1(title) + lines + 2(padding)
        int consumableSectionHeight = consumableTitleLines + consumablePaddingLines + maxConsumableRows; // 1(title) + 1(padding) + 5(rows)
        
        int height = 5 + equipmentSectionHeight + consumableSectionHeight; 
        
        height = Math.Min(height, screenHeight - 2); 
        height = Math.Max(height, 20); 
        
        DrawBox(startX, startY, width, height, "인벤토리");

        // 5. Clear Background
        for (int y = startY + 1; y < startY + height - 1; y++) {
            for (int x = startX + 1; x < startX + width - 1; x++) {
                DrawToBuffer(x, y, ' ', ConsoleColor.Gray, ConsoleColor.Black);
            }
        }

        // --- 6. Draw Equipment (가로 정렬) ---
        int yDraw = startY + 2;
        DrawTextToBuffer(startX + 2, yDraw++, "--- [착용 장비] ---", ConsoleColor.Cyan);
        yDraw++; // Padding

        int yEquipStart = yDraw;
        
        // [수정] 장비 열의 '내용물' 너비 (열 너비 - 좌우 공백 2)
        int equipContentWidth = equipColWidth - 2;

        for (int line = 0; line < maxEquipLines; line++)
        {
            foreach(var (slot, x) in columns)
            {
                int yCurrent = yEquipStart + line;
                if (yCurrent >= startY + height - 3) break; 
                Equipment? equip = equipCache[slot];
                
                // [수정] 고정값 25 대신 동적 'equipContentWidth' 사용
                int contentWidth = equipContentWidth; 

                if (line == 0) {
                    DrawTextToBuffer(x, yCurrent, $"[{slot}]", ConsoleColor.White);
                }
                else if (line == 1) {
                    if (equip == null) {
                        DrawTextToBuffer(x, yCurrent, "(비어있음)", ConsoleColor.DarkGray);
                    } else {
                        string name = TruncateStringByDisplayWidth(equip.Name, contentWidth);
                        DrawTextToBuffer(x, yCurrent, name, GetColorForRarity(equip.Rarity));
                    }
                }
                else if (equip != null && line >= 2) {
                    int modIndex = line - 2;
                    if (modIndex < equip.Modifiers.Count) {
                        string modDesc = TruncateStringByDisplayWidth(equip.Modifiers[modIndex].GetDescription(), contentWidth);
                        DrawTextToBuffer(x, yCurrent, $"- {modDesc}", ConsoleColor.Gray);
                    }
                }
            }
        }

        // --- 7. Draw Consumables (3열) ---
        yDraw = yEquipStart + maxEquipLines + 1;
        DrawTextToBuffer(startX + 2, yDraw++, "--- [소비 아이템] ---", ConsoleColor.Cyan);
        yDraw++; // 간격(Padding) 추가

        int yConsumableStart = yDraw;
        int yDrawClose = startY + height - 2;

        // [수정] 3열 너비 자동 계산 (이 로직은 원래도 동적이었습니다.)
        int colContentWidth = (width - 8) / numCols; 
        int col1X = startX + 4;
        int col2X = startX + 4 + colContentWidth; 
        int col3X = startX + 4 + (colContentWidth * 2); 

        if (consumableGroups.Count == 0)
        {
            DrawTextToBuffer(col1X, yDraw, "(소비 아이템 없음)", ConsoleColor.DarkGray);
        }
        else
        {
            for (int i = 0; i < consumableGroups.Count; i++)
            {
                var group = consumableGroups[i];
                
                int yOffset = i % maxConsumableRows; // 행 (0~4)
                int colIndex = i / maxConsumableRows; // 열 (0, 1, 2)

                if (colIndex > (numCols - 1)) break; // 3열까지만 표시

                int currentX;
                if (colIndex == 0) currentX = col1X;
                else if (colIndex == 1) currentX = col2X;
                else currentX = col3X;
                
                int currentY = yConsumableStart + yOffset;

                if (currentY >= yDrawClose) continue; 

                ConsoleColor color = GetColorForRarity(group.Rarity);
                string text = TruncateStringByDisplayWidth($"{group.Name} (x{group.Count})", colContentWidth - 2); // 패딩 2
                DrawTextToBuffer(currentX, currentY, text, color);
            }
        }

        // --- 8. 닫기 버튼 ---
        DrawTextToBuffer(startX + 2, yDrawClose, "닫으려면 [E] 또는 [B]를 누르세요.", ConsoleColor.Yellow);
    }
    
    private void ProcessInventoryInput(ConsoleKeyInfo key)
    {
       // 1. 닫기 (E, B, ESC)
        if (key.Key == ConsoleKey.E || key.Key == ConsoleKey.B || key.Key == ConsoleKey.Escape)
        {
            currentState = returnStateFromMenu; // [수정] 저장된 상태로 복귀
            return;
        }
        
        char c = char.ToUpper(key.KeyChar);
        if (c == 'E' || c == 'ㄷ' || c == 'B' || c == 'ㅠ')
        {
            currentState = returnStateFromMenu; // [수정]
            return;
        }
    }
    #endregion


    // (월드 맵 그리기... 변경 없음)
    #region WorldMap_Rendering
    private void DrawMapToBuffer_Scrolling(int viewWidth, int viewHeight) 
    {
        int viewportWidth = viewWidth - 2; 
        int viewportHeight = viewHeight - 2; 
        int cameraX = player.X - (viewportWidth / 2);
        int cameraY = player.Y - (viewportHeight / 2);
        
        // 카메라 범위 제한
        cameraX = Math.Max(0, Math.Min(cameraX, MapWidth - viewportWidth));
        cameraY = Math.Max(0, Math.Min(cameraY, MapHeight - viewportHeight));

        for (int y = 0; y < viewportHeight; y++){
            for (int x = 0; x < viewportWidth; x++){
                int mapX = cameraX + x;
                int mapY = cameraY + y;
                
                if (mapX >= 0 && mapX < MapWidth && mapY >= 0 && mapY < MapHeight){
                    char tile = map[mapX, mapY];
                    
                    ConsoleColor fgColor = ConsoleColor.DarkGray; 
                    ConsoleColor bgColor = ConsoleColor.Black; // 기본 배경

                    // [신규] 포탈 주변 범위 시각화
                    // 포탈이 존재하고(-1 아님), 현재 타일이 포탈 범위 내라면 배경색 변경
                    if (portalPosition.x != -1)
                    {
                        int distSq = (mapX - portalPosition.x) * (mapX - portalPosition.x) + 
                                     (mapY - portalPosition.y) * (mapY - portalPosition.y);
                        
                        if (distSq <= PORTAL_DETECTION_RANGE_SQ)
                        {
                            bgColor = ConsoleColor.DarkMagenta; // 보라색 배경
                        }
                    }

                    // 타일 색상 설정
                    if (tile == '█') fgColor = ConsoleColor.Gray; 
                    else if (tile == '^' || tile == '*') fgColor = ConsoleColor.DarkRed; 
                    else if (tile == '.') 
                    {
                        // 포탈 범위 안의 바닥은 좀 더 밝은 보라색으로 점을 찍어줌
                        if (bgColor == ConsoleColor.DarkMagenta) fgColor = ConsoleColor.Magenta;
                        else fgColor = ConsoleColor.DarkGray;
                    }
                    else if (tile == 'O') 
                    {
                        fgColor = ConsoleColor.Magenta; 
                        // 포탈 자체는 배경을 검정으로 두거나 유지 (여기선 범위 표시를 위해 유지)
                    }
                    
                    // [수정] 배경색(bgColor)까지 함께 그리기
                    DrawToBuffer(x + 1, y + 1, tile, fgColor, bgColor);
                }
            }
        }
        
        // [핵심 수정] 몬스터 아이콘 렌더링
        foreach(var monster in monsters){
            if (monster == currentBattleMonster) continue;
            int screenX = monster.X - cameraX + 1; 
            int screenY = monster.Y - cameraY + 1;
            if (screenX > 0 && screenX < viewportWidth + 1 && screenY > 0 && screenY < viewportHeight + 1){
                
                if (monster.Icon == 'B') // 1. 메인 보스
                { 
                    DrawToBuffer(screenX, screenY, 'B', ConsoleColor.Magenta); 
                }
                else if (monster.Icon == 'F') // 2. 필드 보스
                { 
                    DrawToBuffer(screenX, screenY, 'F', ConsoleColor.DarkYellow); 
                }
                else // 3. 일반 몬스터
                { 
                    DrawToBuffer(screenX, screenY, 'M', ConsoleColor.Red); 
                }
            }
        }
        // [신규] 상자 그리기
        foreach(var chest in chests)
        {
            if (chest.IsOpen) continue; 

            int screenX = chest.X - cameraX + 1; 
            int screenY = chest.Y - cameraY + 1;

            if (screenX > 0 && screenX < viewportWidth + 1 && screenY > 0 && screenY < viewportHeight + 1)
            {
                DrawToBuffer(screenX, screenY, chest.Icon, chest.Color);
            }
        }
        int playerScreenX = player.X - cameraX + 1;
        int playerScreenY = player.Y - cameraY + 1;
        if (playerScreenX > 0 && playerScreenX < viewportWidth + 1 && playerScreenY > 0 && playerScreenY < viewportHeight + 1){
            ConsoleColor pColor = player.IsDead ? ConsoleColor.DarkGray : ConsoleColor.White; // [수정]
            DrawToBuffer(playerScreenX, playerScreenY, '@', pColor); 
        }

        if (otherPlayer != null)
        {
            // [핵심] 동료가 포탈 대기 중이면 그리지 않음
                int otherScreenX = otherPlayer.X - cameraX + 1;
                int otherScreenY = otherPlayer.Y - cameraY + 1;
                
                if (otherScreenX > 0 && otherScreenX < viewportWidth + 1 && 
                    otherScreenY > 0 && otherScreenY < viewportHeight + 1)
                {
                    ConsoleColor oColor = otherPlayer.IsDead ? ConsoleColor.DarkGray : ConsoleColor.Cyan;
                    DrawToBuffer(otherScreenX, otherScreenY, '@', oColor); 
                }
        }

        
    }
    private void DrawInfoToBuffer(int x, int y) 
    {
        int barWidth = Math.Max(5, worldInfoWidth - 8);
        DrawTextToBuffer(x, y, $"직업: {player.Class}", ConsoleColor.Cyan);
        DrawTextToBuffer(x, y + 1, $"HP: {player.HP} / {player.MaxHP}", ConsoleColor.White);
        DrawBarToBuffer(x, y + 2, player.HP, player.MaxHP, barWidth, ConsoleColor.Red);
        DrawTextToBuffer(x, y + 3, $"MP: {player.MP} / {player.MaxMP}", ConsoleColor.White);
        DrawBarToBuffer(x, y + 4, player.MP, player.MaxMP, barWidth, ConsoleColor.Blue);
        DrawTextToBuffer(x, y + 6, $"LV: {player.Level}", ConsoleColor.Green);
        DrawTextToBuffer(x, y + 7, $"EXP: {player.EXP} / {player.EXPNext}", ConsoleColor.Gray);
        DrawBarToBuffer(x, y + 8, player.EXP, player.EXPNext, barWidth, ConsoleColor.Green);
        DrawTextToBuffer(x, y + 10, "[E] 인벤토리 열기", ConsoleColor.Yellow);
        DrawTextToBuffer(x, y + 11, "[C] 스탯 보기", ConsoleColor.Yellow); // [신규]
    }
    private void DrawLogRegion(int x, int y, int width, int height) 
    {
        int logX = x + 2;
        int logY = y + 2;
        int logWidth = Math.Max(0, width - 4);
        int logHeight = Math.Max(0, height - 3); 
        
        // [핵심 수정] 채팅 중이면 1줄이 아니라 '2줄'을 줄여서 더 위로 올림
        // (입력창이 덮는 공간 + 여유 공간 확보)
        int effectiveHeight = isGameChatting ? logHeight - 2 : logHeight;

        int maxLines = effectiveHeight;
        int logCount = logMessages.Count;
        
        // 1. 로그 출력
        for (int i = 0; i < maxLines; i++)
        {
            int logIndex = logCount - maxLines + i;
            
            if (logIndex >= 0 && logIndex < logCount) 
            { 
                var logEntry = logMessages[logIndex];
                string displayLine = TruncateStringByDisplayWidth(logEntry.Text, logWidth);
                DrawTextToBuffer(logX, logY + i, displayLine, logEntry.Color);
            }
        }

        // 2. 채팅 입력창 그리기
        if (isGameChatting)
        {
            int inputY = logY + logHeight - 1; 
            
            // 구분선
            for (int k = x + 1; k < x + width - 1; k++) 
                DrawToBuffer(k, inputY - 1, '─', ConsoleColor.DarkGray);

            // 입력 텍스트
            string prefix = "CHAT: ";
            string inputText = gameChatInput + "_"; 
            
            DrawTextToBuffer(logX, inputY, prefix, ConsoleColor.Green);
            DrawTextToBuffer(logX + GetDisplayWidth(prefix), inputY, inputText, ConsoleColor.White);
        }
    }
    #endregion
    
    // (렌더링 헬퍼, 너비 계산... 변경 없음)
    #region Rendering_Helpers_WidthCalc
   // Game.cs -> PrintBufferToConsole 메서드 내부

    private void PrintBufferToConsole()
    {
        try 
        {
            Console.SetCursorPosition(0, 0); 
            ConsoleColor lastFg = Console.ForegroundColor;
            ConsoleColor lastBg = Console.BackgroundColor; 
            
            for (int y = 0; y < screenHeight; y++)
            {
                // 줄 시작 시 커서 초기화
                Console.SetCursorPosition(0, y); 
                int currentCursorX = 0; // 현재 실제 커서의 X 위치 추적

                for (int x = 0; x < screenWidth; x++)
                {
                    var cell = screenBuffer[y, x];
                    
                    // Null 문자는 건너뛰기 (한글의 두 번째 바이트 등)
                    if (cell.Char == '\0') continue;
                    
                    // [핵심 수정] 
                    // 버퍼상의 좌표(x)와 실제 커서 위치(currentCursorX)가 다르면 강제로 맞춤.
                    // (예: 이전 글자가 한글(2칸)이었는데, 이번 글자가 그 한글의 두 번째 칸을 덮어써야 하는 경우)
                    if (currentCursorX != x)
                    {
                        Console.SetCursorPosition(x, y);
                        currentCursorX = x;
                    }

                    // 색상 변경 최적화
                    if (cell.FgColor != lastFg) { Console.ForegroundColor = cell.FgColor; lastFg = cell.FgColor; }
                    if (cell.BgColor != lastBg) { Console.BackgroundColor = cell.BgColor; lastBg = cell.BgColor; }

                    Console.Write(cell.Char);

                    // 글자 폭만큼 커서 위치 예상값 증가
                    currentCursorX += GetCharDisplayWidth(cell.Char);
                }
            }
            Console.ResetColor();
        }
        catch (IOException) { }
        catch (ArgumentOutOfRangeException) { }
    }
    private void DrawToBuffer(int x, int y, char c, ConsoleColor fg = ConsoleColor.White, ConsoleColor bg = ConsoleColor.Black) {
        if (y >= 0 && y < screenHeight && x >= 0 && x < screenWidth){
            screenBuffer[y, x] = new ScreenCell(c, fg, bg);
        }
    }
    private void DrawTextToBuffer(int x, int y, string text, ConsoleColor fg = ConsoleColor.White, ConsoleColor bg = ConsoleColor.Black, bool ignoreSpaceBg = false)
    {
        int currentBufferX = x;
        foreach (char c in text)
        {
            // 1. 맵 경계 확인
            if (y < 0 || y >= screenHeight) continue;
            int charWidth = GetCharDisplayWidth(c);
            if (currentBufferX + charWidth > screenWidth) break;
            if (currentBufferX < 0) { currentBufferX += charWidth; continue; }

            // 2. [원본] 'ignoreSpaceBg'가 true이고 문자가 공백인지 확인
            if (ignoreSpaceBg && c == ' ')
            {
                // 2a. 공백인 경우:
                // 배경색(bg)을 무시하고, 버퍼의 "원래" 배경색(Black)을 유지합니다.
                screenBuffer[y, currentBufferX] = new ScreenCell(c, fg, ConsoleColor.Black);
            }
            else
            {
                // 2b. 공백이 아닌 경우:
                // 요청된 배경색(bg)을 정상적으로 적용합니다.
                screenBuffer[y, currentBufferX] = new ScreenCell(c, fg, bg);
            }
            // --- [원본 끝] ---

            // 3. 2바이트 문자 처리
            if (charWidth == 2 && currentBufferX + 1 < screenWidth)
            {
                screenBuffer[y, currentBufferX + 1] = ScreenCell.Null;
            }
            currentBufferX += charWidth;
        }
    }
        private void DrawBox(int x, int y, int width, int height, string title) {
        // [수정] 화면 밖으로 나가는지 검사하는 조건 제거 (width, height 최소값만 체크)
        if (width <= 2 || height <= 2) return; 

        int endX = x + width - 1;
        int endY = y + height - 1;
        ConsoleColor boxColor = ConsoleColor.DarkGray;
        
        // (이하 그리기 로직은 DrawToBuffer가 범위를 체크해주므로 그대로 둠)
        DrawToBuffer(x, y, '╔', boxColor);
        DrawToBuffer(endX, y, '╗', boxColor);
        DrawToBuffer(x, endY, '╚', boxColor);
        DrawToBuffer(endX, endY, '╝');
        for (int i = x + 1; i < endX; i++){
            DrawToBuffer(i, y, '═', boxColor); DrawToBuffer(i, endY, '═', boxColor);
        }
        for (int i = y + 1; i < endY; i++){
            DrawToBuffer(x, i, '║', boxColor); DrawToBuffer(endX, i, '║', boxColor);
        }
        
        if (!string.IsNullOrWhiteSpace(title))
        {
            // 제목 그리기 (화면 밖이면 DrawTextToBuffer가 알아서 무시함)
            DrawTextToBuffer(x + 2, y, $" {title} ", ConsoleColor.White);
        }
    }
    private void DrawBarToBuffer(int x, int y, int current, int max, int width, ConsoleColor filledColor) {
        if (width <= 0) return; 
        DrawToBuffer(x, y, '[', ConsoleColor.DarkGray);
        float percent = (max == 0) ? 0 : (float)current / max;
        int filledWidth = (int)(percent * width);
        for (int i = 0; i < width; i++) {
            if (i < filledWidth)
                DrawToBuffer(x + 1 + i, y, '█', filledColor); 
            else
                DrawToBuffer(x + 1 + i, y, '░', ConsoleColor.DarkGray); 
        }
        DrawToBuffer(x + width + 1, y, ']', ConsoleColor.DarkGray);
    }
    private int GetCharDisplayWidth(char c) {
        if (c == '\u3000' || (c >= '\uFF00' && c <= '\uFFEF') || (c >= '\uAC00' && c <= '\uD7A3') || (c >= '\u1100' && c <= '\u11FF') || (c >= '\u3130' && c <= '\u318F') || (c >= '\u3040' && c <= '\u30FF') || (c >= '\u4E00' && c <= '\u9FFF')) {
            return 2;
        } else { return 1; }
    }
    private int GetDisplayWidth(string text) {
        int width = 0;
        foreach (char c in text) { width += GetCharDisplayWidth(c); }
        return width;
    }
    private string TruncateStringByDisplayWidth(string text, int maxWidth) {
        int currentWidth = 0;
        StringBuilder sb = new StringBuilder();
        foreach (char c in text) {
            int charWidth = GetCharDisplayWidth(c); 
            if (currentWidth + charWidth <= maxWidth) { currentWidth += charWidth; sb.Append(c); }
            else { break; }
        }
        sb.Append(new string(' ', maxWidth - currentWidth));
        return sb.ToString();
    }
    #endregion
    
    // --- [핵심 수정] ---
    #region Game_Logic_Input_Battle
    public void AddLog(string message)
    {
        AddLog(message, ConsoleColor.White);
    }

    // [신규] 로그 추가 메서드 (색상 지정 가능)
    public void AddLog(string message, ConsoleColor color)
    {
        logMessages.Add((message, color));
        
        // 로그가 50개 넘으면 오래된 것 삭제
        if (logMessages.Count > 50) 
        { 
            logMessages.RemoveAt(0); 
        }
    }    public void UpdateMapTile(int x, int y, char tile) { if (x >= 0 && x < MapWidth && y >= 0 && y < MapHeight) { map[x, y] = tile; } }
    
   public void StartBattle(Monster monster, bool isFromTrap = false)
    {
        ForceCloseChestUI();
        // 1. 전투 대상 설정 (맵상의 몬스터)
        currentBattleMonster = monster;

        // 맵 상의 몬스터 객체 참조 저장
        currentMapMonsterReference = monster;

        isTrapBattle = isFromTrap;
        currentBattleMapX = monster.X;
        currentBattleMapY = monster.Y;

        SetupBattleIntro(monster, isFromTrap);
        currentState = GameState.Battle_Intro;

        // =============================================================
        // [모드 분기] 싱글 vs 멀티
        // =============================================================
        if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
        {
            // [중요] 미믹 스탯 보존 로직
            int passMaxHP = -1, passAtk = -1, passDef = -1, passExp = -1;
            if (monster.MonsterId == "mimic")
            {
                passMaxHP = monster.MaxHP;
                passAtk = monster.ATK;
                passDef = monster.DEF;
                passExp = monster.EXPReward;
            }

            // 1. 나 자신은 무조건 전투 시작 (로컬)
            StartMultiplayerBattle(monster.MonsterId, true, isFromTrap, monster.X, monster.Y, 
                                   -1, passMaxHP, passAtk, passDef, passExp); 

            // 2. [핵심 수정] 동료에게 전투 시작 패킷 전송
            // (이전에는 포탈 대기 체크 로직 때문에 shouldSendPacket 변수가 있었으나, 
            //  이제는 무조건 같이 진입해야 하므로 조건 없이 보냅니다.)

            var data = new BattleStartData 
            { 
                MonsterId = monster.MonsterId, 
                IsFromTrap = isFromTrap,
                MapX = monster.X,
                MapY = monster.Y,
                CurrentHP = -1,
                
                MaxHP = currentBattleMonster.MaxHP,
                ATK = currentBattleMonster.ATK,
                DEF = currentBattleMonster.DEF,
                EXPReward = currentBattleMonster.EXPReward
            };

            Thread.Sleep(100); // 패킷 뭉침 방지 딜레이

            var packet = new Packet 
            { 
                Type = PacketType.BattleStart, 
                Data = JsonSerializer.Serialize(data) 
            };
            NetworkManager.Instance.Send(packet);
        }
        else
        {
            // [싱글플레이] (기존 로직 유지)
            
            // 1. 스탯 초기화
            monster.MaxHP = monster.OriginalMaxHP;
            monster.ATK = monster.OriginalATK;
            monster.DEF = monster.OriginalDEF;
            monster.EXPReward = monster.OriginalEXPReward;

            // 2. 레벨 스케일링 적용
            if (player.Level > 1)
            {
                double levelModifier = (double)(player.Level - 1);
                double scale;
                if (monster.Icon == 'B') scale = levelModifier * BOSS_SCALING_FACTOR;
                else scale = levelModifier * MONSTER_SCALING_FACTOR;

                if (monster.MonsterId == "mimic")
                {
                    int stageAtkBonus = (currentStage - 1) * 10;
                    monster.ATK = monster.OriginalATK + stageAtkBonus;
                }
                else
                {
                    monster.MaxHP = (int)(monster.OriginalMaxHP + (monster.OriginalMaxHP * scale));
                    monster.ATK = (int)(monster.OriginalATK + (monster.OriginalATK * scale));
                    monster.DEF = (int)(monster.OriginalDEF + (monster.OriginalDEF * scale));
                }
            }

            // 3. HP 보정
            if (monster.HP <= 0 || monster.HP > monster.MaxHP)
            {
                monster.HP = monster.MaxHP;
            }

            // 싱글플레이 턴 초기화
            isPlayerTurn = true;
        }
    }
    
    // (ProcessWorldInput ... 변경 없음)
    private void ProcessWorldInput(ConsoleKeyInfo key)
    {
        bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;

        // [신규] 채팅('T')은 죽어도 가능해야 하므로 여기서 체크 (RunGameLoop에서 처리되지만 안전장치)
        if (isMulti && (key.Key == ConsoleKey.T || char.ToUpper(key.KeyChar) == 'T'))
        {
            return; 
        }

        // [핵심] 죽은 상태면 이동/상호작용 일절 금지 (return)
        // 이 코드가 있어야 조작했을 때 게임오버 화면으로 넘어가는 등의 오작동을 막습니다.
        if (player.IsDead)
        {
            return;
        }

        int newX = player.X;
        int newY = player.Y;
        bool moved = false;

        if (isMulti && (key.Key == ConsoleKey.T || char.ToUpper(key.KeyChar) == 'T'))
        {
            isGameChatting = true;
            gameChatInput = "";
            return;
        }

        // --- [메뉴 진입 로직 수정] ---
        // 키 입력 처리를 switch문으로 깔끔하게 통합하고, returnStateFromMenu를 설정합니다.

        // 1. 공통 키 처리 (이동 및 메뉴)
        switch (key.Key)
        {
            case ConsoleKey.W: case ConsoleKey.UpArrow: newY--; moved = true; break;
            case ConsoleKey.S: case ConsoleKey.DownArrow: newY++; moved = true; break;
            case ConsoleKey.A: case ConsoleKey.LeftArrow: newX--; moved = true; break;
            case ConsoleKey.D: case ConsoleKey.RightArrow: newX++; moved = true; break;
            
            // [인벤토리]
            case ConsoleKey.E: 
                returnStateFromMenu = currentState; // (World 또는 Multiplayer_World 저장)
                currentState = GameState.Inventory; 
                return; 
            
            // [스탯 창]
            case ConsoleKey.C: 
                returnStateFromMenu = currentState;
                currentState = GameState.CharacterStat; 
                return; 
            
            // [상자 열기]
            case ConsoleKey.F: 
                TryOpenChest(); 
                return;
                
            // [일시정지] -> RunGameLoop의 전역 ESC 처리에서 넘어옴, 혹은 여기서 직접 처리
            // (ProcessWorldInput은 RunGameLoop의 switch문 안에서 호출되므로, 
            //  RunGameLoop의 전역 ESC가 먼저 작동합니다. 하지만 안전을 위해 여기서도 처리 가능)
        }

        // 2. 한글 키 및 보조 처리
        if (!moved)
        {
            char c = char.ToUpper(key.KeyChar);
            if (c == 'W' || c == 'ㅈ') { newY--; moved = true; }
            else if (c == 'S' || c == 'ㅅ') { newY++; moved = true; }
            else if (c == 'A' || c == 'ㅁ') { newX--; moved = true; }
            else if (c == 'D' || c == 'ㅇ') { newX++; moved = true; }
            
            else if (c == 'E' || c == 'ㄷ') 
            {
                returnStateFromMenu = currentState;
                currentState = GameState.Inventory; 
                return;
            }
            else if (c == 'C' || c == 'ㅊ') 
            {
                returnStateFromMenu = currentState;
                currentState = GameState.CharacterStat; 
                return;
            }
            else if (c == 'F' || c == 'ㄹ') { TryOpenChest(); return; }
        }

       if (newX == portalPosition.x && newY == portalPosition.y)
        {
            // [싱글플레이] 기존대로 즉시 이동
            if (!isMulti)
            {
                if (currentStage < 3)
                {
                    AddLog("다음 스테이지로 이동합니다...");
                    TransitionToStage(currentStage + 1);
                }
                else
                {
                    AddLog("마지막 스테이지입니다. (엔딩)");
                    // (엔딩 처리)
                    gameClearTime = DateTime.Now - gameStartTime;
                    StartEndingStory();
                }
                return;
            }
            
            // [멀티플레이]
            else
            {
                if (currentStage >= 3) return; // 엔딩 처리

                // 1. 동료 사망 or 이미 도착 -> 바로 이동
                bool otherReady = (otherPlayer != null && (otherPlayer.IsDead));
                
                if (otherReady)
                {
                    AddLog("동료와 함께 이동합니다!");
                    
                    // [핵심] 내가 호스트면 바로 진행, 게스트면 '나도 왔다'고 알리고 대기
                    if (NetworkManager.Instance.IsHost)
                    {
                        ProceedToNextStageMultiplayer();
                    }
                    else
                    {

                    }
                }
                // 2. 내가 먼저 도착 -> 대기 진입
                else
                {
                    SendMyPlayerInfo(); // (IsWaitingAtPortal = true 동기화)
                }
                return;
            }
        }

        if (isMulti && otherPlayer != null)
        {
                if (newX == otherPlayer.X && newY == otherPlayer.Y) return;
        }

        // --- 이하 이동 및 충돌 처리 로직 (변경 없음) ---
        if (newX < 0 || newX >= MapWidth || newY < 0 || newY >= MapHeight) { AddLog("더 이상 갈 수 없는 곳입니다."); return; }
        char tile = map[newX, newY];
        if (tile == '█') { AddLog("벽에 부딪혔습니다."); return; }
        if (chests.Any(c => c.X == newX && c.Y == newY && !c.IsOpen)) { AddLog("상자가 길을 막고 있습니다."); return; }

        Trap? trap = traps.Find(t => t.X == newX && t.Y == newY && !t.IsTriggered);
        if (trap != null) {
            // 1. 함정 발동
            trap.Trigger(player, this, rand, currentStage); 
            
            // 2. [신규] 멀티플레이라면 함정 발동 상태 동기화
            if (isMulti)
            {
                SendTrapUpdate(trap);
            }

            // 3. 전투 함정이면 리턴 (StartBattle 내부에서 상태 전환됨)
            if (trap.Type == TrapType.Battle) 
            { 
                return; 
            }
            if (player.HP <= 0) return; 
        }
        Monster? target = monsters.Find(m => m.X == newX && m.Y == newY);
        if (target != null) {
            if (target.Icon == 'B') { StartBattle(target); }
            else { StartBattle(target); }
            return; 
        }
        if (player.X != newX || player.Y != newY)
        {
            player.X = newX;
            player.Y = newY;

            if (isMulti)
            {
                SendMyPosition();
                
                // [핵심 신규] 이동할 때마다 포탈 조건을 체크 (호스트가 수행)
                if (NetworkManager.Instance.IsHost)
                {
                    CheckMultiplayerPortalCondition();
                }
            }
        }
    }

    private void HandleMapMove(string json)
    {
        var data = JsonSerializer.Deserialize<MapMoveData>(json);
        if (otherPlayer != null)
        {
            otherPlayer.X = data.X;
            otherPlayer.Y = data.Y;
            NetworkManager.Instance.IsDirty = true;

            // [신규] 동료가 움직였으니 포탈 조건 재확인 (호스트만)
            if (NetworkManager.Instance.IsHost)
            {
                CheckMultiplayerPortalCondition();
            }
        }
    }

    private void StartMultiplayerBattle(string monsterId, bool isLocalCall, bool isFromTrap, int mapX, int mapY, 
                                        int currentHP = -1, int maxHP = -1, int atk = -1, int def = -1, int exp = -1)
    {

        ForceCloseChestUI();
        // 1. 몬스터 생성
        Monster monster = MonsterDB.CreateMonster(monsterId, 0, 0);
        
        // [핵심 수정] 스탯 설정 분기
        if (maxHP != -1)
        {
            // [Case A] 동기화된 스탯이 있는 경우 (게스트, 또는 미믹)
            // -> 계산 로직을 수행하지 않고 받은 값을 그대로 씁니다. (동기화 보장)
            monster.MaxHP = maxHP;
            monster.ATK = atk;
            monster.DEF = def;
            monster.EXPReward = exp;
        }
        else
        {
            // [Case B] 새로운 전투 생성 (호스트)
            // -> 여기서만 레벨 스케일링을 수행합니다.
            
            monster.MaxHP = monster.OriginalMaxHP;
            monster.ATK = monster.OriginalATK;
            monster.DEF = monster.OriginalDEF;
            monster.EXPReward = monster.OriginalEXPReward;

            // [중요] 이 블록을 else 안으로 넣어야 게스트가 중복 적용하지 않습니다.
            if (player.Level > 1)
            {
                double levelModifier = (double)(player.Level - 1);
                double scale;
                if (monster.Icon == 'B') scale = levelModifier * BOSS_SCALING_FACTOR;
                else scale = levelModifier * MONSTER_SCALING_FACTOR;

                if (monster.MonsterId == "mimic")
                {
                    int stageAtkBonus = (currentStage - 1) * 10;
                    monster.ATK = monster.OriginalATK + stageAtkBonus;
                }
                else
                {
                    monster.MaxHP = (int)(monster.OriginalMaxHP + (monster.OriginalMaxHP * scale));
                    monster.ATK = (int)(monster.OriginalATK + (monster.OriginalATK * scale));
                    monster.DEF = (int)(monster.OriginalDEF + (monster.OriginalDEF * scale));
                }
            }
        }
        
        // 현재 체력 설정
        if (currentHP != -1) monster.HP = currentHP;
        else monster.HP = monster.MaxHP;

        isOtherPlayerFinishedBattleResult = false;

        currentBattleMonster = monster;
        currentBattleMapX = mapX;
        currentBattleMapY = mapY;
        isTrapBattle = isFromTrap; 

        if (currentMapMonsterReference == null && !isFromTrap)
        {
            // 좌표와 ID가 일치하는 몬스터를 내 맵에서 찾음
            currentMapMonsterReference = monsters.FirstOrDefault(m => m.X == mapX && m.Y == mapY && m.MonsterId == monsterId);
        }

        battleTurnCount = 0;

        // 2. 솔로 모드 판정
       bool isFightingSolo = (otherPlayer == null || otherPlayer.IsDead);

        // 턴 순서 결정
        if (isFightingSolo)
        {
            isMyBattleTurn = true; // 혼자니까 내 턴
        }
        else
        {
            if (currentHP != -1) isMyBattleTurn = false; 
            else
            {
                if (player.DEX > otherPlayer.DEX) isMyBattleTurn = true;
                else if (player.DEX < otherPlayer.DEX) isMyBattleTurn = false;
                else isMyBattleTurn = NetworkManager.Instance.IsHost;
            }
        }
        
        if (player.IsDead) isMyBattleTurn = false; // (안전장치)
        
        SetupBattleIntro(monster, isFromTrap);
        currentState = GameState.Battle_Intro;
        battleIntroStartTime = DateTime.Now;
        lastBattleActionTime = DateTime.Now; // 타이머 초기화
        isMonsterTurnInProgress = false;     // 플래그 초기화

        if (currentHP != -1) AddLog($"전투에 난입했습니다! (남은 HP: {monster.HP})");
        else AddLog($"야생의 {monster.Name}이(가) 나타났다!");
    }
    // 2. 전투 시작 수신 (상대가 전투 걸었을 때)
    private void HandleBattleStart(string json)
    {
        // [수정] 포탈 대기 중이라도, 이 패킷은 전투 난입 신호일 수 있으므로 
        // 무조건 무시하지 말고 상황을 봐야 함.
        // currentState는 World일 것임.
        
        // 만약 여전히 PortalWait 상태라면 무시 (아직 복귀 안 함)

        var data = JsonSerializer.Deserialize<BattleStartData>(json);
        
        // [수정] StartMultiplayerBattle에 CurrentHP 전달
        StartMultiplayerBattle(
            data.MonsterId, 
            false, 
            data.IsFromTrap, 
            data.MapX, 
            data.MapY, 
            data.CurrentHP, 
            data.MaxHP, // MaxHP
            data.ATK,   // ATK
            data.DEF,   // DEF
            data.EXPReward // EXP
        );
        
        // 난입한 경우 로그 출력
        if (data.CurrentHP != -1)
        {
            AddLog("전투에 난입했습니다!", ConsoleColor.Cyan);
        }
    }

    // 3. 전투 입력 처리 (내 턴일 때만)
    private void ProcessMultiplayerBattleInput(ConsoleKeyInfo key)
    {
        // [신규] 'T' 키로 채팅 시작 (언제든 가능)
        if (key.Key == ConsoleKey.T || char.ToUpper(key.KeyChar) == 'T')
        {
            isGameChatting = true;
            gameChatInput = "";
            return;
        }

        // [핵심 수정] 1. 애니메이션 중이거나 딜레이 중이면 입력 차단 (중복 실행 방지)
        if (currentState == GameState.Battle_Animation || 
            currentState == GameState.Battle_TurnDelay)
        {
            return;
        }

        // 2. 내 턴이 아니면 조작 불가
        if (!isMyBattleTurn)
        {
            while (Console.KeyAvailable) Console.ReadKey(true);
            return; 
        }

        // 3. 메뉴 선택 및 실행
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                battleMenuIndex = (battleMenuIndex - 1 + 4) % 4;
                break;
            case ConsoleKey.DownArrow:
                battleMenuIndex = (battleMenuIndex + 1) % 4;
                break;
            case ConsoleKey.Enter:
                ExecuteMultiplayerAction(battleMenuIndex);
                break;
            case ConsoleKey.D1: ExecuteMultiplayerAction(0); break;
            case ConsoleKey.D2: ExecuteMultiplayerAction(1); break;
            case ConsoleKey.D3: ExecuteMultiplayerAction(2); break;
            case ConsoleKey.D4: ExecuteMultiplayerAction(3); break;
        }
    }

   private void ExecuteMultiplayerAction(int menuIndex)
    {
        if (currentBattleMonster == null) return;
        if (!isMyBattleTurn) return; 

        switch (menuIndex)
        {
            case 0: // 공격
                // [핵심 수정] 행동 시작 즉시 조작 차단 (중복 패킷 방지)
                isMyBattleTurn = false; 
                MultiplayerBattleManager.PerformLocalAttack(this, player, currentBattleMonster);
                break;

            case 1: // 스킬 (하위 메뉴 진입이므로 차단하지 않음)
                AddLog("사용할 스킬을 선택하세요: [Q/W/E/R] (B: 취소)");
                skillMenuIndex = 0;
                currentState = GameState.Battle_SkillSelect;
                break;
                
            case 2: // 아이템 (하위 메뉴 진입이므로 차단하지 않음)
                AddLog("아이템 종류 선택: [1] HP [2] MP (B: 취소)");
                itemMenuIndex = 0;
                currentState = GameState.Battle_ItemMenu;
                break;
                
            case 3: // 후퇴
                if (isTrapBattle)
                {
                    AddLog("함정에 걸린 전투에서는 도망칠 수 없습니다!");
                    return;
                }
                
                // 후퇴는 턴을 소모하지 않고 동의를 구하는 과정이므로 false 처리 하지 않음
                // (단, 중복 요청 방지는 AttemptFleeVote 내부에 있음)
                AttemptFleeVote();
                break;
        }
    }
    private void PerformMultiplayerAttack()
    {
        int damage = AttackMonster(player, currentBattleMonster);
        currentBattleMonster.HP -= damage;

        if (lastAttackWasMiss)
        {
            AddLog($"[나] 공격 실패!");
            // 패킷 전송
            var data = new BattleActionData { ActionType = 0, Damage = 0 };
            NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleAction, Data = JsonSerializer.Serialize(data) });
            StartAnimation(currentBattleMonster, "M", () => { EndMyTurn(); });
        }
        else
        {
            AddLog($"[나] 공격! {damage} 피해!");
            // 패킷 전송
            var data = new BattleActionData { ActionType = 0, Damage = damage, IsCrit = lastAttackWasCrit };
            NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleAction, Data = JsonSerializer.Serialize(data) });
            
            StartAnimation(currentBattleMonster, $"-{damage}", () => { 
                if (currentBattleMonster.HP <= 0) WinMultiplayerBattle();
                else EndMyTurn(); 
            });
        }
    }

    // [신규] 후퇴 투표 로직
    private void AttemptFleeVote()
    {
        // [핵심 수정] 솔로 모드(동료 사망/포탈)라면 즉시 후퇴
        if (otherPlayer == null || otherPlayer.IsDead)
        {
            AddLog("전투에서 도망칩니다!", ConsoleColor.Yellow);
            FleeBattle(); // 로컬 후퇴 처리
            
            // (선택사항) 상대에게 전투 끝났음을 알림
            NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleEnd });
            return;
        }

        // --- 이하 2인 협동 모드 (투표 로직) ---

        if (myFleeRequest) 
        {
            AddLog("이미 후퇴를 제안했습니다.");
            return;
        }

        // 상대방이 이미 후퇴를 제안했다면 -> 후퇴 확정
        if (otherFleeRequest)
        {
            AddLog("후퇴에 동의했습니다! 도망칩니다!");
            NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleEnd });
            Thread.Sleep(100);
            FleeBattle();
        }
        else
        {
            myFleeRequest = true;
            AddLog("동료에게 후퇴를 제안했습니다.", ConsoleColor.Cyan);
            NetworkManager.Instance.Send(new Packet { Type = PacketType.FleeRequest });
        }
    }

    public void EndMyTurn()
    {
        isMyBattleTurn = false; // 조작 차단
        
        // [!!! 핵심 수정 !!!] 내 행동이 끝났으므로 카운트를 올려야 합니다.
        // 이걸 안 하면 내가 행동했다는 사실이 로직에 반영되지 않습니다.
        battleTurnCount++; 

        lastBattleActionTime = DateTime.Now;

        Thread.Sleep(50);

        // 턴 종료 패킷 전송
        var packet = new Packet { Type = PacketType.BattleTurnEnd };
        NetworkManager.Instance.Send(packet);
        
        AddLog("턴 종료. 동료의 행동을 기다립니다...");
        
        // 내 행동이 끝났으니, 혹시 이게 마지막 행동(2번째)인지 확인하여 적 턴으로 넘김
        CheckEnemyTurnCondition();
    }

    private void HandleBattleTurnEnd()
    {
        AddLog("동료가 턴을 마쳤습니다.");
        
        battleTurnCount++; // 상대방 행동 카운트

        lastBattleActionTime = DateTime.Now;

        CheckEnemyTurnCondition();
        
        // 아직 라운드가 안 끝났고(1명만 행동함), 내가 살아있다면 내 차례
        if (battleTurnCount < 2 && !player.IsDead)
        {
            isMyBattleTurn = true;
            AddLog("나의 턴!");
        }
    }

    // [신규] 적 턴 조건 체크
    private void CheckEnemyTurnCondition()
    {
        int requiredActions = 2;

        if (player.IsDead) requiredActions--;
        if (otherPlayer != null && otherPlayer.IsDead) requiredActions--;
        
        // [수정] 솔로 모드면 1명, 아니면(동료 복귀함) 2명
        if (otherPlayer == null || otherPlayer.IsDead) requiredActions = 1;

        if (requiredActions < 1) requiredActions = 1; 

        // 행동 횟수 체크
        if (battleTurnCount >= requiredActions)
        {
            isMyBattleTurn = false; 

            if (NetworkManager.Instance.IsHost || (otherPlayer == null || otherPlayer.IsDead))
            {
                ProcessMultiplayerMonsterTurn();
            }
        }
    }

    // [신규] 호스트용 적 AI (랜덤 타겟 공격)
    private void ProcessMultiplayerMonsterTurn()
    {
        // [핵심] 이미 진행 중이면 실행하지 않음
        if (isMonsterTurnInProgress) return;
        isMonsterTurnInProgress = true;

        // 1초 딜레이 후 시작
        Timer timer = null;
        timer = new Timer((_) => 
        {
            MultiplayerBattleManager.ProcessMonsterTurn_Host(this);
            timer.Dispose();
        }, null, 1000, Timeout.Infinite);
    }

    // 4. 상대방 행동 수신
    private void HandleBattleAction(string json)
    {
        // [수정] 매니저가 처리하도록 위임
        var data = JsonSerializer.Deserialize<BattleActionData>(json);
        MultiplayerBattleManager.OnReceiveBattleAction(this, data);
        
        NetworkManager.Instance.IsDirty = true;
    }

// [신규 헬퍼] 원격 스킬 효과 적용 (싱글플레이 로직을 복제하여 동기화)
private void ApplyRemoteSkillEffects(string skillName, Monster target)
{
    if (skillName == "처형") {
        target.AddStatusEffect(StatType.StunChance, 1);
        AddLog("적 기절!");
    }
    else if (skillName == "파열") {
        target.AddStatusEffect(StatType.StrongPoison, 3);
        target.StrongPoisonDamagePerTurn = otherPlayer.DEX * 3; // 상대방 스탯 기준
        AddLog("맹독 주입!");
    }
    else if (skillName == "메테오") {
        target.AddStatusEffect(StatType.AtkDebuff, 3);
        AddLog("공격력 하락!");
    }
    else if (skillName == "독 찌르기") {
        target.AddStatusEffect(StatType.PoisonStabDamage, 5);
        target.PoisonDamagePerTurn = Math.Max(1, otherPlayer.DEX / 2);
        AddLog("독 주입!");
    }
}

    // --- [핵심 수정] 턴제 로직 -> 애니메이션 시작 로직 ---

    private void ProcessBattleInput(ConsoleKeyInfo key)
    {
        if (currentBattleMonster == null) { currentState = GameState.World; return; }

        switch (key.Key)
        {
            // [신규] 방향키 선택
            case ConsoleKey.UpArrow:
                battleMenuIndex = (battleMenuIndex - 1 + 4) % 4; // 4개 메뉴 순환
                break;
            case ConsoleKey.DownArrow:
                battleMenuIndex = (battleMenuIndex + 1) % 4;
                break;
            
            // [신규] Enter 키로 확정
            case ConsoleKey.Enter:
                ProcessBattleAction(battleMenuIndex); // 선택한 행동 실행
                break;
            
            // [기존] 숫자 키 선택
            case ConsoleKey.D1: 
                battleMenuIndex = 0;
                ProcessBattleAction(battleMenuIndex);
                break;
            case ConsoleKey.D2: 
                battleMenuIndex = 1;
                ProcessBattleAction(battleMenuIndex);
                break;
            case ConsoleKey.D3: 
                battleMenuIndex = 2;
                ProcessBattleAction(battleMenuIndex);
                break;
            case ConsoleKey.D4: 
                battleMenuIndex = 3;
                ProcessBattleAction(battleMenuIndex);
                break;
        }
    }

    private void ProcessBattleAction(int index)
    {
        switch (index)
        {
            case 0: // 1. 기본 공격
                StartPlayerAttackAnimation();
                break;
            case 1: // 2. 스킬
                AddLog("사용할 스킬을 선택하세요: [Q], [W], [E], [R] (뒤로가기: B)");
                skillMenuIndex = 0; // [신규] 인덱스 0으로 초기화
                currentState = GameState.Battle_SkillSelect; 
                break;
            case 2: // 3. 아이템
                AddLog("어떤 종류의 물약을 사용하시겠습니까? (뒤로가기: B)");
                itemMenuIndex = 0; // [신규] 인덱스 0으로 초기화
                currentState = GameState.Battle_ItemMenu;
                break;
            case 3: // 4. 후퇴
                FleeBattle();
                break;
        }
    }
    private void ProcessSkillSelectInput(ConsoleKeyInfo key) 
    {
        int skillCount = player.Skills.Count;
        if (skillCount == 0) {
            // [수정] 복귀 상태 분기
            ReturnToBattleState(); 
            AddLog("사용할 스킬이 없습니다.");
            return;
        }

        // 1. key.Key 확인 (영문 모드 + 방향키 등)
        switch (key.Key)
        {
            // 방향키 이동
            case ConsoleKey.UpArrow:
                skillMenuIndex = (skillMenuIndex - 1 + skillCount) % skillCount;
                return; 
            case ConsoleKey.DownArrow:
                skillMenuIndex = (skillMenuIndex + 1) % skillCount;
                return; 
            
            // Enter: 현재 선택된 스킬 실행
            case ConsoleKey.Enter:
                TryUseSkill(player.Skills[skillMenuIndex]);
                return;

            // 단축키 (영문): 즉시 실행
            case ConsoleKey.Q: if(skillCount > 0) TryUseSkill(player.Skills[0]); return;
            case ConsoleKey.W: if(skillCount > 1) TryUseSkill(player.Skills[1]); return;
            case ConsoleKey.E: if(skillCount > 2) TryUseSkill(player.Skills[2]); return;
            case ConsoleKey.R: if(skillCount > 3) TryUseSkill(player.Skills[3]); return; 
            
            // 뒤로가기
            case ConsoleKey.B:
            case ConsoleKey.Escape:
                ReturnToBattleState(); // 헬퍼 메서드 사용
                AddLog("행동을 선택하세요.");
                return;
        }

        // 2. key.KeyChar 확인 (한글 모드 Fallback)
        char c = char.ToUpper(key.KeyChar);
        if (c == 'Q' || c == 'ㅂ') { if (skillCount > 0) TryUseSkill(player.Skills[0]); }
        else if (c == 'W' || c == 'ㅈ') { if (skillCount > 1) TryUseSkill(player.Skills[1]); }
        else if (c == 'E' || c == 'ㄷ') { if (skillCount > 2) TryUseSkill(player.Skills[2]); }
        else if (c == 'R' || c == 'ㄱ') { if (skillCount > 3) TryUseSkill(player.Skills[3]); }
        else if (c == 'B' || c == 'ㅠ') {
            ReturnToBattleState(); // [수정]
            AddLog("행동을 선택하세요.");        }
    }

    // [헬퍼] 스킬 사용 시도 (쿨타임/MP 체크 및 싱글/멀티 분기)
    private void TryUseSkill(Skill skill)
    {
        // 1. 쿨타임 체크
        if (skill.CurrentCooldown > 0)
        {
            AddLog($"아직 쿨타임입니다. ({skill.CurrentCooldown}턴 남음)");
            return;
        }

        // 2. MP 체크 (감소율 적용)
        float reductionPercent = player.GetStatBonus(StatType.ResourceCostReduction, ModifierType.Percent);
        int finalMpCost = (int)Math.Floor(skill.MpCost * (1.0f - reductionPercent));
        
        // [핵심] 현재 마나와 최종 비용 비교
        if (player.MP < finalMpCost)
        {
            AddLog("MP가 부족합니다!");
            return;
        }

        if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
        {
            isMyBattleTurn = false;
            currentState = GameState.Multiplayer_Battle;

            // [핵심 수정] 여기서 마나를 미리 차감하고 매니저에 전달
            // (MultiplayerBattleManager.PerformLocalSkill 내부의 차감 로직은 제거해야 함 -> 아래 3번 참조)
            player.MP -= finalMpCost;

            MultiplayerBattleManager.PerformLocalSkill(this, player, currentBattleMonster, skill);
        }
        else
        {
            // 싱글플레이
            StartPlayerSkillAnimation(skill);
        }
    }
   private void UseMultiplayerSkill(Skill skill)
{
    // 1. 코스트 지불
    player.MP -= skill.MpCost;
    skill.CurrentCooldown = skill.Cooldown;

    // 2. 데미지 계산 (싱글플레이 공식 동일 적용)
    int rawDmg = skill.CalculateDamage(player, currentBattleMonster);
    int finalDmg = 0;
    bool isCrit = false;

    // 패킷 데이터 준비
    var data = new BattleActionData { ActionType = 1, SkillName = skill.Name };

    // [공격형 스킬]
    if (skill.IsDamagingSkill)
    {
        // 도적 '파열' 스킬 크리티컬 처리
        if (skill is Eviscerate) {
            rawDmg = (int)(rawDmg * 1.5f); // 파열 계수 보정
            isCrit = true;
        }

        // 방어력 적용 (미믹 예외 처리 포함)
        if (currentBattleMonster.MonsterId == "mimic") finalDmg = 1;
        // 방어 무시 스킬들
        else if (skill.Name == "독 찌르기" || skill.Name == "파이어볼" || skill.Name == "매직 미사일" || skill.Name == "메테오" || skill.Name == "처형")
            finalDmg = Math.Max(1, rawDmg);
        else 
            finalDmg = ApplyDefense(rawDmg, currentBattleMonster.DEF);

        // 로컬 데이터 적용
        currentBattleMonster.HP -= finalDmg;
        data.Damage = finalDmg;
        data.IsCrit = isCrit;

        // 로그 출력
        string critMsg = isCrit ? " (치명타!)" : "";
        AddLog($"[나] {skill.Name}! {finalDmg} 데미지!{critMsg}");

        // 상태이상 적용 (로컬)
        ApplySkillEffectsLocally(skill, currentBattleMonster);

        // 애니메이션 및 턴 넘김
        StartAnimation(currentBattleMonster, $"-{finalDmg}", () => {
            // 처형 스킬 흡혈 연출
            if (skill is Execution) {
                int heal = (int)(finalDmg * 0.5f);
                player.HP = Math.Min(player.MaxHP, player.HP + heal);
                StartBuffAnimation(player, heal, ConsoleColor.Red, () => { 
                    CheckForManaRefund(skill.MpCost);
                    if (currentBattleMonster.HP <= 0) WinMultiplayerBattle(); else EndMyTurn(); 
                });
            }
            else {
                ApplyLifesteal(finalDmg, player);
                CheckForManaRefund(skill.MpCost);
                if (currentBattleMonster.HP <= 0) WinMultiplayerBattle(); else EndMyTurn();
            }
        });
    }
    // [버프/힐 스킬]
    else if (skill.IsBuffSkill)
    {
        int amount = rawDmg; // 힐량/버프량
        data.Damage = -amount; // 음수로 보내서 회복임을 표시 (관례)
        
        AddLog($"[나] {skill.Name} 사용!");

        // 힐/버프 애니메이션 색상
        ConsoleColor color = (skill is Heal) ? ConsoleColor.Green : ConsoleColor.Blue;
        
        // 아이언 윌(방어버프) 같은 경우 즉시 적용됨 (CalculateDamage 내부에서)
        // 힐 스킬인 경우 여기서 HP 적용
        if (skill is Heal) {
            // CalculateDamage에서 이미 적용되었을 수 있으므로 확인 필요하나, 
            // Skill.cs의 Heal은 CalculateDamage 호출 시 HP를 회복시킴.
            // 따라서 여기서는 중복 적용하지 않거나, 로직을 확인해야 함.
            // Skill.cs의 Heal.CalculateDamage가 HP를 회복시키므로 추가 적용 X
        }

        StartBuffAnimation(player, amount, color, () => { 
            CheckForManaRefund(skill.MpCost);
            EndMyTurn(); 
        });
    }

    // 3. 패킷 전송
    NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleAction, Data = JsonSerializer.Serialize(data) });
    
    // 상태 복귀
    currentState = GameState.Multiplayer_Battle;
}

    // [신규] 스킬 효과 적용 헬퍼 (중복 코드 방지)
    private void ApplySkillEffectsLocally(Skill skill, Monster target)
    {
        if (skill is Execution) {
            target.AddStatusEffect(StatType.StunChance, 1);
            AddLog("적 기절!");
        }
        else if (skill is Eviscerate) {
            target.AddStatusEffect(StatType.StrongPoison, 3);
            target.StrongPoisonDamagePerTurn = player.DEX * 3;
            AddLog("맹독 주입!");
        }
        else if (skill is Meteor) {
            target.AddStatusEffect(StatType.AtkDebuff, 3);
            AddLog("공격력 하락!");
        }
        else if (skill.Name == "독 찌르기") {
             target.AddStatusEffect(StatType.PoisonStabDamage, 5);
             target.PoisonDamagePerTurn = Math.Max(1, player.DEX / 2);
             AddLog("독 주입!");
        }
    }
    // (아이템 메뉴 로직 ... 변경 없음)
    #region Item_Menu_Input
    private void ProcessItemMenuInput(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            // [신규] 방향키
            case ConsoleKey.UpArrow:
            case ConsoleKey.DownArrow:
                itemMenuIndex = (itemMenuIndex + 1) % 2; // 0 <-> 1
                break;

            // [신규] Enter
            case ConsoleKey.Enter:
                ProcessItemMenuAction(itemMenuIndex); // (새로 추가할 헬퍼)
                break;

            // [기존] 단축키
            case ConsoleKey.D1:
                itemMenuIndex = 0;
                ProcessItemMenuAction(itemMenuIndex);
                break;
            case ConsoleKey.D2:
                itemMenuIndex = 1;
                ProcessItemMenuAction(itemMenuIndex);
                break;
            case ConsoleKey.B:
            case ConsoleKey.Escape:
                ReturnToBattleState(); // 헬퍼 메서드 사용
                AddLog("행동을 선택하세요.");
                break;
        }
    }
    
    private void ProcessItemMenuAction(int index)
    {
        if (index == 0) // 0. HP
        {
            currentItemSubMenu = ConsumableType.HealthPotion;
            currentState = GameState.Battle_ItemSubMenu;
            itemSubMenuIndex = 0; // [신규] 서브메뉴 인덱스 초기화
            AddLog("어떤 HP 물약을 사용하시겠습니까?");
        }
        else // 1. MP
        {
            currentItemSubMenu = ConsumableType.ManaPotion;
            currentState = GameState.Battle_ItemSubMenu;
            itemSubMenuIndex = 0; // [신규] 서브메뉴 인덱스 초기화
            AddLog("어떤 MP 물약을 사용하시겠습니까?");
        }
    }
    
    private void ProcessItemSubMenuInput(ConsoleKeyInfo key)
    {
        // 1. 현재 선택된 카테고리(HP/MP)의 아이템들을 이름으로 그룹화
        var distinctItemGroups = player.ConsumableInventory
            .Where(item => item.CType == currentItemSubMenu)
            .GroupBy(item => item.Name) 
            .Select(group => new { 
                Item = group.First(), 
                Count = group.Count()   
            })
            .OrderBy(g => g.Item.Rarity) 
            .ToList();
        
        // 입력 처리를 위해 대표 아이템 리스트 추출
        var distinctItems = distinctItemGroups.Select(g => g.Item).ToList();

        if (distinctItems.Count == 0)
        {
            currentState = GameState.Battle_ItemMenu;
            AddLog("해당 종류의 물약이 없습니다.");
            return;
        }

        // 2. 키 입력 처리
        switch (key.Key)
        {
            // 방향키 이동
            case ConsoleKey.UpArrow:
                itemSubMenuIndex = (itemSubMenuIndex - 1 + distinctItems.Count) % distinctItems.Count;
                break;
            case ConsoleKey.DownArrow:
                itemSubMenuIndex = (itemSubMenuIndex + 1) % distinctItems.Count;
                break;
            
            // 선택 (Enter)
            case ConsoleKey.Enter:
                TryUseItem(distinctItems, itemSubMenuIndex);
                break;

            // 단축키 (숫자)
            case ConsoleKey.D1: TryUseItem(distinctItems, 0); break;
            case ConsoleKey.D2: TryUseItem(distinctItems, 1); break;
            case ConsoleKey.D3: TryUseItem(distinctItems, 2); break;
            case ConsoleKey.D4: TryUseItem(distinctItems, 3); break;
            
            // 뒤로가기
            case ConsoleKey.B:
            case ConsoleKey.Escape:
                currentState = GameState.Battle_ItemMenu; 
                AddLog("어떤 종류의 물약을 사용하시겠습니까?");
                break;
        }
    }

    // [신규] 아이템 사용 분기 처리 헬퍼
    private void TryUseItem(List<Consumable> items, int index)
    {
        if (index < 0 || index >= items.Count) return;

        if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
        {
            // 1. 아이템 사용 시도 (성공 시 내부에서 애니메이션 상태로 전환됨)
            bool success = MultiplayerBattleManager.PerformLocalItem(this, player, items[index]);

            if (success)
            {
                // [핵심 수정] currentState 변경 코드 삭제!
                // PerformLocalItem -> StartBuffAnimation에서 이미 'Battle_Animation'으로 상태를 바꿨으므로,
                // 여기서 'Multiplayer_Battle'로 덮어쓰면 애니메이션과 턴 종료 콜백이 씹혀버립니다.
                
                isMyBattleTurn = false; // 조작만 차단

                // currentState = GameState.Multiplayer_Battle; // <--- [삭제] 이 줄을 지우세요.
            }
            else
            {
                // 실패 시(가득 참 등)에는 메뉴 유지
            }
        }
        else
        {
            // 싱글플레이
            UseItemFromList(items, index);
        }
    }
    
    // Game.cs
    private bool UseItemFromList(List<Consumable> distinctItems, int index)
    {
        if (index >= distinctItems.Count) { AddLog("해당 번호의 아이템이 없습니다."); return false; }
        Consumable itemToUse = distinctItems[index];

        // [핵심 수정] 1. '사용'을 먼저 시도 (이 안에서 AddLog가 호출됨)
        bool success = player.UseConsumable(itemToUse.CType, itemToUse.Rarity, this);

        // [핵심 수정] 2. 사용에 '성공'했을 때만 애니메이션 실행
        if (success)
        {
            // [신규] 1. 아스키 아트 표시를 위해 회복량 계산 (Consumable.cs 로직 복제)
            float percent = 0.25f;
            switch (itemToUse.Rarity) // (Consumable.cs의 Rarity 속성 가정)
            {
                case ItemRarity.Common:    percent = 0.25f; break;
                case ItemRarity.Rare:      percent = 0.50f; break;
                case ItemRarity.Unique:    percent = 0.75f; break;
                case ItemRarity.Legendary: percent = 1.00f; break;
            }

            int amount = 0;
            if (itemToUse.CType == ConsumableType.HealthPotion)
            {
                int percentHeal = (int)(player.MaxHP * percent);
                // (Value는 Consumable의 속성으로, 이전 파일에 있었습니다)
                amount = Math.Max(percentHeal, itemToUse.Value); // (Consumable.cs의 Value 속성 가정)
            }
            else // ManaPotion
            {
                int percentHealMP = (int)(player.MaxMP * percent);
                amount = Math.Max(percentHealMP, itemToUse.Value); // (Consumable.cs의 Value 속성 가정)
            }

            // 3. 몬스터 턴을 즉시 시작하는 대신, 애니메이션이 끝난 후 실행될 '콜백'을 정의
            Action onComplete = () => {
                currentState = GameState.Battle;
                if (currentBattleMonster != null && currentBattleMonster.HP > 0)
                {
                    StartMonsterAttackAnimation(); // (애니메이션 종료 후 몬스터 턴 시작)
                }
            };

            // 4. 아이템 종류에 따라 다른 색상으로 애니메이션 시작
            if (itemToUse.CType == ConsumableType.HealthPotion)
            {
                StartBuffAnimation(player, amount, ConsoleColor.Red, onComplete);
            }
            else // (ConsumableType.ManaPotion)
            {
                StartBuffAnimation(player, amount, ConsoleColor.Blue, onComplete);
            }
        }

        return success; // (성공 여부 반환)
    }
    #endregion

    // (Loot 창 로직 ... 변경 없음)
    #region Loot_Input
    private void ProcessLootDropInput(ConsoleKeyInfo key)
    {
        if (currentLootEquipment == null) {
            if (equipmentDropQueue.Count > 0) { currentLootEquipment = equipmentDropQueue.Dequeue(); return; }
            currentState = GameState.LootSummary; return;
        }
        
        // 1. IME에 영향받지 않는 키 (방향키, Enter)
        switch (key.Key) {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
                lootDropIndex = (lootDropIndex + 1) % 2; 
                break; 
                
            case ConsoleKey.Enter:
                ProcessLootDropAction(lootDropIndex == 0); 
                return; // [수정] return으로 변경 (아래 3번 로직 실행 방지)

            // 2. IME에 영향받는 키 (Y/N) - 영문 모드
            case ConsoleKey.Y: 
                lootDropIndex = 0;
                ProcessLootDropAction(true);
                return; // [수정] return으로 변경
            case ConsoleKey.N: 
                lootDropIndex = 1;
                ProcessLootDropAction(false);
                return; // [수정] return으로 변경
            
            case ConsoleKey.Escape: // (편의상 ESC도 No로)
                lootDropIndex = 1;
                ProcessLootDropAction(false);
                return;
        }

        // 3. [신규] IME에 영향받는 키 (Y/N) - 한글 모드 Fallback
        // (key.Key가 Process 등으로 들어왔을 때 key.KeyChar를 확인)
        char c = char.ToUpper(key.KeyChar);
        if (c == 'Y' || c == 'ㅛ') // 'Y' 또는 'ㅛ'
        {
            lootDropIndex = 0;
            ProcessLootDropAction(true);
        }
        else if (c == 'N' || c == 'ㅜ') // 'N' 또는 'ㅜ'
        {
            lootDropIndex = 1;
            ProcessLootDropAction(false);
        }
    }

    private void ProcessLootDropAction(bool isEquip)
    {
        if (currentLootEquipment == null) return;

        if (isEquip) // (Yes)
        {
            Equipment? oldItem = player.EquipItem(currentLootEquipment);
            AddLog($"[장착] {currentLootEquipment.Name}");
            if (oldItem != null) { AddLog($"[해제] {oldItem.Name} (버려짐)"); }
        }
        else // (No)
        {
            AddLog($"{currentLootEquipment.Name} 을(를) 버렸습니다.");
            currentLootList.Remove(currentLootEquipment);
        }

       if (equipmentDropQueue.Count > 0)
        {
            currentLootEquipment = equipmentDropQueue.Dequeue();
            currentState = GameState.LootDrop;
        }
        else 
            { 
                currentLootList.Clear(); 
                
                // [핵심 수정] 어디서 왔는지(Battle vs World)에 따라 분기
                if (stateBeforeLoot == GameState.Battle || stateBeforeLoot == GameState.Multiplayer_Battle)
                {
                    // 전투 보상이면: 대기 시퀀스 (몬스터 정보 유지)
                    FinishBattleResultSequence();
                }
                else
                {
                    // 일반 상자(World)면: 즉시 복귀 (몬스터 정보 삭제)
                    currentBattleMonster = null;
                    
                    if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
                        currentState = GameState.Multiplayer_World;
                    else
                        currentState = GameState.World;
                }
            }
    }
    
    private void ProcessLootSummaryInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter) 
        {
            foreach (var item in currentLootList) 
            {
                if (item is Consumable consumable) 
                { 
                    player.AddConsumable(consumable); 
                    AddLog($"[아이템 획득] {consumable.Name}"); 
                }
            }
            currentLootList.Clear(); 
            
            // [핵심 수정] 분기 처리
            if (stateBeforeLoot == GameState.Battle || stateBeforeLoot == GameState.Multiplayer_Battle)
            {
                // 전투 보상 -> 대기
                FinishBattleResultSequence();
            }
            else
            {
                // 일반 상자 -> 즉시 복귀
                currentBattleMonster = null;
                
                if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
                    currentState = GameState.Multiplayer_World;
                else
                    currentState = GameState.World;
            }
        }
    }
    #endregion

    // --- [신규] 전투 애니메이션 핵심 로직 ---
    #region Battle_Animation_System

    // 애니메이션이 현재 재생 중인지 확인
   private bool IsAnimationPlaying()
    {
        return currentState == GameState.Battle_Animation;
    }

    // [수정] 현재 대상(currentAnimationTarget)만 초기화하도록 변경
   private void ProcessBattleAnimation()
    {
        // 애니메이션 총 지속 시간 (예: 100ms * 4회 깜빡임 = 400ms + 여유시간)
        int totalDuration = ANIM_BLINK_DURATION_MS * ANIM_TOTAL_BLINKS; 

        TimeSpan elapsed = DateTime.Now - battleAnimationStartTime;

        // 시간이 아직 안 됐으면 계속 진행
        if (elapsed.TotalMilliseconds < totalDuration) return;

        // [종료 처리]
        showHitOverlay = false;
        currentAsciiDamageString = ""; 
        currentAnimationTarget = null;
        customBlinkColor = ConsoleColor.Black;

        currentState = GameState.Battle_TurnDelay;
        turnDelayEndTime = DateTime.Now.AddMilliseconds(BATTLE_TURN_DELAY_MS);
    }


    // --- [핵심 수정] ---
    // 1. 불필요해진 'int durationMs' 매개변수 제거
    public void StartAnimation(object target, string numberString, Action onComplete)
    {
        // 1. 애니메이션 정보 설정
        currentAnimationTarget = target;
        animationCallback = onComplete;
        currentState = GameState.Battle_Animation;

        // [핵심 수정] 프레임 카운트 대신 '시작 시간'을 기록합니다.
        battleAnimationStartTime = DateTime.Now;

        // 2. 데미지 텍스트 설정
        currentAsciiDamageString = numberString;
        
        // (showHitOverlay는 이제 DrawSingleEntityArt에서 시간 기준으로 계산하므로 초기값은 무관하지만 true로 둠)
        showHitOverlay = true; 
    }
    // --- [끝] ---

    // 1. 플레이어 기본 공격
    private void StartPlayerAttackAnimation()
    {
        if (IsAnimationPlaying() || currentBattleMonster == null) return;

        int damage = AttackMonster(player, currentBattleMonster);
        
        if (lastAttackWasMiss)
        {
            // [수정]
            StartAnimation(currentBattleMonster, "M", () => { // "M" = MISS
                StartMonsterAttackAnimation();
            });
            return; 
        }

        currentBattleMonster.HP -= damage;
        
        // [신규] 공격 명중 시 기절/출혈 효과 적용 시도
        ApplyOnHitEffects(player, currentBattleMonster);

        if (lastAttackWasCrit)
        {
            AddLog($"크리티컬! {player.Class}이(가) {currentBattleMonster.Name}에게 {damage}의 데미지를 입혔습니다!");
        }
        else
        {
            AddLog($"{player.Class}이(가) {currentBattleMonster.Name}에게 {damage}의 데미지를 입혔습니다!");
        }
        
        // [수정]
        StartAnimation(currentBattleMonster, $"-{damage}", () => {
            ApplyLifesteal(damage, player);
            if (currentBattleMonster.HP <= 0)
                WinBattle();
            else
                StartMonsterAttackAnimation();
        });
    }

    // 2. 플레이어 스킬 사용
   private void StartPlayerSkillAnimation(Skill skill)
    {
        if (IsAnimationPlaying() || currentBattleMonster == null) return;
        
        if (skill.CurrentCooldown > 0) { AddLog($"쿨타임: {skill.CurrentCooldown}턴 남음"); return; }

        float reductionPercent = player.GetStatBonus(StatType.ResourceCostReduction, ModifierType.Percent);
        int finalMpCost = (int)Math.Floor(skill.MpCost * (1.0f - reductionPercent));
        if (player.MP < finalMpCost) { AddLog("MP가 부족합니다!"); return; }
        player.MP -= finalMpCost;
        
        skill.CurrentCooldown = skill.Cooldown;

        int rawSkillDamage = skill.CalculateDamage(player, currentBattleMonster);
        int finalSkillDamage = 0;

        if (skill.IsDamagingSkill)
        {
            if (skill is Eviscerate) {
                lastAttackWasCrit = true;
                rawSkillDamage = (int)(rawSkillDamage * 1.5f);
                AddLog("도적: 파열! 치명적인 일격!");
            }

            if (currentBattleMonster.MonsterId == "mimic") finalSkillDamage = 1;
            else {
                if (skill.Name == "독 찌르기" || skill.Name == "파이어볼" || skill.Name == "매직 미사일" ||
                    skill.Name == "메테오" || skill.Name == "처형")
                    finalSkillDamage = Math.Max(1, rawSkillDamage);
                else 
                    finalSkillDamage = ApplyDefense(rawSkillDamage, currentBattleMonster.DEF);
            }

            AddLog($"{skill.Name}! {finalSkillDamage} 데미지!");
            currentBattleMonster.HP -= finalSkillDamage;
            
            // --- 상태이상 적용 (AddStatusEffect 사용) ---
            if (skill is Execution) {
                currentBattleMonster.AddStatusEffect(StatType.StunChance, 1);
                // (흡혈 로직은 아래 애니메이션 콜백으로 이동)
                AddLog("적을 기절시켰습니다!");
            }
            else if (skill is Eviscerate) {
                currentBattleMonster.AddStatusEffect(StatType.StrongPoison, 3);
                currentBattleMonster.StrongPoisonDamagePerTurn = player.DEX * 3; 
                AddLog("맹독 주입!");
            }
            else if (skill is Meteor) {
                currentBattleMonster.AddStatusEffect(StatType.AtkDebuff, 3); 
                AddLog("공격력 하락!");
            }
            else if (skill.Name == "독 찌르기") {
                 currentBattleMonster.AddStatusEffect(StatType.PoisonStabDamage, 5);
                 currentBattleMonster.PoisonDamagePerTurn = Math.Max(1, player.DEX / 2);
            }

            ApplyOnHitEffects(player, currentBattleMonster);

            // [신규] 애니메이션: 몬스터 피격 -> (전사라면) 플레이어 힐 -> 턴 종료
            StartAnimation(currentBattleMonster, $"-{finalSkillDamage}", () => {
                
                // [신규] 전사 처형 스킬 흡혈 연출
                if (skill is Execution)
                {
                    int heal = (int)(finalSkillDamage * 0.5f);
                    player.HP = Math.Min(player.MaxHP, player.HP + heal);
                    AddLog($"체력 {heal} 흡수!");
                    
                    // 플레이어에게 힐 애니메이션 재생
                    StartBuffAnimation(player, heal, ConsoleColor.Red, () => {
                        CheckForManaRefund(finalMpCost);
                        if (currentBattleMonster.HP <= 0) WinBattle();
                        else StartMonsterAttackAnimation();
                    });
                }
                else
                {
                    ApplyLifesteal(finalSkillDamage, player); 
                    CheckForManaRefund(finalMpCost);
                    if (currentBattleMonster.HP <= 0) WinBattle();
                    else StartMonsterAttackAnimation();
                }
            });
        }
        else if (skill.IsBuffSkill) 
        {
            // ... (기존 버프 로직) ...
            AddLog($"{skill.Name}!");
            StartAnimation(player, $"+{rawSkillDamage}", () => { 
                CheckForManaRefund(finalMpCost);
                StartMonsterAttackAnimation();
            });
        }
    }
    
    private void ApplyOnHitEffects(Player player, Monster target)
    {
        float stunChance = player.GetStatBonus(StatType.StunChance, ModifierType.Percent);
        if (rand.NextDouble() < stunChance)
        {
            target.AddStatusEffect(StatType.StunChance, 1); // [수정] AddStatusEffect 사용
            AddLog($"전사: {target.Name}을(를) 기절시켰습니다!");
        }

        float bleedChance = player.GetStatBonus(StatType.BleedChance, ModifierType.Percent);
        if (rand.NextDouble() < bleedChance)
        {
            int bleedDamage = Math.Max(1, player.DEX / 2); 
            target.BleedDamagePerTurn = bleedDamage;
            target.AddStatusEffect(StatType.BleedChance, 3); // [수정] AddStatusEffect 사용
            AddLog($"도적: 출혈 효과 적용!");
        }
    }

    // 3. 몬스터 턴
    private void StartMonsterAttackAnimation()
    {
        if (IsAnimationPlaying() || currentBattleMonster == null) return;

        isPlayerTurn = false;
        lastAttackWasCrit = false;
        lastAttackWasMiss = false;

        int AdjustDamageForMimic(int damage) => (currentBattleMonster.MonsterId == "mimic") ? 1 : damage;

        // --- [연쇄 애니메이션 로직] ---
        // Step 1: 맹독 -> Step 2: 일반독 -> Step 3: 출혈 -> Step 4: 행동(기절/공격)

        void DoStrongPoison()
        {
            if (currentBattleMonster.StatusEffects.GetValueOrDefault(StatType.StrongPoison, 0) > 0)
            {
                int dmg = AdjustDamageForMimic(currentBattleMonster.StrongPoisonDamagePerTurn);
                currentBattleMonster.HP -= dmg;
                currentBattleMonster.StatusEffects[StatType.StrongPoison]--;
                if (currentBattleMonster.StatusEffects[StatType.StrongPoison] == 0) currentBattleMonster.RemoveStatusEffect(StatType.StrongPoison);

                AddLog($"맹독 피해! {dmg} 데미지!");
                
                // [신규] 독 데미지 연출 (보라색)
                // StartAnimation은 customBlinkColor를 설정하지 않으므로, StartBuffAnimation을 응용하거나 필드를 직접 설정해야 함
                // 여기서는 StartBuffAnimation을 변형하여 데미지용으로 사용 (Game.cs 내부에 StartDamageAnimation 메서드를 만드는게 좋지만, 기존 메서드 활용)
                
                // *임시 방편*: customBlinkColor를 설정하고 StartAnimation 호출
                customBlinkColor = ConsoleColor.Magenta; 
                StartAnimation(currentBattleMonster, $"-{dmg}", () => 
                {
                    if (currentBattleMonster.HP <= 0) WinBattle();
                    else DoPoison(); // 다음 단계
                });
            }
            else DoPoison();
        }

        void DoPoison()
        {
            if (currentBattleMonster.StatusEffects.GetValueOrDefault(StatType.PoisonStabDamage, 0) > 0)
            {
                int dmg = AdjustDamageForMimic(currentBattleMonster.PoisonDamagePerTurn);
                currentBattleMonster.HP -= dmg;
                currentBattleMonster.StatusEffects[StatType.PoisonStabDamage]--;
                if (currentBattleMonster.StatusEffects[StatType.PoisonStabDamage] == 0) currentBattleMonster.RemoveStatusEffect(StatType.PoisonStabDamage);

                AddLog($"독 피해! {dmg} 데미지!");
                
                customBlinkColor = ConsoleColor.Magenta;
                StartAnimation(currentBattleMonster, $"-{dmg}", () => 
                {
                    if (currentBattleMonster.HP <= 0) WinBattle();
                    else DoBleed();
                });
            }
            else DoBleed();
        }

        void DoBleed()
        {
            if (currentBattleMonster.StatusEffects.GetValueOrDefault(StatType.BleedChance, 0) > 0)
            {
                int dmg = AdjustDamageForMimic(currentBattleMonster.BleedDamagePerTurn);
                currentBattleMonster.HP -= dmg;
                currentBattleMonster.StatusEffects[StatType.BleedChance]--;
                if (currentBattleMonster.StatusEffects[StatType.BleedChance] == 0) currentBattleMonster.RemoveStatusEffect(StatType.BleedChance);

                AddLog($"출혈 피해! {dmg} 데미지!");

                customBlinkColor = ConsoleColor.DarkRed; // 출혈 색상
                StartAnimation(currentBattleMonster, $"-{dmg}", () => 
                {
                    if (currentBattleMonster.HP <= 0) WinBattle();
                    else DoMonsterAction();
                });
            }
            else DoMonsterAction();
        }

        void DoMonsterAction()
        {
            customBlinkColor = ConsoleColor.Black; // 색상 초기화

            // 공격력 감소 턴 차감
            if (currentBattleMonster.StatusEffects.ContainsKey(StatType.AtkDebuff))
            {
                if (currentBattleMonster.StatusEffects[StatType.AtkDebuff] > 0)
                {
                    currentBattleMonster.StatusEffects[StatType.AtkDebuff]--;
                    if (currentBattleMonster.StatusEffects[StatType.AtkDebuff] == 0)
                    {
                        currentBattleMonster.RemoveStatusEffect(StatType.AtkDebuff);
                        AddLog($"{currentBattleMonster.Name}의 공격력이 회복되었습니다.");
                    }
                }
            }

            // 기절 확인
            if (currentBattleMonster.StatusEffects.GetValueOrDefault(StatType.StunChance, 0) > 0)
            {
                currentBattleMonster.StatusEffects[StatType.StunChance]--;
                if (currentBattleMonster.StatusEffects[StatType.StunChance] == 0) currentBattleMonster.RemoveStatusEffect(StatType.StunChance);
                
                AddLog($"{currentBattleMonster.Name}은(는) 기절해서 움직일 수 없다!");
                currentState = GameState.Battle;
                ProcessPlayerBuffs();
                foreach (var s in player.Skills) { if (s.CurrentCooldown > 0) s.CurrentCooldown--; }
                isPlayerTurn = true;
                return;
            }

            // 플레이어 회피
            double playerEvasionChance = (player.DEX * PLAYER_DEX_EVASION_SCALER);
            if (rand.NextDouble() < playerEvasionChance) {
                AddLog("플레이어가 몬스터의 공격을 멋지게 회피했습니다!");
                lastAttackWasMiss = true;
                StartAnimation(player, "M", () => { 
                    currentState = GameState.Battle;
                    ProcessPlayerBuffs();
                    foreach (var s in player.Skills) { if (s.CurrentCooldown > 0) s.CurrentCooldown--; }
                    isPlayerTurn = true;
                });
                return; 
            }

            // 공격 실행
            AddLog($"{currentBattleMonster.Name}의 턴!");
            int rawDamage = currentBattleMonster.ATK; 
            int damage = ApplyDefense(rawDamage, player.DEF);
            
            // 반사
            float reflectChance = player.GetStatBonus(StatType.DamageReflectChance, ModifierType.Percent);
            if (rand.NextDouble() < reflectChance) {
                int reflectDamage = AdjustDamageForMimic((int)Math.Max(1, damage * 0.5));
                currentBattleMonster.HP -= reflectDamage;
                AddLog($"전사: 피해 반사! {reflectDamage} 데미지!");
                if (currentBattleMonster.HP <= 0) { WinBattle(); return; }
            }
            
            // 마력 보호막
            float conversionRate = player.GetStatBonus(StatType.ManaShieldConversion, ModifierType.Percent);
            int finalHpDamage = damage;
            int finalMpDamage = 0;
            if (conversionRate > 0 && player.MP > 0)
            {
                finalMpDamage = Math.Min(player.MP, (int)(damage * conversionRate));
                finalHpDamage = damage - finalMpDamage; 
                player.MP -= finalMpDamage;
                player.HP -= finalHpDamage;
                AddLog($"마력 보호막! HP -{finalHpDamage}, MP -{finalMpDamage}");
            }
            else
            {
                player.HP -= finalHpDamage;
                AddLog($"{currentBattleMonster.Name}의 공격! {finalHpDamage} 데미지!");
            }

            customBlinkColor = ConsoleColor.DarkRed;

            StartAnimation(player, $"-{finalHpDamage}", () => {
                if (player.HP <= 0) currentState = GameState.GameOver;
                else currentState = GameState.Battle;
                ProcessPlayerBuffs();
                foreach (var s in player.Skills) { if (s.CurrentCooldown > 0) s.CurrentCooldown--; }
                isPlayerTurn = true;
            });
        }

        // 체인 시작
        DoStrongPoison();
    }

    #endregion
    
    // (Lifesteal, ManaRefund 헬퍼 ... 변경 없음)
    #region Battle_Helpers
    public void ApplyLifesteal(int damageDealt, Player caster)
    {
        float lifestealPercent = caster.GetStatBonus(StatType.LifeStealPercent, ModifierType.Percent);
        if (lifestealPercent > 0)
        {
            int healAmount = (int)(damageDealt * lifestealPercent);
            if (healAmount > 0)
            {
                caster.HP = Math.Min(caster.MaxHP, caster.HP + healAmount);
                AddLog($"도적: 생명력 흡수! HP {healAmount}를 회복!");
            }
        }
    }

    public void CheckForManaRefund(int mpCost)
    {
        if (mpCost == 0) return; 
        float refundChance = player.GetStatBonus(StatType.ManaRefundChance, ModifierType.Percent);
        if (rand.NextDouble() < refundChance)
        {
            int refundAmount = (int)Math.Max(1, mpCost * 0.25); 
            player.MP = Math.Min(player.MaxMP, player.MP + refundAmount);
            AddLog($"마법사: 마나 환급! MP {refundAmount}를 회복!");
        }
    }
    #endregion
    
    // [수정] UseSkill 메서드는 스킬 실행만 담당 (Skill.cs로 로직 이전됨)
    private bool UseSkill(int skillIndex)
    {
        // (이 메서드는 이제 ProcessSkillSelectInput에서만 호출됨)
        if (currentBattleMonster == null) return false;
        if (skillIndex >= player.Skills.Count)
        {
            AddLog("해당 슬롯에 스킬이 없습니다.");
            return false;
        }
        
        Skill skill = player.Skills[skillIndex]; 
        if (player.MP < skill.MpCost) 
        { 
            AddLog("MP가 부족합니다!"); 
            return false; 
        }
        
        // [수정] 스킬 애니메이션 로직으로 대체됨
        // 이 함수는 사실상 StartPlayerSkillAnimation의 사전 체크용으로만 쓰임
        return true; 
    }

    // (WinBattle, FleeBattle ... 변경 없음)
    #region Battle_End_Conditions
    private void WinBattle()
    {
        if (currentBattleMonster == null) return;
        
        AddLog($"{currentBattleMonster.Name}을(를) 처리했습니다!");
        char monsterIcon = currentBattleMonster.Icon;
        
        // 1. 상태이상 및 임시 버프 초기화
        currentBattleMonster.StatusEffects.Clear();
        currentBattleMonster.PoisonDamagePerTurn = 0;
        currentBattleMonster.BleedDamagePerTurn = 0;
        currentBattleMonster.StrongPoisonDamagePerTurn = 0;

        player.TempDefBuff = 0;
        player.StatusEffects.Remove(StatType.DEF);
        
        // 쿨타임 초기화 (전투 종료 시 쿨타임을 리셋해주는 기획)
        foreach (var s in player.Skills) s.CurrentCooldown = 0;

        // 2. 경험치 획득 (살아있는 경우에만)
        bool didLevelUp = false;

        if (!player.IsDead)
        {
            int baseExp = currentBattleMonster.EXPReward;
            float expBonusPercent = player.GetStatBonus(StatType.EXPGain, ModifierType.Percent);
            int finalExp = (int)Math.Round(baseExp * (1.0f + expBonusPercent));
            
            if (expBonusPercent > 0) 
                AddLog($"경험치를 {finalExp} 획득했다! (기본 {baseExp} + 보너스 {(finalExp - baseExp)})");
            else 
                AddLog($"경험치를 {finalExp} 획득했다!");

            didLevelUp = player.AddExperience(finalExp);
            if (didLevelUp) 
            { 
                AddLog($"LEVEL UP! {player.Level}레벨이 되었습니다!"); 
            }

            // [멀티] 내 스탯 정보(레벨, 경험치 등) 갱신 전송
            if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
            {
                SendMyPlayerInfo();
            }
        }
        else
        {
            // 사망 시 경험치 획득 불가 로그
            AddLog("당신은 쓰러져 있어 경험치를 얻지 못했습니다.", ConsoleColor.DarkGray);
        }

        // 3. 맵 상의 몬스터 삭제 (멀티플레이 동기화)
        bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;

        // 미믹(상자)은 이미 Chest.Open에서 처리되므로 제외
        if (currentBattleMonster.MonsterId != "mimic")
        {
            // [Case A] 함정 전투였을 경우 (Trap)
           // [공통] 함정 전투였다면, 내 화면에서 함정 삭제 처리
            if (isTrapBattle && isMulti)
            {
                // 좌표로 함정 찾기
                var mapTrap = traps.FirstOrDefault(t => t.X == currentBattleMapX && t.Y == currentBattleMapY);
                
                // 만약 리스트에 없더라도(이미 지워졌거나 등), 좌표 기반으로 삭제 패킷은 보내야 함
                if (mapTrap == null)
                {
                    // 더미 트랩 생성 (삭제 패킷 전송용)
                    mapTrap = new Trap(currentBattleMapX, currentBattleMapY, TrapType.Battle, '^');
                }
                else
                {
                    // 내 화면 갱신
                    mapTrap.ForceTrigger(this);
                    Thread.Sleep(50);
                    SendTrapUpdatePacket(mapTrap);
                }

                // [핵심 수정] 멀티플레이라면 상대방에게 "이 좌표의 함정을 지워라" 명령 전송
                if (isMulti)
                {
                    SendTrapUpdatePacket(mapTrap);
                }
            }
            // [Case B] 일반 몬스터 전투였을 경우 (Monster)
            else
            {
                // 1. 내 화면에서 삭제
                var mapMonster = monsters.FirstOrDefault(m => m.X == currentBattleMapX && m.Y == currentBattleMapY);
                
                // (혹시 이동했을 수 있으니 참조로도 시도 - 호스트용)
                if (mapMonster == null && NetworkManager.Instance.IsHost && currentMapMonsterReference != null)
                {
                    mapMonster = currentMapMonsterReference;
                }

                if (mapMonster != null)
                {
                    monsters.Remove(mapMonster);
                    Thread.Sleep(50);
                    SendMonsterDead(currentBattleMapX, currentBattleMapY);
                }

                // 2. 멀티플레이라면 상대방에게 삭제 요청
                if (isMulti)
                {
                    // [핵심] MonsterDead 패킷 전송 -> 상대방 화면에서도 지워짐
                    // (내가 지운 몬스터의 좌표를 보냄. 만약 내 화면에서 못 찾았어도 전투 좌표로 보냄)
                    int delX = (mapMonster != null) ? mapMonster.X : currentBattleMapX;
                    int delY = (mapMonster != null) ? mapMonster.Y : currentBattleMapY;

                    SendMonsterDead(delX, delY);
                }
            }
        }

        // 4. 아이템 드랍 및 엔딩 처리
        // (죽은 플레이어도 드랍 테이블은 돌리지만, 마지막에 리스트를 비워서 획득 못하게 처리함)

        // [미믹 보상]
        if (currentBattleMonster.MonsterId == "mimic")
        {
            AddLog("미믹이 품고 있던 보물을 뱉어냅니다!");
            
            int lootCount = 1;
            if (rand.NextDouble() < 0.10) lootCount = 3;
            else if (rand.NextDouble() < 0.25) lootCount = 2;

            double equipmentChance = 0.40 + ((currentStage - 1) * 0.10);

            currentLootList = new List<Item>();
            for (int i = 0; i < lootCount; i++)
            {
                if (rand.NextDouble() < equipmentChance)
                    currentLootList.Add(ItemDB.GenerateRandomEquipment(player.Class, rand, false, currentStage));
                else
                    currentLootList.Add(ItemDB.CreateRandomConsumable(rand, false, currentStage));
            }
        }
        // [일반/보스 드랍]
        else 
        {
            if (monsterIcon == 'B') // 메인 보스
            {
                // [엔딩 체크] 3스테이지 보스라면 엔딩으로 직행
                if (currentStage == 3)
                {
                    // 클리어 타임 기록
                    gameClearTime = DateTime.Now - gameStartTime;
                    StartEndingStory();
                    return; // 여기서 함수 종료
                }

                AddLog("보스 몬스터가 희귀한 아이템을 드랍합니다!");
                currentLootList = ItemDB.GenerateBossDrops(player.Class, rand, currentStage);
                
                // 포탈 생성 (보스방 오른쪽 벽 중앙)
                portalPosition = GetPortalSpawnPoint(bossRoom);
                map[portalPosition.x, portalPosition.y] = 'O';
                AddLog("신비한 포탈이 열렸습니다!", ConsoleColor.Magenta);
            }
            else if (monsterIcon == 'F') // 필드 보스
            {
                AddLog("필드 보스가 특별한 아이템을 드랍합니다!");
                currentLootList = ItemDB.GenerateFieldBossDrops(player.Class, rand, currentStage);
            }
            else // 일반 몬스터
            {
                currentLootList = ItemDB.GenerateAllDrops(player.Class, rand, currentStage);
            }
        }

        // [사망 패널티] 죽은 플레이어는 아이템 획득 불가
        if (player.IsDead)
        {
            isTrapBattle = false;
            FinishBattleResultSequence(); // -> 대기 화면 ("동료 기다리는 중...")
            return;
        }

        // 아이템 큐 구성 (장비 아이템 분리)
        var equipmentDrops = currentLootList.Where(item => item is Equipment).Cast<Equipment>();
        equipmentDropQueue.Clear();
        foreach (var eq in equipmentDrops) { equipmentDropQueue.Enqueue(eq); }

        // 루팅 창 종료 후 복귀할 상태 지정 (전투에서 왔음을 표시)
        stateBeforeLoot = GameState.Battle;

        // 5. 상태 전환 (우선순위: 레벨업 -> 장비획득 -> 소비템획득 -> 종료)
        if (didLevelUp)
        {
            currentState = GameState.LevelUp;
        }
        else if (equipmentDropQueue.Count > 0)
        {
            currentLootEquipment = equipmentDropQueue.Dequeue();
            currentState = GameState.LootDrop;
        }
        else if (currentLootList.Count > 0)
        {
            currentState = GameState.LootSummary;
        }
        else
        {
            // 획득할 게 없으면 즉시 종료 시퀀스 진입
            // (FinishBattleResultSequence 내부에서 멀티플레이 대기 로직 수행)
            isTrapBattle = false;
            FinishBattleResultSequence();
        }
    }
    
    private (int x, int y) GetPortalSpawnPoint(Rectangle bossRoom)
    {
        // 보스방은 오른쪽에 생성됨
        // 보스방의 '오른쪽 벽' 중앙 좌표를 찾음
        int px = bossRoom.Right; // (벽 타일)
        int py = bossRoom.Center.y;
        
        // 맵 경계를 넘지 않도록
        if (px >= MapWidth - 1) px = MapWidth - 2; 

        // 포탈이 스폰될 3칸(위/중간/아래)의 벽을 '.' (바닥)으로 뚫음
        if (py - 1 > 0) map[px, py - 1] = '.';
        map[px, py] = '.';
        if (py + 1 < MapHeight) map[px, py + 1] = '.';
        
        // 포탈은 '중앙' 타일에만 스폰 (맵에는 'O'로 표시됨)
        return (px, py);
    }

    private void FleeBattle()
    {
        // [수정] 1. 함정(미믹, 몬스터 함정) 전투라면 후퇴 절대 불가
        if (isTrapBattle)
        {
            AddLog("함정에 걸린 전투에서는 도망칠 수 없습니다!");
            return;
        }

        // [수정] 2번 요청: 후퇴 시 몬스터가 맵에 남음 (별도 코드 필요 없음)
        // 몬스터를 '제거'하지 않고 전투만 종료
        AddLog("무사히 도망쳤습니다!");

        if (currentBattleMonster != null)
        {
            currentBattleMonster.StatusEffects.Clear();
            currentBattleMonster.PoisonDamagePerTurn = 0;
            currentBattleMonster.BleedDamagePerTurn = 0;
        }

        player.TempDefBuff = 0;
        player.StatusEffects.Remove(StatType.DEF);

        // [수정] currentBattleMonster = null; 삭제! (배경 유지를 위해)
        isTrapBattle = false;

        FinishBattleResultSequence();
    }
    #endregion
    
    private bool IsValidMove(int x, int y, Monster self)
    {
        // 맵 경계
        if (x <= 0 || x >= MapWidth -1 || y <= 0 || y >= MapHeight - 1) return false;
        
        // 벽
        if (map[x, y] != '.') return false; 
        
        // 플레이어
        if (player.X == x && player.Y == y) return false;
        
        // 다른 몬스터
        if (monsters.Any(m => m != self && m.X == x && m.Y == y)) return false;
        
        return true;
    }
    
    private void ProcessMonsterTurn_World()
    {
        if (monsters == null) return;
        // 호스트만 몬스터 AI를 연산합니다.
        if (NetworkManager.Instance.IsConnected && !NetworkManager.Instance.IsHost) return;

        foreach (var monster in monsters.ToList()) 
        {
            if (monster.Icon == 'B') continue; 

            // 50% 확률로 이동 시도
            if (rand.NextDouble() < 0.5)
            {
                int move = rand.Next(0, 5);
                int newX = monster.X;
                int newY = monster.Y;

                switch (move) { case 1: newY--; break; case 2: newY++; break; case 3: newX--; break; case 4: newX++; break; }
                
                bool hitPlayer = (newX == player.X && newY == player.Y);
                bool hitOther = (otherPlayer != null && newX == otherPlayer.X && newY == otherPlayer.Y);

                // [수정] 포탈 대기 중인 플레이어는 충돌 무시 (유령 취급)
                if (hitPlayer) hitPlayer = false;
                if (hitOther && otherPlayer != null) hitOther = false;

                if (hitPlayer || hitOther)
                {
                    // [핵심 수정] 호스트가 포탈에 있는데 동료(게스트)가 맞은 경우
                    // -> 동료에게만 전투를 걸고, 호스트는 관여하지 않음 (몬스터 삭제)
                    if (hitOther)
                    {
                        // 1. 패킷 전송 (동료에게 싸우라고 명령)
                        var data = new BattleStartData 
                        { 
                            MonsterId = monster.MonsterId, 
                            IsFromTrap = false,
                            MapX = monster.X,
                            MapY = monster.Y,
                            CurrentHP = -1,
                            MaxHP = monster.MaxHP, ATK = monster.ATK, DEF = monster.DEF, EXPReward = monster.EXPReward
                        };
                        NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleStart, Data = JsonSerializer.Serialize(data) });

                        // 2. 호스트 맵에서 몬스터 삭제 (중복 충돌 및 인트로 중복 방지)
                        monsters.Remove(monster);
                        
                        // 3. 상태 갱신 전송
                        SendMonsterUpdate();
                        
                        continue; // 나는 전투 진입 안 함
                    }

                    // 일반적인 경우 (내가 맵에 있음 -> 같이 싸우거나 내가 싸움)
                    StartBattle(monster);
                    return; 
                }

                if (IsValidMove(newX, newY, monster))
                {
                    monster.X = newX; 
                    monster.Y = newY;
                }
            }
        }
        if (NetworkManager.Instance.IsHost) SendMonsterUpdate();
    }

    // [신규] 몬스터 위치 전송 (호스트 -> 게스트)
    private void SendMonsterUpdate()
    {
        var data = new MonsterUpdateData
        {
            XPositions = monsters.Select(m => m.X).ToList(),
            YPositions = monsters.Select(m => m.Y).ToList(),
            
            // [신규] 현재 몬스터들의 ID 리스트도 함께 전송
            MonsterIds = monsters.Select(m => m.MonsterId).ToList()
        };

        var packet = new Packet 
        { 
            Type = PacketType.MonsterUpdate, 
            Data = JsonSerializer.Serialize(data) 
        };
        NetworkManager.Instance.Send(packet);
    }

    // [신규] 몬스터 위치 수신 (게스트)
    private void HandleMonsterUpdate(string json)
    {
        var data = JsonSerializer.Deserialize<MonsterUpdateData>(json);
        
        int hostCount = data.XPositions.Count;

        // 1. 몬스터 개수 맞추기
        // (호스트보다 내 몬스터가 적으면 추가, 많으면 삭제)
        while (monsters.Count < hostCount)
        {
            // 임시 몬스터 추가 (아래에서 ID와 좌표로 덮어씌워짐)
            monsters.Add(MonsterDB.CreateMonster("slime", 0, 0)); 
        }
        while (monsters.Count > hostCount)
        {
            monsters.RemoveAt(monsters.Count - 1);
        }

        // 2. 몬스터 정보 동기화
        for (int i = 0; i < hostCount; i++)
        {
            string hostId = data.MonsterIds[i];
            int hostX = data.XPositions[i];
            int hostY = data.YPositions[i];

            // [핵심] ID가 다르면 몬스터 객체를 새로 생성하여 교체 (일반 몬스터화 버그 해결)
            if (monsters[i].MonsterId != hostId)
            {
                monsters[i] = MonsterDB.CreateMonster(hostId, hostX, hostY);
            }
            else
            {
                // ID가 같으면 좌표만 갱신 (부드러운 이동)
                monsters[i].X = hostX;
                monsters[i].Y = hostY;
            }
        }

        NetworkManager.Instance.IsDirty = true;
    }

    // [수정] 기본 공격도 데미지 계산만 하도록 변경
    private int AttackMonster(Player attacker, Monster target)
    {
        lastAttackWasCrit = false;
        lastAttackWasMiss = false;

        if (rand.NextDouble() < MONSTER_EVASION_CHANCE)
        {
            AddLog($"{target.Name}이(가) 플레이어의 공격을 회피했습니다!");
            lastAttackWasMiss = true; 
            return 0; 
        }
        
        int damage = 0; // 최종 데미지
        
        if (target.MonsterId == "mimic")
        {
            damage = 1;
            AddLog("미믹의 단단한 껍질이 공격을 튕겨냅니다! (데미지 1)");
            return damage;
        }
        
        switch (attacker.Class)
        {
            case PlayerClass.Warrior:
                int rawWarriorDmg = (player.ATK + player.STR);
                damage = ApplyDefense(rawWarriorDmg, target.DEF); // [신규] 방어력 적용
                AddLog("시스템 방어자: 물리적 오류(충돌) 방어!");
                break;
                
            case PlayerClass.Wizard:
                // [수정] 마법사의 기본 공격은 '주문'으로 취급 (방어력 무시)
                float intMultiplier = 1.0f + (player.INT / 100.0f);
                int magicDamage = (int)(player.ATK * intMultiplier);
                damage = Math.Max(1, magicDamage); // (ApplyDefense 없음)
                AddLog("버그 수정자: 주문으로 오류 수정!");
                break;
                
            case PlayerClass.Rogue:
                float dexMultiplier = 1.0f + (player.DEX / 100.0f);
                int rawRogueDmg = (int)(player.ATK * dexMultiplier);
                
                float totalCritChance = player.CritChance;
                if (rand.NextDouble() < totalCritChance)
                {
                    rawRogueDmg = (int)(rawRogueDmg * 1.5); 
                    lastAttackWasCrit = true;
                }
                damage = ApplyDefense(rawRogueDmg, target.DEF); // [신규] 방어력 적용
                break;
        }
        
        if (damage < 1) damage = 1;
        return damage; 
    }

    // (Chest_Logic ... 변경 없음)
    #region Chest_Logic
    private void TryOpenChest()
    {
        int px = player.X; int py = player.Y;
        int[] dx = { 0, 0, 1, -1 }; int[] dy = { 1, -1, 0, 0 };
        
        for (int i = 0; i < 4; i++) 
        {
            int targetX = px + dx[i]; 
            int targetY = py + dy[i];
            
            Chest? targetChest = chests.Find(c => c.X == targetX && c.Y == targetY && !c.IsOpen);
            
            if (targetChest != null) 
            { 
                // [핵심 수정] 이미 다른 플레이어가 상호작용 중인지 확인
                if (targetChest.IsBusy)
                {
                    AddLog("동료가 상자를 확인하고 있습니다.");
                    return;
                }

                // [핵심] 상자 점유 시작 (잠금)
                targetChest.IsBusy = true;
                
                // 멀티플레이라면 잠금 사실 전파
                if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
                {
                    SendChestBusy(targetChest, true);
                }

                currentTargetChest = targetChest;
                currentState = GameState.Chest_Confirm;
                return; 
            }
        }
        
        AddLog("주변에 열 수 있는 상자가 없습니다.");
    }
    public void ProcessChestLoot(List<Item> foundItems)
    {
        if (foundItems.Count == 0) { currentState = GameState.World; return; }
        currentLootList = foundItems;
        stateBeforeLoot = GameState.World; 
        var equipmentDrops = currentLootList.Where(item => item is Equipment).Cast<Equipment>();
        equipmentDropQueue.Clear();
        foreach (var eq in equipmentDrops) { equipmentDropQueue.Enqueue(eq); }
        if (equipmentDropQueue.Count > 0) { currentLootEquipment = equipmentDropQueue.Dequeue(); currentState = GameState.LootDrop; }
        else { currentState = GameState.LootSummary; }
    }
    #endregion
    
    // (Pause_Logic ... 변경 없음)
    #region Pause_Logic
    private void DrawPauseWindow()
    {
        int width = (int)(screenWidth * 0.5); 
        int height = (int)(screenHeight * 0.5); // 높이 약간 증가
        width = Math.Max(40, width); 
        height = Math.Max(14, height); 
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;
        
        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
        DrawBox(startX, startY, width, height, "PAUSE");
        
        int yDraw = startY + 4; 

       bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;
        string mainBtnText = isMulti ? "[1] 연결 끊기 (메인)" : "[1] 메인 화면으로";

        // 메뉴 항목들
        string[] items = {
            mainBtnText,
            "[2] 조작법 확인",
            "[3] 게임 종료",
            "[ESC] 계속하기"
        };

        for (int i = 0; i < items.Length; i++)
        {
            string text = items[i];
            bool isSelected = (pauseMenuIndex == i);
            
            string prefix = isSelected ? "► " : "  ";
            ConsoleColor color = isSelected ? ConsoleColor.Yellow : ConsoleColor.White;

            DrawTextToBuffer(startX + (width / 2) - (GetDisplayWidth(text) / 2), yDraw, prefix + text, color);
            yDraw += 2;
        }
    }
   private void ProcessPauseInput(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                pauseMenuIndex = (pauseMenuIndex - 1 + 4) % 4; // 4개 항목 순환
                break;
            case ConsoleKey.DownArrow:
                pauseMenuIndex = (pauseMenuIndex + 1) % 4;
                break;
            case ConsoleKey.Enter:
                ProcessPauseAction(pauseMenuIndex);
                break;

            // 단축키 지원
            case ConsoleKey.D1: ProcessPauseAction(0); break;
            case ConsoleKey.D2: ProcessPauseAction(1); break; // 조작법
            case ConsoleKey.D3: ProcessPauseAction(2); break; // 종료
            case ConsoleKey.Escape: ProcessPauseAction(3); break; // 계속
        }
    }
    
    private void ProcessPauseAction(int index)
    {
        switch (index)
        {
            case 0: // 1. 메인으로
                // [확인] 여기서 Close()를 호출하면 위에서 수정한 로직 덕분에 IsHost가 false가 됩니다.
                NetworkManager.Instance.Close(); 
                
                needsRestart = true; 
                gameRunning = false; 
                break;

            case 1: // 2. 조작법
                returnStateFromHelp = GameState.Pause; // 조작법 보고 Pause로 복귀
                currentState = GameState.HowToPlay;
                break;

            case 2: // 3. 종료
                gameRunning = false; 
                break;

            case 3: // 4. 계속하기
                currentState = returnStateFromMenu; // [수정] 저장된 상태로 복귀
                break;
        }
    }

    // [신규] 레벨 업 창 입력 처리
    private void ProcessLevelUpInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            if (equipmentDropQueue.Count > 0)
            {
                currentLootEquipment = equipmentDropQueue.Dequeue();
                currentState = GameState.LootDrop;
            }
            else if (currentLootList.Count > 0)
            {
                currentState = GameState.LootSummary;
            }
            else
            {
                // [수정] currentBattleMonster = null; 삭제!
                FinishBattleResultSequence();
            }
        }
    }

    // [신규] 레벨 업 창 그리기
    private void DrawLevelUpWindow()
    {
        var stats = player.LastLevelUpStats;
        int levelsGained = player.LevelsGainedThisTurn;
        int oldLevel = player.Level - levelsGained;

        // 1. 창 크기 및 위치 계산 (변경 없음)
        int width = 45;
        int height = 8 + stats.Count;
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawBox(startX, startY, width, height, "레벨 업!");

        // 2. 창 내부 클리어 (변경 없음)
        for (int y = startY + 1; y < startY + height - 1; y++)
        {
            for (int x = startX + 1; x < startX + width - 1; x++)
            {
                DrawToBuffer(x, y, ' ', ConsoleColor.Gray, ConsoleColor.Black);
            }
        }

        int yDraw = startY + 2;

        // 3. 레벨 표시 (변경 없음)
        string levelText = $"{oldLevel}LV  ->  {player.Level}LV";
        DrawTextToBuffer(startX + (width / 2) - (GetDisplayWidth(levelText) / 2), yDraw++, levelText, ConsoleColor.Yellow);
        yDraw++; // 공백 1줄

        // --- [핵심 수정] ---

        // 4. 스탯 표시 (정렬 로직 변경)

        // 4a. 부제목 가운데 정렬 (변경 없음)
        string subtitle = "--- [상승한 스탯] ---";
        int subtitleX = startX + (width / 2) - (GetDisplayWidth(subtitle) / 2);
        DrawTextToBuffer(subtitleX, yDraw++, subtitle, ConsoleColor.Cyan);

        if (stats.Count == 0)
        {
            // (가운데 정렬을 위해 subtitleX와 동일한 X좌표 사용)
            DrawTextToBuffer(subtitleX, yDraw++, "(상승한 스탯 없음)", ConsoleColor.DarkGray);
        }
        else
        {
            // 4b. 정렬을 위해 모든 스탯 이름의 "최대 표시 너비" 계산 (변경 없음)
            int maxLabelWidth = 0;
            foreach (var stat in stats)
            {
                maxLabelWidth = Math.Max(maxLabelWidth, GetDisplayWidth(stat.Key));
            }

            // [신규] 4c. 모든 스탯 라인을 '먼저' 생성해서 리스트에 저장
            //            이 과정에서 "가장 넓은 스탯 라인"의 너비를 찾습니다.
            List<string> statLinesToShow = new List<string>();
            int maxStatLineWidth = 0;

            foreach (var stat in stats)
            {
                string label = stat.Key;
                string padding = new string(' ', maxLabelWidth - GetDisplayWidth(label));
                string labelPart = label + padding;

                string oldVal = stat.Value.old.ToString();
                string newVal = stat.Value.@new.ToString();
                string gain = $"(+{stat.Value.@new - stat.Value.old})";

                // (컬럼 정렬 포맷은 그대로 유지)
                string statLine = $"{labelPart} : {oldVal,3} -> {newVal,3} {gain,-5}";

                statLinesToShow.Add(statLine);
                // (가장 넓은 라인의 너비 갱신)
                maxStatLineWidth = Math.Max(maxStatLineWidth, GetDisplayWidth(statLine));
            }

            // [신규] 4d. 스탯 블록 '전체'를 가운데 정렬하기 위한 시작 X좌표 계산
            int statBlockStartX = startX + (width / 2) - (maxStatLineWidth / 2);

            // 4e. [수정] 고정된 'startX + 6' 대신, 계산된 'statBlockStartX'를 사용
            foreach (string lineToDraw in statLinesToShow)
            {
                DrawTextToBuffer(statBlockStartX, yDraw++, lineToDraw, ConsoleColor.White);
            }
        }

        // --- [수정 끝] ---

        // 5. 닫기 버튼 (변경 없음)
        yDraw = startY + height - 2;
        DrawTextToBuffer(startX + 2, yDraw, "계속하려면 [Enter]를 누르세요.", ConsoleColor.Yellow);
    }
    
    // [신규] 방어력(DEF)에 따른 데미지 감소율(%)을 계산하는 헬퍼
    private float CalculateDefenseReduction(int defenderDEF)
    {
        // 공식: Reduction % = DEF / (DEF + K)
        if (defenderDEF <= 0) return 0f;
        
        // (float) 캐스팅으로 나눗셈이 0이 되는 것을 방지
        return (float)defenderDEF / (float)(defenderDEF + DEFENSE_CONSTANT);
    }

    // [신규] 최종 '물리' 데미지를 계산하는 헬퍼
    private int ApplyDefense(int rawDamage, int defenderDEF)
    {
        // 1. 방어력에 따른 감소율 계산
        float reduction = CalculateDefenseReduction(defenderDEF);

        // 2. 최종 데미지 계산
        int finalDamage = (int)Math.Round(rawDamage * (1.0f - reduction));

        // 3. 최소 데미지 1 보장
        return Math.Max(1, finalDamage);
    }
    
    // [신규] 캐릭터 스탯 창 닫기
    private void ProcessStatWindowInput(ConsoleKeyInfo key)
    {
        // 닫기 키
        if (key.Key == ConsoleKey.C || key.Key == ConsoleKey.B || key.Key == ConsoleKey.Escape)
        {
            currentState = returnStateFromMenu; // [수정] 저장된 상태로 복귀
            return;
        }

        char c = char.ToUpper(key.KeyChar);
        if (c == 'C' || c == 'ㅊ' || c == 'B' || c == 'ㅠ')
        {
            currentState = returnStateFromMenu; // [수정]
        }
    }

    // [신규] 캐릭터 스탯 창 그리기
    private void DrawCharacterStatWindow()
    {
        // 1. 창 크기 및 위치 계산

        // [수정] 
        int width = (int)(screenWidth * 0.30);
        width = Math.Max(70, width); // 최소 70
        width = Math.Min(screenWidth - 2, width); // 최대 (화면-2)

        // 높이(Height): 컨텐츠에 맞춰 18칸으로 고정합니다.
        int height = 18;
        height = Math.Min(screenHeight - 2, height); // (화면이 18보다 작을 경우 방지)

        // 시작 위치는 동적으로 계산 (변경 없음)
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawBox(startX, startY, width, height, "캐릭터 스탯");

        // 2. 창 내부 클리어 (변경 없음)
        for (int y = startY + 1; y < startY + height - 1; y++)
        {
            for (int x = startX + 1; x < startX + width - 1; x++)
            {
                DrawToBuffer(x, y, ' ', ConsoleColor.Gray, ConsoleColor.Black);
            }
        }

        // 3. 스탯 그리기 (2열 레이아웃)
        int xCol1 = startX + 4;

        // [수정] 2열 시작 위치를 (고정 35)가 아닌 (동적 너비 / 2)로 변경
        int xCol2 = startX + (width / 2);

        int yDraw = startY + 2;

        // --- 1열: 기본 스탯 ---
        DrawTextToBuffer(xCol1, yDraw++, $"이름:   {player.Class}");
        DrawTextToBuffer(xCol1, yDraw++, $"레벨:   {player.Level}");
        DrawTextToBuffer(xCol1, yDraw++, $"경험치: {player.EXP} / {player.EXPNext}");
        yDraw++;
        DrawTextToBuffer(xCol1, yDraw++, $"HP: {player.HP} / {player.MaxHP}");
        DrawTextToBuffer(xCol1, yDraw++, $"MP: {player.MP} / {player.MaxMP}");
        yDraw++;
        DrawTextToBuffer(xCol1, yDraw++, $"공격력 (ATK): {player.ATK}");

        float defReduction = CalculateDefenseReduction(player.DEF);
        DrawTextToBuffer(xCol1, yDraw++, $"방어력 (DEF): {player.DEF} (피해 감소: {defReduction:P1})");
        yDraw++;
        DrawTextToBuffer(xCol1, yDraw++, $"STR: {player.STR}");
        DrawTextToBuffer(xCol1, yDraw++, $"INT: {player.INT}");
        DrawTextToBuffer(xCol1, yDraw++, $"DEX: {player.DEX}");

        // --- 2열: 보너스 스탯 ---
        yDraw = startY + 2;
        DrawTextToBuffer(xCol2, yDraw++, "--- 보너스 스탯 ---", ConsoleColor.Cyan);

        float crit = player.CritChance;
        float evasion = (float)(player.DEX * PLAYER_DEX_EVASION_SCALER);
        DrawTextToBuffer(xCol2, yDraw++, $"크리티컬 확률: {crit:P1} (DEX)");
        DrawTextToBuffer(xCol2, yDraw++, $"회피율: {evasion:P1} (DEX)");

        yDraw++;

        var statTypes = new[]
        {
            StatType.EXPGain, StatType.ResourceCostReduction,
            StatType.DamageReflectChance, StatType.StunChance,
            StatType.ManaRefundChance, StatType.ManaShieldConversion,
            StatType.LifeStealPercent, StatType.BleedChance
        };

        foreach (var statType in statTypes)
        {
            float bonus = player.GetStatBonus(statType, ModifierType.Percent);
            if (bonus > 0)
            {
                StatModifier tempMod = new StatModifier(statType, bonus, ModifierType.Percent);
                DrawTextToBuffer(xCol2, yDraw++, tempMod.GetDescription(), ConsoleColor.Green);
            }
        }

        // --- 닫기 버튼 ---
        yDraw = startY + height - 2;
        DrawTextToBuffer(startX + 2, yDraw, "닫으려면 [C], [B]를 누르세요.", ConsoleColor.Yellow);
    }

    // [신규] 1. 상자 확인 창 입력 처리
    private void ProcessChestConfirmInput(ConsoleKeyInfo key)
    {
        // 1. IME에 영향받지 않는 키 (방향키, Enter)
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.RightArrow:
                chestConfirmIndex = (chestConfirmIndex + 1) % 2; 
                break;
                
            case ConsoleKey.Enter:
                ProcessChestConfirmAction(chestConfirmIndex == 0);
                return; // [수정] return으로 변경

            // 2. IME에 영향받는 키 (F/B) - 영문 모드
            case ConsoleKey.F: 
                chestConfirmIndex = 0;
                ProcessChestConfirmAction(true);
                return; 
            case ConsoleKey.B:
                chestConfirmIndex = 1;
                ProcessChestConfirmAction(false);
                return;
                
            case ConsoleKey.Escape: // (편의상 ESC도 B로)
                chestConfirmIndex = 1;
                ProcessChestConfirmAction(false);
                return;
        }

        // 3. [신규] IME에 영향받는 키 (F/B) - 한글 모드 Fallback
        char c = char.ToUpper(key.KeyChar);
        if (c == 'F' || c == 'ㄹ') // 'F' 또는 'ㄹ'
        {
            chestConfirmIndex = 0;
            ProcessChestConfirmAction(true);
        }
        else if (c == 'B' || c == 'ㅠ') // 'B' 또는 'ㅠ'
        {
            chestConfirmIndex = 1;
            ProcessChestConfirmAction(false);
        }
    }
    
    private void ProcessChestConfirmAction(bool isOpen)
    {
        if (isOpen) // (Yes) -> 열기 진행
        {
            chestAnimStartTime = DateTime.Now; 
            currentState = GameState.Chest_Opening;
            // (상자가 열리면 IsOpen이 true가 되어 IsBusy 여부와 상관없이 접근 불가하므로 여기서 IsBusy를 굳이 끌 필요 없음)
        }
        else // (No) -> 취소
        {
            AddLog("상자를 열지 않았습니다.");
            
            // [핵심 수정] 점유 해제 (잠금 풀기)
            if (currentTargetChest != null)
            {
                currentTargetChest.IsBusy = false;
                
                if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
                {
                    SendChestBusy(currentTargetChest, false);
                }
            }

            currentTargetChest = null;
            
            if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
                currentState = GameState.Multiplayer_World;
            else
                currentState = GameState.World;
        }
    }

    // [신규] 2. 상자 열기 애니메이션 처리 (매 프레임 호출)
    private void ProcessChestOpeningAnimation()
    {
        TimeSpan elapsed = DateTime.Now - chestAnimStartTime;

        if (elapsed.TotalMilliseconds >= CHEST_ANIM_TOTAL_MS)
        {
            // 애니메이션 종료 후 상태 복귀
            if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
                currentState = GameState.Multiplayer_World;
            else
                currentState = GameState.World;
            
            Chest? chest = currentTargetChest;
            currentTargetChest = null;
            
            if (chest != null)
            {
                // 1. 상자 열기 로직 실행 (아이템 획득 or 미믹 전투)
                // (Chest.Open 내부에서 game.StartBattle을 호출하면 멀티플레이 분기는 StartBattle에서 처리됨)
                chest.Open(player, this, rand, currentStage); 

                // 2. [신규] 멀티플레이라면 상자가 열렸음을 상대에게 알림
                if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
                {
                    Thread.Sleep(100);
                    SendChestUpdate(chest);
                }
            }
        }
    }

    // [신규] 3. 상자 확인/애니메이션 창 그리기
    private void DrawChestWindow()
    {
        string[] art;
        ConsoleColor bgColor = ConsoleColor.Black;
        ConsoleColor artColor = ConsoleColor.Yellow;

        // 1. 현재 상태에 따라 아트와 배경색 결정 (변경 없음)
        if (currentState == GameState.Chest_Confirm)
        {
            art = AsciiArt.GetChestArt(false); 
        }
        else // GameState.Chest_Opening
        {
            TimeSpan elapsed = DateTime.Now - chestAnimStartTime;
            if (elapsed.TotalMilliseconds < CHEST_ANIM_FLASH_MS)
            {
                art = AsciiArt.GetChestArt(false); 
                bgColor = ConsoleColor.DarkYellow; 
            }
            else
            {
                art = AsciiArt.GetChestArt(true); 
            }
        }

        // 2. 아스키 아트 크기를 기반으로 창 크기 동적 계산 (변경 없음)
        int artHeight = art.Length;
        int artWidth = 0;
        foreach (string line in art) { artWidth = Math.Max(artWidth, GetDisplayWidth(line)); }
        int width = Math.Max(40, artWidth + 4); 
        int height = artHeight + 6; 
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        // 3. 창 그리기 (배경색 적용) (변경 없음)
        DrawBox(startX, startY, width, height, "상자");
        DrawFilledBox(startX + 1, startY + 1, width - 2, height - 2, bgColor); 

        // 4. 아트 그리기 (가운데 정렬) (변경 없음)
        int artX = startX + (width / 2) - (artWidth / 2);
        int artY = startY + 2;
        for (int i = 0; i < art.Length; i++)
        {
            DrawTextToBuffer(artX, artY + i, art[i], artColor, bgColor, true);
        }

        // 5. 버튼 그리기 (Confirm 상태일 때만)
        if (currentState == GameState.Chest_Confirm)
        {
            // [핵심 수정] F/B 버튼 하이라이트
            string buttonF = " [F] 열기 ";
            string buttonB = " [B] 열지 않기 ";
            int buttonY = startY + height - 2;

            int totalButtonWidth = GetDisplayWidth(buttonF) + GetDisplayWidth(buttonB) + 2;
            int buttonStartX = startX + (width / 2) - (totalButtonWidth / 2);

            DrawTextToBuffer(buttonStartX, buttonY, buttonF, 
                             chestConfirmIndex == 0 ? ConsoleColor.Black : ConsoleColor.White, 
                             chestConfirmIndex == 0 ? ConsoleColor.Green : bgColor);

            DrawTextToBuffer(buttonStartX + GetDisplayWidth(buttonF) + 2, buttonY, buttonB, 
                             chestConfirmIndex == 1 ? ConsoleColor.Black : ConsoleColor.White, 
                             chestConfirmIndex == 1 ? ConsoleColor.Red : bgColor);
        }
    }
    private void DrawFilledBox(int x, int y, int width, int height, ConsoleColor color)
    {
        if (width <= 0 || height <= 0) return;

        // [수정] 화면 안에 들어오는 영역만 계산해서 루프 (성능 최적화)
        int startY = Math.Max(0, y);
        int endY = Math.Min(screenHeight, y + height);
        int startX = Math.Max(0, x);
        int endX = Math.Min(screenWidth, x + width);

        for (int row = startY; row < endY; row++)
        {
            for (int col = startX; col < endX; col++)
            {
                screenBuffer[row, col] = new ScreenCell(' ', color, color);
            }
        }
    }

    private void DrawClassConfirmation(string className, bool confirmChoice)
    {
        int width = 70;
        int height = 10;
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        // 배경 (일시정지 창이나 상자 창과 유사하게)
        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
        DrawBox(startX, startY, width, height, "직업 선택 확인");

        // 메시지
        string msg = $"정말 [{className}]을(를) 선택하시겠습니까?";
        DrawTextToBuffer(startX + (width / 2) - (GetDisplayWidth(msg) / 2), startY + 4, msg, ConsoleColor.White);

        // 버튼
        string yes = "  예(Y)  ";
        string no = "  아니요(N)  ";

        // 선택된 버튼 하이라이트
        ConsoleColor yesFg = confirmChoice ? ConsoleColor.Black : ConsoleColor.White;
        ConsoleColor yesBg = confirmChoice ? ConsoleColor.Green : ConsoleColor.Black;
        ConsoleColor noFg = !confirmChoice ? ConsoleColor.Black : ConsoleColor.White;
        ConsoleColor noBg = !confirmChoice ? ConsoleColor.Red : ConsoleColor.Black;

        int totalWidth = GetDisplayWidth(yes) + GetDisplayWidth(no) + 4; // 버튼 사이 간격 4
        int buttonX_Yes = startX + (width / 2) - (totalWidth / 2);
        int buttonX_No = buttonX_Yes + GetDisplayWidth(yes) + 4;
        int buttonY = startY + 6;

        DrawTextToBuffer(buttonX_Yes, buttonY, yes, yesFg, yesBg);
        DrawTextToBuffer(buttonX_No, buttonY, no, noFg, noBg);
    }
    // [신규] 1. 메인 타이틀 깜빡임 애니메이션 처리
    private void ProcessMainMenuAnimation()
    {
        if (DateTime.Now >= nextTitleBlink)
        {
            isTitleBright = !isTitleBright; // 상태 토글
            nextTitleBlink = DateTime.Now.AddMilliseconds(TITLE_BLINK_DURATION_MS);
        }
    }

    // [신규] 2. '게임 시작' 선택 시, 맵과 플레이어를 초기화하는 헬퍼
    private void InitializeGameData(PlayerClass selectedClass)
    {
        // 1. 맵 크기 동적 설정
        int viewportWidth = screenWidth - 2;
        int viewportHeight = worldMapHeight - 2;
        MapWidth = viewportWidth;   
        MapHeight = viewportHeight; 

        // [신규] 1번 요청: 재시작 시 이전 로그 초기화
        logMessages.Clear(); 

        // 2. 플레이어 생성
        player = new Player(selectedClass);
        
        // 3. 데이터 리스트 초기화
        InitializeGameData(); // 몬스터, 방 등 리스트 클리어

        // 4. 첫 스테이지 로드
        gameStartTime = DateTime.Now; // (클리어 타임용 시간 초기화도 여기서)
        TransitionToStage(1); 
    }

    // [신규] 3. 메인 메뉴 입력 처리
   private void ProcessMainMenuInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.UpArrow)
        {
            mainMenuIndex = (mainMenuIndex - 1 + 4) % 4;
        }
        else if (key.Key == ConsoleKey.DownArrow)
        {
            mainMenuIndex = (mainMenuIndex + 1) % 4;
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            switch (mainMenuIndex)
            {
                case 0: // 싱글 플레이
                    StartIntro();
                    break;
                case 1: // 멀티 플레이 [신규]
                    currentState = GameState.Multiplayer_Nick;
                    playerNickname = ""; // 초기화
                    break;
                case 2: // 조작법
                    returnStateFromHelp = GameState.MainMenu;
                    currentState = GameState.HowToPlay;
                    break;
                case 3: // 종료
                    gameRunning = false;
                    break;
            }
        }
    }
    

    // [신규] 4. 조작법 창 입력 처리
    private void ProcessHowToPlayInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.B || key.Key == ConsoleKey.Enter)
        {
            currentState = returnStateFromHelp; // (이 변수는 기존 로직 유지)
            return;
        }
        
        char c = char.ToUpper(key.KeyChar);
        if (c == 'B' || c == 'ㅠ')
        {
            currentState = returnStateFromHelp;
        }
    }

    // [신규] 5. 메인 메뉴 그리기
    private void DrawMainMenu()
    {
        // 1. 타이틀 아트 그리기
        string[] titleArt = AsciiArt.GetMainTitleArt();
        int titleHeight = titleArt.Length;
        int titleWidth = 0;
        foreach(string line in titleArt) { titleWidth = Math.Max(titleWidth, GetDisplayWidth(line)); }
        
        int titleX = screenWidth / 2 - titleWidth / 2;
        int titleY = screenHeight / 4; // 화면 1/4 지점
        
        ConsoleColor titleColor = isTitleBright ? ConsoleColor.Yellow : ConsoleColor.White;
        
        for(int i=0; i<titleHeight; i++)
        {
            DrawTextToBuffer(titleX, titleY + i, titleArt[i], titleColor, ConsoleColor.Black, true);
        }

        // 2. 메뉴 옵션 그리기
        string[] menuItems = { "싱글 플레이", "멀티 플레이", "조작법", "게임 종료" }; // [수정]
        int menuY = titleY + titleHeight + 4;

        for (int i = 0; i < menuItems.Length; i++)
        {
            string item = menuItems[i];
            ConsoleColor color = ConsoleColor.DarkGray;

            if (i == mainMenuIndex)
            {
                item = $"► {item} ◄";
                color = ConsoleColor.Yellow;
            }
            
            int itemX = screenWidth / 2 - GetDisplayWidth(item) / 2;
            DrawTextToBuffer(itemX, menuY + (i * 2), item, color);
        }
    }

    // [신규] 6. 조작법 창 그리기
    private void DrawHowToPlayWindow()
    {
        // (월드 레이아웃 위에 겹쳐서 그림)
        int width = (int)(screenWidth * 0.7);
        int height = (int)(screenHeight * 0.7);
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
        DrawBox(startX, startY, width, height, "조작법");

        int yDraw = startY + 2;
        int xDraw = startX + 4;

        DrawTextToBuffer(xDraw, yDraw++, "[맵 탐험]", ConsoleColor.Cyan);
        DrawTextToBuffer(xDraw, yDraw++, " - [W/A/S/D] 또는 [방향키]: 이동");
        DrawTextToBuffer(xDraw, yDraw++, " - [F]:                       주변의 상자 열기");
        DrawTextToBuffer(xDraw, yDraw++, " - [E]:                       인벤토리 열기");
        DrawTextToBuffer(xDraw, yDraw++, " - [C]:                       캐릭터 스탯 보기");
        DrawTextToBuffer(xDraw, yDraw++, " - [ESC]:                     일시정지 (메뉴)");
        yDraw++;
        DrawTextToBuffer(xDraw, yDraw++, "[전투]", ConsoleColor.Cyan);
        DrawTextToBuffer(xDraw, yDraw++, " - [1]~[4]:                   기본 행동 선택");
        DrawTextToBuffer(xDraw, yDraw++, " - [Q/W/E/R]:                 스킬 선택");
        DrawTextToBuffer(xDraw, yDraw++, " - [B]:                       뒤로가기");
        DrawTextToBuffer(xDraw, yDraw++, " - [ESC]:                     일시정지 (메Loop)");
        yDraw++;
        DrawTextToBuffer(xDraw, yDraw++, "[창 닫기]", ConsoleColor.Cyan);
        DrawTextToBuffer(xDraw, yDraw++, " - [Enter], [ESC], [B]:       대부분의 창 닫기");
        
        yDraw = startY + height - 2;
        DrawTextToBuffer(startX + 2, yDraw, "돌아가려면 [Enter] 또는 [ESC]를 누르세요.", ConsoleColor.Yellow);
    }

    public void StartBuffAnimation(object target, int amount, ConsoleColor blinkColor, Action onComplete)
    {
        // 1. 애니메이션 정보 설정
        currentAnimationTarget = target;
        animationCallback = onComplete;
        currentState = GameState.Battle_Animation;

        // [핵심 수정] 시간 기반 렌더링을 위해 '시작 시간'을 반드시 초기화해야 합니다.
        // 이 줄이 없어서 회복 연출이 즉시 종료되거나 색상이 안 나왔던 것입니다.
        battleAnimationStartTime = DateTime.Now;

        // 2. 텍스트 설정
        currentAsciiDamageString = $"+{amount}"; // (예: "+50")
        
        // (showHitOverlay는 DrawSingleEntityArt에서 시간 기준으로 계산하지만 true로 둠)
        showHitOverlay = true; 

        // 3. 색상 설정 (Green, Blue, Gray 등)
        this.customBlinkColor = blinkColor;
    }

    // [신규] 턴 딜레이 타이머를 처리하는 헬퍼
   private void ProcessBattleTurnDelay()
    {
        if (DateTime.Now < turnDelayEndTime) return;

        // 호스트이거나 연결된 상태라면 무조건 멀티플레이 전투 상태로 복귀
        if (NetworkManager.Instance.IsHost || NetworkManager.Instance.IsConnected)
        {
            currentState = GameState.Multiplayer_Battle;
        }
        else
        {
            currentState = GameState.Battle;
        }

        // 보류해뒀던 콜백 실행 (다음 턴 로직 등)
        if (animationCallback != null)
        {
            var callback = animationCallback;
            animationCallback = null; 
            callback.Invoke(); 
        }
        
    }

    // [신규] 아스키 아트 숫자를 그리는 헬퍼
    private void DrawAsciiNumber(int startX, int startY, string numberString, ConsoleColor defaultColor, int padding = 1)
    {
        // 1. 아스키 아트 라이브러리 가져오기
        var artLibrary = AsciiArt.GetNumberArtLibrary();
        if (artLibrary == null || artLibrary.Count == 0) return; 

        int currentX = startX;
        int artHeight = 3; // (모든 숫자 아트의 높이가 5라고 가정)
        int artWidth = 0;

        // 2. 문자열 반복 (정방향 또는 역방향)
        // (padding이 음수면 오른쪽 정렬을 위해 역방향으로 그립니다)
        IEnumerable<char> characters = (padding > 0) ? numberString : numberString.Reverse();

        foreach (char c in characters)
        {
            // 3. 문자에 해당하는 아트 가져오기 (없으면 ? 로)
            if (!artLibrary.TryGetValue(c, out string[]? art))
            {
                art = artLibrary.GetValueOrDefault('?', new string[] { "?", "?", "?", "?", "?" });
            }

            // (아트 파일이 손상되었거나 비어있는 경우 방지)
            if (art == null || art.Length == 0) continue; 
            
            // 4. 아트의 너비 계산
            artWidth = 0;
            foreach(string line in art) { artWidth = Math.Max(artWidth, GetDisplayWidth(line)); }
            if (artWidth == 0) continue; 

            // 5. 색상 결정
            ConsoleColor charColor = defaultColor;
            if (c == '+') charColor = ConsoleColor.Green;
            else if (c == '-') charColor = ConsoleColor.Red;
            else if (c == 'M') charColor = ConsoleColor.Cyan; // MISS

            // 6. 그리기 (정렬 적용)
            int drawX = currentX;
            if (padding < 0)
            {
                drawX = currentX - artWidth; // 오른쪽 정렬
            }
            
            for (int i = 0; i < art.Length; i++)
            {
                // (화면 밖으로 나가는지 체크)
                if (startY + i < 0 || startY + i >= screenHeight) continue;
                // (배경을 투명하게 그리기: true)
                DrawTextToBuffer(drawX, startY + i, art[i], charColor, ConsoleColor.Black, true);
            }

            // 7. 다음 X좌표 계산
            if (padding > 0)
            {
                currentX += (artWidth + padding); // 왼쪽 -> 오른쪽
            }
            else
            {
                currentX -= (artWidth + Math.Abs(padding)); // 오른쪽 -> 왼쪽
            }
        }
    }

    // [신규] 아스키 아트 배열을 그리는 헬퍼 (DrawAsciiNumber와 달리 문자열을 순회하지 않음)
    private void DrawAsciiArtToBuffer(int startX, int startY, string[] art, ConsoleColor color, int padding = 1, bool ignoreSpaceBg = true)
    {
        if (art == null || art.Length == 0) return;

        // 1. 아트 너비 계산 (정렬용)
        int artWidth = 0;
        foreach(string line in art) { artWidth = Math.Max(artWidth, GetDisplayWidth(line)); }
        if (artWidth == 0) return;

        // 2. 정렬 적용
        int drawX = startX;
        if (padding < 0)
        {
            drawX = startX - artWidth; // 오른쪽 정렬
        }

        // 3. 그리기
        for (int i = 0; i < art.Length; i++)
        {
            if (startY + i < 0 || startY + i >= screenHeight) continue;
            DrawTextToBuffer(drawX, startY + i, art[i], color, ConsoleColor.Black, ignoreSpaceBg);
        }
    }

    private void ProcessPlayerBuffs()
    {
        // 1. IronWill (방어력) 버프 처리
        if (player.StatusEffects.ContainsKey(StatType.DEF))
        {
            if (player.StatusEffects[StatType.DEF] > 0)
            {
                player.StatusEffects[StatType.DEF]--; // 턴 차감
                
                if (player.StatusEffects[StatType.DEF] == 0)
                {
                    player.TempDefBuff = 0; // 버프 값 초기화
                    player.StatusEffects.Remove(StatType.DEF); // 딕셔너리에서 제거
                    AddLog("강철의 의지 효과가 만료되었습니다.");
                }
            }
        }
        
        // (추후 다른 플레이어 버프가 생기면 여기에 추가)
    }

    private void ProcessGameEndInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.UpArrow)
        {
            gameEndMenuIndex = (gameEndMenuIndex - 1 + 3) % 3; // (0 -> 2 -> 1 -> 0)
        }
        else if (key.Key == ConsoleKey.DownArrow)
        {
            gameEndMenuIndex = (gameEndMenuIndex + 1) % 3; // (0 -> 1 -> 2 -> 0)
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            ProcessGameEndAction(gameEndMenuIndex);
        }
    }

    // [신규] 2번 요청: 엔딩 화면 액션 처리
    private void ProcessGameEndAction(int index)
    {
        bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;

        if (isMulti)
        {
            // --- [멀티플레이 엔딩 메뉴] ---
            switch (index)
            {
                case 0: // 1. 로비(대기실)로 돌아가기
                    // (게임오버 때의 복귀 로직과 동일)
                    
                    // 1. 플레이어 상태 리셋
                    if (player != null) 
                    {
                        player.HP = player.MaxHP;
                        player.MP = player.MaxMP;
                    }

                    // 2. 게임 상태 초기화 (채팅, 직업 등)
                    chatLog.Clear(); 
                    iSelectedClass = false;
                    otherSelectedClass = false;
                    isOtherPlayerReady = false;
                    mpIsConfirming = false;
                    
                    if (otherPlayer != null) otherPlayer.HP = otherPlayer.MaxHP;

                    // 3. 몬스터/전투 정보 초기화
                    currentBattleMonster = null;
                    monsters.Clear();
                    
                    // 4. 대기실로 이동
                    currentState = GameState.Multiplayer_RoomWait;
                    
                    // 5. 내 정보 갱신 전송
                    SendMyPlayerInfo();
                    
                    // (호스트 인원수 갱신 등)
                    if (NetworkManager.Instance.IsHost) NetworkManager.Instance.UpdateRoomPlayerCount(1);

                    AddChatLog("▷ 게임 클리어! 대기실로 돌아왔습니다.");
                    break;

                case 1: // 2. 메인 화면으로
                    NetworkManager.Instance.Close();
                    ResetMultiplayerData(); 
                    player = null; 
                    needsRestart = true; 
                    gameRunning = false; 
                    break;

                case 2: // 3. 게임 종료
                    gameRunning = false;
                    break;
            }
        }
        else
        {
            // --- [싱글플레이 엔딩 메뉴] (기존 유지) ---
            switch (index)
            {
                case 0: // 1. 메인 화면으로
                    player = null; 
                    needsRestart = true; 
                    gameRunning = false; 
                    break;
                case 1: // 2. 재시작 (직업 선택)
                    PlayerClass? selectedClass = ChooseClass();
                    if (selectedClass.HasValue)
                    {
                        InitializeGameData(selectedClass.Value);
                        currentState = GameState.World;    
                    }
                    else
                    {
                        needsRestart = true;
                        gameRunning = false;
                    }
                    break;
                case 2: // 3. 게임 종료
                    gameRunning = false;
                    break;
            }
        }
    }

    // [신규] 2번 요청: 엔딩 화면 그리기
    private void DrawGameEndWindow()
    {
        // 1. 타이틀 아트 (기존 유지)
        string[] titleArt = AsciiArt.GetGameClearArt(); 
        int titleHeight = titleArt.Length;
        int titleWidth = 0;
        foreach(string line in titleArt) { titleWidth = Math.Max(titleWidth, GetDisplayWidth(line)); }
        
        int titleX = screenWidth / 2 - titleWidth / 2;
        int titleY = screenHeight / 4; 
        
        ConsoleColor titleColor = isTitleBright ? ConsoleColor.Yellow : ConsoleColor.White;
        
        for(int i=0; i<titleHeight; i++)
        {
            DrawTextToBuffer(titleX, titleY + i, titleArt[i], titleColor, ConsoleColor.Black, true);
        }

        int timeY = titleY + titleHeight + 2;
        string timeText = $"CLEAR TIME : {gameClearTime.Hours:D2}:{gameClearTime.Minutes:D2}:{gameClearTime.Seconds:D2}";
        int timeX = screenWidth / 2 - GetDisplayWidth(timeText) / 2;
        DrawTextToBuffer(timeX, timeY, timeText, ConsoleColor.Cyan);

        // 2. 메뉴 옵션 그리기 (수정됨)
        bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;
        string[] menuItems;

        if (isMulti)
        {
            // [멀티플레이 메뉴]
            menuItems = new string[] { "로비(대기실)로 돌아가기", "메인 화면으로", "게임 종료" };
        }
        else
        {
            // [싱글플레이 메뉴]
            menuItems = new string[] { "메인 화면으로", "재시작 (직업 선택)", "게임 종료" };
        }

        int menuY = titleY + titleHeight + 4; 

        for (int i = 0; i < menuItems.Length; i++)
        {
            string item = menuItems[i];
            ConsoleColor color = ConsoleColor.DarkGray;

            if (i == gameEndMenuIndex)
            {
                item = $"► {item} ◄";
                color = ConsoleColor.Yellow;
            }
            
            int itemX = screenWidth / 2 - GetDisplayWidth(item) / 2;
            DrawTextToBuffer(itemX, menuY + (i * 2), item, color); 
        }
    }

    // [신규] 인트로 시작 초기화
    private void StartIntro()
    {
        // [중요] 멀티플레이 중이라면 상태를 'Intro'로 바꾸되, 네트워크 연결은 끊지 않음
        currentState = GameState.Intro;
        
        introPageIndex = 0;
        introCharIndex = 0;
        currentIntroBuffer = "";
        
        // 페이지 텍스트 로드
        currentIntroFullText = string.Join("\n", introStoryPages[introPageIndex]);
    }

    // [신규] 인트로 타이핑 애니메이션
    private void ProcessIntroAnimation()
    {
        // 타이핑이 완료되었으면 아무것도 안 함
        if (introCharIndex >= currentIntroFullText.Length) return;

        if ((DateTime.Now - lastIntroTypeTime).TotalMilliseconds > INTRO_TYPE_SPEED_MS)
        {
            currentIntroBuffer += currentIntroFullText[introCharIndex];
            introCharIndex++;
            lastIntroTypeTime = DateTime.Now;
        }
    }

    // [신규] 인트로 입력 처리
    private void ProcessIntroInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            // 1. 타이핑이 아직 안 끝났으면 -> 스킵
            if (introCharIndex < currentIntroFullText.Length)
            {
                currentIntroBuffer = currentIntroFullText;
                introCharIndex = currentIntroFullText.Length;
            }
            // 2. 타이핑이 끝났으면 -> 다음 페이지
            else
            {
                introPageIndex++;
                if (introPageIndex < introStoryPages.Length)
                {
                    // 다음 페이지 로드
                    introCharIndex = 0;
                    currentIntroBuffer = "";
                    currentIntroFullText = string.Join("\n", introStoryPages[introPageIndex]);
                }
                else
                {
                    // 인트로 종료 -> 직업 선택 화면으로 이동
                    if (introPageIndex >= introStoryPages.Length)
                    {
                        // [핵심 수정] 
                        // "연결된 클라이언트(게스트)" 이거나 "호스트(방장)" 라면 멀티플레이로 간주
                        if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
                        {
                            // 멀티플레이 직업 선택 화면으로 전환
                            currentState = GameState.Multiplayer_ClassSelect;
                            iSelectedClass = false;
                            otherSelectedClass = false;
                        }
                        else 
                        {
                            // 싱글플레이 (기존 로직)
                            PlayerClass? selectedClass = ChooseClass();

                            if (selectedClass.HasValue)
                            {
                                InitializeGameData(selectedClass.Value);
                            }
                            else
                            {
                                currentState = GameState.MainMenu;
                            }
                        }
                    }
                }
            }
        }
    }
        

    // [신규] 인트로 화면 그리기
   private void DrawIntroWindow()
    {
        int width = (int)(screenWidth * 0.8);
        int height = (int)(screenHeight * 0.8);
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        // 1. 전체 배경 암전
        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);

        // 2. 타이틀 아스키 아트 그리기
        string[] titleArt = AsciiArt.GetIntroTitleArt();
        int titleHeight = titleArt.Length;
        int titleWidth = 0;
        foreach(string line in titleArt) { titleWidth = Math.Max(titleWidth, GetDisplayWidth(line)); }

        int titleX = startX + (width / 2) - (titleWidth / 2);
        
        // [수정] 제목 시작 위치를 맨 위(startY)로 바짝 붙임
        int titleY = startY - 1; 

        for (int i = 0; i < titleHeight; i++)
        {
            DrawTextToBuffer(titleX, titleY + i, titleArt[i], ConsoleColor.Yellow, ConsoleColor.Black, true);
        }

        // 3. 텍스트 상자(Box) 그리기
        // [수정] 제목과 상자 사이 간격을 2칸 -> 1칸으로 줄임
        int boxStartY = titleY + titleHeight + 1; 
        int boxHeight = height - (titleHeight + 1); 
        
        DrawBox(startX, boxStartY, width, boxHeight, ""); 

        // 4. 스토리 텍스트 그리기
        string[] lines = currentIntroBuffer.Split('\n');
        int textStartY = boxStartY + 3; 
        
        for (int i = 0; i < lines.Length; i++)
        {
            if (textStartY + (i * 2) >= boxStartY + boxHeight - 1) break;

            int textX = startX + (width / 2) - (GetDisplayWidth(lines[i]) / 2);
            DrawTextToBuffer(textX, textStartY + (i * 2), lines[i], ConsoleColor.White);
        }

        // 5. 하단 버튼
        bool isFinished = (introCharIndex >= currentIntroFullText.Length);
        bool isLastPage = (introPageIndex == introStoryPages.Length - 1); // [Game.cs] introStoryPages 필드 필요
        
        if (isFinished)
        {
            string btnText = isLastPage ? "[Enter] 시작" : "[Enter] 다음";
            ConsoleColor btnColor = ConsoleColor.Yellow;
            if ((DateTime.Now.Millisecond / 500) % 2 == 0) btnColor = ConsoleColor.DarkGray;

            DrawTextToBuffer(startX + width - GetDisplayWidth(btnText) - 4, boxStartY + boxHeight - 2, btnText, btnColor);
        }
    }

    // [신규] 엔딩 스토리 시작
    private void StartEndingStory()
    {
        currentState = GameState.EndingStory;
        endingPageIndex = 0;
        endingCharIndex = 0;
        currentEndingBuffer = "";
        currentEndingFullText = string.Join("\n", endingStoryPages[endingPageIndex]);
    }

    // [신규] 엔딩 타이핑 애니메이션
    private void ProcessEndingStoryAnimation()
    {
        if (endingCharIndex >= currentEndingFullText.Length) return;

        if ((DateTime.Now - lastEndingTypeTime).TotalMilliseconds > INTRO_TYPE_SPEED_MS)
        {
            currentEndingBuffer += currentEndingFullText[endingCharIndex];
            endingCharIndex++;
            lastEndingTypeTime = DateTime.Now;
        }
    }

    // [신규] 엔딩 입력 처리
    private void ProcessEndingStoryInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            // 1. 타이핑 스킵
            if (endingCharIndex < currentEndingFullText.Length)
            {
                currentEndingBuffer = currentEndingFullText;
                endingCharIndex = currentEndingFullText.Length;
            }
            // 2. 다음 페이지 또는 최종 클리어 화면
            else
            {
                endingPageIndex++;
                if (endingPageIndex < endingStoryPages.Length)
                {
                    endingCharIndex = 0;
                    currentEndingBuffer = "";
                    currentEndingFullText = string.Join("\n", endingStoryPages[endingPageIndex]);
                }
                else
                {
                    // 엔딩 스토리 종료 -> 최종 클리어 화면(GameEnd)으로 이동
                    currentState = GameState.GameEnd;
                    gameEndMenuIndex = 0; 
                }
            }
        }
    }

    // [신규] 엔딩 스토리 그리기
    private void DrawEndingStoryWindow()
    {
        int width = (int)(screenWidth * 0.8);
        int height = (int)(screenHeight * 0.8);
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);

        string[] titleArt = AsciiArt.GetEndingTitleArt();
        int titleHeight = titleArt.Length;
        int titleWidth = 0;
        foreach(string line in titleArt) { titleWidth = Math.Max(titleWidth, GetDisplayWidth(line)); }

        int titleX = startX + (width / 2) - (titleWidth / 2);
        
        // [수정] 제목 시작 위치 조정
        int titleY = startY - 1; 

        for (int i = 0; i < titleHeight; i++)
        {
            DrawTextToBuffer(titleX, titleY + i, titleArt[i], ConsoleColor.Magenta, ConsoleColor.Black, true);
        }

        // [수정] 상자 시작 위치 조정 (간격 축소)
        int boxStartY = titleY + titleHeight + 1;
        int boxHeight = height - (titleHeight + 1);

        DrawBox(startX, boxStartY, width, boxHeight, ""); 

        string[] lines = currentEndingBuffer.Split('\n');
        int textStartY = boxStartY + 3;
        
        for (int i = 0; i < lines.Length; i++)
        {
            if (textStartY + (i * 2) >= boxStartY + boxHeight - 1) break;

            int textX = startX + (width / 2) - (GetDisplayWidth(lines[i]) / 2);
            DrawTextToBuffer(textX, textStartY + (i * 2), lines[i], ConsoleColor.Cyan);
        }

        bool isFinished = (endingCharIndex >= currentEndingFullText.Length);
        
        if (isFinished)
        {
            string btnText = "[Enter] 다음";
            ConsoleColor btnColor = ConsoleColor.Yellow;
            if ((DateTime.Now.Millisecond / 500) % 2 == 0) btnColor = ConsoleColor.DarkGray;

            DrawTextToBuffer(startX + width - GetDisplayWidth(btnText) - 4, boxStartY + boxHeight - 2, btnText, btnColor);
        }
    }
    // [신규] 전투 진입 애니메이션 로직
    private void ProcessBattleIntroAnimation()
    {
        TimeSpan elapsed = DateTime.Now - battleIntroStartTime;

        if (elapsed.TotalMilliseconds >= currentBattleIntroDuration)
        {
            // [핵심 수정] 여기서 멀티플레이인지 확인 안 하면 바로 싱글로 튕깁니다!
            if (NetworkManager.Instance.IsHost || NetworkManager.Instance.IsConnected)
            {
                currentState = GameState.Multiplayer_Battle;
            }
            else
            {
                currentState = GameState.Battle;
            }
        }
    }

  // [신규] 전투 진입 화면 그리기 (위치 수정됨)
    private void DrawBattleIntroWindow()
    {
        // 1. 전체 화면 암전
        DrawFilledBox(0, 0, screenWidth, screenHeight, ConsoleColor.Black);

        TimeSpan elapsed = DateTime.Now - battleIntroStartTime;
        double totalMs = elapsed.TotalMilliseconds;

        // Phase 1: 완전 암전 (0~0.5초)
        if (totalMs < 500) return;

        // 애니메이션 시간 계산
        double animationMs = totalMs - 500;

        // 아트 데이터 준비
        string[] icon = AsciiArt.GetErrorIcon();
        string[] text = currentIntroTextArt;
        
        int iconHeight = icon.Length;
        int iconWidth = 0;
        foreach (var line in icon) iconWidth = Math.Max(iconWidth, GetDisplayWidth(line));

        int textHeight = text.Length;
        int textWidth = 0;
        foreach (var line in text) textWidth = Math.Max(textWidth, GetDisplayWidth(line));

        int contentTotalHeight = iconHeight + 2 + textHeight; 
        int boxWidth = Math.Max(iconWidth, textWidth) + 6;
        int boxHeight = contentTotalHeight + 4;

        // 깜빡임 효과
        bool isVisible = ((int)(totalMs / currentIntroBlinkInterval)) % 2 == 0;
        ConsoleColor contentColor = isVisible ? currentIntroColor : ConsoleColor.DarkGray;

        // 리스트에 있는 모든 창을 순회하며 그리기
        foreach (var win in introWindows)
        {
            // 등장 시간이 안 된 창은 건너뜀
            if (animationMs < win.delay) continue;

            double localMs = animationMs - win.delay;

            // [!!!] --- 핵심 수정 --- [!!!]
            // 창 영역을 검은색으로 먼저 채워서, 뒤에 있는 창을 가립니다(Occlusion).
            DrawFilledBox(win.x, win.y, boxWidth, boxHeight, ConsoleColor.Black);
            // [!!!] --- 수정 끝 --- [!!!]

            // 1. 창 테두리 그리기 (이제 검은 배경 위에 그려짐)
            DrawBox(win.x, win.y, boxWidth, boxHeight, "SYSTEM ALERT"); 

            int currentDrawY = win.y + 2; 
            int centerX = win.x + (boxWidth / 2);

            // 2. 아이콘 그리기 (등장 0.1초 후)
            if (localMs > 100)
            {
                int iconDrawX = centerX - (iconWidth / 2);
                for (int i = 0; i < icon.Length; i++)
                {
                    DrawTextToBuffer(iconDrawX, currentDrawY + i, icon[i], contentColor, ConsoleColor.Black, true);
                }
            }

            currentDrawY += iconHeight + 2;

            // 3. 텍스트 그리기 (등장 0.3초 후)
            if (localMs > 300)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    int textDrawX = centerX - (GetDisplayWidth(text[i]) / 2);
                    DrawTextToBuffer(textDrawX, currentDrawY + i, text[i], contentColor, ConsoleColor.Black, true);
                }
            }
        }
    }
    // [신규] 게임 오버 입력 처리
    private void ProcessGameOverInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.UpArrow)
        {
            gameOverMenuIndex = (gameOverMenuIndex - 1 + 3) % 3;
        }
        else if (key.Key == ConsoleKey.DownArrow)
        {
            gameOverMenuIndex = (gameOverMenuIndex + 1) % 3;
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            ProcessGameOverAction(gameOverMenuIndex);
        }
    }

    // [신규] 게임 오버 메뉴 동작
    private void ProcessGameOverAction(int index)
    {
        bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;

        switch (index)
        {
           case 0: // 1. 로비(대기실)로 돌아가기
                if (isMulti)
                {
                    // 1. 플레이어 부활
                    if (player != null) 
                    {
                        player.HP = player.MaxHP; // HP가 찼으므로 IsDead는 자동으로 false가 됨
                        player.MP = player.MaxMP;
                        
                        // [삭제] 이 줄을 지워주세요. (읽기 전용 프로퍼티임)
                        // player.IsDead = false; 
                    }

                    // 2. 게임 상태 초기화
                    chatLog.Clear(); 
                    iSelectedClass = false;
                    otherSelectedClass = false;
                    isOtherPlayerReady = false;
                    mpIsConfirming = false;
                    
                    if (otherPlayer != null)
                    {
                        otherPlayer.HP = otherPlayer.MaxHP; 
                    }

                    // 3. 몬스터/전투 정보 초기화
                    currentBattleMonster = null;
                    monsters.Clear();
                    
                    // 4. 대기실로 이동
                    currentState = GameState.Multiplayer_RoomWait;
                    
                    // 5. 내 정보 갱신 전송
                    SendMyPlayerInfo();

                    AddChatLog("▷ 게임 오버되어 대기실로 돌아왔습니다.");
                }
                else
                {
                    // (싱글 재시작 로직)
                    PlayerClass? selectedClass = ChooseClass();
                    if (selectedClass.HasValue) { InitializeGameData(selectedClass.Value); currentState = GameState.World; }
                    else { needsRestart = true; gameRunning = false; }
                }
                break;
            
            case 1: // 2. 메인 화면으로
                if (isMulti)
                {
                    NetworkManager.Instance.Close();
                    ResetMultiplayerData(); 
                }
                player = null; 
                needsRestart = true; 
                gameRunning = false;
                break;
                
            case 2: // 3. 게임 종료
                gameRunning = false;
                break;
        }
    }

    // [신규] 게임 오버 화면 그리기
    private void DrawGameOverWindow()
    {
        // 1. 배경 암전 (검은색 채우기)
        DrawFilledBox(0, 0, screenWidth, screenHeight, ConsoleColor.Black);

        // 2. 아스키 아트 가져오기
        string[] art = AsciiArt.GetGameOverArt(); // GAME_OVER.txt
        int artHeight = art.Length;
        
        // 3. 화면 중앙 위치 계산
        int centerX = screenWidth / 2;
        int centerY = screenHeight / 2;
        
        // 아트를 화면 상단부(1/3 지점)에 배치
        int artStartY = (screenHeight / 3) - (artHeight / 2);

        // 4. 빨간색 깜빡임 효과
        bool isBright = (DateTime.Now.Millisecond / 500) % 2 == 0;
        ConsoleColor artColor = isBright ? ConsoleColor.Red : ConsoleColor.DarkRed;

        // 아트 그리기
        for (int i = 0; i < art.Length; i++)
        {
            int lineX = centerX - (GetDisplayWidth(art[i]) / 2);
            DrawTextToBuffer(lineX, artStartY + i, art[i], artColor, ConsoleColor.Black, true);
        }

        // 5. 메뉴 그리기
        bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;
        
        // [수정] 멀티플레이면 '로비로', 싱글이면 '재시작'
        string restartBtn = isMulti ? "로비(대기실)로 돌아가기" : "재시작 (직업 선택)";
        
        string[] menuItems = { restartBtn, "메인 화면으로", "게임 종료" };
        int menuStartY = artStartY + artHeight + 4; // 아트 아래 4칸 띄움

        for (int i = 0; i < menuItems.Length; i++)
        {
            string text = menuItems[i];
            ConsoleColor color = ConsoleColor.DarkGray;

            if (i == gameOverMenuIndex)
            {
                // 선택된 항목 하이라이트
                text = $"► {text} ◄";
                color = ConsoleColor.Yellow;
            }

            int textX = centerX - (GetDisplayWidth(text) / 2);
            DrawTextToBuffer(textX, menuStartY + (i * 2), text, color);
        }
    }
    // [신규] 스테이지 인트로 로직
    private void ProcessStageIntroAnimation()
    {
        TimeSpan elapsed = DateTime.Now - stageIntroStartTime;

        if (elapsed.TotalMilliseconds >= STAGE_INTRO_DURATION_MS)
        {
            // [핵심 수정] 멀티/싱글 여부에 따라 도착지 분기
            if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
            {
                currentState = GameState.Multiplayer_World;
            }
            else
            {
                currentState = GameState.World;
            }
        }
    }

    // [신규] 스테이지 인트로 그리기
    private void DrawStageIntroWindow()
    {
        // 1. 배경 암전
        DrawFilledBox(0, 0, screenWidth, screenHeight, ConsoleColor.Black);

        // [!!!] --- 1번 요청 수정 (색상 변경) --- [!!!]
        // 스테이지가 높을수록 붉고 어둡게 (White -> Red -> DarkRed)
        ConsoleColor color;
        if (currentStage == 1) color = ConsoleColor.White;
        else if (currentStage == 2) color = ConsoleColor.Red;      // [수정] Gray -> Red
        else color = ConsoleColor.DarkRed;                         // [수정] DarkGray -> DarkRed
        // [!!!] --- 수정 끝 --- [!!!]

        int centerX = screenWidth / 2;
        int centerY = screenHeight / 2;

        // 아트 데이터 가져오기
        string[] stageNumArt = AsciiArt.GetStageNumberArt(currentStage);
        string[] stageNameArt = AsciiArt.GetStageNameArt(currentStage);

        // 3. 스테이지 이름 아트 ("ASCII LABYRINTH") - 화면 정중앙 배치
        int nameY = centerY - (stageNameArt.Length / 2); 

        for (int i = 0; i < stageNameArt.Length; i++)
        {
            int lineX = centerX - (GetDisplayWidth(stageNameArt[i]) / 2);
            DrawTextToBuffer(lineX, nameY + i, stageNameArt[i], color, ConsoleColor.Black, true);
        }

        // 4. 스테이지 번호 아트 ("STAGE 1") - 이름 위에 배치
        int numY = nameY - stageNumArt.Length - 2; 

        for (int i = 0; i < stageNumArt.Length; i++)
        {
            int lineX = centerX - (GetDisplayWidth(stageNumArt[i]) / 2);
            DrawTextToBuffer(lineX, numY + i, stageNumArt[i], color, ConsoleColor.Black, true);
        }
    }
    private void DrawExitConfirmation(bool confirmExit)
    {
        int width = 60;
        int height = 10;
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
        DrawBox(startX, startY, width, height, "메인 화면으로");

        string msg = "메인 화면으로 돌아가시겠습니까?";
        DrawTextToBuffer(startX + (width / 2) - (GetDisplayWidth(msg) / 2), startY + 4, msg, ConsoleColor.White);

        string yes = "  예(Y)  ";
        string no = "  아니요(N)  ";

        ConsoleColor yesFg = confirmExit ? ConsoleColor.Black : ConsoleColor.White;
        ConsoleColor yesBg = confirmExit ? ConsoleColor.Green : ConsoleColor.Black;
        ConsoleColor noFg = !confirmExit ? ConsoleColor.Black : ConsoleColor.White;
        ConsoleColor noBg = !confirmExit ? ConsoleColor.Red : ConsoleColor.Black;

        int totalWidth = GetDisplayWidth(yes) + GetDisplayWidth(no) + 4; 
        int buttonX_Yes = startX + (width / 2) - (totalWidth / 2);
        int buttonX_No = buttonX_Yes + GetDisplayWidth(yes) + 4;
        int buttonY = startY + 6;

        DrawTextToBuffer(buttonX_Yes, buttonY, yes, yesFg, yesBg);
        DrawTextToBuffer(buttonX_No, buttonY, no, noFg, noBg);
    }

    private void DrawNicknameInput()
    {
        int width = 40, height = 10;
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black); // 배경 지우기
        DrawBox(startX, startY, width, height, "닉네임 설정");
        
        string msg = "사용할 닉네임을 입력하세요:";
        DrawTextToBuffer(startX + 2, startY + 2, msg, ConsoleColor.White);

        // 입력된 닉네임 표시
        DrawTextToBuffer(startX + 4, startY + 4, playerNickname + "_", ConsoleColor.Yellow);
        
        // [신규] 에러 메시지 표시 (입력칸 바로 아래 Y+5 위치)
        if (!string.IsNullOrEmpty(nicknameError))
        {
            DrawTextToBuffer(startX + 4, startY + 5, nicknameError, ConsoleColor.Red);
        }
        
        string guide = "[Enter] 결정  [ESC] 뒤로";
        DrawTextToBuffer(startX + width/2 - GetDisplayWidth(guide)/2, startY + height - 2, guide, ConsoleColor.Gray);
    }

    private void ProcessNicknameInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape) 
        { 
            currentState = GameState.Multiplayer_Nick_ExitConfirm; 
            popupIndex = 1; // 기본값 '아니요'
            return; 
        }

        if (key.Key == ConsoleKey.Enter) 
        {
            // [수정] 빈 값 체크
            if (string.IsNullOrWhiteSpace(playerNickname))
            {
                nicknameError = "닉네임을 입력하세요."; // 에러 설정
                return; // 진행 막기
            }

            // (성공 시)
            NetworkManager.Instance.StartSearchingRooms();
            currentState = GameState.Multiplayer_Lobby;
            return;
        }

        // 백스페이스
        if (key.Key == ConsoleKey.Backspace)
        {
            if (playerNickname.Length > 0) 
            {
                playerNickname = playerNickname.Substring(0, playerNickname.Length - 1);
                nicknameError = ""; // [신규] 수정 시 에러 지움
            }
        }
        // 문자 입력
        else if (!char.IsControl(key.KeyChar) && playerNickname.Length < 10)
        {
            playerNickname += key.KeyChar;
            nicknameError = ""; // [신규] 입력 시 에러 지움
        }
    }

    private void DrawNickExitConfirm()
    {
        DrawConfirmPopup("메인 화면으로 돌아가시겠습니까?");
    }

    // [신규] 닉네임 나가기 입력 처리
    private void ProcessNickExitConfirmInput(ConsoleKeyInfo key)
    {
        HandleConfirmPopupInput(key,
            onYes: () => {
                currentState = GameState.MainMenu;
            },
            onNo: () => {
                currentState = GameState.Multiplayer_Nick;
            }
        );
    }
    // [신규] 멀티플레이 로비 (방 목록)
    private void DrawMultiplayerLobby()
    {
        lock (NetworkManager.Instance.RoomListLock)
        {
            roomList = new List<RoomInfo>(NetworkManager.Instance.DiscoveredRooms);
        }

        int width = (int)(screenWidth * 0.8);
        int height = (int)(screenHeight * 0.8);
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawBox(startX, startY, width, height, $"멀티플레이 로비 (닉네임: {playerNickname})");

        // 1. 방 목록 영역
        int listStartY = startY + 2;
        int listHeight = height - 6;
        DrawBox(startX + 1, listStartY, width - 2, listHeight, ""); // 내부 박스

        if (roomList.Count == 0)
        {
            DrawTextToBuffer(startX + width/2 - 5, listStartY + 2, "방이 없습니다.", ConsoleColor.DarkGray);
        }
        else
        {
            for (int i = 0; i < roomList.Count; i++)
            {
                bool isSelected = (lobbySelectIndex == 0 && roomListIndex == i);
                RoomInfo info = roomList[i];
                
                // [신규] 방이 꽉 찼는지 확인
                bool isFull = info.CurrentPlayers >= info.MaxPlayers;
                
                // 색상 결정
                ConsoleColor color;
                if (isSelected) color = ConsoleColor.Yellow;
                else if (isFull) color = ConsoleColor.DarkGray; // 꽉 차면 회색
                else color = ConsoleColor.White;

                string cursor = isSelected ? "►" : " ";
                string lockIcon = info.IsPrivate ? "🔒" : "  ";
                
                // [신규] 제목 표시 (꽉 찼으면 [FULL] 표시)
                string titleStr = info.Title;
                // if (isFull) titleStr = "[FULL] " + titleStr; // (선택사항)

                string line = $"{cursor} [{i+1}] {titleStr} ({info.HostName}) {info.CurrentPlayers}/{info.MaxPlayers} {lockIcon}";
                DrawTextToBuffer(startX + 3, listStartY + 1 + i, line, color);
            }
        }

        // 2. 하단 버튼 영역
        int btnY = startY + height - 2;
        string[] btns = { "[F1] 참가하기", "[F2] IP 접속", "[F3] 방 만들기", "[F5] 새로고침" };
        int btnStartX = startX + 4;
        for (int i=0; i<btns.Length; i++)
        {
            bool isSelected = (lobbySelectIndex == 1 && lobbyBtnIndex == i);
            ConsoleColor color = isSelected ? ConsoleColor.Yellow : ConsoleColor.Gray;
            DrawTextToBuffer(btnStartX, btnY, btns[i], color);
            btnStartX += GetDisplayWidth(btns[i]) + 4;
        }

        if (roomList.Count > 0 && roomListIndex >= roomList.Count) 
        roomListIndex = roomList.Count - 1;
    }

    private void ProcessLobbyInput(ConsoleKeyInfo key)
{
    // [수정] 위쪽 화살표 (버튼 -> 방 목록, 혹은 이전 방)
    if (key.Key == ConsoleKey.UpArrow)
    {
        if (lobbySelectIndex == 1) // 버튼 영역에 있었다면
        {
            lobbySelectIndex = 0; // 방 목록 영역으로 이동
            
            // 자연스러운 조작을 위해 방 목록의 '맨 마지막 방'을 선택
            if (roomList.Count > 0) 
            {
                roomListIndex = roomList.Count - 1; 
            }
        }
        else // 방 목록 영역에 있었다면
        {
            if (roomList.Count > 0)
            {
                // 맨 위가 아니면 위로 이동, 맨 위면 맨 아래로 순환 (취향에 따라 순환 제거 가능)
                roomListIndex = (roomListIndex - 1 + roomList.Count) % roomList.Count;
            }
        }
    }
    // [수정] 아래쪽 화살표 (방 목록 -> 다음 방, 혹은 버튼)
    else if (key.Key == ConsoleKey.DownArrow)
    {
        if (lobbySelectIndex == 0) // 방 목록 영역에 있다면
        {
            // 방이 하나도 없으면 바로 버튼으로
            if (roomList.Count == 0)
            {
                lobbySelectIndex = 1;
            }
            // 현재 선택된 방이 마지막 방이 아니라면 -> 다음 방으로 이동
            else if (roomListIndex < roomList.Count - 1)
            {
                roomListIndex++;
            }
            // 현재 선택된 방이 마지막 방이라면 -> 버튼 영역으로 이동
            else
            {
                lobbySelectIndex = 1;
                lobbyBtnIndex = 0; // 버튼의 첫 번째 항목(참가하기) 선택
            }
        }
        // 버튼 영역에서는 아래키를 눌러도 아무 동작 안 함 (혹은 순환)
    }
    // 좌우 이동 (버튼 선택 - 버튼 영역일 때만 동작)
    else if (key.Key == ConsoleKey.LeftArrow && lobbySelectIndex == 1)
        {
            lobbyBtnIndex = (lobbyBtnIndex - 1 + 4) % 4; // [수정] 버튼 4개로 증가
        }
        else if (key.Key == ConsoleKey.RightArrow && lobbySelectIndex == 1)
        {
            lobbyBtnIndex = (lobbyBtnIndex + 1) % 4; // [수정] 버튼 4개로 증가
        }
    // ESC (나가기 팝업)
    else if (key.Key == ConsoleKey.Escape)
    {
        currentState = GameState.Multiplayer_Lobby_ExitConfirm;
        popupIndex = 1; 
    }
    // 실행 (Enter, F키)
    else if (key.Key == ConsoleKey.Enter)
        {
            if (lobbySelectIndex == 0) 
            {
                JoinRoom(roomListIndex);
            }
            else 
            {
                // 하단 버튼 선택 시 동작 (인덱스 순서 주의)
                if (lobbyBtnIndex == 0) JoinRoom(roomListIndex); // 참가
                else if (lobbyBtnIndex == 1) OpenDirectIpWindow(); // IP 접속
                else if (lobbyBtnIndex == 2) 
                {
                    ResetCreateRoomData(); 
                    currentState = GameState.Multiplayer_CreateRoom; // 방 만들기
                }
                else if (lobbyBtnIndex == 3) RefreshRoomList(); // 새로고침
            }
        }
        // 단축키
        else if (key.Key == ConsoleKey.F1) JoinRoom(roomListIndex);
        
        // [수정] F2 -> IP 접속
        else if (key.Key == ConsoleKey.F2) OpenDirectIpWindow(); 
        
        // [수정] F3 -> 방 만들기
        else if (key.Key == ConsoleKey.F3) 
        {
            ResetCreateRoomData();
            currentState = GameState.Multiplayer_CreateRoom;
        }    
        else if (key.Key == ConsoleKey.F5) RefreshRoomList();
    }

    private void OpenDirectIpWindow()
    {
        directIpInput = "";
        directIpError = "";
        isEnteringIp = true;
        currentState = GameState.Multiplayer_DirectIpConnect;
    }

    private void ResetCreateRoomData()
    {
        roomTitleInput = "";      // 제목 초기화
        roomPwInput = "";         // 비번 초기화
        createRoomError = "";     // 에러 메시지 초기화
        isEnteringTitle = true;   // 포커스를 제목으로
        createRoomBtnIndex = 0;   // 버튼 포커스 초기화
        isFocusOnButtons = false; // 텍스트 입력 모드로 시작
    }

    // [신규] 로비 나가기 확인 창 그리기
    private void DrawLobbyExitConfirm()
    {
        DrawConfirmPopup("메인 화면으로 돌아가시겠습니까?");
    }

    // [신규] 로비 나가기 입력 처리
    private void ProcessLobbyExitConfirmInput(ConsoleKeyInfo key)
    {
        HandleConfirmPopupInput(key, 
            onYes: () => {
                NetworkManager.Instance.StopSearchingRooms(); // 검색 중지
                currentState = GameState.MainMenu; 
            },
            onNo: () => {
                currentState = GameState.Multiplayer_Lobby;
            }
        );
    }

    // [신규] 방 만들기 화면
   private void DrawCreateRoom()
{
    int width = 50, height = 15;
    int startX = screenWidth / 2 - width / 2;
    int startY = screenHeight / 2 - height / 2;

    DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
    DrawBox(startX, startY, width, height, "방 만들기");

    string titleFocus = (!isFocusOnButtons && isEnteringTitle) ? "►" : " ";
    string pwFocus = (!isFocusOnButtons && !isEnteringTitle) ? "►" : " ";

    // 방 제목 (Y+3)
    DrawTextToBuffer(startX + 2, startY + 3, $"{titleFocus} 방 제목: {roomTitleInput}", isFocusOnButtons ? ConsoleColor.Gray : ConsoleColor.White);
    
    // [신규] 에러 메시지 (Y+4) - 제목 바로 아래
    if (!string.IsNullOrEmpty(createRoomError))
    {
        DrawTextToBuffer(startX + 4, startY + 4, createRoomError, ConsoleColor.Red);
    }

    DrawTextToBuffer(startX + 2, startY + 5, $"{pwFocus} 비밀번호: {roomPwInput}", isFocusOnButtons ? ConsoleColor.Gray : ConsoleColor.White);

    // 안내
    DrawTextToBuffer(startX + 2, startY + 8, "[Tab/↕] 항목 이동", ConsoleColor.DarkGray);

    // 버튼 영역
    int btnY = startY + 11;
    string create = " [생성] ";
    string cancel = " [취소] ";
    
    ConsoleColor createBg = (isFocusOnButtons && createRoomBtnIndex == 0) ? ConsoleColor.Green : ConsoleColor.Black;
    ConsoleColor createFg = (isFocusOnButtons && createRoomBtnIndex == 0) ? ConsoleColor.Black : ConsoleColor.White;
    
    ConsoleColor cancelBg = (isFocusOnButtons && createRoomBtnIndex == 1) ? ConsoleColor.Red : ConsoleColor.Black;
    ConsoleColor cancelFg = (isFocusOnButtons && createRoomBtnIndex == 1) ? ConsoleColor.Black : ConsoleColor.White;

    int totalWidth = GetDisplayWidth(create) + GetDisplayWidth(cancel) + 4;
    int btnX = startX + width / 2 - totalWidth / 2;

    DrawTextToBuffer(btnX, btnY, create, createFg, createBg);
    DrawTextToBuffer(btnX + GetDisplayWidth(create) + 4, btnY, cancel, cancelFg, cancelBg);
}

    private void ProcessCreateRoomInput(ConsoleKeyInfo key)
{
    // 1. ESC: 무조건 취소
    if (key.Key == ConsoleKey.Escape) 
    { 
        currentState = GameState.Multiplayer_Lobby;
        return; 
    }

    // 2. 포커스 이동 (Tab, 위, 아래)
    if (key.Key == ConsoleKey.Tab || key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow)
    {
        // 제목 -> 비번 -> 버튼 -> 제목 순환
        if (!isFocusOnButtons)
        {
            if (isEnteringTitle) isEnteringTitle = false; // 제목 -> 비번
            else isFocusOnButtons = true; // 비번 -> 버튼
        }
        else
        {
            isFocusOnButtons = false;
            isEnteringTitle = true; // 버튼 -> 제목
        }
        return;
    }

    // 3. 버튼 영역일 때 (좌우 이동 및 엔터)
    if (isFocusOnButtons)
    {
        if (key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow)
        {
            createRoomBtnIndex = (createRoomBtnIndex + 1) % 2; // 0 <-> 1 토글
        }
        if (key.Key == ConsoleKey.Enter)
        {
            if (createRoomBtnIndex == 0) // [생성] 버튼
            {
                // [수정] 빈 값 체크
                if (string.IsNullOrWhiteSpace(roomTitleInput))
                {
                    createRoomError = "방 제목을 입력하세요.";
                    return;
                }
                CreateRoomAction();
            }
            else // [취소] 버튼
            {
                currentState = GameState.Multiplayer_Lobby;
            }
        }
        return; // 버튼 처리 후 리턴
    }

    // 4. 텍스트 입력 영역일 때
    ref string target = ref roomTitleInput;
    if (!isEnteringTitle) target = ref roomPwInput;

    if (key.Key == ConsoleKey.Enter)
    {
        // [수정] 엔터 키로 제목 입력 완료 시에도 검증
        if (isEnteringTitle) 
        {
            if (string.IsNullOrWhiteSpace(roomTitleInput))
            {
                createRoomError = "방 제목을 입력하세요.";
                // 포커스 이동 안 함
            }
            else
            {
                isEnteringTitle = false; // 다음(비번)으로 이동
            }
        }
        else 
        {
            isFocusOnButtons = true;
        }
    }
    else if (key.Key == ConsoleKey.Backspace)
    {
        if (target.Length > 0) 
        {
            target = target.Substring(0, target.Length - 1);
            createRoomError = ""; // [신규] 수정 시 에러 지움
        }
    }
    else if (!char.IsControl(key.KeyChar) && target.Length < 15)
    {
        target += key.KeyChar;
        createRoomError = ""; // [신규] 입력 시 에러 지움
    }
}

// 방 생성 로직 분리 (재사용)
    private void CreateRoomAction()
    {
        string title = roomTitleInput.Length > 0 ? roomTitleInput : $"{playerNickname}의 방";

        RoomInfo myRoom = new RoomInfo
        {
            Title = title,
            HostName = playerNickname,
            IsPrivate = roomPwInput.Length > 0,
            Password = roomPwInput,
            CurrentPlayers = 1,
            // Port는 StartHost 내부에서 자동 할당됩니다.
        };

        myHostingRoom = myRoom;

        currentRoomTitle = title;
        currentRoomPassword = roomPwInput; 

        NetworkManager.Instance.StopSearchingRooms();
        
        // 호스트 시작 (포트 0 -> 시스템 자동 할당)
        NetworkManager.Instance.StartHost(0, myRoom);
        
        // 데이터 초기화
        ResetMultiplayerData();
        
        currentState = GameState.Multiplayer_RoomWait;

        // [신규] 생성된 방의 접속 정보 출력
        // StartHost 실행 후 myRoom.Port에 할당된 포트 번호가 저장됩니다.
        string ip = GetLocalIPAddress();
        int port = myRoom.Port;

        AddChatLog($"▷ 방이 생성되었습니다.");
        // '▷'를 붙여서 노란색(시스템 메시지)으로 잘 보이게 출력
        AddChatLog($"▷ 접속 주소: {ip}:{port}");
    }

    // [신규] 대기실 화면
    // Game.cs - DrawRoomWait 수정

private void DrawRoomWait()
{
    // 1. 창 크기 확장 (너비 110, 높이 28)
    int width = 110; 
    int height = 28;
    int startX = screenWidth / 2 - width / 2;
    int startY = screenHeight / 2 - height / 2;

    // 전체 박스 그리기
    DrawBox(startX, startY, width, height, "대기실");

    // --- [화면 분할] ---
    int dividerX = startX + 35; // 좌측 패널 너비 35
    
    // 수직 구분선 그리기
    for (int i = 1; i < height - 1; i++)
    {
        DrawToBuffer(dividerX, startY + i, '│', ConsoleColor.DarkGray);
    }
    
    // 상단/하단 연결부위 처리 ('┬', '┴')
    DrawToBuffer(dividerX, startY, '┬', ConsoleColor.DarkGray);
    DrawToBuffer(dividerX, startY + height - 1, '┴', ConsoleColor.DarkGray);


    // ============================================================
    // [좌측 패널] 플레이어 목록 및 메뉴 버튼
    // ============================================================
    
    int leftContentX = startX + 2;
    int leftContentWidth = 32; // (35 - 2 - 1)

    DrawTextToBuffer(leftContentX, startY + 2, "[ 플레이어 목록 ]", ConsoleColor.Cyan);

    // 1. 호스트 표시
    string hostName = "(비어있음)";
    if (NetworkManager.Instance.IsHost) hostName = $"{playerNickname} (Me)";
    else if (!string.IsNullOrEmpty(otherPlayerNickname) && otherPlayerNickname != "???") hostName = otherPlayerNickname;
    else hostName = "Host (연결 중...)";

    DrawTextToBuffer(leftContentX, startY + 5, "1. HOST (방장)", ConsoleColor.DarkGray);
    DrawTextToBuffer(leftContentX, startY + 6, $"   {hostName}", ConsoleColor.Green);

    // 2. 게스트 표시
    string guestName = "(접속 대기중...)";
    ConsoleColor guestColor = ConsoleColor.DarkGray;
    if (NetworkManager.Instance.IsHost)
    {
        if (!string.IsNullOrEmpty(otherPlayerNickname) && otherPlayerNickname != "???") 
        {
            guestName = otherPlayerNickname;
            guestColor = ConsoleColor.Cyan;
        }
    }
    else
    {
        guestName = $"{playerNickname} (Me)";
        guestColor = ConsoleColor.Cyan;
    }

    DrawTextToBuffer(leftContentX, startY + 9, "2. GUEST (참가자)", ConsoleColor.DarkGray);
    DrawTextToBuffer(leftContentX, startY + 10, $"   {guestName}", guestColor);


    // [좌측 하단] 버튼 영역 (시작 / 나가기)
    int buttonY = startY + height - 6; // 아래쪽 여백
    
    // 안내선
    DrawTextToBuffer(leftContentX, buttonY - 2, "────────────────", ConsoleColor.DarkGray);

    // 게임 시작 버튼 (호스트만 활성화)
    string startMsg = "[S] 게임 시작";
    ConsoleColor startColor;
    
    if (NetworkManager.Instance.GuestClient == null) 
    {
        startMsg += " (대기)";
        startColor = ConsoleColor.DarkGray;
    }
    else
    {
        startColor = ConsoleColor.Yellow;
    }
    
    // 게스트일 경우 '준비 완료' 등으로 표시 가능하나 여기선 비활성 처리
    if (!NetworkManager.Instance.IsHost) 
    {
        startMsg = "호스트 대기 중...";
        startColor = ConsoleColor.Gray;
    }

    DrawTextToBuffer(leftContentX, buttonY, startMsg, startColor);
    DrawTextToBuffer(leftContentX, buttonY + 2, "[Q/ESC] 나가기", ConsoleColor.Red);


    // ============================================================
    // [우측 패널] 채팅창
    // ============================================================

    int rightContentX = dividerX + 2;
    int rightContentWidth = width - 35 - 3; // 전체 - 좌측 - 여백

    DrawTextToBuffer(rightContentX, startY + 2, "[ 채팅 ]", ConsoleColor.Yellow);
    DrawTextToBuffer(rightContentX + 10, startY + 2, "(Enter키를 눌러 입력)", ConsoleColor.Gray);

    // 채팅 로그 출력 영역 계산
    int chatLogStartY = startY + 4;
    int chatLogHeight = height - 7; // 상단 제목(4) + 하단 입력창(3) 제외
    
    // 로그 출력 (최근 메시지가 아래로 오도록)
    // chatLog 리스트의 끝부분에서 chatLogHeight만큼 가져와서 그림
    int startIndex = Math.Max(0, chatLog.Count - chatLogHeight);
    int drawLine = 0;

    for (int i = startIndex; i < chatLog.Count; i++)
    {
        string msg = chatLog[i];
        
        // [신규] 시스템 메시지(▶, ▷)는 노란색으로 표시
        ConsoleColor msgColor = ConsoleColor.White;
        if (msg.StartsWith("▶") || msg.StartsWith("▷")) 
        {
            msgColor = ConsoleColor.Yellow;
        }

        if (GetDisplayWidth(msg) > rightContentWidth) 
            msg = msg.Substring(0, Math.Min(msg.Length, 35)) + "...";

        DrawTextToBuffer(rightContentX, chatLogStartY + drawLine, msg, msgColor);
        drawLine++;
    }

    // [우측 하단] 채팅 입력창
    int inputY = startY + height - 2;
    
    // 입력창 구분선
    for(int k = dividerX + 1; k < startX + width - 1; k++) 
        DrawToBuffer(k, inputY - 1, '─', ConsoleColor.DarkGray);

    if (isChatting)
    {
        DrawTextToBuffer(rightContentX, inputY, $"입력: {chatInput}_", ConsoleColor.White);
        DrawTextToBuffer(startX + width - 15, inputY, "[Enter] 전송", ConsoleColor.Green);
    }
    else
    {
        DrawTextToBuffer(rightContentX, inputY, "대화하려면 [Enter]를 누르세요...", ConsoleColor.Gray);
    }
}

   private void ProcessRoomWaitInput(ConsoleKeyInfo key)
{
    // [1] 채팅 모드일 때의 입력 처리
    if (isChatting)
    {
        // 전송 (Enter)
        if (key.Key == ConsoleKey.Enter)
        {
            SendChat(chatInput); // 메시지 전송
            chatInput = "";      // 입력창 초기화
            isChatting = false;  // 명령 모드로 복귀
            return;
        }
        // 취소 (ESC)
        if (key.Key == ConsoleKey.Escape)
        {
            isChatting = false;
            chatInput = "";
            return;
        }
        // 백스페이스
        if (key.Key == ConsoleKey.Backspace)
        {
            if (chatInput.Length > 0)
                chatInput = chatInput.Substring(0, chatInput.Length - 1);
        }
        // 글자 입력 (한글 포함)
        else if (!char.IsControl(key.KeyChar))
        {
            // 길이 제한 (화면 밖으로 안 나가게)
            if (GetDisplayWidth(chatInput) < 30) 
                chatInput += key.KeyChar;
        }
        return;
    }

    // [2] 명령 모드일 때의 입력 처리 (기존 로직)
    
    // 채팅 시작 (Enter)
    if (key.Key == ConsoleKey.Enter)
    {
        isChatting = true;
        chatInput = "";
        return;
    }

    // 나가기 (Q 또는 ESC)
    if (key.Key == ConsoleKey.Q || key.Key == ConsoleKey.Escape)
    {
        currentState = GameState.Multiplayer_Room_LeaveConfirm;
        popupIndex = 1;
        return;
    }

    // 게임 시작 (S) - 호스트만 가능
    if (key.Key == ConsoleKey.S)
    {
        if (NetworkManager.Instance.IsHost && NetworkManager.Instance.GuestClient != null)
        {
            // [수정] 1. 게스트에게 '게임 시작' 패킷 전송
            var packet = new Packet { Type = PacketType.GameStart }; // 데이터는 필요 없음
            NetworkManager.Instance.Send(packet);

            // [수정] 2. 나(호스트)도 인트로 시작
            StartIntro();
        }
    }
}
    private void DrawRoomLeaveConfirm()
    {
        DrawConfirmPopup("방을 나가시겠습니까?");
    }

    // [신규] 방 나가기 입력 처리
    private void ProcessRoomLeaveConfirmInput(ConsoleKeyInfo key)
    {
        HandleConfirmPopupInput(key,
            onYes: () => {
                // [기존 나가기 로직 재사용]
                NetworkManager.Instance.Close();
                NetworkManager.Instance.StartSearchingRooms();
                currentState = GameState.Multiplayer_Lobby;
                
                // 데이터 초기화
                otherPlayerNickname = "???";
                isOtherPlayerReady = false;
            },
            onNo: () => {
                currentState = GameState.Multiplayer_RoomWait;
            }
        );
    }

    // 헬퍼 메서드
    private void JoinRoom(int index)
{
    if (roomList.Count <= index) return;
    RoomInfo targetRoom = roomList[index];

    if (targetRoom.CurrentPlayers >= targetRoom.MaxPlayers)
    {
        // AddLog("방이 꽉 찼습니다! (2/2)"); // 기존 코드 삭제
        currentState = GameState.Multiplayer_FullRoomWarning; // 팝업 띄우기
        return; 
    }

    // [신규] 비밀번호가 걸려있는지 확인
    if (targetRoom.IsPrivate)
    {
        pendingJoinRoom = targetRoom; // 방 정보 임시 저장
        joinPasswordInput = "";       // 입력창 초기화
        joinPasswordError = "";       // 에러 메시지 초기화
        isEnteringJoinPw = true;      // 텍스트 입력 모드로 시작
        
        currentState = GameState.Multiplayer_PasswordInput;
    }
    else
    {
        // 비밀번호 없으면 바로 접속 시도 (기존 로직)
        ConnectToRoom(targetRoom);
    }
}

// [신규] 실제 접속 시도 로직 분리
private void ConnectToRoom(RoomInfo room)
    {
        // [핵심 수정] 이미 연결된 상태(IP 접속)라면 인원수 체크를 건너뜁니다.
        // (연결된 상태라면 내가 이미 그 인원수(2/2)에 포함되어 있기 때문입니다)
        if (!NetworkManager.Instance.IsConnected)
        {
            if (room.CurrentPlayers >= room.MaxPlayers)
            {
                AddLog("접속 실패: 방이 꽉 찼습니다.");
                currentState = GameState.Multiplayer_Lobby;
                return;
            }
        }

        NetworkManager.Instance.StopSearchingRooms();
        
        bool success = true;

        // 연결되어 있지 않다면(방 목록 접속) 연결 시도
        if (!NetworkManager.Instance.IsConnected)
        {
            AddLog("접속 시도 중...");
            success = NetworkManager.Instance.ConnectToHost(room.IpAddress, room.Port);
        }
        
        if (success)
        {
            // 데이터 초기화
            ResetMultiplayerData();

            currentRoomTitle = room.Title;
            currentRoomPassword = room.Password; 

            // 내 정보 전송
            SendMyPlayerInfo();
            
            // 대기실로 이동
            currentState = GameState.Multiplayer_RoomWait;
        }
        else
        {
            AddLog("접속 실패!");
            NetworkManager.Instance.StartSearchingRooms();
            currentState = GameState.Multiplayer_Lobby;
        }
    }

    private void RefreshRoomList()
    {
        // [수정] 방 검색 재시작 (기존 목록을 비우고 다시 찾음)
        NetworkManager.Instance.StartSearchingRooms();
        AddLog("방 목록을 새로고침했습니다.");
    }

    private void ProcessNetworkPackets()
    {
        if (NetworkManager.Instance.PacketQueue.Count == 0) return;

        lock (NetworkManager.Instance.QueueLock)
        {
            while (NetworkManager.Instance.PacketQueue.Count > 0)
            {
                Packet packet = NetworkManager.Instance.PacketQueue.Dequeue();

                switch (packet.Type)
                {
                    // [Phase 1 기존 처리]
                    case PacketType.PlayerInfo: HandlePlayerInfo(packet.Data); break;
                    case PacketType.Chat:       HandleChat(packet.Data); break;
                    case PacketType.Disconnect: HandleDisconnection(); break;

                    // [Phase 2 신규 처리]
                    case PacketType.GameStart:    StartIntro(); break;                 // 게임 시작
                    case PacketType.ClassSelect:  HandleClassSelect(packet.Data); break; // 직업 선택
                    case PacketType.MapMove:      HandleMapMove(packet.Data); break;     // 이동
                    case PacketType.BattleStart:  HandleBattleStart(packet.Data); break; // 전투 시작
                    case PacketType.BattleAction: HandleBattleAction(packet.Data); break;// 전투 행동
                    case PacketType.BattleTurnEnd: HandleBattleTurnEnd(); break; // (기존 로직 변경됨)
                    case PacketType.BattleResultFinished: HandleBattleResultFinished(); break;
                    case PacketType.MapInit:
                        HandleMapInit(packet.Data);
                        break;
                    // [신규] 몬스터 동기화
                    case PacketType.MonsterUpdate:
                        HandleMonsterUpdate(packet.Data);
                        break;
                    case PacketType.EnemyAction:   HandleEnemyAction(packet.Data); break;
                    case PacketType.FleeRequest:   HandleFleeRequest(); break;
                    case PacketType.BattleEnd:     HandleBattleEnd(); break;
                    case PacketType.ChestUpdate:  HandleChestUpdate(packet.Data); break;
                    case PacketType.TrapUpdate:   HandleTrapUpdate(packet.Data); break;
                    case PacketType.MonsterDead:  HandleMonsterDead(packet.Data); break;
                    case PacketType.RoomInfoRequest: HandleRoomInfoRequest(); break;
                    case PacketType.RoomInfoResponse: HandleRoomInfoResponse(packet.Data); break;
                    case PacketType.ChestBusy: HandleChestBusy(packet.Data); break;
                }
            }
        }
    }

// 1. 멀티플레이 직업 선택 화면 그리기
    private void DrawMultiplayerClassSelect()
    {
        // 1. 아직 내가 직업을 고르지 않았다면 -> [싱글플레이와 동일한 선택 화면] 출력
        if (!iSelectedClass)
        {
            // (ChooseClass의 로직을 그대로 가져와서 그립니다)
            string[] titleArt = AsciiArt.GetChooseClassTitleArt();
            int titleHeight = titleArt.Length;
            int titleWidth = 0;
            foreach(string line in titleArt) { titleWidth = Math.Max(titleWidth, GetDisplayWidth(line)); }
            
            int titleX = screenWidth / 2 - titleWidth / 2;
            int titleY = 1; 
            
            for(int i=0; i<titleHeight; i++)
            {
                DrawTextToBuffer(titleX, titleY + i, titleArt[i], ConsoleColor.White, ConsoleColor.Black, true);
            }

            var classInfos = new[] {
                new { Name = "Warrior (시스템 방어자)", Desc = "높은 체력/방어력 (STR/DEF)", Skills = "주요 스킬: 파워 스트라이크, 방패 치기" },
                new { Name = "Wizard (버그 수정자)", Desc = "강력한 주문 공격 (INT)", Skills = "주요 스킬: 파이어볼, 힐" },
                new { Name = "Rogue (정보 수집가)", Desc = "회피와 치명타 (DEX)", Skills = "주요 스킬: 백스탭, 독 찌르기" }
            };

            int boxWidth = Math.Max(35, screenWidth / 3); 
            int totalWidth = boxWidth * 3;
            int startX = screenWidth / 2 - totalWidth / 2;
            int artHeight = 20; 
            int descHeight = 6; 
            int boxHeight = artHeight + descHeight + 3; 
            int boxY = titleY + titleHeight + 2;

            for (int i = 0; i < 3; i++)
            {
                bool isSelected = (i == mpClassSelectedIndex);
                ConsoleColor boxColor = isSelected ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
                
                int boxX = startX + (i * boxWidth);
                int currentBoxWidth = (i == 2) ? (screenWidth - boxX - 1) : boxWidth;
                
                DrawBox(boxX, boxY, currentBoxWidth, boxHeight, "CLASS"); 

                PlayerClass currentClass = (PlayerClass)i;
                string[] art = AsciiArt.GetPlayerArt(currentClass);

                int artActualMaxWidth = 0;
                foreach(string line in art) { artActualMaxWidth = Math.Max(artActualMaxWidth, GetDisplayWidth(line)); }
                
                int baseArtStartX = boxX + (currentBoxWidth / 2) - (artActualMaxWidth / 2);
                int baseArtStartY = boxY + 2; 

                (float percentX, float percentY) = AsciiArt.GetClassSelectOffset(currentClass);
                int offsetX = (int)(currentBoxWidth * percentX);
                int offsetY = (int)(artHeight * percentY);

                int artStartX = baseArtStartX + offsetX;
                int artStartY = baseArtStartY + offsetY;
            
                ConsoleColor artColor = ConsoleColor.Green; 
                if ((PlayerClass)i == PlayerClass.Wizard) artColor = ConsoleColor.Cyan;
                else if ((PlayerClass)i == PlayerClass.Rogue) artColor = ConsoleColor.Yellow;

                for(int j=0; j<art.Length; j++) {
                    if (artStartY + j < boxY + artHeight) 
                        DrawTextToBuffer(artStartX, artStartY + j, art[j], artColor, ConsoleColor.Black, true);
                }

                int descY = boxY + artHeight + 1;
                DrawTextToBuffer(boxX + 2, descY, classInfos[i].Name, boxColor);
                DrawTextToBuffer(boxX + 2, descY + 2, classInfos[i].Desc, ConsoleColor.White);
                DrawTextToBuffer(boxX + 2, descY + 3, classInfos[i].Skills, ConsoleColor.DarkGray);

                if (isSelected) {
                    string selectText = "[ Enter: 선택 ]";
                    DrawTextToBuffer(boxX + (boxWidth / 2) - (GetDisplayWidth(selectText) / 2), boxY + boxHeight - 2, selectText, ConsoleColor.Yellow);
                }
            }
            
            DrawTextToBuffer(screenWidth / 2 - 20, boxY + boxHeight + 1, "[←/→] 이동  [Enter] 선택", ConsoleColor.White);

            // [팝업 그리기] - mpIsConfirming 상태일 때
            if (mpIsConfirming)
            {
                DrawClassConfirmation(classInfos[mpClassSelectedIndex].Name, mpConfirmChoice);
            }
        }
        // 2. 내가 직업을 골랐다면 -> [최종 준비 화면 (좌우 분할)] 출력
        else
        {
            DrawMultiplayerReadyScreen();
        }
    }

    // [신규] 최종 준비 화면 (좌우 분할) 그리기
    private void DrawMultiplayerReadyScreen()
    {
        // 1. 레이아웃 설정
        int totalWidth = (int)(screenWidth * 0.95); // 전체 너비 (화면 95%)
        int gap = 30; // [신규] 중앙 여백 (이곳에 카운트다운 표시)
        int cardWidth = (totalWidth - gap) / 2; // 카드 하나의 너비
        int height = (int)(screenHeight * 0.8);
        
        int startX = screenWidth / 2 - totalWidth / 2;
        int startY = screenHeight / 2 - height / 2;

        // 좌측 카드 시작 위치
        int leftCardX = startX;
        // 우측 카드 시작 위치 (좌측 끝 + 여백)
        int rightCardX = startX + cardWidth + gap;

        // 2. 좌측 패널 (나) 그리기
        DrawBox(leftCardX, startY, cardWidth, height, "My Status");
        DrawPlayerInfoCard(leftCardX, startY, cardWidth, height, player, playerNickname, true);

        // 3. 우측 패널 (상대방) 그리기
        DrawBox(rightCardX, startY, cardWidth, height, "Enemy Status"); // 혹은 "Ally Status"

        if (otherSelectedClass && otherPlayer != null)
        {
            DrawPlayerInfoCard(rightCardX, startY, cardWidth, height, otherPlayer, otherPlayerNickname, false);
        }
        else
        {
            // 상대방 대기 중 메시지
            string waitMsg = "상대방 선택 중...";
            DrawTextToBuffer(rightCardX + (cardWidth/2) - (GetDisplayWidth(waitMsg)/2), startY + height/2, waitMsg, ConsoleColor.DarkGray);
        }
    }

    // [신규] 플레이어 정보 카드 그리기 헬퍼
    private void DrawPlayerInfoCard(int x, int y, int w, int h, Player p, string nick, bool isMe)
    {
        if (p == null) return;

        // 1. 아스키 아트
        string[] art = AsciiArt.GetPlayerArt(p.Class);
        int artHeight = art.Length;
        
        // 아트 중앙 정렬
        int artX = x + (w / 2); // 중심 X
        int artY = y + (h / 2) - (artHeight / 2) - 3; // 약간 위로 올림

        ConsoleColor color = isMe ? ConsoleColor.White : ConsoleColor.Cyan; 

        // 아트 그리기 (DrawSingleEntityArt 로직 일부 차용 or 직접 그리기)
        // 여기선 직접 그립니다.
        int artMaxWidth = 0;
        foreach(var line in art) artMaxWidth = Math.Max(artMaxWidth, GetDisplayWidth(line));
        
        for (int i = 0; i < artHeight; i++)
        {
            int lineX = artX - (GetDisplayWidth(art[i]) / 2);
            DrawTextToBuffer(lineX, artY + i, art[i], color, ConsoleColor.Black, true);
        }

        // 2. 직업 이름
        string className = p.Class.ToString();
        int nameY = artY + artHeight + 2;
        DrawTextToBuffer(x + (w/2) - (GetDisplayWidth(className)/2), nameY, className, ConsoleColor.White);

        // 3. 닉네임
        string displayNick = isMe ? $"{nick} (Me)" : nick;
        DrawTextToBuffer(x + (w/2) - (GetDisplayWidth(displayNick)/2), nameY + 2, displayNick, ConsoleColor.Yellow);
    }
    // 2. 직업 선택 입력 처리
    private void ProcessMultiplayerClassSelectInput(ConsoleKeyInfo key)
    {
        if (iSelectedClass) return; // 이미 선택했으면 무시

        // [수정] 1. ESC 누르면 '나가기 팝업' 상태로 전환 (튕김 방지)
        if (key.Key == ConsoleKey.Escape)
        {
            currentState = GameState.Multiplayer_ClassSelect_ExitConfirm;
            popupIndex = 1; // 기본값 '아니요'
            return;
        }

        // 1. 확인 팝업 상태일 때
        if (mpIsConfirming)
        {
            if (key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow)
            {
                mpConfirmChoice = !mpConfirmChoice; // 예/아니요 토글
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                if (mpConfirmChoice) ConfirmClassSelection(); // "예" -> 확정
                else mpIsConfirming = false; // "아니요" -> 팝업 닫기
            }
            else if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.N)
            {
                mpIsConfirming = false;
            }
            else if (key.Key == ConsoleKey.Y)
            {
                ConfirmClassSelection();
            }
            return;
        }

        // 2. 직업 선택 상태일 때
        switch (key.Key)
        {
            // 방향키 이동
            case ConsoleKey.LeftArrow:
                mpClassSelectedIndex = (mpClassSelectedIndex - 1 + 3) % 3;
                break;
            case ConsoleKey.RightArrow:
                mpClassSelectedIndex = (mpClassSelectedIndex + 1) % 3;
                break;

            // [신규] 숫자 키 이동 (싱글플레이와 동일하게 커서만 이동)
            case ConsoleKey.D1: mpClassSelectedIndex = 0; break; // 전사
            case ConsoleKey.D2: mpClassSelectedIndex = 1; break; // 마법사
            case ConsoleKey.D3: mpClassSelectedIndex = 2; break; // 도적

            // 선택 (팝업 띄우기)
            case ConsoleKey.Enter:
                mpIsConfirming = true; 
                mpConfirmChoice = true; // 기본값 "예"
                break;
        }
    }

    // [신규] 직업 확정 및 패킷 전송 로직
    private void ConfirmClassSelection()
    {
        PlayerClass selected = (PlayerClass)mpClassSelectedIndex;
        
        // 플레이어 생성
        player = new Player(selected);
        iSelectedClass = true;

        // 패킷 전송
        var data = new ClassSelectData { SelectedClass = selected };
        var packet = new Packet { Type = PacketType.ClassSelect, Data = JsonSerializer.Serialize(data) };
        NetworkManager.Instance.Send(packet);

        // 게임 시작 가능한지 체크 (상대방도 준비됐는지)
        CheckStartGame();
    }

    // 3. 상대방 직업 정보 수신
    private void HandleClassSelect(string json)
    {
        var data = JsonSerializer.Deserialize<ClassSelectData>(json);
        
        // 1. 상대방 플레이어 생성
        otherPlayer = new Player(data.SelectedClass);
        otherSelectedClass = true; // 상대방 준비 완료 표시

        // 2. 화면 갱신
        NetworkManager.Instance.IsDirty = true;
        
        // [핵심 수정] Handshake 로직 추가
        // 상대방 정보를 받았는데, 나도 이미 직업을 골라놓은 상태라면?
        // -> 상대방이 내 정보를 모를 수도 있으니(늦게 들어왔거나 패킷 유실 등), 내 정보를 다시 보내준다.
        // -> 단, 무한 루프를 방지하기 위해 'CheckStartGame'이 실패할 때만(아직 시작 안했을 때만) 보낸다.
        if (iSelectedClass)
        {
            // 아직 게임 시작 조건을 충족하지 못했거나, 상대방 화면 갱신을 위해 재전송
            // (TCP라 유실은 없지만 시점 차이 해결용)
            // 여기서는 간단하게 "내가 고른 상태면 무조건 한 번 더 보냄"으로 처리하되,
            // 상대방이 이미 알고 있다면(otherSelectedClass) 무시되도록 로직이 필요하지만
            // 현재 구조상 그냥 보내도 안전함 (덮어쓰기됨)
            
            // *단, 내가 방금 패킷을 보내서 이 함수가 호출된게 아니라(Loopback 방지), 
            //  순수하게 상대방 패킷을 받았을 때만 답장해야 함.*
            //  TCP는 Loopback이 안되므로 안심하고 답장 전송.
            
            // 이미 게임이 시작된 상태라면 보내지 않음
            if (currentState != GameState.Multiplayer_ClassSelect) return;

            // 내 정보 재전송 (상대방 업데이트용)
            var myData = new ClassSelectData { SelectedClass = player.Class };
            var packet = new Packet { Type = PacketType.ClassSelect, Data = JsonSerializer.Serialize(myData) };
            NetworkManager.Instance.Send(packet);
        }
        
        // 3. 게임 시작 시도
        CheckStartGame();
    }

    private void DrawClassSelectExitConfirm()
    {
        DrawConfirmPopup("방을 나가시겠습니까?");
    }

    // [신규] 직업 선택 나가기 입력 처리
    private void ProcessClassSelectExitConfirmInput(ConsoleKeyInfo key)
    {
        HandleConfirmPopupInput(key,
            onYes: () => {
                // 방 나가기 로직 (연결 끊고 로비로)
                NetworkManager.Instance.Close();
                NetworkManager.Instance.StartSearchingRooms();
                
                // 데이터 초기화
                iSelectedClass = false;
                otherSelectedClass = false;
                otherPlayer = null;
                otherPlayerNickname = "???";
                isOtherPlayerReady = false;

                currentState = GameState.Multiplayer_Lobby;
            },
            onNo: () => {
                currentState = GameState.Multiplayer_ClassSelect;
            }
        );
    }
    // 4. 게임 시작 확인 (둘 다 골랐으면 맵으로 이동)
    private void CheckStartGame()
    {
        // 나도 골랐고, 상대방도 골랐다면
        if (iSelectedClass && otherSelectedClass)
        {
            // [수정] 바로 시작하지 않고 카운트다운 상태로 전환
            currentState = GameState.Multiplayer_Countdown;
            
            isCountdownStarted = false; // 카운트다운 초기화
            currentCountdownNumber = 5;
            countdownStartTime = DateTime.Now;
        }
    }

   private void InitializeMultiplayerGame(int seed)
    {
        InitializeGameData(); 
        MapWidth = screenWidth - 2;
        MapHeight = worldMapHeight - 2;

        InitializeMap(seed);

        // [수정] 저장된 안전 방의 중앙 좌표 사용
        (player.X, player.Y) = startSafeRoomRect.Center;
    }

    private void HandleMapInit(string json)
    {
        var data = JsonSerializer.Deserialize<MapInitData>(json);

        TransitionToStage(data.Stage, data.Seed, data.MapWidth, data.MapHeight);

        if (otherPlayer != null)
        {
            otherPlayer.X = data.HostX;
            otherPlayer.Y = data.HostY;
            
        }

        player.X = data.HostX + 1;
        player.Y = data.HostY;

        Thread.Sleep(100);

        SendMyPlayerInfo();
        NetworkManager.Instance.IsDirty = true;
    }
   private void HandleDisconnection()
    {
        // 1. 퇴장 메시지용 이름 준비
        string leaverName = string.IsNullOrEmpty(otherPlayerNickname) || otherPlayerNickname == "???" 
                            ? "상대방" 
                            : otherPlayerNickname;
        
        bool isInGame = (currentState == GameState.Multiplayer_World ||
                         currentState == GameState.Multiplayer_Battle ||
                         currentState == GameState.Multiplayer_BattleResultWait ||
                         currentState == GameState.Battle_TurnDelay ||
                         currentState == GameState.StageIntro ||
                         currentState == GameState.Multiplayer_Countdown);

        bool isPreGame = (currentState == GameState.Intro || 
                          currentState == GameState.Multiplayer_ClassSelect);

        bool isRoomWait = (currentState == GameState.Multiplayer_RoomWait);
        bool wasHost = NetworkManager.Instance.IsHost;

        // [Case 1: 대기실]
        if (isRoomWait)
        {
            // [핵심 수정] 정식으로 입장하여 닉네임이 확인된 경우에만 퇴장 메시지 출력
            // (비밀번호 입력 중에 나간 경우, 닉네임이 아직 "???"이므로 메시지를 띄우지 않음)
            if (otherPlayerNickname != "???" && !string.IsNullOrEmpty(otherPlayerNickname))
            {
                AddChatLog($"▷ '{leaverName}' 님이 퇴장하셨습니다.");
            }

            if (wasHost)
            {
                // [호스트] 방 유지 (접속 대기 재개)
                NetworkManager.Instance.RestartListening(); 
                NetworkManager.Instance.UpdateRoomPlayerCount(1);
                
                // 데이터 초기화
                otherPlayer = null;
                otherPlayerNickname = "???";
                isOtherPlayerReady = false;
                otherSelectedClass = false;

                NetworkManager.Instance.IsDirty = true; 
                return;
            }
            else
            {
                // [게스트] 호스트 승계
                NetworkManager.Instance.Close(); 
                AddChatLog("▷ 방장이 되었습니다.");
                
                string newTitle = string.IsNullOrEmpty(currentRoomTitle) ? $"{playerNickname}의 방" : currentRoomTitle;
                string newPw = currentRoomPassword;

                RoomInfo myRoom = new RoomInfo
                {
                    Title = newTitle,
                    HostName = playerNickname,
                    CurrentPlayers = 1,
                    IsPrivate = !string.IsNullOrEmpty(newPw), 
                    Password = newPw 
                };
                NetworkManager.Instance.StartHost(0, myRoom);

                otherPlayer = null;
                otherPlayerNickname = "???";
                isOtherPlayerReady = false;
                otherSelectedClass = false;

                NetworkManager.Instance.IsDirty = true;
                return;
            }
        }

        // [Case 2: 그 외 (게임 중 등)] - 기존 로직 유지
        NetworkManager.Instance.Close(); 

        otherPlayer = null;
        otherPlayerNickname = "???";
        isOtherPlayerReady = false;
        otherSelectedClass = false;
        myFleeRequest = false;
        otherFleeRequest = false;

        if (isInGame)
        {
            AddLog($"{leaverName} 님이 게임을 나갔습니다!", ConsoleColor.Red);
            AddLog("싱글 플레이 모드로 전환됩니다.", ConsoleColor.Gray);

            if (currentState == GameState.Multiplayer_World || 
                currentState == GameState.Multiplayer_Countdown ||
                currentState == GameState.StageIntro)
            {
                currentState = GameState.World;
            }
            else if (currentState == GameState.Multiplayer_Battle || 
                     currentState == GameState.Battle_TurnDelay ||
                     currentState == GameState.Multiplayer_BattleResultWait)
            {
                currentState = GameState.Battle;
                isPlayerTurn = true; 
                battleTurnCount = 0; 
                if (currentBattleMonster == null) currentState = GameState.World;
            }
        }
        else if (isPreGame)
        {
            // 인트로/직업선택 중 퇴장
            AddChatLog($"▷ '{leaverName}' 님이 퇴장하셨습니다.");
            AddChatLog("▷ 대기실로 돌아갑니다.");

            iSelectedClass = false;
            mpIsConfirming = false; 

            string newTitle = string.IsNullOrEmpty(currentRoomTitle) ? $"{playerNickname}의 방" : currentRoomTitle;

            RoomInfo myRoom = new RoomInfo
            {
                Title = newTitle,
                HostName = playerNickname,
                CurrentPlayers = 1,
                IsPrivate = false
            };
            NetworkManager.Instance.StartHost(0, myRoom);
            if (!wasHost) AddChatLog("▷ 방장이 되었습니다.");

            currentState = GameState.Multiplayer_RoomWait;
        }
        else
        {
            currentState = GameState.Multiplayer_Lobby;
            NetworkManager.Instance.StartSearchingRooms();
        }

        NetworkManager.Instance.IsDirty = true; 
    }

    // 플레이어 정보(닉네임) 수신 처리
    private void HandlePlayerInfo(string json)
    {
        var info = JsonSerializer.Deserialize<PlayerInfoData>(json);

        if (info.PlayerId == myPlayerId) return;

        bool isNewUser = (otherPlayerNickname == "???");

        otherPlayerNickname = info.Nickname;
        isOtherPlayerReady = true;

        // 1. 상대방 객체 생성 및 스탯 초기화
        if (otherPlayer == null || otherPlayer.Class != (PlayerClass)info.PlayerClass)
        {
            otherPlayer = new Player((PlayerClass)info.PlayerClass);
            otherPlayer.EquippedGear.Clear();
        }

        // 2. 스탯 동기화
        otherPlayer.baseMaxHP = info.MaxHP;
        otherPlayer.baseDEF = info.DEF;
        otherPlayer.baseDEX = info.DEX;
        otherPlayer.HP = info.HP;

        // 4. 입장 메시지
        if (isNewUser)
        {
            if (NetworkManager.Instance.IsHost || !info.IsHost)
            {
                AddChatLog($"▶ '{otherPlayerNickname}' 님이 입장하셨습니다.");
            }
        }

        // 5. 답장 (호스트인 경우)
        if (NetworkManager.Instance.IsHost && !info.IsHost)
        {
            SendMyPlayerInfo();
        }

        NetworkManager.Instance.IsDirty = true;
    }
    // 내 정보를 패킷으로 만들어 보내는 헬퍼 함수
    private void SendMyPlayerInfo()
    {
        // 1. player 객체가 현재 존재하는지 확인 (직업 선택 전에는 null임)
        bool hasPlayer = (player != null);

        var myInfo = new PlayerInfoData 
        { 
            PlayerId = myPlayerId, 
            Nickname = playerNickname, 
            IsHost = NetworkManager.Instance.IsHost,
            
            // [수정] player가 있으면 실제 값을, 없으면 0(기본값)을 보냄
            PlayerClass = hasPlayer ? (int)player.Class : 0, 
            HP = hasPlayer ? player.HP : 0,
            MaxHP = hasPlayer ? player.MaxHP : 0,
            DEX = hasPlayer ? player.DEX : 0,
            DEF = hasPlayer ? player.DEF : 0
        };

        var packet = new Packet 
        { 
            Type = PacketType.PlayerInfo, 
            Data = JsonSerializer.Serialize(myInfo) 
        };

        NetworkManager.Instance.Send(packet);
    }
private void DrawConfirmPopup(string message)
{
    int width = 40;
    int height = 10;
    int startX = screenWidth / 2 - width / 2;
    int startY = screenHeight / 2 - height / 2;

    // 검은 배경으로 뒤를 가림
    DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
    DrawBox(startX, startY, width, height, "알림");

    DrawTextToBuffer(startX + width/2 - GetDisplayWidth(message)/2, startY + 3, message, ConsoleColor.White);

    string yes = "  예(Y)  ";
    string no = " 아니요(N) ";

    // 버튼 하이라이트
    ConsoleColor yesBg = (popupIndex == 0) ? ConsoleColor.Green : ConsoleColor.Black;
    ConsoleColor yesFg = (popupIndex == 0) ? ConsoleColor.Black : ConsoleColor.White;
    ConsoleColor noBg = (popupIndex == 1) ? ConsoleColor.Red : ConsoleColor.Black;
    ConsoleColor noFg = (popupIndex == 1) ? ConsoleColor.Black : ConsoleColor.White;

    int btnY = startY + 7;
    int btnGap = 4;
    int totalBtnWidth = GetDisplayWidth(yes) + GetDisplayWidth(no) + btnGap;
    int btnX = startX + width / 2 - totalBtnWidth / 2;

    DrawTextToBuffer(btnX, btnY, yes, yesFg, yesBg);
    DrawTextToBuffer(btnX + GetDisplayWidth(yes) + btnGap, btnY, no, noFg, noBg);
}

// [신규] 공용 팝업 입력 처리 헬퍼
private void HandleConfirmPopupInput(ConsoleKeyInfo key, Action onYes, Action onNo)
{
    if (key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow)
    {
        popupIndex = (popupIndex + 1) % 2; // 0 <-> 1 토글
    }
    else if (key.Key == ConsoleKey.Enter)
    {
        if (popupIndex == 0) onYes();
        else onNo();
    }
    else if (key.Key == ConsoleKey.Y) onYes();
    else if (key.Key == ConsoleKey.N || key.Key == ConsoleKey.Escape) onNo();
}
private void DrawPasswordInputWindow()
{
    int width = 50, height = 13;
    int startX = screenWidth / 2 - width / 2;
    int startY = screenHeight / 2 - height / 2;

    DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
    DrawBox(startX, startY, width, height, "비밀번호 입력");

    string msg = $"[{pendingJoinRoom.Title}] 방은 잠겨있습니다.";
    DrawTextToBuffer(startX + 2, startY + 2, msg, ConsoleColor.Gray);

    // 포커스 상태에 따라 입력창 색상 변경
    string inputIndicator = isEnteringJoinPw ? "► " : "  ";
    ConsoleColor inputColor = isEnteringJoinPw ? ConsoleColor.Yellow : ConsoleColor.White;
    DrawTextToBuffer(startX + 4, startY + 4, $"{inputIndicator}PW: {joinPasswordInput}", inputColor);

    if (!string.IsNullOrEmpty(joinPasswordError))
    {
        DrawTextToBuffer(startX + 4, startY + 6, joinPasswordError, ConsoleColor.Red);
    }

    // 버튼 그리기
    int btnY = startY + 9;
    string confirm = " [확인] ";
    string cancel = " [취소] ";
    
    // 버튼 활성화 상태 (입력 중이 아닐 때)
    bool btnActive = !isEnteringJoinPw;

    ConsoleColor confirmBg = (btnActive && passwordBtnIndex == 0) ? ConsoleColor.Green : ConsoleColor.Black;
    ConsoleColor confirmFg = (btnActive && passwordBtnIndex == 0) ? ConsoleColor.Black : ConsoleColor.White;
    
    ConsoleColor cancelBg = (btnActive && passwordBtnIndex == 1) ? ConsoleColor.Red : ConsoleColor.Black;
    ConsoleColor cancelFg = (btnActive && passwordBtnIndex == 1) ? ConsoleColor.Black : ConsoleColor.White;

    int totalWidth = GetDisplayWidth(confirm) + GetDisplayWidth(cancel) + 4;
    int btnX = startX + width / 2 - totalWidth / 2;

    DrawTextToBuffer(btnX, btnY, confirm, confirmFg, confirmBg);
    DrawTextToBuffer(btnX + GetDisplayWidth(confirm) + 4, btnY, cancel, cancelFg, cancelBg);
}

// [신규] 비밀번호 입력 처리
private void ProcessPasswordInputWindow(ConsoleKeyInfo key)
{
    // 1. ESC: 취소
    if (key.Key == ConsoleKey.Escape)
    {
        ClosePasswordWindow();
        return;
    }

    // 2. 탭/방향키 위아래: 입력창 <-> 버튼 전환
    if (key.Key == ConsoleKey.Tab || key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow)
    {
        isEnteringJoinPw = !isEnteringJoinPw;
        return;
    }

    // 3. 버튼 영역일 때
    if (!isEnteringJoinPw)
    {
        if (key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow)
        {
            passwordBtnIndex = (passwordBtnIndex + 1) % 2;
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            if (passwordBtnIndex == 0) CheckPasswordAndJoin(); // 확인
            else ClosePasswordWindow(); // 취소
        }
        return;
    }

    // 4. 입력 영역일 때
    if (key.Key == ConsoleKey.Enter)
    {
        // 엔터치면 바로 확인 시도
        CheckPasswordAndJoin();
        return;
    }

    if (key.Key == ConsoleKey.Backspace)
    {
        if (joinPasswordInput.Length > 0)
        {
            joinPasswordInput = joinPasswordInput.Substring(0, joinPasswordInput.Length - 1);
            joinPasswordError = "";
        }
    }
    else if (!char.IsControl(key.KeyChar) && joinPasswordInput.Length < 15)
    {
        joinPasswordInput += key.KeyChar;
        joinPasswordError = "";
    }
}

// 헬퍼 메서드
private void CheckPasswordAndJoin()
{
    if (joinPasswordInput == pendingJoinRoom.Password)
    {
        ConnectToRoom(pendingJoinRoom);
    }
    else
    {
        joinPasswordError = "비밀번호가 틀렸습니다.";
        joinPasswordInput = "";
        isEnteringJoinPw = true; // 다시 입력하게 포커스 이동
    }
}

private void ClosePasswordWindow()
    {
        // [핵심 수정] 연결되어 있다면(IP 접속 중 취소) 연결 해제
        if (NetworkManager.Instance.IsConnected)
        {
            NetworkManager.Instance.Close();
            NetworkManager.Instance.StartSearchingRooms(); // 검색 재개
        }

        currentState = GameState.Multiplayer_Lobby;
        pendingJoinRoom = null;
    }

private void DrawFullRoomWarning()
{
    int width = 40;
    int height = 8;
    int startX = screenWidth / 2 - width / 2;
    int startY = screenHeight / 2 - height / 2;

    // 배경 암전 및 박스 그리기
    DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
    DrawBox(startX, startY, width, height, "알림");

    // 메시지
    string msg = "방 인원이 가득 찼습니다!";
    DrawTextToBuffer(startX + width/2 - GetDisplayWidth(msg)/2, startY + 3, msg, ConsoleColor.Red);

    // 버튼
    string btn = " [확인] ";
    int btnX = startX + width/2 - GetDisplayWidth(btn)/2;
    
    // 항상 선택된 상태로 표시 (버튼이 하나라서)
    DrawTextToBuffer(btnX, startY + 5, btn, ConsoleColor.Black, ConsoleColor.Green);
}

// [신규] 경고창 입력 처리
private void ProcessFullRoomWarningInput(ConsoleKeyInfo key)
{
    // 엔터, ESC, 스페이스바 등 아무 키나 누르면 닫기
    if (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Spacebar)
    {
        currentState = GameState.Multiplayer_Lobby;
    }
}
// [신규] 채팅 수신 처리
    private void HandleChat(string json)
    {
        var data = JsonSerializer.Deserialize<ChatData>(json);
        
        // [핵심 수정] 결과창 상태들 추가
        bool isInGame = (currentState == GameState.Multiplayer_World ||
                         currentState == GameState.Multiplayer_Battle ||
                         currentState == GameState.Battle_SkillSelect || 
                         currentState == GameState.Battle_ItemMenu ||    
                         currentState == GameState.Battle_ItemSubMenu || 
                         currentState == GameState.Multiplayer_BattleResultWait ||
                         currentState == GameState.Battle_TurnDelay ||
                         currentState == GameState.StageIntro ||
                         currentState == GameState.Multiplayer_Countdown ||
                         // [추가된 상태들]
                         currentState == GameState.LevelUp ||
                         currentState == GameState.LootDrop ||
                         currentState == GameState.LootSummary);

        if (isInGame)
        {
            // 인게임 로그창에 표시
            ConsoleColor color = (data.Nickname == playerNickname) ? ConsoleColor.Yellow : ConsoleColor.Cyan;
            AddLog($"{data.Nickname} : {data.Message}", color);
        }
        else
        {
            // 대기실 채팅창에 표시
            AddChatLog($"[{data.Nickname}] {data.Message}");
        }

        NetworkManager.Instance.IsDirty = true; 
    }

// [신규] 채팅 발신 처리
    private void SendChat(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        // [핵심 수정] 결과창 상태들 추가
        bool isInGame = (currentState == GameState.Multiplayer_World ||
                         currentState == GameState.Multiplayer_Battle ||
                         currentState == GameState.Battle_SkillSelect || 
                         currentState == GameState.Battle_ItemMenu ||    
                         currentState == GameState.Battle_ItemSubMenu || 
                         currentState == GameState.Multiplayer_BattleResultWait ||
                         currentState == GameState.Battle_TurnDelay ||
                         currentState == GameState.StageIntro ||
                         currentState == GameState.Multiplayer_Countdown ||
                         // [추가된 상태들]
                         currentState == GameState.LevelUp ||
                         currentState == GameState.LootDrop ||
                         currentState == GameState.LootSummary);

        // 1. 내 화면 표시
        if (isInGame)
        {
            AddLog($"{playerNickname} : {message}", ConsoleColor.Yellow);
        }
        else
        {
            AddChatLog($"[{playerNickname}] {message}");
        }

        // 2. 패킷 전송
        var chatData = new ChatData { Nickname = playerNickname, Message = message };
        var packet = new Packet 
        { 
            Type = PacketType.Chat, 
            Data = JsonSerializer.Serialize(chatData) 
        };
        NetworkManager.Instance.Send(packet);
    }

// [신규] 채팅 로그 관리 (최대 10줄까지만 저장)
private void AddChatLog(string text)
{
    chatLog.Add(text);
    if (chatLog.Count > 22) // 높이에 맞춰 갯수 증가
    {
        chatLog.RemoveAt(0); 
    }
}

private void SendMyPosition()
    {
        var data = new MapMoveData { X = player.X, Y = player.Y };
        var packet = new Packet 
        { 
            Type = PacketType.MapMove, 
            Data = JsonSerializer.Serialize(data) 
        };
        NetworkManager.Instance.Send(packet);
    }

    private void HandleEnemyAction(string json)
    {
        var data = JsonSerializer.Deserialize<BattleActionData>(json);
        
        // 매니저가 패킷 종류(독, 기절, 공격 등)를 분석해서 처리
        MultiplayerBattleManager.OnReceiveEnemyAction(this, data);
        
        NetworkManager.Instance.IsDirty = true;
    }

    // [보조] 적 공격 종료 후 턴 재개 헬퍼
   public void ResumeTurnAfterEnemyAction()
    {
        isMonsterTurnInProgress = false;
        battleTurnCount = 0;
        myFleeRequest = false;
        otherFleeRequest = false;

        // 스킬 쿨타임 감소
        if (player != null) { foreach (var s in player.Skills) { if (s.CurrentCooldown > 0) s.CurrentCooldown--; } }

        bool amIDead = player.IsDead;
        bool isOtherDead = (otherPlayer != null && otherPlayer.IsDead);

        if (amIDead && isOtherDead)
        {
            currentState = GameState.GameOver;
            gameOverMenuIndex = 0;
            AddLog("전멸했습니다...", ConsoleColor.Red);
            return;
        }

        bool isFightingSolo = (otherPlayer == null || otherPlayer.IsDead);

        if (isFightingSolo) 
        {
            isMyBattleTurn = !player.IsDead; 
        }
        else 
        {
            if (amIDead) 
            {
                isMyBattleTurn = false; 
                // battleTurnCount++;  <-- [삭제] 죽은 사람은 행동 카운트에 포함 안 함 (목표치가 줄어드므로)
                AddLog("당신은 쓰러져 있어 움직일 수 없습니다.", ConsoleColor.DarkGray);
            }
            else if (isOtherDead)
            {
                isMyBattleTurn = true;  
                // battleTurnCount++;  <-- [삭제] 동료 몫을 미리 채우지 않음
                AddLog("동료는 쓰러져 있습니다. 당신의 턴!", ConsoleColor.Green);
            }
            else 
            {
                if (player.DEX > otherPlayer.DEX) isMyBattleTurn = true;
                else if (player.DEX < otherPlayer.DEX) isMyBattleTurn = false;
                else isMyBattleTurn = NetworkManager.Instance.IsHost;
            }
        }

        AddLog("==============================", ConsoleColor.DarkGray);
        if (isMyBattleTurn) AddLog("새 라운드! 당신의 턴입니다.", ConsoleColor.Green);
        else if (!amIDead && !isOtherDead && !isFightingSolo) AddLog("새 라운드! 동료의 턴입니다.", ConsoleColor.Yellow);
        
        NetworkManager.Instance.IsDirty = true;
    }
    private void HandleFleeRequest()
    {
        otherFleeRequest = true;
        AddLog($"'{otherPlayerNickname}'님이 후퇴를 제안했습니다!", ConsoleColor.Cyan);
        AddLog("동의하려면 [후퇴]를 선택하세요.");
        NetworkManager.Instance.IsDirty = true;
    }

    private void HandleBattleEnd()
    {
        // [핵심 수정] 내가 포탈 대기 중이라면 전투 종료 패킷 무시

        FleeBattle();
    }

    public void WinMultiplayerBattle()
    {
        // 기존 싱글플레이 승리 로직 재사용 (보상, 경험치 등)
        WinBattle();
    }

    private void EndMultiplayerBattle()
    {
        currentBattleMonster = null; 
        currentState = GameState.Multiplayer_World;
        
        battleTurnCount = 0;
        myFleeRequest = false;
        otherFleeRequest = false;
        
        lastMonsterMoveTime = DateTime.Now;

        NetworkManager.Instance.IsDirty = true;
    }

   private void UseMultiplayerItem(List<Consumable> distinctItems, int index)
{   
    if (index >= distinctItems.Count) { AddLog("아이템이 없습니다."); return; }
    Consumable itemToUse = distinctItems[index];

    // 아이템 사용 시도 (로컬 효과 적용 및 인벤토리 차감)
    bool success = player.UseConsumable(itemToUse.CType, itemToUse.Rarity, this);

    if (success)
    {
        // 1. 회복량 계산 (연출용)
        int healAmount = itemToUse.Value; // 기본값
        // 퍼센트 계산 로직은 Consumable.cs에 있으나 여기서는 근사값 또는 
        // UseConsumable이 리턴값을 주도록 수정하면 좋지만, 
        // 간단히 로그와 애니메이션을 위해 재계산하거나 고정값 사용.
        // 정확한 동기화를 위해 여기서는 애니메이션용으로 양수값만 보냄.

        var data = new BattleActionData { 
            ActionType = 2, 
            Damage = -healAmount, // 음수로 힐 표시
            SkillName = itemToUse.Name 
        };
        
        // 2. 패킷 전송
        NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleAction, Data = JsonSerializer.Serialize(data) });

        ConsoleColor color = (itemToUse.CType == ConsumableType.HealthPotion) ? ConsoleColor.Red : ConsoleColor.Blue;
        
        // 3. 로컬 애니메이션 및 턴 넘김
        StartBuffAnimation(player, healAmount, color, () => 
        { 
            EndMyTurn(); // 턴 종료 및 패킷 전송
        });
        
        currentState = GameState.Multiplayer_Battle;
    }
}

private void ProcessCountdownLogic()
    {
        if (!isCountdownStarted)
        {
            isCountdownStarted = true;
            countdownStartTime = DateTime.Now;
        }

        // 경과 시간 계산
        TimeSpan elapsed = DateTime.Now - countdownStartTime;
        int secondsPassed = (int)elapsed.TotalSeconds;
        int timeLeft = 5 - secondsPassed;

        if (timeLeft < 0) timeLeft = 0;
        currentCountdownNumber = timeLeft;

        // 0초가 되면 실제 게임 시작 로직 실행
        if (timeLeft == 0 && elapsed.TotalMilliseconds >= 5500) // 0초 보여주고 약간 뒤에 시작
        {
            StartMultiplayerGameReal();
        }
    }

    // [신규] 실제 게임 시작 (기존 CheckStartGame 내부 로직 이동)
    private void StartMultiplayerGameReal()
    {
        if (NetworkManager.Instance.IsHost)
        {
            int commonSeed = new Random().Next();
            InitializeMultiplayerGame(commonSeed);

            gameStartTime = DateTime.Now;

            var data = new MapInitData 
            { 
                Seed = commonSeed, 
                Stage = currentStage,
                HostX = player.X,
                HostY = player.Y,
                MapWidth = this.MapWidth,
                MapHeight = this.MapHeight
            };

            Thread.Sleep(100);
            
            var packet = new Packet 
            { 
                Type = PacketType.MapInit, 
                Data = JsonSerializer.Serialize(data) 
            };
            NetworkManager.Instance.Send(packet);

            currentState = GameState.StageIntro;
            stageIntroStartTime = DateTime.Now;
            SendMyPosition();
        }
        else
        {
            // 게스트는 MapInit 패킷을 기다리는 중...
            // (이미 받았을 수도 있음. HandleMapInit에서 상태를 StageIntro로 바꿔줄 것임)
            // 혹시 카운트다운 끝났는데 아직 맵 패킷이 안 왔으면 대기
        }
    }

    // [신규] 카운트다운 화면 그리기
    private void DrawMultiplayerCountdown()
    {
        // 1. 대기 화면(좌우 분할된 상태)을 배경으로 먼저 그림
        DrawMultiplayerReadyScreen();

        // --- 아트 데이터 가져오기 ---
        string[] gameArt = AsciiArt.GetGameTextArt();
        string[] startArt = AsciiArt.GetStartTextArt();
        
        // 카운트다운 숫자 (3초 이하는 빨간색)
        string numStr = currentCountdownNumber.ToString();
        ConsoleColor numColor = currentCountdownNumber <= 3 ? ConsoleColor.Red : ConsoleColor.White;
        
        // 숫자 아트는 딕셔너리에서 가져옴 (크기 계산용)
        var numLib = AsciiArt.GetNumberArtLibrary();
        string[] numArt = numLib.ContainsKey(numStr[0]) ? numLib[numStr[0]] : numLib['?'];

        // --- 위치 계산 (화면 정중앙 정렬) ---
        int centerX = screenWidth / 2;
        int centerY = screenHeight / 2;

        // 전체 컨텐츠의 높이 계산 (Game높이 + 간격 + Start높이 + 간격 + 숫자높이)
        int gap = 1; // 텍스트 간격
        int totalHeight = gameArt.Length + gap + startArt.Length + gap + 2 + numArt.Length; // +2는 숫자와 텍스트 사이 여백

        // 그리기 시작할 Y 좌표 (화면 중앙 - 전체 높이의 절반)
        int currentY = centerY - (totalHeight / 2);

        // 2. "GAME" 아트 그리기
        int gameWidth = 0;
        foreach (var line in gameArt) gameWidth = Math.Max(gameWidth, GetDisplayWidth(line));
        
        int gameX = centerX - (gameWidth / 2);
        for (int i = 0; i < gameArt.Length; i++)
        {
            DrawTextToBuffer(gameX, currentY + i, gameArt[i], ConsoleColor.Cyan, ConsoleColor.Black, true);
        }
        currentY += gameArt.Length + gap;

        // 3. "START" 아트 그리기
        int startWidth = 0;
        foreach (var line in startArt) startWidth = Math.Max(startWidth, GetDisplayWidth(line));

        int startX = centerX - (startWidth / 2);
        for (int i = 0; i < startArt.Length; i++)
        {
            DrawTextToBuffer(startX, currentY + i, startArt[i], ConsoleColor.Yellow, ConsoleColor.Black, true);
        }
        currentY += startArt.Length + gap + 2; // 숫자는 좀 더 띄움

        // 4. 카운트다운 숫자 그리기
        // DrawAsciiNumber는 X좌표가 시작점이므로, 숫자 너비의 절반만큼 왼쪽으로 이동해야 중앙에 옴
        // (숫자 1개라고 가정하고 대략 너비 6~7 계산)
        int numApproxWidth = 0;
        foreach(var line in numArt) numApproxWidth = Math.Max(numApproxWidth, GetDisplayWidth(line));
        
        int numX = centerX - (numApproxWidth / 2);
        
        DrawAsciiNumber(numX, currentY, numStr, numColor);
    }

    private void SendChestUpdate(Chest chest)
    {
        var data = new ChestUpdateData { X = chest.X, Y = chest.Y, IsOpen = chest.IsOpen };
        var packet = new Packet { Type = PacketType.ChestUpdate, Data = JsonSerializer.Serialize(data) };
        NetworkManager.Instance.Send(packet);
    }

    private void SendTrapUpdate(Trap trap)
    {
        var data = new TrapUpdateData { X = trap.X, Y = trap.Y, IsTriggered = trap.IsTriggered };
        var packet = new Packet { Type = PacketType.TrapUpdate, Data = JsonSerializer.Serialize(data) };
        NetworkManager.Instance.Send(packet);
    }

    private void HandleChestUpdate(string json)
    {
        var data = JsonSerializer.Deserialize<ChestUpdateData>(json);
        Chest? chest = chests.Find(c => c.X == data.X && c.Y == data.Y);
        
        if (chest != null)
        {
            // 상자를 '열린 상태'로 강제 변경 (Game 로직 우회)
            // Chest 클래스에 public으로 상태를 바꾸는 메서드가 없으므로,
            // Chest.cs에 'ForceOpen()' 같은 메서드를 추가하거나, 
            // 임시로 SetIsOpen을 public으로 바꾸거나, Reflection을 써야 함.
            // 여기서는 Chest.cs 수정을 제안합니다. (아래 3번 항목 참조)
            
            // *Chest.cs 수정을 가정하고 호출*
            chest.ForceOpen(this); 
            
            AddLog("동료가 상자를 열었습니다.");
            NetworkManager.Instance.IsDirty = true;
        }
    }

   private void HandleTrapUpdate(string json)
    {
        var data = JsonSerializer.Deserialize<TrapUpdateData>(json);
        Trap? trap = traps.Find(t => t.X == data.X && t.Y == data.Y);

        if (trap != null)
        {
            trap.ForceTrigger(this); // 여기서 맵 타일 갱신
            NetworkManager.Instance.IsDirty = true;
        }
    }

    private void SendMonsterDead(int x, int y)
    {
        var data = new MonsterDeadData { X = x, Y = y };
        var packet = new Packet 
        { 
            Type = PacketType.MonsterDead, 
            Data = JsonSerializer.Serialize(data) 
        };
        NetworkManager.Instance.Send(packet);
    }

    private void HandleMonsterDead(string json)
    {
        var data = JsonSerializer.Deserialize<MonsterDeadData>(json);
        
        // 1. 몬스터 삭제
        var target = monsters.FirstOrDefault(m => m.X == data.X && m.Y == data.Y);
        
        // (좌표 오차 보정 검색)
        if (target == null)
        {
            target = monsters.FirstOrDefault(m => Math.Abs(m.X - data.X) <= 1 && Math.Abs(m.Y - data.Y) <= 1);
        }

        if (target != null)
        {
            monsters.Remove(target);
            NetworkManager.Instance.IsDirty = true;
        }

        // [핵심 수정] 2. 해당 좌표에 '함정'이 있다면 같이 삭제 (안전장치)
        // 몬스터가 죽었다는 건 함정도 발동했다는 뜻이므로 강제 제거
        var trap = traps.FirstOrDefault(t => t.X == data.X && t.Y == data.Y);
        if (trap != null && !trap.IsTriggered)
        {
            trap.ForceTrigger(this);
            NetworkManager.Instance.IsDirty = true;

            // (호스트라면 다른 게스트에게도 전파 가능하지만, 현재는 불필요)
        }
    }

    private void ReturnToBattleState()
    {
        // 멀티플레이 연결 상태 확인
        if (NetworkManager.Instance.IsHost || NetworkManager.Instance.IsConnected)
        {
            currentState = GameState.Multiplayer_Battle;
        }
        else
        {
            currentState = GameState.Battle;
        }
    }

    private void FinishBattleResultSequence()
    {
        // 1. 싱글플레이
        if (!NetworkManager.Instance.IsConnected && !NetworkManager.Instance.IsHost)
        {
            currentBattleMonster = null; 
            currentState = GameState.World;
            return;
        }

        // [핵심 수정] TCP 패킷 뭉침 방지를 위한 미세 지연
        // (이전 패킷들과 섞이지 않도록 잠시 대기 후 전송)
        Thread.Sleep(100); 

        // 2. 종료 신호 전송
        var packet = new Packet { Type = PacketType.BattleResultFinished };
        NetworkManager.Instance.Send(packet);

        // 3. 솔로 모드(동료 부재/사망)인 경우 즉시 종료
        if (otherPlayer == null || otherPlayer.IsDead)
        {
            EndMultiplayerBattle();
            return;
        }

        // 4. 협동 모드라면 대기 상태 진입
        currentState = GameState.Multiplayer_BattleResultWait;
        CheckBattleResultSync();
    }

    // [신규] 두 명 다 끝났는지 확인하고 월드로 이동하는 메서드
    private void CheckBattleResultSync()
    {
        // 나는 대기 상태이고, 상대방도 끝났다는 신호가 왔다면
        if (currentState == GameState.Multiplayer_BattleResultWait && isOtherPlayerFinishedBattleResult)
        {
            // 플래그 초기화 (다음 전투를 위해)
            isOtherPlayerFinishedBattleResult = false;
            
            // 월드로 이동
            EndMultiplayerBattle(); 
        }
    }

    private void HandleBattleResultFinished()
    {
        // 1. 상대방이 끝났다는 사실 기록
        isOtherPlayerFinishedBattleResult = true;

        // [롤백 & 수정]
        // 내가 죽어있든 살았든, 내가 아이템 창을 보고 있다면 강제로 끄지 않습니다.
        // 내가 볼일을 다 보고 '대기 상태(Wait)'에 들어갔을 때, CheckBattleResultSync가 호출되어 같이 나가게 됩니다.
        
        // 단, 내가 '이미' 대기 상태라면 -> 이제 둘 다 끝났으므로 나감
        if (currentState == GameState.Multiplayer_BattleResultWait)
        {
            CheckBattleResultSync();
        }
        
        // 추가: 만약 내가 죽어있어서 '대기 상태' 화면을 보고 있다면?
        // -> 위 조건(currentState == Wait)에 걸려서 CheckBattleResultSync가 호출되고,
        //    EndMultiplayerBattle()이 실행되어 맵으로 나가집니다. (정상 작동)
    }

    private void DrawBattleResultWait()
    {
        // 배경은 전투 화면(BattleLayout)을 그대로 유지하거나 검은색으로 덮을 수 있음
        // 여기서는 깔끔하게 전투 화면 위에 팝업을 띄움
        DrawBattleLayout(); 

        int width = 40;
        int height = 8;
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
        DrawBox(startX, startY, width, height, "알림");

        string msg = "동료를 기다리는 중...";
        
        // 점(.) 애니메이션
        int dotCount = (DateTime.Now.Millisecond / 500) % 4; // 0~3
        string dots = new string('.', dotCount);

        DrawTextToBuffer(startX + width/2 - GetDisplayWidth(msg)/2, startY + 3, msg + dots, ConsoleColor.Yellow);
    }

    private bool IsSafeZone(int x, int y)
    {
        // 시작 방: X(2), Y(2), W(7), H(7) -> Right(9), Bottom(9)
        // 안전하게 경계선 포함해서 2~9 범위는 차단
        return (x >= 2 && x <= 9 && y >= 2 && y <= 9);
    }

    private void ResetMultiplayerData()
    {
        // [핵심] 채팅 기록 및 입력값 초기화
        chatLog.Clear();
        chatInput = "";
        isChatting = false;

        // 직업 선택 상태 초기화
        mpClassSelectedIndex = 0;
        mpIsConfirming = false;
        mpConfirmChoice = true;
        
        iSelectedClass = false;
        otherSelectedClass = false;
        
        // 상대방 정보 초기화
        otherPlayerNickname = "???";
        isOtherPlayerReady = false;
        
        // 에러 메시지 초기화
        createRoomError = "";
        joinPasswordError = "";
    }

    private void ProcessGameChatInput(ConsoleKeyInfo key)
    {
        // 1. 전송 (Enter)
        if (key.Key == ConsoleKey.Enter)
        {
            if (!string.IsNullOrWhiteSpace(gameChatInput))
            {
                SendChat(gameChatInput); // 기존 SendChat 재활용
            }
            gameChatInput = "";
            isGameChatting = false; // 채팅 종료
            return;
        }

        // 2. 취소 (ESC)
        if (key.Key == ConsoleKey.Escape)
        {
            gameChatInput = "";
            isGameChatting = false;
            return;
        }

        // 3. 백스페이스
        if (key.Key == ConsoleKey.Backspace)
        {
            if (gameChatInput.Length > 0)
                gameChatInput = gameChatInput.Substring(0, gameChatInput.Length - 1);
        }
        // 4. 글자 입력
        else if (!char.IsControl(key.KeyChar))
        {
            // 길이 제한 (화면 폭 고려)
            if (GetDisplayWidth(gameChatInput) < worldLogWidth - 10) 
                gameChatInput += key.KeyChar;
        }
    }

    private void ProceedToNextStageMultiplayer()
    {
        // 호스트가 주도적으로 다음 스테이지 맵 생성 및 전파
        if (NetworkManager.Instance.IsHost)
        {
            int nextStage = currentStage + 1;
            int commonSeed = rand.Next(); // 새 시드 생성

            TransitionToStage(nextStage, commonSeed);

            // 1. 맵 초기화 데이터 전송
            var data = new MapInitData 
            { 
                Seed = commonSeed, 
                Stage = nextStage, 
                HostX = player.X, 
                HostY = player.Y,

                // [신규] 맵 크기 동기화
                MapWidth = this.MapWidth,
                MapHeight = this.MapHeight
            };

            Thread.Sleep(100);
            
            NetworkManager.Instance.Send(new Packet { 
                Type = PacketType.MapInit, 
                Data = JsonSerializer.Serialize(data) 
            });
        }
        else
        {
            // 게스트는 호스트가 MapInit을 보낼 때까지 대기
            // (이미 PortalWait 상태라면 그대로 있고, 아니라면 대기 화면 띄움)
        }
    }


    private void SetupBattleIntro(Monster monster, bool isFromTrap)
    {
        battleIntroStartTime = DateTime.Now;

        // 1. 기본 설정 (창 개수, 텍스트, 색상, 등장 간격)
        int windowCount = 1;
        int spawnInterval = 0; 

        if (monster.Icon == 'B') // 보스
        {
            currentIntroTextArt = AsciiArt.GetFatalText();
            currentIntroColor = ConsoleColor.Red;
            isBossEncounter = true;
            windowCount = 15;
            spawnInterval = 150;

            currentIntroBlinkInterval = 200;
        }
        else if (monster.Icon == 'F') // 필드 보스
        {
            currentIntroTextArt = AsciiArt.GetWarningText();
            currentIntroColor = ConsoleColor.DarkYellow;
            isBossEncounter = true;
            windowCount = 5;
            spawnInterval = 300;

            currentIntroBlinkInterval = 300;
        }
        else if (isFromTrap) // 함정
        {
            currentIntroTextArt = AsciiArt.GetTrapText();
            currentIntroColor = ConsoleColor.Yellow;
            isBossEncounter = false;

            currentIntroBlinkInterval = 400;
        }
        else // 일반
        {
            currentIntroTextArt = AsciiArt.GetErrorText();
            currentIntroColor = ConsoleColor.Yellow;
            isBossEncounter = false;

            currentIntroBlinkInterval = 400;
        }

        // 2. 창 위치 및 등장 시간 생성
        if (introWindows == null) introWindows = new List<(int, int, int)>();
        introWindows.Clear();

        int contentW = Math.Max(
            AsciiArt.GetErrorIcon().Max(s => GetDisplayWidth(s)),
            currentIntroTextArt.Max(s => GetDisplayWidth(s))
        );
        int contentH = AsciiArt.GetErrorIcon().Length + 2 + currentIntroTextArt.Length;
        int boxW = contentW + 6;
        int boxH = contentH + 4;

        int minDistanceSq = 22 * 22;
        int maxDelay = 0;
        int quadrantIndex = rand.Next(0, 4);

        for (int i = 0; i < windowCount; i++)
        {
            int x, y, delay;

            if (i == 0) // 첫 번째 창 (중앙)
            {
                x = (screenWidth - boxW) / 2;
                y = (screenHeight - boxH) / 2;
                delay = 0;
            }
            else // 나머지 (랜덤)
            {
                int attempts = 0;
                int halfW = screenWidth / 2;
                int halfH = screenHeight / 2;

                do
                {
                    int minX = 2, maxX = screenWidth - boxW - 2;
                    int minY = 2, maxY = screenHeight - boxH - 2;

                    switch (quadrantIndex % 4)
                    {
                        case 0: maxX = halfW - (boxW / 4); maxY = halfH - (boxH / 4); break;
                        case 1: minX = halfW - (boxW * 3 / 4); maxY = halfH - (boxH / 4); break;
                        case 2: maxX = halfW - (boxW / 4); minY = halfH - (boxH * 3 / 4); break;
                        case 3: minX = halfW - (boxW * 3 / 4); minY = halfH - (boxH * 3 / 4); break;
                    }

                    if (minX >= maxX) minX = maxX - 1;
                    if (minY >= maxY) minY = maxY - 1;

                    x = rand.Next(Math.Max(2, minX), Math.Max(3, maxX));
                    y = rand.Next(Math.Max(2, minY), Math.Max(3, maxY));

                    bool tooClose = false;
                    foreach (var w in introWindows)
                    {
                        int dx = x - w.x;
                        int dy = y - w.y;
                        int distSq = (dx * dx) + ((dy * 2) * (dy * 2));

                        if (distSq < minDistanceSq)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (!tooClose) break;
                    attempts++;
                    if (attempts % 10 == 0) quadrantIndex++;

                } while (attempts < 50);

                delay = 1500 + ((i - 1) * spawnInterval);
                quadrantIndex++;
            }

            introWindows.Add((x, y, delay));
            if (delay > maxDelay) maxDelay = delay;
        }

        // 유지 시간 설정
        int holdTime;
        if (monster.Icon == 'B') holdTime = 0;
        else if (monster.Icon == 'F') holdTime = 1000;
        else holdTime = 2000;

        currentBattleIntroDuration = 500 + maxDelay + holdTime;
    }

    public void UpdateLastBattleActionTime()
    {
        lastBattleActionTime = DateTime.Now;
    }

    // 1. 함정 발동 상태 전송 (Trap.cs에서 호출)
    public void SendTrapUpdatePacket(Trap trap)
    {
        if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
        {
            // (기존 SendTrapUpdate 로직 재활용 또는 직접 구현)
            Thread.Sleep(50);
            var data = new TrapUpdateData { X = trap.X, Y = trap.Y, IsTriggered = trap.IsTriggered };
            var packet = new Packet { Type = PacketType.TrapUpdate, Data = JsonSerializer.Serialize(data) };
            NetworkManager.Instance.Send(packet);
        }
    }

    // 2. [핵심] 함정 데미지 처리 및 동기화 (Trap.cs에서 호출)
    public void OnTrapDamageTaken()
    {
        // A. 멀티플레이 동기화 (내 체력이 깎였음을 알림)
        if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
        {
            Thread.Sleep(50);
            SendMyPlayerInfo(); 
        }

        // B. 사망 체크
        if (player.IsDead)
        {
            AddLog("치명상을 입고 쓰러졌습니다...", ConsoleColor.Red);
            
            // 멀티플레이 여부 확인
            bool isMulti = NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost;
            bool isOtherDead = (otherPlayer != null && otherPlayer.IsDead);

            // [핵심] 게임 오버 분기
            if (isMulti)
            {
                if (isOtherDead)
                {
                    // 둘 다 죽었으므로 게임 오버 (RunGameLoop에서 감지됨)
                    // (즉시 반영을 위해 여기서 상태 변경해도 됨)
                    currentState = GameState.GameOver;
                    gameOverMenuIndex = 0; 
                }
                else
                {
                    // 나만 죽음 -> 관전 모드 (조작 불가, 회색 변환은 렌더링에서 자동 처리됨)
                    // (별도 상태 전환 없이 World 상태 유지하되 조작만 막힘)
                }
            }
            else
            {
                // 싱글플레이 -> 즉시 게임 오버
                currentState = GameState.GameOver;
                gameOverMenuIndex = 0; 
            }
        }
    }

    private void DrawDirectIpWindow()
    {
        int width = 50, height = 12;
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
        DrawBox(startX, startY, width, height, "IP 직접 접속");

        string msg = "호스트의 주소를 입력하세요. (IP:Port)";
        DrawTextToBuffer(startX + 2, startY + 2, msg, ConsoleColor.Gray);

        // 입력창
        string indicator = isEnteringIp ? "► " : "  ";
        ConsoleColor color = isEnteringIp ? ConsoleColor.Yellow : ConsoleColor.White;
        DrawTextToBuffer(startX + 4, startY + 4, $"{indicator}Address: {directIpInput}_", color);

        // 에러 메시지
        if (!string.IsNullOrEmpty(directIpError))
        {
            DrawTextToBuffer(startX + 4, startY + 6, directIpError, ConsoleColor.Red);
        }

        // 버튼
        int btnY = startY + 8;
        string connect = " [접속] ";
        string cancel = " [취소] ";
        
        bool btnActive = !isEnteringIp;
        ConsoleColor connBg = (btnActive && directIpBtnIndex == 0) ? ConsoleColor.Green : ConsoleColor.Black;
        ConsoleColor connFg = (btnActive && directIpBtnIndex == 0) ? ConsoleColor.Black : ConsoleColor.White;
        ConsoleColor cancBg = (btnActive && directIpBtnIndex == 1) ? ConsoleColor.Red : ConsoleColor.Black;
        ConsoleColor cancFg = (btnActive && directIpBtnIndex == 1) ? ConsoleColor.Black : ConsoleColor.White;

        int totalW = GetDisplayWidth(connect) + GetDisplayWidth(cancel) + 4;
        int btnX = startX + width / 2 - totalW / 2;

        DrawTextToBuffer(btnX, btnY, connect, connFg, connBg);
        DrawTextToBuffer(btnX + GetDisplayWidth(connect) + 4, btnY, cancel, cancFg, cancBg);
    }

    private void ProcessDirectIpInput(ConsoleKeyInfo key)
    {
        // 취소 (ESC)
        if (key.Key == ConsoleKey.Escape)
        {
            currentState = GameState.Multiplayer_Lobby;
            return;
        }

        // 탭/방향키 상하: 포커스 전환
        if (key.Key == ConsoleKey.Tab || key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.DownArrow)
        {
            isEnteringIp = !isEnteringIp;
            return;
        }

        // 버튼 조작
        if (!isEnteringIp)
        {
            if (key.Key == ConsoleKey.LeftArrow || key.Key == ConsoleKey.RightArrow)
            {
                directIpBtnIndex = (directIpBtnIndex + 1) % 2;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                if (directIpBtnIndex == 0) TryConnectDirectIp();
                else currentState = GameState.Multiplayer_Lobby;
            }
            return;
        }

        // 텍스트 입력
        if (key.Key == ConsoleKey.Enter)
        {
            TryConnectDirectIp();
        }
        else if (key.Key == ConsoleKey.Backspace)
        {
            if (directIpInput.Length > 0) 
            {
                directIpInput = directIpInput.Substring(0, directIpInput.Length - 1);
                directIpError = "";
            }
        }
        else if (!char.IsControl(key.KeyChar) && directIpInput.Length < 25)
        {
            directIpInput += key.KeyChar;
            directIpError = "";
        }
    }

    // [신규] IP 파싱 및 접속 시도
    private void TryConnectDirectIp()
    {
        if (string.IsNullOrWhiteSpace(directIpInput))
        {
            directIpError = "주소를 입력하세요.";
            return;
        }

        string ip = directIpInput;
        int port = 0;

        // 포트 분리 (예: 127.0.0.1:7777)
        if (directIpInput.Contains(":"))
        {
            var parts = directIpInput.Split(':');
            ip = parts[0];
            if (parts.Length > 1 && int.TryParse(parts[1], out int p))
            {
                port = p;
            }
        }
        else
        {
            // 포트를 입력하지 않았으면 기본 포트 사용 (또는 에러 처리)
            // 여기서는 코드에 고정된 포트가 없으면 접속이 불가능하므로,
            // NetworkManager.StartHost()에서 포트를 고정(예: 7777)하거나
            // 유저가 반드시 포트를 입력하게 해야 합니다.
            // 일단 에러 메시지를 띄웁니다.
            directIpError = "포트를 포함해주세요 (IP:Port)";
            return;
        }

        NetworkManager.Instance.StopSearchingRooms();
        AddLog($"접속 시도: {ip}:{port}");

        bool success = NetworkManager.Instance.ConnectToHost(ip, port);
        
        if (success)
        {
            ResetMultiplayerData();
            
            // [핵심 수정] 바로 입장하지 않고 방 정보 요청
            NetworkManager.Instance.Send(new Packet { Type = PacketType.RoomInfoRequest });
            
            // 대기 상태로 전환
            currentState = GameState.Multiplayer_DirectConnect_Wait;
        }
        else
        {
            directIpError = "접속 실패. 주소를 확인하세요.";
            NetworkManager.Instance.StartSearchingRooms(); // 검색 재개
        }
    }

    private string GetLocalIPAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1"; // 실패 시 루프백
    }

    private void HandleRoomInfoRequest()
    {
        if (myHostingRoom != null)
        {
            // 현재 인원수 갱신 후 전송
            // (이미 접속된 상태이므로 CurrentPlayers는 2명일 것임)
            string json = RoomInfo.Serialize(myHostingRoom);
            NetworkManager.Instance.Send(new Packet { Type = PacketType.RoomInfoResponse, Data = json });
        }
    }

    // 2. 게스트: 방 정보 수신 -> 비밀번호 체크 or 입장
    private void HandleRoomInfoResponse(string json)
    {
        RoomInfo room = RoomInfo.Deserialize(json);
        pendingJoinRoom = room; // 입장 대기 방으로 설정

        if (room.IsPrivate)
        {
            // 비밀번호가 있다면 입력창으로 이동
            joinPasswordInput = "";
            joinPasswordError = "";
            isEnteringJoinPw = true;
            currentState = GameState.Multiplayer_PasswordInput;
        }
        else
        {
            // 공개방이면 바로 입장
            ConnectToRoom(room);
        }
    }

    private void DrawDirectConnectWait()
    {
        DrawMultiplayerLobby(); // 배경
        
        int width = 40, height = 8;
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;

        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black);
        DrawBox(startX, startY, width, height, "연결 중");
        
        string msg = "방 정보를 확인하고 있습니다...";
        DrawTextToBuffer(startX + 2, startY + 3, msg, ConsoleColor.White);
    }

    private void SendChestBusy(Chest chest, bool isBusy)
    {
        var data = new ChestBusyData { X = chest.X, Y = chest.Y, IsBusy = isBusy };
        var packet = new Packet { Type = PacketType.ChestBusy, Data = JsonSerializer.Serialize(data) };
        NetworkManager.Instance.Send(packet);
    }

    // 2. 점유 상태 수신
    private void HandleChestBusy(string json)
    {
        var data = JsonSerializer.Deserialize<ChestBusyData>(json);
        
        // 해당 좌표의 상자를 찾음
        Chest? chest = chests.Find(c => c.X == data.X && c.Y == data.Y);
        
        if (chest != null)
        {
            chest.IsBusy = data.IsBusy;
            
            // (선택사항) 시각적 피드백을 위해 로그 출력? (너무 자주 뜨면 방해되니 생략)
            // if (chest.IsBusy) AddLog("동료가 상자를 살펴보고 있습니다.");
        }
    }

    private void ForceCloseChestUI()
    {
        // 상자 관련 상태일 때만 작동
        if (currentState == GameState.Chest_Confirm || currentState == GameState.Chest_Opening)
        {
            // 점유 해제 및 패킷 전송
            if (currentTargetChest != null)
            {
                currentTargetChest.IsBusy = false;
                
                if (NetworkManager.Instance.IsConnected || NetworkManager.Instance.IsHost)
                {
                    var data = new ChestBusyData { X = currentTargetChest.X, Y = currentTargetChest.Y, IsBusy = false };
                    var packet = new Packet { Type = PacketType.ChestBusy, Data = JsonSerializer.Serialize(data) };
                    NetworkManager.Instance.Send(packet);
                }
            }
            
            // 데이터 초기화
            currentTargetChest = null;
            
            // 로그 알림
            AddLog("전투가 발생하여 상자 열기가 취소되었습니다!");
        }
    }

    private bool IsInPortalRange(Player p)
    {
        if (p == null || portalPosition.x == -1) return false;

        int dx = p.X - portalPosition.x;
        int dy = p.Y - portalPosition.y;
        int distSq = dx * dx + dy * dy;

        return distSq <= PORTAL_DETECTION_RANGE_SQ;
    }

    // [핵심] 멀티플레이 포탈 이동 조건 체크 (호스트 전용)
    private void CheckMultiplayerPortalCondition()
    {
        // 호스트만 판정
        if (!NetworkManager.Instance.IsHost) return;
        
        if (currentStage >= 3) return; 

        // [핵심 수정] "내가 죽었거나" 포탈 범위에 있으면 OK
        bool amIReady = player.IsDead || IsInPortalRange(player);
        
        bool otherReady = false;
        if (otherPlayer != null)
        {
            // [핵심 수정] "동료가 죽었거나" 포탈 범위에 있으면 OK
            otherReady = otherPlayer.IsDead || IsInPortalRange(otherPlayer);
        }

        // [안전장치] 둘 다 죽은 상태에서 이동하는 걸 막기 위해(게임오버겠지만),
        // 적어도 한 명은 '살아서 포탈 범위 내에' 있어야 한다는 조건 추가
        bool anyoneActiveAtPortal = (IsInPortalRange(player) && !player.IsDead) || 
                                    (otherPlayer != null && IsInPortalRange(otherPlayer) && !otherPlayer.IsDead);

        // [조건 충족]
        if (amIReady && otherReady && anyoneActiveAtPortal)
        {
            AddLog("다음 스테이지로 이동합니다!");
            ProceedToNextStageMultiplayer();
        }
    }
    
    #endregion

    #endregion
}