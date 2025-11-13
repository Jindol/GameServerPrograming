// Game.cs
using System.Text;
using System.Threading; 
using System.Collections.Generic; 
using System.Linq; 

public class Game
{
    // 맵 생성 설정
    private int MapWidth; 
    private int MapHeight;


    // 게임 상태
    internal enum GameState
    {
        MainMenu,         // [신규] 메인 메뉴
        HowToPlay,        // [신규] 조작법
        World,  // 맵 탐험
        Battle, // 전투 메인 메뉴
        Battle_SkillSelect, // 전투 스킬 선택
        Battle_ItemMenu,  // 전투 아이템 (메인)
        Battle_ItemSubMenu, // 전투 아이템 (서브: 등급 선택)
        LootDrop,         // "장비 비교" 창
        LevelUp, //레벨 업 창
        LootSummary,      // "획득 아이템 목록" 창
        Inventory,        // 인벤토리 확인 창
        CharacterStat,
        GameOver, // 게임 오버
        Pause,
        Battle_Animation, // [신규] 전투 애니메이션 재생
        Chest_Confirm,    // 1. 열기 확인 창
        Chest_Opening,    // 2. 열기 애니메이션
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

    // 게임 데이터
    private Player player = null!;
    private List<Monster> monsters = null!;
    private char[,] map = null!;
    private List<string> logMessages = new List<string>();
    private List<Rectangle> rooms = new List<Rectangle>(); 
    private Rectangle bossRoom; 
    private List<Trap> traps = null!;
    private List<Chest> chests = null!; // [신규]

    private int currentStage = 1; // 1, 2, 3
    private (int x, int y) portalPosition = (-1, -1); // (-1, -1) = 포탈 없음

    // 상태 관리 변수
    private GameState currentState = GameState.MainMenu; // [수정] 시작 상태를 World -> MainMenu로
    private Monster? currentBattleMonster = null;
    private Random rand = new Random(); 
    
    // [신규] 1번 요청 (함정 전투)
    private bool isTrapBattle = false; 
    
    // [신규] 3번 요청 (몬스터 추적)
    private const int MONSTER_AGGRO_RANGE_SQUARED = 100; // (10*10 타일)
    
    // [신규] 4번 요청 (크리티컬/MISS)
    private bool lastAttackWasCrit = false;
    private bool lastAttackWasMiss = false;
    private const int ANIM_BLINK_DURATION_MS = 100; 
    private const int ANIM_TOTAL_BLINKS = 4; 
    private int currentBlinkCount = 0; 
    private DateTime nextBlinkTime = DateTime.MinValue;
    private Action? animationCallback = null; 
    private object? currentAnimationTarget = null; 

    // [신규] "야매" 오버레이 제어용 변수
    private bool showHitOverlay = false;       // true일 때 오버레이를 그림
    private int currentAnimationDamage = 0;  // 데미지 숫자를 임시 저장 
    
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
    private const double PLAYER_DEX_EVASION_SCALER = 0.005; // (DEX 1당 0.5%)
    private const double MONSTER_EVASION_CHANCE = 0.05;

    // [신규] 몬스터 레벨 스케일링 배율 (플레이어 레벨 1당 20%씩 강해짐)
    private const double MONSTER_SCALING_FACTOR = 0.20;

    //방어력 공식을 위한 상수 (방어력 K=50일 때 50% 감소)
    private const int DEFENSE_CONSTANT = 50;

    // [신규] 상자 열기 연출용 변수
    private Chest? currentTargetChest = null;         // 현재 열려고 하는 상자
    private DateTime chestAnimStartTime = DateTime.MinValue; // 애니메이션 시작 시간
    private const int CHEST_ANIM_FLASH_MS = 200;  // 0.2초간 노랗게 빛남
    private const int CHEST_ANIM_TOTAL_MS = 600;  // 0.6초 후 결과 창으로

    private int mainMenuIndex = 0; // 0=시작, 1=조작법, 2=종료
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


    // (Start, ChooseClass, InitConsole... 변경 없음)
    #region Game_Initialization
    public bool Start()
    {
        // 1. 콘솔 초기화
        InitializeConsole();

        // 2. 상태 변수 초기화
        gameRunning = true;
        needsRestart = false;
        logMessages.Clear();

        // 3. 메인 메뉴
        currentState = GameState.MainMenu;
        mainMenuIndex = 0;

        // 4. [수정] 플레이어 생성 및 데이터 로드는 '게임 시작' 선택 시로 이동

        // 5. 메인 루프 시작
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
    private void TransitionToStage(int stage)
    {
        currentStage = stage;
        portalPosition = (-1, -1); // 포탈 초기화

        InitializeGameData(); // 몬스터, 방 등 모든 리스트 초기화
        InitializeMap();      // 현재 currentStage에 맞게 맵 생성

        // 맵 생성 후 플레이어 스폰 위치 설정 (맵이 없으면 오류)
        if (rooms.Count > 1) { (player.X, player.Y) = rooms[1].Center; }
        else if (rooms.Count > 0) { (player.X, player.Y) = rooms[0].Center; }
        else { (player.X, player.Y) = (MapWidth / 2, MapHeight / 2); } // (비상 스폰)

        AddLog($"Stage {currentStage}에 입장했습니다.");
        currentState = GameState.World;
    }
    
    private PlayerClass ChooseClass()
    {
        // 1. 각 직업의 설명 데이터
        var classInfos = new[] {
            new { 
                Name = "Warrior (시스템 방어자)", 
                Desc = "높은 체력과 방어력을 가진 탱커", 
                Skills = "주요 스킬: 파워 스트라이크, 방패 치기, 처형" 
            },
            new { 
                Name = "Wizard (버그 수정자)", 
                Desc = "강력한 주문으로 적의 방어를 무시하는 폭발형 딜러", 
                Skills = "주요 스킬: 파이어볼, 힐, 메테오" 
            },
            new { 
                Name = "Rogue (정보 수집가)", 
                Desc = "민첩함과 치명타, 그리고 지속 피해에 지속형 딜러", 
                Skills = "주요 스킬: 백스탭, 독 찌르기, 파열" 
            }
        };

        // 2. 선택 상태 변수
        int selectedIndex = 0; // 0=전사, 1=마법사, 2=도적
        bool isConfirming = false;
        bool confirmChoice = true; // true=예, false=아니요

        // 3. 선택 화면 전용 루프
        while (true)
        {
            // --- 3A. 렌더링 --- [!!! 여기가 수정되었습니다 !!!]
            ClearBuffer();
            
            // [신규] 아스키 아트 제목 그리기
            string[] titleArt = AsciiArt.GetChooseClassTitleArt();
            int titleHeight = titleArt.Length;
            int titleWidth = 0;
            foreach(string line in titleArt) { titleWidth = Math.Max(titleWidth, GetDisplayWidth(line)); }
            
            int titleX = screenWidth / 2 - titleWidth / 2;
            int titleY = 1; // 화면 상단에서 2칸 아래
            
            for(int i=0; i<titleHeight; i++)
            {
                DrawTextToBuffer(titleX, titleY + i, titleArt[i], ConsoleColor.White, ConsoleColor.Black, true);
            }

            // 3열 레이아웃 계산
            int boxWidth = Math.Max(35, screenWidth / 3); 
            int totalWidth = boxWidth * 3;
            int startX = screenWidth / 2 - totalWidth / 2;
            
            int artHeight = 20; 
            int descHeight = 6; 
            int boxHeight = artHeight + descHeight + 3; 
            
            // [수정] boxY 위치를 아스키 아트 제목 바로 아래로 조정
            int boxY = titleY + titleHeight + 2; // 제목 + 2칸 패딩

            for (int i = 0; i < 3; i++)
        {
            bool isSelected = (i == selectedIndex);
            ConsoleColor boxColor = isSelected ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            
            int boxX = startX + (i * boxWidth);
            int currentBoxWidth = (i == 2) ? (screenWidth - boxX - 1) : boxWidth;
            
            DrawBox(boxX, boxY, currentBoxWidth, boxHeight, "CLASS"); 

            // --- [핵심 수정] ---
            
            // 1. 현재 직업과 아트 가져오기
            PlayerClass currentClass = (PlayerClass)i;
            string[] art = AsciiArt.GetPlayerArt(currentClass);

            int artActualMaxWidth = 0;
            foreach(string line in art) { artActualMaxWidth = Math.Max(artActualMaxWidth, GetDisplayWidth(line)); }
            
            // 1. '자동' 중앙 정렬 위치 계산
            int baseArtStartX = boxX + (currentBoxWidth / 2) - (artActualMaxWidth / 2);
            int baseArtStartY = boxY + 2; 

            // 2. '수동' 비율(%) 오프셋 값 가져오기 (AsciiArt.cs에서)
            (float percentX, float percentY) = AsciiArt.GetClassSelectOffset(currentClass);

            // 3. [신규] 비율(%)을 실제 픽셀(칸) 오프셋으로 변환
            // X 오프셋 = (박스 너비 * X비율)
            // Y 오프셋 = (아트 높이 * Y비율)
            int offsetX = (int)(currentBoxWidth * percentX);
            int offsetY = (int)(artHeight * percentY);

            // 4. 최종 위치 = 자동 위치 + 수동 오프셋
            int artStartX = baseArtStartX + offsetX;
                int artStartY = baseArtStartY + offsetY;
            
                // 직업별 아트 색상
                ConsoleColor artColor = ConsoleColor.Green; // 기본
                if ((PlayerClass)i == PlayerClass.Wizard) artColor = ConsoleColor.Cyan;
                else if ((PlayerClass)i == PlayerClass.Rogue) artColor = ConsoleColor.Yellow;

                for(int j=0; j<art.Length; j++) {
                    if (artStartY + j < boxY + artHeight) 
                        DrawTextToBuffer(artStartX, artStartY + j, art[j], artColor, ConsoleColor.Black, true);
                }

                // 직업 이름 및 설명
                int descY = boxY + artHeight + 1;
                DrawTextToBuffer(boxX + 2, descY, classInfos[i].Name, boxColor);
                DrawTextToBuffer(boxX + 2, descY + 2, classInfos[i].Desc, ConsoleColor.White);
                DrawTextToBuffer(boxX + 2, descY + 3, classInfos[i].Skills, ConsoleColor.DarkGray);

                // 선택 버튼
                if (isSelected) {
                    string selectText = "[ Enter: 선택 ]";
                    DrawTextToBuffer(boxX + (boxWidth / 2) - (GetDisplayWidth(selectText) / 2), boxY + boxHeight - 2, selectText, ConsoleColor.Yellow);
                }
            }
            
            // 하단 조작키
            DrawTextToBuffer(screenWidth / 2 - 10, boxY + boxHeight + 1, "[←] [→] or [1],[2],[3] : 이동", ConsoleColor.White);

            // 확인 창 그리기
            if (isConfirming)
            {
                // (새로 추가할 헬퍼 메서드)
                DrawClassConfirmation(classInfos[selectedIndex].Name, confirmChoice);
            }

            PrintBufferToConsole();

            // --- 3B. 입력 처리 ---
            ConsoleKeyInfo key = Console.ReadKey(true);

            if (isConfirming) // "정말 선택하시겠습니까?" 확인 창
            {
                // 1. key.Key 확인 (영문 모드 + Non-IME)
                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.RightArrow:
                        confirmChoice = !confirmChoice; // 예/아니요 토글
                        break; // UI만 업데이트
                    case ConsoleKey.Enter:
                        if (confirmChoice) return (PlayerClass)selectedIndex;
                        else isConfirming = false;
                        break;
                    case ConsoleKey.Y:
                        return (PlayerClass)selectedIndex; // "예"
                    case ConsoleKey.N:
                    case ConsoleKey.Escape:
                        isConfirming = false; // "아니요"
                        break;
                }

                // 2. key.KeyChar 확인 (한글 모드 Fallback)
                char c = char.ToUpper(key.KeyChar);
                if (c == 'Y' || c == 'ㅛ')
                {
                    return (PlayerClass)selectedIndex; // "예"
                }
                else if (c == 'N' || c == 'ㅜ')
                {
                    isConfirming = false; // "아니요"
                }
            }
            else // isConfirming == false (클래스 선택 중)
            {
                if (key.Key == ConsoleKey.LeftArrow)
                {
                    selectedIndex = (selectedIndex - 1 + 3) % 3; // (0 -> 2 -> 1 -> 0)
                }
                else if (key.Key == ConsoleKey.RightArrow)
                {
                    selectedIndex = (selectedIndex + 1) % 3; // (0 -> 1 -> 2 -> 0)
                }
                else if (key.Key == ConsoleKey.D1 || key.Key == ConsoleKey.NumPad1) // [신규]
                {
                    selectedIndex = 0; // 전사
                }
                else if (key.Key == ConsoleKey.D2 || key.Key == ConsoleKey.NumPad2) // [신규]
                {
                    selectedIndex = 1; // 마법사
                }
                else if (key.Key == ConsoleKey.D3 || key.Key == ConsoleKey.NumPad3) // [신규]
                {
                    selectedIndex = 2; // 도적
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    isConfirming = true;
                    confirmChoice = true; // "예"에 기본 포커스
                }
            }
        } // end while(true)
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
   private void InitializeMap()
    {
        // 0. 리스트 초기화 (TransitionToStage에서 이미 처리됨)
        map = new char[MapWidth, MapHeight]; 
        rooms.Clear();
        monsters.Clear();
        traps.Clear(); 
        chests.Clear(); 
        for (int y = 0; y < MapHeight; y++){
            for (int x = 0; x < MapWidth; x++){ map[x, y] = '█'; }
        }

        // [신규] 1. 스테이지별 맵 생성 파라미터 (Request 6)
        int maxRooms, minRoomSize, maxRoomSize, monsterCount, damageTraps, battleTraps, chestCount;
        double lRoomChance = 0.25; // L자 방 확률

        switch (currentStage)
        {
            case 2: // Stage 2: 데이터 동굴 (복잡, 좁음)
                maxRooms = 40; minRoomSize = 6; maxRoomSize = 12;
                monsterCount = 25; damageTraps = 20; battleTraps = 15; chestCount = 7;
                lRoomChance = 0.50; // L자 방이 더 자주 나옴
                break;
            case 3: // Stage 3: 커널 코어 (위험, 큼)
                maxRooms = 20; minRoomSize = 15; maxRoomSize = 25;
                monsterCount = 15; damageTraps = 10; battleTraps = 20; chestCount = 10;
                lRoomChance = 0.10; // 크고 넓은 방 위주
                break;
            case 1: // Stage 1: ASCII 미궁 (기본)
            default:
                maxRooms = 30; minRoomSize = 10; maxRoomSize = 20;
                monsterCount = 20; damageTraps = 15; battleTraps = 10; chestCount = 5;
                lRoomChance = 0.25;
                break;
        }

        // 2. 보스 방 생성 (동일)
        int bossRoomW = Math.Min(25, MapWidth / 4);
        int bossRoomH = Math.Min(15, MapHeight / 2);
        int bossRoomX = MapWidth - bossRoomW - 2; 
        int bossRoomY = (MapHeight - bossRoomH) / 2;
        bossRoom = new Rectangle(bossRoomX, bossRoomY, bossRoomW, bossRoomH);
        CreateRoom(bossRoom, rand, false);
        rooms.Add(bossRoom);

        // 3. 일반 방 생성 (수정된 파라미터 사용)
        for (int i = 0; i < maxRooms - 1; i++) 
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

                if (rand.NextDouble() < lRoomChance) // [수정] L자 방 확률
                {
                    isLShaped = true;
                    int w1 = rand.Next(minRoomSize, maxRoomSize); 
                    int h1 = rand.Next(minRoomSize, maxRoomSize);
                    int x1 = rand.Next(1, MapWidth - w1 - 1);
                    int y1 = rand.Next(1, MapHeight - h1 - 1);
                    newRoom = new Rectangle(x1, y1, w1, h1);

                    int w2, h2, x2, y2;
                    if (rand.Next(0, 2) == 0) // 가로 팔
                    {
                        w2 = rand.Next(minRoomSize / 2, maxRoomSize); 
                        h2 = h1; 
                        x2 = (rand.Next(0, 2) == 0) ? (x1 - w2) : (x1 + w1); 
                        y2 = y1;
                    }
                    else // 세로 팔
                    {
                        w2 = w1; 
                        h2 = rand.Next(minRoomSize / 2, maxRoomSize);
                        x2 = x1;
                        y2 = (rand.Next(0, 2) == 0) ? (y1 - h2) : (y1 + h1);
                    }
                    attachedRoom = new Rectangle(x2, y2, w2, h2);

                    // 1. 맵 경계 밖
                    if (attachedRoom.Left < 1 || attachedRoom.Right >= MapWidth - 1 ||
                        attachedRoom.Top < 1 || attachedRoom.Bottom >= MapHeight - 1)
                    {
                        overlap = true;
                        continue;       
                    }
                    // 2. 다른 방과 겹침
                    if (rooms.Any(r => r.Intersects(newRoom) || r.Intersects(attachedRoom)))
                    {
                        overlap = true;
                        continue;
                    }
                }
                else // [수정] 일반 방
                {
                    int w = rand.Next(minRoomSize, maxRoomSize + 1);
                    int h_variation = (int)(w * 0.5);
                    int h = rand.Next(Math.Max(minRoomSize, w - h_variation), 
                                     Math.Min(maxRoomSize, w + h_variation) + 1);
                    
                    int x = rand.Next(1, MapWidth - w - 1);
                    int y = rand.Next(1, MapHeight - h - 1);
                    newRoom = new Rectangle(x, y, w, h);
                    
                    if (rooms.Any(r => r.Intersects(newRoom)))
                    {
                        overlap = true;
                        continue;
                    }
                }
                
                // (attempts++가 여기 있었음)

            } while (overlap && attempts < 100); // 100회 시도

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

        // 3. 맵 구조 정리
        var boss = rooms[0];
        rooms = rooms.Skip(1).OrderBy(r => r.Center.x).ToList();
        rooms.Insert(0, boss); 
        rooms.Add(boss); 

        // 4. 터널 생성
        for (int i = 1; i < rooms.Count; i++) {
            var (prevX, prevY) = rooms[i - 1].Center;
            var (currX, currY) = rooms[i].Center;
            CreateHorizontalTunnel(prevX, currX, prevY);
            CreateVerticalTunnel(prevY, currY, currX);
        }
        
        // 5. 스폰
        (int bossX, int bossY) = bossRoom.Center;
        // [수정] 현재 스테이지의 보스 생성
        monsters.Add(MonsterDB.CreateBoss(bossX, bossY, currentStage));
        
        SpawnTraps(TrapType.Damage, '^', damageTraps);
        SpawnTraps(TrapType.Battle, '*', battleTraps);
        SpawnMonsters(monsterCount);
        SpawnChests(chestCount);
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
            if (!allowInBossRoom && bossRoom.Contains(x, y)) { continue; }
        }
        while (map[x, y] != '.' || // [핵심] '바닥'이어야 함
               (player != null && player.X == x && player.Y == y) ||
               (monsters != null && monsters.Any(m => m.X == x && m.Y == y)) ||
               (traps != null && traps.Any(t => t.X == x && t.Y == y)) ||
               (chests != null && chests.Any(c => c.X == x && c.Y == y))); // [핵심]
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
    
    // [신규] 상자 스폰
   private void SpawnChests(int count)
    {
        int chestsSpawned = 0;
        
        // --- [핵심 수정] ---
        // 'r != bossRoom' (에러) 대신 '!r.Equals(bossRoom)' (정상) 사용
        foreach (var room in rooms.Skip(1).Where(r => !r.Equals(bossRoom)))
        {
            if (chestsSpawned >= count) break;

            // 50% 확률로 이 방에 상자를 스폰
            if (rand.NextDouble() < 0.5)
            {
                // 1. 방의 구석을 찾음
                var corner = GetRandomCornerInRoom(room);
                if (corner.HasValue)
                {
                    var (x, y) = corner.Value;
                    // 2. 해당 위치가 다른 개체와 겹치지 않는지 최종 확인
                    if (map[x, y] == '.' && !chests.Any(c => c.X == x && c.Y == y))
                    {
                        Chest chest = new Chest(x, y);
                        chests.Add(chest);
                        map[x, y] = chest.Icon; // 맵에 상자 아이콘 표시
                        chestsSpawned++;
                    }
                }
            }
        }
        
        // 만약 방 구석에 스폰하지 못해 상자 개수가 모자라면,
        // 남은 상자는 그냥 빈 공간에 스폰
        int remainingChests = count - chestsSpawned;
        for (int i = 0; i < remainingChests; i++)
        {
             var (x, y) = GetRandomEmptyPosition(allowInBossRoom: false);
             if (x == 1 && y == 1) continue; // 스폰 실패
             
             Chest chest = new Chest(x, y);
             chests.Add(chest);
             map[x, y] = chest.Icon;
        }
    }
    #endregion

    // [수정] 게임 루프
    #region Game_Loop
    private void RunGameLoop()
    {
        // [수정] 'gameRunning = true' 제거 (클래스 필드 사용)
        bool needsRender = true; 

        while (gameRunning)
        {
            if (UpdateScreenSize(false)) { needsRender = true; }

            // [신규] 메인 타이틀 애니메이션 처리
            if (currentState == GameState.MainMenu)
            {
                ProcessMainMenuAnimation();
                needsRender = true; // [<<<] 이 줄을 추가하세요!
            }

            // [수정] 상자 애니메이션 상태 처리 (else if로 변경)
            else if (currentState == GameState.Battle_Animation)
            {
                ProcessBattleAnimation();
                needsRender = true;
            }

            else if (currentState == GameState.Chest_Opening) // [신규]
            {
                ProcessChestOpeningAnimation(); // (새로 추가할 메서드)
                needsRender = true;
            }

            if (Console.KeyAvailable)
            {
                try { Console.SetCursorPosition(0, 0); } catch { }
                ConsoleKeyInfo key = Console.ReadKey(true); 
                needsRender = true; 

                // --- [핵심 수정] ESC 키 전역 처리 ---
                if (key.Key == ConsoleKey.Escape)
                {
                    if (currentState == GameState.Pause)
                    {
                        currentState = stateBeforePause; 
                    }
                    // [수정]
                    else if (currentState != GameState.GameOver && 
                             currentState != GameState.LootDrop && 
                             currentState != GameState.LootSummary &&
                             currentState != GameState.MainMenu && // [신규]
                             currentState != GameState.HowToPlay)  // [신규]
                    {
                        stateBeforePause = currentState;
                        currentState = GameState.Pause;
                    }
                }
                // --- [끝] ---
                else // ESC가 아닌 다른 키
                {
                    switch (currentState)
                    {
                        case GameState.MainMenu: ProcessMainMenuInput(key); break;
                        case GameState.HowToPlay: ProcessHowToPlayInput(key); break;
                        case GameState.World: ProcessWorldInput(key); break;
                        case GameState.Battle: ProcessBattleInput(key); break;
                        case GameState.Battle_SkillSelect: ProcessSkillSelectInput(key); break;
                        case GameState.Battle_ItemMenu: ProcessItemMenuInput(key); break;
                        case GameState.Battle_ItemSubMenu: ProcessItemSubMenuInput(key); break;
                        case GameState.LevelUp: ProcessLevelUpInput(key); break;
                        case GameState.LootDrop: ProcessLootDropInput(key); break;
                        case GameState.LootSummary: ProcessLootSummaryInput(key); break;
                        case GameState.Inventory: ProcessInventoryInput(key); break;
                        case GameState.CharacterStat: ProcessStatWindowInput(key); break;
                        case GameState.Chest_Confirm: ProcessChestConfirmInput(key); break;
                        
                        case GameState.Pause: // [신규] 일시정지 메뉴 입력
                            ProcessPauseInput(key); 
                            break;
                            
                        case GameState.GameOver:
                            gameRunning = false; // 루프 종료
                            break;
                    }
                }

                if (player != null && player.HP <= 0 && currentState != GameState.GameOver)
                {
                    currentState = GameState.GameOver;
                    AddLog("플레이어가 쓰러졌다...");
                }

                while (Console.KeyAvailable)
                {
                    Console.ReadKey(true);
                }
            }

            if (needsRender)
            {
                Render(); 
                PrintBufferToConsole(); 
                needsRender = false; 
            }
            Thread.Sleep(16);
        }
        // [삭제] return needsRestart; (Start()로 이동됨)
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
            case GameState.HowToPlay:
                DrawHowToPlayWindow();
                break;
            case GameState.World:
                DrawWorldLayout();
                break;
            case GameState.Battle:
            case GameState.Battle_SkillSelect:
            case GameState.Battle_ItemMenu:
            case GameState.Battle_ItemSubMenu:
            case GameState.Battle_Animation: // [수정] 애니메이션 상태도 기본 BattleLayout을 먼저 그리도록 추가
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
                DrawGameOverLayout();
                break;
            case GameState.Pause:
                // 일시정지 직전 상태에 따라 배경을 그림
                if (stateBeforePause == GameState.World ||
                    stateBeforePause == GameState.Inventory)
                {
                    DrawWorldLayout();
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
        // Loot 상태일 땐 BattleMonster가 null일 수 있으므로
        if (currentBattleMonster == null && currentState != GameState.LootDrop && currentState != GameState.LootSummary) return; 
        
        DrawBox(0, 0, screenWidth, battleArtHeight_BATTLE, "Battle Stage");
        
        // 전투 중일 때만 아트워크 그림
        if (currentState == GameState.Battle ||
            currentState == GameState.Battle_ItemMenu ||
            currentState == GameState.Battle_SkillSelect ||
            currentState == GameState.Battle_ItemSubMenu ||
            currentState == GameState.Battle_Animation)
        {
            DrawBattleArt();
        }

        DrawBox(0, playerStatusY_BATTLE, screenWidth, playerStatusHeight_BATTLE, "Player Status & Actions");
        DrawBattlePlayerStatus(); 
        DrawBox(0, logWindowY_BATTLE, screenWidth, logWindowHeight_BATTLE, "Log");
        DrawLogRegion(0, logWindowY_BATTLE, screenWidth, logWindowHeight_BATTLE);
    }
    #endregion
    
    // (전투 아트 그리기... 변경 없음)
    #region Battle_Art
   private void DrawBattleArt()
    {
        if (currentBattleMonster == null) return;
        
        // --- 플레이어 아트 ---
        ConsoleColor playerFgColor = ConsoleColor.Green; 
        ConsoleColor playerBgColor = ConsoleColor.Black;
        if (currentState == GameState.Battle_Animation && showHitOverlay && currentAnimationTarget == player)
        {
            playerFgColor = ConsoleColor.White; 
        }
        
        int playerX = (screenWidth / 4) + player.ArtOffsetX; 
        string[] playerArt = AsciiArt.GetPlayerArt(player.Class);
        int playerArtY = player.ArtOffsetY;
        int playerMaxWidth = 0;
        foreach (string line in playerArt) { playerMaxWidth = Math.Max(playerMaxWidth, GetDisplayWidth(line)); }
        int playerBlockStartX = playerX - (playerMaxWidth / 2);
        for(int i = 0; i < playerArt.Length; i++) {
            DrawTextToBuffer(playerBlockStartX, playerArtY + i, playerArt[i], playerFgColor, playerBgColor, true); 
        }

        // [핵심 수정 4] 플레이어 'MISS' 표시
        if (currentState == GameState.Battle_Animation && currentAnimationTarget == player)
        {
            int dmgX = playerBlockStartX + playerMaxWidth + 1; 
            int dmgY = playerArtY + playerArt.Length / 2;     
            
            if (lastAttackWasMiss)
            {
                DrawTextToBuffer(dmgX, dmgY, "MISS", ConsoleColor.Cyan);
            }
            else if (currentAnimationDamage > 0)
            {
                DrawTextToBuffer(dmgX, dmgY, $"-{currentAnimationDamage}-", ConsoleColor.Red);
            }
        }
        
        // --- 몬스터 아트 ---
        Monster monster = currentBattleMonster; 
        ConsoleColor monsterFgColor = ConsoleColor.Red; 
        ConsoleColor monsterBgColor = ConsoleColor.Black;
        if (currentState == GameState.Battle_Animation && showHitOverlay && currentAnimationTarget == currentBattleMonster)
        {
            monsterFgColor = ConsoleColor.Yellow; 
        }
        
        int monsterX = ((screenWidth * 3) / 4) + monster.ArtOffsetX; 
        string[] monsterArt = AsciiArt.GetMonsterArt(monster.Icon); 
        int monsterArtY = monster.ArtOffsetY;
        int monsterMaxWidth = 0;
        foreach (string line in monsterArt) { monsterMaxWidth = Math.Max(monsterMaxWidth, GetDisplayWidth(line)); }
        int monsterBlockStartX = monsterX - (monsterMaxWidth / 2);
        for(int i = 0; i < monsterArt.Length; i++) {
            DrawTextToBuffer(monsterBlockStartX, monsterArtY + i, monsterArt[i], monsterFgColor, monsterBgColor, true); 
        }

        // [핵심 수정 4] 몬스터 'CRITICAL' / 'MISS' 표시
        if (currentState == GameState.Battle_Animation && currentAnimationTarget == currentBattleMonster)
        {
            string dmgTextCheck = $"-{currentAnimationDamage}-";
            int dmgWidth = GetDisplayWidth(dmgTextCheck);
            int dmgX = monsterBlockStartX - dmgWidth - 1;   
            int dmgY = monsterArtY + monsterArt.Length / 2; 

            if (lastAttackWasMiss)
            {
                DrawTextToBuffer(dmgX, dmgY, "MISS", ConsoleColor.Cyan);
            }
            else if (currentAnimationDamage > 0)
            {
                DrawTextToBuffer(dmgX, dmgY, $"-{currentAnimationDamage}-", ConsoleColor.White);
                
                if (lastAttackWasCrit)
                {
                    // "CRITICAL!" 메시지를 데미지 숫자 '위'에 표시
                    DrawTextToBuffer(dmgX, dmgY - 1, "CRITICAL!", ConsoleColor.Yellow); 
                }
            }
        }
        
        // (몬스터 HP 바... 변경 없음)
        int monsterHpTextY = monsterArtY + monsterArt.Length + 1;
        int monsterHpBarY = monsterHpTextY + 1;
        string monsterHP = $"{monster.Name} HP: {monster.HP}/{monster.MaxHP}";
        DrawTextToBuffer(monsterX - (GetDisplayWidth(monsterHP) / 2), monsterHpTextY, monsterHP, ConsoleColor.Yellow); 
        DrawBarToBuffer(monsterX - 10, monsterHpBarY, monster.HP, monster.MaxHP, 20, ConsoleColor.Red);
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
        if (currentState == GameState.Battle || currentState == GameState.Battle_Animation)
        {
            // [수정] battleMenuIndex를 기반으로 하이라이트
            DrawTextToBuffer(x, y,     (battleMenuIndex == 0 ? "► 1. 기본 공격" : "  1. 기본 공격"), 
                             battleMenuIndex == 0 ? ConsoleColor.Yellow : ConsoleColor.White);
            DrawTextToBuffer(x, y + 1, (battleMenuIndex == 1 ? "► 2. 스킬" : "  2. 스킬"), 
                             battleMenuIndex == 1 ? ConsoleColor.Yellow : ConsoleColor.White);
            DrawTextToBuffer(x, y + 2, (battleMenuIndex == 2 ? "► 3. 아이템" : "  3. 아이템"), 
                             battleMenuIndex == 2 ? ConsoleColor.Yellow : ConsoleColor.White); 
            DrawTextToBuffer(x, y + 3, (battleMenuIndex == 3 ? "► 4. 후퇴" : "  4. 후퇴"), 
                             battleMenuIndex == 3 ? ConsoleColor.Yellow : ConsoleColor.White);
        }
        else if (currentState == GameState.Battle_SkillSelect)
        {
            // [수정] skillMenuIndex를 기반으로 하이라이트
            DrawTextToBuffer(x, y + 5, "[B] 뒤로가기", ConsoleColor.Yellow);
            
            // 그리기용 로컬 함수
            Action<int, string, Skill, ConsoleColor> DrawSkill = 
                (index, key, skill, defaultColor) =>
            {
                string prefix = (skillMenuIndex == index) ? "►" : " ";
                bool available = player.MP >= skill.MpCost;
                ConsoleColor color;
                if (skillMenuIndex == index)
                {
                    color = (index == 3) ? ConsoleColor.Cyan : ConsoleColor.Yellow; // R은 Cyan
                }
                else
                {
                    if (!available) color = ConsoleColor.DarkGray;
                    else color = defaultColor;
                }
                DrawTextToBuffer(x, y + index, $"{prefix} [{key}] {skill.Name.PadRight(15)} (MP {skill.MpCost})", color);
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
        // 1. key.Key 확인 (영문 모드)
        if (key.Key == ConsoleKey.E || key.Key == ConsoleKey.B || key.Key == ConsoleKey.Escape)
        {
            currentState = GameState.World;
            return;
        }

        // 2. key.KeyChar 확인 (한글 모드 Fallback)
        char c = char.ToUpper(key.KeyChar);
        if (c == 'E' || c == 'ㄷ' || c == 'B' || c == 'ㅠ')
        {
            currentState = GameState.World;
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
        cameraX = Math.Max(0, Math.Min(cameraX, MapWidth - viewportWidth));
        cameraY = Math.Max(0, Math.Min(cameraY, MapHeight - viewportHeight));
        for (int y = 0; y < viewportHeight; y++){
            for (int x = 0; x < viewportWidth; x++){
                int mapX = cameraX + x;
                int mapY = cameraY + y;
                if (mapX >= 0 && mapX < MapWidth && mapY >= 0 && mapY < MapHeight){
                    char tile = map[mapX, mapY];
                    ConsoleColor color = ConsoleColor.DarkGray; 
                    if (tile == '█') color = ConsoleColor.Gray; 
                    else if (tile == '^' || tile == '*') color = ConsoleColor.DarkRed; 
                    else if (tile == '.') color = ConsoleColor.DarkGray;
                    DrawToBuffer(x + 1, y + 1, tile, color);
                }
            }
        }
        foreach(var monster in monsters){
            if (monster == currentBattleMonster) continue;
            int screenX = monster.X - cameraX + 1; 
            int screenY = monster.Y - cameraY + 1;
            if (screenX > 0 && screenX < viewportWidth + 1 && screenY > 0 && screenY < viewportHeight + 1){
                if (monster.Icon == 'B') { DrawToBuffer(screenX, screenY, 'B', ConsoleColor.Magenta); }
                else { DrawToBuffer(screenX, screenY, 'M', ConsoleColor.Red); }
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
            DrawToBuffer(playerScreenX, playerScreenY, '@', ConsoleColor.Green);
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
    private void DrawLogRegion(int x, int y, int width, int height) {
        int logX = x + 2;
        int logY = y + 2;
        int logWidth = Math.Max(0, width - 4);
        int logHeight = Math.Max(0, height - 3);
        int maxLines = logHeight;
        int logCount = logMessages.Count;
        for (int i = 0; i < maxLines; i++){
            int logIndex = logCount - maxLines + i;
            string logLine = "";
            if (logIndex >= 0) { logLine = logMessages[logIndex]; }
            string displayLine = TruncateStringByDisplayWidth(logLine, logWidth);
            DrawTextToBuffer(logX, logY + i, displayLine, ConsoleColor.White);
        }
    }
    #endregion
    
    // (렌더링 헬퍼, 너비 계산... 변경 없음)
    #region Rendering_Helpers_WidthCalc
   // Game.cs -> PrintBufferToConsole 메서드 내부

    private void PrintBufferToConsole()
    {
        try {
            Console.SetCursorPosition(0, 0); 
            ConsoleColor lastFg = Console.ForegroundColor;
            ConsoleColor lastBg = Console.BackgroundColor; 
            
            for (int y = 0; y < screenHeight; y++){
                Console.SetCursorPosition(0, y); 
                for (int x = 0; x < screenWidth; x++){
                    var cell = screenBuffer[y, x];
                    
                    if (cell.Char == '\0') { continue; }
                    
                    if (cell.FgColor != lastFg) { Console.ForegroundColor = cell.FgColor; lastFg = cell.FgColor; }
                    if (cell.BgColor != lastBg) { Console.BackgroundColor = cell.BgColor; lastBg = cell.BgColor; } // [!!!] (참고) 여기도 lastFg = cell.BgColor; 오타가 있었는데, 제 코드엔 수정되어 있네요. 혹시 수정 안 하셨다면 lastBg = cell.BgColor;로 바꿔주세요.

                    // [핵심 수정] 
                    // if 문을 제거하여 모든 픽셀이 그려지도록 합니다.
                    Console.Write(cell.Char);
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
        if (width <= 2 || height <= 2 || x + width > screenWidth || y + height > screenHeight) return;
        int endX = x + width - 1;
        int endY = y + height - 1;
        ConsoleColor boxColor = ConsoleColor.DarkGray;
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
        DrawTextToBuffer(x + 2, y, $" {title} ", ConsoleColor.White);
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
    public void AddLog(string message) { logMessages.Add(message); if (logMessages.Count > 50) { logMessages.RemoveAt(0); } }
    public void UpdateMapTile(int x, int y, char tile) { if (x >= 0 && x < MapWidth && y >= 0 && y < MapHeight) { map[x, y] = tile; } }
    
    public void StartBattle(Monster monster, bool isFromTrap = false) 
    { 
        // [신규] 몬스터 레벨 스케일링 (보스 'B'는 제외)
        if (monster.Icon != 'B' && player.Level > 1)
        {
            double levelModifier = (double)(player.Level - 1); // Lvl 1 플레이어는 0
            double scale = levelModifier * MONSTER_SCALING_FACTOR;
            
            // 원본 스탯을 기준으로 현재 플레이어 레벨에 맞게 스탯 재계산
            monster.MaxHP = (int)(monster.OriginalMaxHP + (monster.OriginalMaxHP * scale));
            monster.ATK = (int)(monster.OriginalATK + (monster.OriginalATK * scale));
            monster.DEF = (int)(monster.OriginalDEF + (monster.OriginalDEF * scale));
            monster.EXPReward = (int)(monster.OriginalEXPReward + (monster.OriginalEXPReward * scale));
        }
        
        // 전투 시작 시 HP를 (스케일링된) MaxHP로 설정
        monster.HP = monster.MaxHP; 

        AddLog($"야생의 {monster.Name}이(가) 나타났다!"); 
        currentBattleMonster = monster; 
        currentState = GameState.Battle; 
        isTrapBattle = isFromTrap; // 함정 전투 여부 저장
    }
    
    // (ProcessWorldInput ... 변경 없음)
    private void ProcessWorldInput(ConsoleKeyInfo key)
    {
        int newX = player.X;
        int newY = player.Y;
        bool moved = false;

        // 1. key.Key 확인 (영문 모드 + 방향키 등)
        switch (key.Key)
        {
            case ConsoleKey.W: case ConsoleKey.UpArrow: newY--; moved = true; break;
            case ConsoleKey.S: case ConsoleKey.DownArrow: newY++; moved = true; break;
            case ConsoleKey.A: case ConsoleKey.LeftArrow: newX--; moved = true; break;
            case ConsoleKey.D: case ConsoleKey.RightArrow: newX++; moved = true; break;
            case ConsoleKey.E: currentState = GameState.Inventory; return; 
            case ConsoleKey.C: currentState = GameState.CharacterStat; return; 
            case ConsoleKey.F: TryOpenChest(); return;
        }

        // 2. key.KeyChar 확인 (한글 모드 Fallback)
        if (!moved) // (key.Key에서 아무것도 감지되지 않았다면)
        {
            char c = char.ToUpper(key.KeyChar);
            if (c == 'W' || c == 'ㅈ') { newY--; moved = true; }
            else if (c == 'S' || c == 'ㅅ') { newY++; moved = true; }
            else if (c == 'A' || c == 'ㅁ') { newX--; moved = true; }
            else if (c == 'D' || c == 'ㅇ') { newX++; moved = true; }

            // [!!!] --- 여기가 수정된 부분입니다 --- [!!!]
            // 'ㅊ'가 'E' 로직에서 분리되었습니다.
            else if (c == 'E' || c == 'ㄷ')
            {
                currentState = GameState.Inventory;
                return;
            }
            else if (c == 'C' || c == 'ㅊ')
            {
                currentState = GameState.CharacterStat;
                return;
            }
            // [!!!] --- 수정 끝 ---

            else if (c == 'F' || c == 'ㄹ') { TryOpenChest(); return; }
            else { return; } // 유효하지 않은 키
        }
        if (newX == portalPosition.x && newY == portalPosition.y)
        {
            if (currentStage < 3)
            {
                AddLog("다음 스테이지로 이동합니다...");
                TransitionToStage(currentStage + 1);
            }
            else
            {
                AddLog("마지막 스테이지입니다. (게임 엔딩 구현 필요)");
                // (임시로 월드로 복귀)
                currentState = GameState.World;
            }
            return;
        }

        // --- 이하 이동 및 충돌 처리 로직 (변경 없음) ---
        if (newX < 0 || newX >= MapWidth || newY < 0 || newY >= MapHeight) { AddLog("더 이상 갈 수 없는 곳입니다."); return; }
        char tile = map[newX, newY];
        if (tile == '█') { AddLog("벽에 부딪혔습니다."); return; }
        if (chests.Any(c => c.X == newX && c.Y == newY && !c.IsOpen)) { AddLog("상자가 길을 막고 있습니다."); return; }

        Trap? trap = traps.Find(t => t.X == newX && t.Y == newY && !t.IsTriggered);
        if (trap != null) {
            // [수정] currentStage 변수를 Trigger 메서드에 전달합니다.
            trap.Trigger(player, this, rand, currentStage); 
            
            if (trap.Type == TrapType.Battle) { return; }
            if (player.HP <= 0) return; 
        }
        Monster? target = monsters.Find(m => m.X == newX && m.Y == newY);
        if (target != null) {
            if (target.Icon == 'B') { StartBattle(target); }
            else { StartBattle(target); }
            return; 
        }
        player.X = newX;
        player.Y = newY;
        ProcessMonsterTurn_World();
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
    private void ProcessSkillSelectInput(ConsoleKeyInfo key) {
        int skillCount = player.Skills.Count;
        if (skillCount == 0) {
            currentState = GameState.Battle; 
            AddLog("사용할 스킬이 없습니다.");
            return;
        }

        // 1. key.Key 확인 (영문 모드 + 방향키 등)
        switch (key.Key)
        {
            // 방향키
            case ConsoleKey.UpArrow:
                skillMenuIndex = (skillMenuIndex - 1 + skillCount) % skillCount;
                return; // [수정] break -> return (아래 2, 3번 실행 방지)
            case ConsoleKey.DownArrow:
                skillMenuIndex = (skillMenuIndex + 1) % skillCount;
                return; 
            
            // Enter
            case ConsoleKey.Enter:
                StartPlayerSkillAnimation(player.Skills[skillMenuIndex]);
                return;

            // 단축키 (영문)
            case ConsoleKey.Q: if(player.Skills.Count > 0) StartPlayerSkillAnimation(player.Skills[0]); return;
            case ConsoleKey.W: if(player.Skills.Count > 1) StartPlayerSkillAnimation(player.Skills[1]); return;
            case ConsoleKey.E: if(player.Skills.Count > 2) StartPlayerSkillAnimation(player.Skills[2]); return;
            case ConsoleKey.R: if(player.Skills.Count > 3) StartPlayerSkillAnimation(player.Skills[3]); return; 
            case ConsoleKey.B:
                currentState = GameState.Battle; AddLog("행동을 선택하세요.");
                return;
        }

        // 2. key.KeyChar 확인 (한글 모드 Fallback)
        char c = char.ToUpper(key.KeyChar);
        if (c == 'Q' || c == 'ㅂ') { if (player.Skills.Count > 0) StartPlayerSkillAnimation(player.Skills[0]); }
        else if (c == 'W' || c == 'ㅈ') { if (player.Skills.Count > 1) StartPlayerSkillAnimation(player.Skills[1]); }
        else if (c == 'E' || c == 'ㄷ') { if (player.Skills.Count > 2) StartPlayerSkillAnimation(player.Skills[2]); }
        else if (c == 'R' || c == 'ㄱ') { if (player.Skills.Count > 3) StartPlayerSkillAnimation(player.Skills[3]); }
        else if (c == 'B' || c == 'ㅠ') {
            currentState = GameState.Battle; AddLog("행동을 선택하세요.");
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
                currentState = GameState.Battle;
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
        // [수정] Draw 로직과 동일하게 'Name'으로 그룹화 (기존 Rarity 버그 수정)
        var distinctItemGroups = player.ConsumableInventory
            .Where(item => item.CType == currentItemSubMenu)
            .GroupBy(item => item.Name) 
            .Select(group => new { 
                Item = group.First(), 
                Count = group.Count()   
            })
            .OrderBy(g => g.Item.Rarity) 
            .ToList();
        
        // (입력 처리를 위해 Consumable 리스트만 따로 추출)
        var distinctItems = distinctItemGroups.Select(g => g.Item).ToList();

        if (distinctItems.Count == 0)
        {
            currentState = GameState.Battle_ItemMenu;
            AddLog("해당 종류의 물약이 없습니다.");
            return;
        }

        bool itemUsed = false;
        switch (key.Key)
        {
            // [신규] 방향키
            case ConsoleKey.UpArrow:
                itemSubMenuIndex = (itemSubMenuIndex - 1 + distinctItems.Count) % distinctItems.Count;
                break;
            case ConsoleKey.DownArrow:
                itemSubMenuIndex = (itemSubMenuIndex + 1) % distinctItems.Count;
                break;
            
            // [신규] Enter
            case ConsoleKey.Enter:
                itemUsed = UseItemFromList(distinctItems, itemSubMenuIndex);
                break;

            // [기존] 단축키
            case ConsoleKey.D1: itemUsed = UseItemFromList(distinctItems, 0); break;
            case ConsoleKey.D2: itemUsed = UseItemFromList(distinctItems, 1); break;
            case ConsoleKey.D3: itemUsed = UseItemFromList(distinctItems, 2); break;
            case ConsoleKey.D4: itemUsed = UseItemFromList(distinctItems, 3); break;
            
            case ConsoleKey.B:
                currentState = GameState.Battle_ItemMenu; 
                AddLog("어떤 종류의 물약을 사용하시겠습니까?");
                break;
        }

        if (itemUsed)
        {
            currentState = GameState.Battle; 
            if (currentBattleMonster != null && currentBattleMonster.HP > 0)
            {
                StartMonsterAttackAnimation();
            }
        }
    }
    
    private bool UseItemFromList(List<Consumable> distinctItems, int index)
    {
        if (index >= distinctItems.Count) { AddLog("해당 번호의 아이템이 없습니다."); return false; }
        Consumable itemToUse = distinctItems[index];
        return player.UseConsumable(itemToUse.CType, itemToUse.Rarity, this);
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

        // 다음 아이템/상태로 이동
        if (equipmentDropQueue.Count > 0)
        {
            currentLootEquipment = equipmentDropQueue.Dequeue();
            currentState = GameState.LootDrop;
        }
        else
        {
            currentLootEquipment = null;
            if (currentLootList.Any(item => item is Consumable)) { currentState = GameState.LootSummary; }
            else { currentLootList.Clear(); currentBattleMonster = null; currentState = GameState.World; }
        }
    }
    
    private void ProcessLootSummaryInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter) {
            foreach (var item in currentLootList) {
                if (item is Consumable consumable) { player.AddConsumable(consumable); AddLog($"[아이템 획득] {consumable.Name}"); }
            }
            currentLootList.Clear(); currentBattleMonster = null; currentState = GameState.World; 
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
        // 1. 다음 깜빡일 시간이 될 때까지 대기
        if (DateTime.Now < nextBlinkTime)
        {
            return;
        }
            
        // 2. 시간 도달, 카운트 감소
        currentBlinkCount--;
            
        if (currentBlinkCount > 0)
        {
            // 3. [수정] IsHit 대신, 오버레이 표시 여부를 토글
            showHitOverlay = !showHitOverlay; 
            nextBlinkTime = DateTime.Now.AddMilliseconds(ANIM_BLINK_DURATION_MS);
        }
        else
        {
            // 4. 모든 깜빡임(4회) 종료
            showHitOverlay = false;         // [수정] 오버레이 숨기기
            currentAnimationDamage = 0;     // [수정] 데미지 초기화
            currentAnimationTarget = null; 

            // 4b. 애니메이션 상태 종료 및 콜백 실행
            currentState = GameState.Battle; // 턴으로 복귀
            if (animationCallback != null)
            {
                var callback = animationCallback;
                animationCallback = null; 
                callback.Invoke(); 
            }
        }
    }


    // --- [핵심 수정] ---
    // 1. 불필요해진 'int durationMs' 매개변수 제거
   private void StartAnimation(object target, int damageToDisplay, Action onComplete)
    {
        // 1. 애니메이션 정보 설정
        currentAnimationTarget = target; 
        currentBlinkCount = ANIM_TOTAL_BLINKS; 
        nextBlinkTime = DateTime.Now; 
        animationCallback = onComplete;
        currentState = GameState.Battle_Animation;

        // 2. [수정] 데미지 숫자와 오버레이 상태 설정
        currentAnimationDamage = damageToDisplay;
        showHitOverlay = true; // (첫 번째 깜빡임 ON)
    }
    // --- [끝] ---

    // 1. 플레이어 기본 공격
    private void StartPlayerAttackAnimation()
    {
        if (IsAnimationPlaying() || currentBattleMonster == null) return;

        int damage = AttackMonster(player, currentBattleMonster);
        
        if (lastAttackWasMiss)
        {
            StartAnimation(currentBattleMonster, 0, () => {
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
        
        StartAnimation(currentBattleMonster, damage, () => {
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
        
        // (자원 소모 감소 로직 - 변경 없음)
        float reductionPercent = player.GetStatBonus(StatType.ResourceCostReduction, ModifierType.Percent);
        int finalMpCost = (int)Math.Floor(skill.MpCost * (1.0f - reductionPercent));
        if (player.MP < finalMpCost) { AddLog("MP가 부족합니다!"); return; }
        player.MP -= finalMpCost;
        
        // [핵심 수정]
        // 1. Skill.cs에서 '방어력 무시'된 '순수 데미지'를 계산
        int rawSkillDamage = skill.CalculateDamage(player, currentBattleMonster);
        int finalSkillDamage = 0;

        if (skill.IsDamagingSkill)
        {
            // 2. 스킬 종류에 따라 방어력 적용 여부 결정
            if (skill.Name == "독 찌르기")
            {
                finalSkillDamage = rawSkillDamage; // DoT는 방어력 무시
                AddLog($"도적: 독 찌르기! {currentBattleMonster.Name}에게 {finalSkillDamage}의 독 데미지 (5턴)!");
            }
            // 2a. 방어력 '무시' 스킬 (마법, 필살기)
            else if (skill.Name == "파이어볼" || skill.Name == "매직 미사일" ||
                     skill.Name == "메테오" || skill.Name == "처형")
            {
                finalSkillDamage = Math.Max(1, rawSkillDamage); // ApplyDefense 없음
                
                if (skill.Name.Contains("파이어볼") || skill.Name.Contains("매직 미사일"))
                    AddLog($"마법사: {skill.Name}! {currentBattleMonster.Name}에게 {finalSkillDamage}의 (고정) 데미지!");
                else
                    AddLog($"{player.Class}: {skill.Name}! {currentBattleMonster.Name}에게 {finalSkillDamage}의 데미지!");
            }
            // 2b. 방어력 '적용' 스킬 (물리)
            else 
            {
                // (PowerStrike, ShieldBash, Backstab, QuickAttack, Eviscerate)
                finalSkillDamage = ApplyDefense(rawSkillDamage, currentBattleMonster.DEF);
                AddLog($"{player.Class}: {skill.Name}! {currentBattleMonster.Name}에게 {finalSkillDamage}의 데미지!");
            }

            // 3. HP 감소 (독 찌르기 제외)
            if (skill.Name != "독 찌르기")
            {
                currentBattleMonster.HP -= finalSkillDamage;
            }
            
            // (OnHitEffects, 애니메이션 호출 - 변경 없음)
            ApplyOnHitEffects(player, currentBattleMonster);

            StartAnimation(currentBattleMonster, finalSkillDamage, () => {
                ApplyLifesteal(finalSkillDamage, player); // [수정] finalSkillDamage 사용
                CheckForManaRefund(finalMpCost); 
                if (currentBattleMonster.HP <= 0)
                    WinBattle();
                else
                    StartMonsterAttackAnimation();
            });
        }
        else if (skill.IsBuffSkill) // [!!!] --- 여기가 수정된 부분입니다 --- [!!!]
        {
            
            if (skill.Name == "힐")
                AddLog($"마법사: 힐! HP를 {rawSkillDamage}만큼 회복!"); // [수정] value -> rawSkillDamage
            else
                AddLog($"{player.Class}: {skill.Name}!"); 
            
            StartAnimation(player, rawSkillDamage, () => { // [수정] value -> rawSkillDamage
                CheckForManaRefund(finalMpCost); 
                StartMonsterAttackAnimation(); 
            });
        }
    }
    
    private void ApplyOnHitEffects(Player player, Monster target)
    {
        // 1. 기절 (StunChance)
        float stunChance = player.GetStatBonus(StatType.StunChance, ModifierType.Percent);
        if (rand.NextDouble() < stunChance)
        {
            // 몬스터의 '다음 1턴'을 행동 불능으로 만듭니다.
            target.StatusEffects[StatType.StunChance] = 1; // 1턴간 기절
            AddLog($"전사: {target.Name}을(를) 기절시켰습니다!");
        }

        // 2. 출혈 (BleedChance)
        float bleedChance = player.GetStatBonus(StatType.BleedChance, ModifierType.Percent);
        if (rand.NextDouble() < bleedChance)
        {
            // '독 찌르기'와 유사하게 DEX 기반 데미지
            int bleedDamage = Math.Max(1, player.DEX / 2); 
            
            // 몬스터에게 출혈 데미지와 지속시간(3턴) 적용
            target.BleedDamagePerTurn = bleedDamage;
            target.StatusEffects[StatType.BleedChance] = 3; 
            AddLog($"도적: {target.Name}에게 출혈을 일으켰습니다! (턴당 {bleedDamage})");
        }
    }

    // 3. 몬스터 턴
    private void StartMonsterAttackAnimation()
    {
        if (IsAnimationPlaying() || currentBattleMonster == null) return;

        lastAttackWasCrit = false;
        lastAttackWasMiss = false;

        // [신규] 3번 요청: 기절(Stun) 상태 확인
        if (currentBattleMonster.StatusEffects.GetValueOrDefault(StatType.StunChance, 0) > 0)
        {
            currentBattleMonster.StatusEffects[StatType.StunChance]--; // 턴 차감
            AddLog($"{currentBattleMonster.Name}은(는) 기절해서 움직일 수 없다!");
            
            // 몬스터의 턴을 종료하고 즉시 플레이어 턴으로
            currentState = GameState.Battle; 
            return; 
        }

        // [신규] 4번 요청: 출혈(Bleed) 상태 확인 (독과 중첩됨)
        if (currentBattleMonster.StatusEffects.GetValueOrDefault(StatType.BleedChance, 0) > 0)
        {
            int bleedDmg = currentBattleMonster.BleedDamagePerTurn;
            currentBattleMonster.HP -= bleedDmg;
            currentBattleMonster.StatusEffects[StatType.BleedChance]--; // 턴 차감
            AddLog($"{currentBattleMonster.Name}이(가) 출혈으로 {bleedDmg}의 데미지를 입었다!");
            
            if (currentBattleMonster.StatusEffects[StatType.BleedChance] == 0)
            {
                currentBattleMonster.BleedDamagePerTurn = 0; // 출혈 종료
            }
            if (currentBattleMonster.HP <= 0) { WinBattle(); return; }
        }
        
        // (기존 독 데미지 로직 - 변경 없음)
        if (currentBattleMonster.StatusEffects.GetValueOrDefault(StatType.PoisonStabDamage, 0) > 0) 
        {
            int poisonDmg = currentBattleMonster.PoisonDamagePerTurn;
            currentBattleMonster.HP -= poisonDmg;
            if (currentBattleMonster.StatusEffects[StatType.PoisonStabDamage] == 1)  { // (버그 픽스: -- 전에 1인지 확인)
                AddLog($"{currentBattleMonster.Name}이(가) 독 데미지로 {poisonDmg}의 피해를 입었다! (독 효과 만료)");
                currentBattleMonster.PoisonDamagePerTurn = 0; 
            } else {
                AddLog($"{currentBattleMonster.Name}이(가) 독 데미지로 {poisonDmg}의 피해를 입었다!");
            }
            currentBattleMonster.StatusEffects[StatType.PoisonStabDamage]--;
            if (currentBattleMonster.HP <= 0) { WinBattle(); return; }
        }
        
        // (플레이어 회피 로직 - 변경 없음)
        double playerEvasionChance = (player.DEX * PLAYER_DEX_EVASION_SCALER);
        if (rand.NextDouble() < playerEvasionChance) {
            AddLog("플레이어가 몬스터의 공격을 멋지게 회피했습니다!");
            lastAttackWasMiss = true; 
            StartAnimation(player, 0, () => {
                currentState = GameState.Battle; 
            });
            return; 
        }
        
        AddLog($"{currentBattleMonster.Name}의 턴!");
        
        // [핵심 수정] 새로운 방어력 공식 적용
        // 1. 몬스터의 '순수 공격력'
        int rawDamage = currentBattleMonster.ATK;
        
        // 2. 플레이어의 방어력(DEF)을 적용하여 최종 데미지 계산
        int damage = ApplyDefense(rawDamage, player.DEF);        
        // (피해 반사 로직 - 변경 없음)
        float reflectChance = player.GetStatBonus(StatType.DamageReflectChance, ModifierType.Percent);
        if (rand.NextDouble() < reflectChance) {
            int reflectDamage = (int)Math.Max(1, damage * 0.5); 
            currentBattleMonster.HP -= reflectDamage;
            AddLog($"전사: 피해 반사! {currentBattleMonster.Name}에게 {reflectDamage}의 데미지를 되돌려주었다!");
            if (currentBattleMonster.HP <= 0) { WinBattle(); return; }
        }
        
        // [핵심 수정] 4번 요청: 마력 보호막 (ManaShieldConversion)
        float conversionRate = player.GetStatBonus(StatType.ManaShieldConversion, ModifierType.Percent);
        int finalHpDamage = damage;
        int finalMpDamage = 0;

        if (conversionRate > 0 && player.MP > 0)
        {
            // 1. 데미지 분배
            int mpAbsorbAmount = (int)Math.Ceiling(damage * conversionRate);
            int hpDamageAmount = damage - mpAbsorbAmount;
            
            // 2. MP가 데미지를 감당할 수 있는지 확인
            if (player.MP >= mpAbsorbAmount)
            {
                // (감당 가능)
                finalMpDamage = mpAbsorbAmount;
                finalHpDamage = hpDamageAmount;
                player.MP -= finalMpDamage;
            }
            else
            {
                // (감당 불가) MP가 0이 되고, '막지 못한 데미지'가 HP로 넘어감
                int overflowDamage = mpAbsorbAmount - player.MP;
                finalMpDamage = player.MP; // (남은 MP 전부 소모)
                finalHpDamage = hpDamageAmount + overflowDamage; // (HP가 나머지 다 받음)
                player.MP = 0;
            }

            // 3. 최종 데미지 적용
            player.HP -= finalHpDamage;
            AddLog($"마력 보호막! {finalHpDamage}의 HP 데미지와 {finalMpDamage}의 MP 데미지를 입었습니다!");
        }
        else
        {
            // (보호막 없거나 MP 0)
            player.HP -= finalHpDamage;
            AddLog($"{currentBattleMonster.Name}이(가) 플레이어를 공격! {finalHpDamage}의 데미지!");
        }

        StartAnimation(player, finalHpDamage, () => { // [수정] HP가 입은 데미지만큼만 표시
            if (player.HP <= 0)
                currentState = GameState.GameOver;
            else
                currentState = GameState.Battle; 
        });
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

    private void CheckForManaRefund(int mpCost)
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
        bool wasBoss = (currentBattleMonster.Icon == 'B');
        currentBattleMonster.StatusEffects.Clear();
        currentBattleMonster.PoisonDamagePerTurn = 0;
        currentBattleMonster.BleedDamagePerTurn = 0; // [신규]

        // (경험치 획득 로직 ...)
        int baseExp = currentBattleMonster.EXPReward;
        float expBonusPercent = player.GetStatBonus(StatType.EXPGain, ModifierType.Percent);
        int finalExp = (int)Math.Round(baseExp * (1.0f + expBonusPercent));
        if (expBonusPercent > 0) { AddLog($"경험치를 {finalExp} 획득했다! (기본 {baseExp} + 보너스 {(finalExp - baseExp)})"); }
        else { AddLog($"경험치를 {finalExp} 획득했다!"); }
        if (monsters.Contains(currentBattleMonster)) { monsters.Remove(currentBattleMonster); }

        bool didLevelUp = player.AddExperience(finalExp);
        if (didLevelUp) { AddLog($"LEVEL UP! {player.Level}레벨이 되었습니다!"); }

        // [핵심 수정] 보스인지 일반 몬스터인지 확인하여 드랍 테이블 결정
        if (wasBoss)
        {
            AddLog("보스 몬스터가 희귀한 아이템을 드랍합니다!");
            currentLootList = ItemDB.GenerateBossDrops(player.Class, rand, currentStage);
        }
        else
        {
            currentLootList = ItemDB.GenerateAllDrops(player.Class, rand, currentStage);
        }

        var equipmentDrops = currentLootList.Where(item => item is Equipment).Cast<Equipment>();
        // [수정 끝]

        // (이하 아이템 큐에 담고 상태 전환하는 로직 동일)
        equipmentDropQueue.Clear();
        foreach (var eq in equipmentDrops) { equipmentDropQueue.Enqueue(eq); }

        stateBeforeLoot = GameState.Battle;

        if (wasBoss && currentStage < 3)
        {
            portalPosition = GetPortalSpawnPoint(bossRoom);
            map[portalPosition.x, portalPosition.y] = 'O'; // 포탈 타일
            AddLog("신비한 포탈이 열렸습니다!");
        }

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
            currentBattleMonster = null;
            currentState = GameState.World;
            isTrapBattle = false;
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
        // [수정] 1번 요청: 보스이거나 '함정 전투'이면 후퇴 불가
        if (currentBattleMonster != null && currentBattleMonster.Icon == 'B')
        {
            AddLog("보스에게서 도망칠 수 없습니다!");
            return;
        }
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
            currentBattleMonster.BleedDamagePerTurn = 0; // [신규]
        }

        currentBattleMonster = null;
        currentState = GameState.World;
        isTrapBattle = false; // [신규] 전투 상태 초기화
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
        foreach (var monster in monsters.ToList()) 
        {
            if (monster.Icon == 'B') continue; // 보스는 움직이지 않음

            int newX = monster.X; 
            int newY = monster.Y;
            
            // [신규] 3번 요청 (추적 로직)
            int dx = player.X - monster.X;
            int dy = player.Y - monster.Y;
            int distSq = (dx * dx) + (dy * dy);

            if (distSq <= MONSTER_AGGRO_RANGE_SQUARED)
            {
                // 1. 추적 (Aggro) 상태
                int moveX = 0; 
                int moveY = 0;

                // 1a. 더 먼 축을 기준으로 먼저 이동 (대각선 이동 방지)
                if (Math.Abs(dx) > Math.Abs(dy))
                {
                    moveX = Math.Sign(dx); // X축(좌/우)으로 먼저 이동
                }
                else
                {
                    moveY = Math.Sign(dy); // Y축(위/아래)으로 먼저 이동
                }
                
                int primaryX = monster.X + moveX;
                int primaryY = monster.Y + moveY;
                
                if (IsValidMove(primaryX, primaryY, monster))
                {
                    // 1b. 주 이동 경로가 유효하면 이동
                    (monster.X, monster.Y) = (primaryX, primaryY);
                    continue; 
                }
                
                // 1c. 주 이동 경로가 막혔으면, 2순위 경로(다른 축) 시도
                if (moveX != 0) // (X축이 주 경로였다면)
                {
                    moveY = Math.Sign(dy); // Y축(2순위)으로 이동
                    moveX = 0;
                }
                else // (Y축이 주 경로였다면)
                {
                    moveX = Math.Sign(dx); // X축(2순위)으로 이동
                    moveY = 0;
                }
                
                int secondaryX = monster.X + moveX;
                int secondaryY = monster.Y + moveY;

                if (IsValidMove(secondaryX, secondaryY, monster))
                {
                    (monster.X, monster.Y) = (secondaryX, secondaryY);
                    continue;
                }
                
                // (둘 다 막혔으면 이동 안 함)
                continue;
            }
            else
            {
                // 2. 비추적 (Random) 상태 (기존 로직)
                int move = rand.Next(0, 5); // 0=대기, 1~4=이동
                switch (move) 
                { 
                    case 1: newY--; break; 
                    case 2: newY++; break; 
                    case 3: newX--; break; 
                    case 4: newX++; break; 
                    default: continue; // 0 (대기)
                }

                if (IsValidMove(newX, newY, monster))
                {
                    monster.X = newX; 
                    monster.Y = newY;
                }
            }
        }
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
            
            // [수정] 
            // 1. 상자를 찾습니다.
            Chest? targetChest = chests.Find(c => c.X == targetX && c.Y == targetY && !c.IsOpen);
            
            if (targetChest != null) 
            { 
                // [수정] currentStage 변수를 Open 메서드에 전달합니다.
                // (이후 Open 메서드가 이 값을 Chest_Confirm으로 넘길 것입니다)
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
        int height = (int)(screenHeight * 0.4); 
        width = Math.Max(40, width); 
        height = Math.Max(12, height); // [수정] 높이 10 -> 12
        int startX = screenWidth / 2 - width / 2;
        int startY = screenHeight / 2 - height / 2;
        
        DrawFilledBox(startX, startY, width, height, ConsoleColor.Black); // [신규] 배경 추가
        DrawBox(startX, startY, width, height, "PAUSE");
        
        // [수정] 3개 메뉴를 중앙 정렬 및 하이라이트
        int yDraw = startY + (height / 2) - 2; 

        string text1 = "[1] 메인 화면으로 (진행 초기화)";
        string text2 = "[2] 게임 종료";
        string text3 = "[ESC] 계속하기";

        DrawTextToBuffer(startX + (width / 2) - (GetDisplayWidth(text1) / 2), yDraw, 
                         (pauseMenuIndex == 0 ? "► " : "  ") + text1, 
                         pauseMenuIndex == 0 ? ConsoleColor.Yellow : ConsoleColor.White);
        yDraw += 2; 

        DrawTextToBuffer(startX + (width / 2) - (GetDisplayWidth(text2) / 2), yDraw, 
                         (pauseMenuIndex == 1 ? "► " : "  ") + text2, 
                         pauseMenuIndex == 1 ? ConsoleColor.Yellow : ConsoleColor.White);
        yDraw += 2;

        DrawTextToBuffer(startX + (width / 2) - (GetDisplayWidth(text3) / 2), yDraw, 
                         (pauseMenuIndex == 2 ? "► " : "  ") + text3, 
                         pauseMenuIndex == 2 ? ConsoleColor.Yellow : ConsoleColor.White);
    }
    private void ProcessPauseInput(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            // [신규] 방향키
            case ConsoleKey.UpArrow:
                pauseMenuIndex = (pauseMenuIndex - 1 + 3) % 3; // 3개 메뉴 순환
                break;
            case ConsoleKey.DownArrow:
                pauseMenuIndex = (pauseMenuIndex + 1) % 3;
                break;

            // [신규] Enter
            case ConsoleKey.Enter:
                ProcessPauseAction(pauseMenuIndex);
                break;

            // [기존] 숫자/ESC
            case ConsoleKey.D1:
                pauseMenuIndex = 0;
                ProcessPauseAction(pauseMenuIndex);
                break;
            case ConsoleKey.D2:
                pauseMenuIndex = 1;
                ProcessPauseAction(pauseMenuIndex);
                break;
            case ConsoleKey.Escape:
                pauseMenuIndex = 2; // (계속하기)
                ProcessPauseAction(pauseMenuIndex);
                break;
        }
    }
    
    private void ProcessPauseAction(int index)
    {
        switch (index)
        {
            case 0: // 1. 직업 선택 화면으로
                needsRestart = true; 
                gameRunning = false; 
                break;
            case 1: // 2. 게임 종료
                gameRunning = false; 
                break;
            case 2: // 3. 계속하기 (ESC)
                currentState = stateBeforePause;
                break;
        }
    }

    // [신규] 레벨 업 창 입력 처리
    private void ProcessLevelUpInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Enter)
        {
            // [Enter]를 누르면, WinBattle에서 '미뤄뒀던' 다음 상태로 진행
            if (equipmentDropQueue.Count > 0)
            {
                // 1순위: 장비 비교 창
                currentLootEquipment = equipmentDropQueue.Dequeue();
                currentState = GameState.LootDrop;
            }
            else if (currentLootList.Count > 0)
            {
                // 2순위: 아이템 요약 창
                currentState = GameState.LootSummary;
            }
            else
            {
                // (둘 다 없으면 월드로)
                currentBattleMonster = null; 
                currentState = GameState.World;
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
        // 1. key.Key 확인 (영문 모드 + ESC)
        if (key.Key == ConsoleKey.C || key.Key == ConsoleKey.B || key.Key == ConsoleKey.Escape)
        {
            currentState = GameState.World;
            return;
        }

        // 2. key.KeyChar 확인 (한글 모드 Fallback)
        char c = char.ToUpper(key.KeyChar);
        if (c == 'C' || c == 'ㅊ' || c == 'B' || c == 'ㅠ')
        {
            currentState = GameState.World;
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
        if (isOpen) // (Yes)
        {
            chestAnimStartTime = DateTime.Now; 
            currentState = GameState.Chest_Opening;
        }
        else // (No)
        {
            AddLog("상자를 열지 않았습니다.");
            currentTargetChest = null;
            currentState = GameState.World;
        }
    }

    // [신규] 2. 상자 열기 애니메이션 처리 (매 프레임 호출)
    private void ProcessChestOpeningAnimation()
    {
        // 애니메이션 타이머가 만료될 때까지 대기
        TimeSpan elapsed = DateTime.Now - chestAnimStartTime;

        if (elapsed.TotalMilliseconds >= CHEST_ANIM_TOTAL_MS)
        {
            // 0.6초 경과: 애니메이션 종료
            
            // 1. 임시로 월드 상태로 변경 (Open()이 상태를 덮어쓸 수 있도록)
            currentState = GameState.World; 
            
            // 2. 현재 상자 정보를 가져오고 필드 초기화
            Chest? chest = currentTargetChest;
            currentTargetChest = null;
            
            if (chest != null)
            {
                // [수정] currentStage 변수를 Open 메서드에 전달합니다.
                chest.Open(player, this, rand, currentStage); 
            }
        }
        // (애니메이션이 진행 중일 때는 아무것도 하지 않고 렌더링만 대기)
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

        for (int row = y; row < y + height; row++)
        {
            for (int col = x; col < x + width; col++)
            {
                // 화면 경계 체크
                if (row >= 0 && row < screenHeight && col >= 0 && col < screenWidth)
                {
                    // ' ' 문자를 지정된 '배경색'으로 채웁니다.
                    screenBuffer[row, col] = new ScreenCell(' ', color, color);
                }
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
        // 1. 맵 크기 동적 설정 (Start()에서 이동)
        int viewportWidth = screenWidth - 2;
        int viewportHeight = worldMapHeight - 2;
        MapWidth = viewportWidth;   
        MapHeight = viewportHeight; 

        // 2. [수정] 플레이어 생성
        player = new Player(selectedClass);
        
        // 3. [수정] 데이터 리스트 초기화 (이전 코드는 player 생성 전에 호출됨)
        InitializeGameData(); // 몬스터, 방 등 리스트 클리어

        // 4. [수정] 첫 스테이지 로드
        TransitionToStage(1); // (이 안에서 InitializeMap() 호출)
        
        // [삭제] AddLog(...) (TransitionToStage가 처리)
    }

    // [신규] 3. 메인 메뉴 입력 처리
    private void ProcessMainMenuInput(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.UpArrow)
        {
            mainMenuIndex = (mainMenuIndex - 1 + 3) % 3; // (0 -> 2 -> 1 -> 0)
        }
        else if (key.Key == ConsoleKey.DownArrow)
        {
            mainMenuIndex = (mainMenuIndex + 1) % 3; // (0 -> 1 -> 2 -> 0)
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            switch (mainMenuIndex)
            {
                case 0: // 1. 게임 시작 (직업 선택)
                    PlayerClass selectedClass = ChooseClass(); // [수정]

                    // (ChooseClass가 취소(null?)될 수도 있지만,
                    // 현재 ChooseClass는 취소가 없으므로 바로 초기화)

                    InitializeGameData(selectedClass);
                    break;
                case 1: // 2. 조작법
                    currentState = GameState.HowToPlay;
                    break;
                case 2: // 3. 게임 종료
                    gameRunning = false;
                    break;
            }
        }
    }

    // [신규] 4. 조작법 창 입력 처리
    private void ProcessHowToPlayInput(ConsoleKeyInfo key)
    {
        // 1. key.Key 확인 (영문 모드 + Non-IME)
        if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.B || key.Key == ConsoleKey.Enter)
        {
            currentState = GameState.MainMenu;
            return;
        }

        // 2. key.KeyChar 확인 (한글 모드 Fallback)
        char c = char.ToUpper(key.KeyChar);
        if (c == 'B' || c == 'ㅠ')
        {
            currentState = GameState.MainMenu;
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
        string[] menuItems = { "게임 시작", "조작법", "게임 종료" };
        int menuY = titleY + titleHeight + 4; // 타이틀 + 4칸 아래

        for (int i = 0; i < menuItems.Length; i++)
        {
            string item = menuItems[i];
            ConsoleColor color = ConsoleColor.DarkGray;

            if (i == mainMenuIndex)
            {
                // 선택된 항목
                item = $"► {item} ◄";
                color = ConsoleColor.Yellow;
            }
            
            int itemX = screenWidth / 2 - GetDisplayWidth(item) / 2;
            DrawTextToBuffer(itemX, menuY + (i * 2), item, color); // 2칸씩 띄움
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
    #endregion

    #endregion
}