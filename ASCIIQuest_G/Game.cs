// Game.cs
using System.Text;
using System.Threading; 

public class Game
{
    // 게임 상태
    internal enum GameState
    {
        World,  // 맵 탐험
        Battle, // 전투 메인 메뉴
        Battle_SkillSelect, // 전투 스킬 선택
        GameOver // 게임 오버
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
            Char = c;
            FgColor = fg;
            BgColor = bg;
        }
    }

    // 게임 데이터
    private Player player = null!;
    private List<Monster> monsters = null!;
    private char[,] map = null!;
    private List<string> logMessages = new List<string>();

    // 상태 관리 변수
    private GameState currentState = GameState.World; 
    private Monster? currentBattleMonster = null; 
    private Random rand = new Random(); 

    // [핵심 수정] 맵 크기 확장
    private const int MapWidth = 40;
    private const int MapHeight = 20;

    // 화면 버퍼
    private ScreenCell[,] screenBuffer = null!; 
    private int screenWidth;
    private int screenHeight;
    
    // 레이아웃 변수
    private int logWindowHeight = 10;       
    private int playerStatusHeight = 8;     
    private int battleArtHeight;            
    private int logWindowY;                 
    private int playerStatusY;              

    // 게임 시작
    public void Start()
    {
        PlayerClass selectedClass = ChooseClass();
        InitializeConsole(); 

        player = new Player(selectedClass);
        player.X = 5; 
        player.Y = 5;
        InitializeMap();
        
        // (신규) 몬스터 추가
        monsters = new List<Monster>
        {
            new Monster("데이터 덩어리", 10, 10, 50, 5, 2, 'M', 50),
            new Monster("고블린", 20, 15, 30, 3, 1, 'G', 30),
            new Monster("슬라임", 30, 5, 20, 2, 0, 'S', 20)
        };

        AddLog($"'{player.Class}'(이)가 ASCII 미궁에 입장했습니다.");
        RunGameLoop(); 
    }

    // 직업 선택 UI
    private PlayerClass ChooseClass()
    {
        Console.Clear(); 
        Console.WriteLine("직업을 선택하세요:");
        Console.WriteLine("1. Warrior (시스템 방어자)");
        Console.WriteLine("2. Wizard (버그 수정자)");
        Console.WriteLine("3. Rogue (정보 수집가)");

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(true);
            switch (key.KeyChar)
            {
                case '1': return PlayerClass.Warrior;
                case '2': return PlayerClass.Wizard;
                case '3': return PlayerClass.Rogue;
            }
        }
    }

    // 콘솔 초기화
    private void InitializeConsole()
    {
        Console.CursorVisible = false;
        Console.Clear(); 
        UpdateScreenSize(true); 
    }

    // [핵심 수정] 맵 생성 (장애물, 함정 추가)
    private void InitializeMap()
    {
        map = new char[MapWidth, MapHeight];
        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                if (x == 0 || x == MapWidth - 1 || y == 0 || y == MapHeight - 1)
                {
                    map[x, y] = '█'; // 외벽
                }
                else
                {
                    map[x, y] = ' '; // 바닥
                }
            }
        }
        
        // (신규) 장애물 (내부 벽)
        for (int y = 5; y < 15; y++)
        {
            map[15, y] = '█'; 
        }
        for (int x = 25; x < 35; x++)
        {
            map[x, 8] = '█';
        }

        // (신규) 함정
        map[10, 5] = '^'; // 데미지 함정
        map[12, 12] = '^'; // 데미지 함정
        map[30, 10] = '*'; // 몬스터 함정
    }

    // 메인 게임 루프
    private void RunGameLoop()
    {
        bool gameRunning = true;
        bool needsRender = true; 

        while (gameRunning)
        {
            if (UpdateScreenSize(false))
            {
                needsRender = true; 
            }

            if (Console.KeyAvailable)
            {
                ConsoleKeyInfo key = Console.ReadKey(true); 
                needsRender = true; 

                switch (currentState)
                {
                    case GameState.World:
                        ProcessWorldInput(key); 
                        break;
                    case GameState.Battle:
                        ProcessBattleInput(key); 
                        break;
                    case GameState.Battle_SkillSelect: 
                        ProcessSkillSelectInput(key);
                        break;
                    case GameState.GameOver:
                        gameRunning = false; 
                        break;
                }

                if (player.HP <= 0 && currentState != GameState.GameOver)
                {
                    currentState = GameState.GameOver;
                    AddLog("플레이어가 쓰러졌다...");
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
    }

    // 창 크기 변경 감지
    private bool UpdateScreenSize(bool force)
    {
        if (force || screenWidth != Console.WindowWidth || screenHeight != Console.WindowHeight)
        {
            screenWidth = Math.Max(1, Console.WindowWidth);
            screenHeight = Math.Max(1, Console.WindowHeight); 
            
            Console.Clear(); // 리사이즈 시 잔상 제거

            screenBuffer = new ScreenCell[screenHeight, screenWidth];
            
            // 레이아웃 동적 계산
            logWindowY = screenHeight - logWindowHeight;
            playerStatusY = screenHeight - logWindowHeight - playerStatusHeight;
            battleArtHeight = playerStatusY; 
            
            return true; 
        }
        return false; 
    }

    // 렌더링 총괄
    private void Render()
    {
        ClearBuffer(); 

        switch (currentState) 
        {
            case GameState.World:
                DrawWorldLayout();
                break;
            case GameState.Battle:
            case GameState.Battle_SkillSelect: 
                DrawBattleLayout(); 
                break;
            case GameState.GameOver:
                DrawGameOverLayout();
                break;
        }
    }

    // 버퍼 클리어
    private void ClearBuffer()
    {
        for (int y = 0; y < screenHeight; y++)
        {
            for (int x = 0; x < screenWidth; x++)
            {
                screenBuffer[y, x] = ScreenCell.Empty;
            }
        }
    }

    // --- 레이아웃 그리기 함수들 ---

    // 1. 월드(맵) 레이아웃 그리기
    private void DrawWorldLayout()
    {
        int mapWindowWidth = MapWidth + 2; 
        int mapWindowHeight = MapHeight + 2; 
        int infoWindowX = mapWindowWidth + 1;
        int infoWindowWidth = Math.Max(0, screenWidth - infoWindowX);

        DrawBox(0, 0, mapWindowWidth, mapWindowHeight, "ASCII 미궁");
        DrawBox(infoWindowX, 0, infoWindowWidth, mapWindowHeight, "플레이어 정보");
        DrawBox(0, logWindowY, screenWidth, logWindowHeight, "Log");
        
        DrawMapToBuffer(mapWindowWidth, mapWindowHeight); 
        DrawInfoToBuffer(infoWindowX + 2, 2); 
        DrawLogRegion(0, logWindowY, screenWidth, logWindowHeight); 
    }

    // 2. 전투 레이아웃 그리기 (3단 분리)
    private void DrawBattleLayout()
    {
        if (currentBattleMonster == null) return; 
        
        DrawBox(0, 0, screenWidth, battleArtHeight, "Battle Stage");
        DrawBattleArt();
        DrawBox(0, playerStatusY, screenWidth, playerStatusHeight, "Player Status & Actions");
        DrawBattlePlayerStatus(); 
        DrawBox(0, logWindowY, screenWidth, logWindowHeight, "Log");
        DrawLogRegion(0, logWindowY, screenWidth, logWindowHeight);
    }

    // 전투 아트 그리기 (상단)
    private void DrawBattleArt()
    {
        if (currentBattleMonster == null) return;

        // 플레이어 아트 (왼쪽)
        int playerX = screenWidth / 4; 
        int artY = Math.Max(1, battleArtHeight / 2 - 2); 
        DrawTextToBuffer(playerX - 2, artY, @"  @  ", ConsoleColor.Green);
        DrawTextToBuffer(playerX - 2, artY+1, @" /|\ ", ConsoleColor.Green);
        DrawTextToBuffer(playerX - 2, artY+2, @" / \ ", ConsoleColor.Green);
        
        // 몬스터 아트 (오른쪽)
        int monsterX = (screenWidth * 3) / 4; 
        string[] monsterArt = {
            @"/----\",
            @"| M  M |",
            @"|  --  |",
            @"\----/"
        };
        for(int i = 0; i < monsterArt.Length; i++) {
            DrawTextToBuffer(monsterX - monsterArt[i].Length / 2, artY + i, monsterArt[i], ConsoleColor.Red);
        }
        
        string monsterHP = $"{currentBattleMonster.Name} HP: {currentBattleMonster.HP}/{currentBattleMonster.MaxHP}";
        DrawTextToBuffer(monsterX - (monsterHP.Length / 2), artY + 5, monsterHP, ConsoleColor.Yellow);
        DrawBarToBuffer(monsterX - 10, artY + 6, currentBattleMonster.HP, currentBattleMonster.MaxHP, 20, ConsoleColor.Red);
    }
    
    // [핵심 수정] 전투 시 플레이어 상태/메뉴 그리기 (UI 정렬)
    private void DrawBattlePlayerStatus()
    {
        int x = 2;
        int y = playerStatusY + 2; // 중단 박스 내부

        // --- 왼쪽: 전투 메뉴 ---
        if (currentState == GameState.Battle)
        {
            DrawTextToBuffer(x, y, "1. 기본 공격", ConsoleColor.White);
            DrawTextToBuffer(x, y + 1, "2. 스킬", ConsoleColor.White);
            DrawTextToBuffer(x, y + 2, "3. 아이템", ConsoleColor.Gray);
            DrawTextToBuffer(x, y + 3, "4. 후퇴", ConsoleColor.White);
        }
        else if (currentState == GameState.Battle_SkillSelect)
        {
            // (신규) Player.cs의 스킬 리스트에서 읽어와 표시
            DrawTextToBuffer(x, y + 5, "[B] 뒤로가기", ConsoleColor.Yellow);
            if (player.Skills.Count > 0)
            {
                var s1 = player.Skills[0];
                DrawTextToBuffer(x, y, $"[Q] {s1.Name.PadRight(15)} (MP {s1.MpCost})", player.MP >= s1.MpCost ? ConsoleColor.White : ConsoleColor.DarkGray);
            }
            if (player.Skills.Count > 1)
            {
                var s2 = player.Skills[1];
                DrawTextToBuffer(x, y + 1, $"[W] {s2.Name.PadRight(15)} (MP {s2.MpCost})", player.MP >= s2.MpCost ? ConsoleColor.White : ConsoleColor.DarkGray);
            }
            if (player.Skills.Count > 2)
            {
                var s3 = player.Skills[2];
                DrawTextToBuffer(x, y + 2, $"[E] {s3.Name.PadRight(15)} (MP {s3.MpCost})", player.MP >= s3.MpCost ? ConsoleColor.White : ConsoleColor.DarkGray);
            }
        }

        // --- 오른쪽: 플레이어 스탯 (UI 정렬) ---
        int statX = screenWidth / 2;
        string hpLabel = "HP:".PadRight(4); // 4칸 확보
        string mpLabel = "MP:".PadRight(4); // 4칸 확보

        DrawTextToBuffer(statX, y, $"{hpLabel} {player.HP} / {player.MaxHP}", ConsoleColor.White);
        DrawBarToBuffer(statX + 4, y, player.HP, player.MaxHP, 15, ConsoleColor.Red);
        DrawTextToBuffer(statX, y + 1, $"{mpLabel} {player.MP} / {player.MaxMP}", ConsoleColor.White);
        DrawBarToBuffer(statX + 4, y + 1, player.MP, player.MaxMP, 15, ConsoleColor.Blue);
        
        DrawTextToBuffer(statX, y + 3, $"STR: {player.STR} | INT: {player.INT} | DEX: {player.DEX}", ConsoleColor.Yellow);
        DrawTextToBuffer(statX, y + 4, $"ATK: {player.ATK} | DEF: {player.DEF}", ConsoleColor.White);
    }

    // 3. 게임 오버 레이아웃 그리기
    private void DrawGameOverLayout()
    {
        string msg = "GAME OVER";
        DrawTextToBuffer(screenWidth/2 - msg.Length/2, screenHeight/2, msg, ConsoleColor.Red);
        string msg2 = "아무 키나 눌러 종료합니다.";
        DrawTextToBuffer(screenWidth/2 - msg2.Length/2, screenHeight/2 + 1, msg2, ConsoleColor.Gray);
        
        DrawBox(0, logWindowY, screenWidth, logWindowHeight, "Final Log");
        DrawLogRegion(0, logWindowY, screenWidth, logWindowHeight);
    }
    
    // --- 월드/공용 그리기 함수 ---
    private void DrawMapToBuffer(int mapBoxWidth, int mapBoxHeight)
    {
        int drawHeight = Math.Min(MapHeight, mapBoxHeight - 2);
        int drawWidth = Math.Min(MapWidth, mapBoxWidth - 2);

        for (int y = 0; y < drawHeight; y++)
        {
            for (int x = 0; x < drawWidth; x++)
            {
                char tile = map[x, y];
                ConsoleColor color = ConsoleColor.Gray;
                if (tile == '█') color = ConsoleColor.DarkGray;
                else if (tile == '^' || tile == '*') color = ConsoleColor.Red; // 함정 색상
                
                DrawToBuffer(x + 1, y + 1, tile, color);
            }
        }
        
        foreach(var monster in monsters)
        {
            if (monster != currentBattleMonster && monster.X < drawWidth && monster.Y < drawHeight)
            {
                DrawToBuffer(monster.X + 1, monster.Y + 1, monster.Icon, ConsoleColor.Red);
            }
        }
        if(player.X < drawWidth && player.Y < drawHeight)
        {
            DrawToBuffer(player.X + 1, player.Y + 1, '@', ConsoleColor.Green);
        }
    }

    // [수정] DrawInfoToBuffer (레벨, 경험치 추가)
    private void DrawInfoToBuffer(int x, int y)
    {
        DrawTextToBuffer(x, y, $"직업: {player.Class}", ConsoleColor.Cyan);
        DrawTextToBuffer(x, y + 1, $"HP: {player.HP} / {player.MaxHP}", ConsoleColor.White);
        DrawBarToBuffer(x + 4, y + 1, player.HP, player.MaxHP, 15, ConsoleColor.Red);
        DrawTextToBuffer(x, y + 2, $"MP: {player.MP} / {player.MaxMP}", ConsoleColor.White);
        DrawBarToBuffer(x + 4, y + 2, player.MP, player.MaxMP, 15, ConsoleColor.Blue);
        DrawTextToBuffer(x, y + 4, $"STR: {player.STR} | INT: {player.INT} | DEX: {player.DEX}", ConsoleColor.Yellow);
        DrawTextToBuffer(x, y + 5, $"ATK: {player.ATK} | DEF: {player.DEF}", ConsoleColor.White);
        
        DrawTextToBuffer(x, y + 7, $"LV: {player.Level}", ConsoleColor.Green);
        DrawTextToBuffer(x, y + 8, $"EXP: {player.EXP} / {player.EXPNext}", ConsoleColor.Gray);
        DrawBarToBuffer(x + 5, y + 8, player.EXP, player.EXPNext, 15, ConsoleColor.Green);
    }

    // 범용 로그 그리기 함수
    private void DrawLogRegion(int x, int y, int width, int height)
    {
        int logX = x + 2;
        int logY = y + 2;
        int logWidth = Math.Max(0, width - 4);
        int logHeight = Math.Max(0, height - 3);

        int maxLines = logHeight;
        int logCount = logMessages.Count;
        
        for (int i = 0; i < maxLines; i++)
        {
            int logIndex = logCount - maxLines + i;
            if (logIndex >= 0)
            {
                string log = logMessages[logIndex];
                if (log.Length > logWidth) { log = log.Substring(0, logWidth); }
                DrawTextToBuffer(logX, logY + i, log, ConsoleColor.White);
            }
        }
    }


    // 버퍼 출력
    private void PrintBufferToConsole()
    {
        try
        {
            Console.SetCursorPosition(0, 0); 
            
            ConsoleColor lastFg = Console.ForegroundColor;
            ConsoleColor lastBg = Console.BackgroundColor;

            for (int y = 0; y < screenHeight; y++)
            {
                for (int x = 0; x < screenWidth; x++)
                {
                    var cell = screenBuffer[y, x];
                    if (cell.FgColor != lastFg)
                    {
                        Console.ForegroundColor = cell.FgColor;
                        lastFg = cell.FgColor;
                    }
                    if (cell.BgColor != lastBg)
                    {
                        Console.BackgroundColor = cell.BgColor;
                        lastBg = cell.BgColor;
                    }
                    
                    if (x < screenWidth - 1 || y < screenHeight - 1)
                    {
                         Console.Write(cell.Char);
                    }
                }
                
                if (y < screenHeight - 1)
                {
                    Console.SetCursorPosition(0, y + 1);
                }
            }
            Console.ResetColor();
        }
        catch (IOException) { }
        catch (ArgumentOutOfRangeException) { }
    }

    // --- 렌더링 헬퍼 ---
    private void DrawToBuffer(int x, int y, char c, ConsoleColor fg = ConsoleColor.White, ConsoleColor bg = ConsoleColor.Black)
    {
        if (y >= 0 && y < screenHeight && x >= 0 && x < screenWidth)
        {
            screenBuffer[y, x] = new ScreenCell(c, fg, bg);
        }
    }
    private void DrawTextToBuffer(int x, int y, string text, ConsoleColor fg = ConsoleColor.White, ConsoleColor bg = ConsoleColor.Black)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (y >= 0 && y < screenHeight && (x + i) >= 0 && (x + i) < screenWidth)
            {
                screenBuffer[y, x + i] = new ScreenCell(text[i], fg, bg);
            }
        }
    }
    private void DrawBox(int x, int y, int width, int height, string title)
    {
        if (width <= 2 || height <= 2 || x + width > screenWidth || y + height > screenHeight) return;
        int endX = x + width - 1;
        int endY = y + height - 1;
        ConsoleColor boxColor = ConsoleColor.DarkGray;
        DrawToBuffer(x, y, '╔', boxColor);
        DrawToBuffer(endX, y, '╗', boxColor);
        DrawToBuffer(x, endY, '╚', boxColor);
        DrawToBuffer(endX, endY, '╝');
        for (int i = x + 1; i < endX; i++)
        {
            DrawToBuffer(i, y, '═', boxColor);
            DrawToBuffer(i, endY, '═', boxColor);
        }
        for (int i = y + 1; i < endY; i++)
        {
            DrawToBuffer(x, i, '║', boxColor);
            DrawToBuffer(endX, i, '║', boxColor);
        }
        DrawTextToBuffer(x + 2, y, $" {title} ", ConsoleColor.White);
    }
    private void DrawBarToBuffer(int x, int y, int current, int max, int width, ConsoleColor filledColor)
    {
        DrawToBuffer(x, y, '[', ConsoleColor.DarkGray);
        float percent = (max == 0) ? 0 : (float)current / max;
        int filledWidth = (int)(percent * width);
        for (int i = 0; i < width; i++)
        {
            if (i < filledWidth)
                DrawToBuffer(x + 1 + i, y, '█', filledColor); 
            else
                DrawToBuffer(x + 1 + i, y, '░', ConsoleColor.DarkGray); 
        }
        DrawToBuffer(x + width + 1, y, ']', ConsoleColor.DarkGray);
    }

    // --- 게임 로직 ---
    private void AddLog(string message)
    {
        logMessages.Add(message);
        if (logMessages.Count > 50) 
        {
            logMessages.RemoveAt(0);
        }
    }
    
    // (신규) 전투 시작 공용 함수
    private void StartBattle(Monster monster)
    {
        AddLog($"야생의 {monster.Name}이(가) 나타났다!");
        currentBattleMonster = monster;
        currentState = GameState.Battle; // 상태 변경
    }

    // 맵 이동 입력 처리
    private void ProcessWorldInput(ConsoleKeyInfo key)
    {
        int newX = player.X;
        int newY = player.Y;

        switch (key.Key)
        {
            case ConsoleKey.W: newY--; break;
            case ConsoleKey.S: newY++; break;
            case ConsoleKey.A: newX--; break;
            case ConsoleKey.D: newX++; break;
            default: return; 
        }

        if (newX < 0 || newX >= MapWidth || newY < 0 || newY >= MapHeight)
        {
            AddLog("더 이상 갈 수 없는 곳입니다.");
            return; 
        }

        // [핵심 수정] 장애물 및 함정 처리
        char tile = map[newX, newY];
        if (tile == '█') 
        {
            AddLog("벽에 부딪혔습니다.");
            return; 
        }
        
        if (tile == '^') // 데미지 함정
        {
            player.HP -= 5;
            AddLog("날카로운 함정을 밟았다! (HP -5)");
            map[newX, newY] = ' '; // 함정 제거
            if (player.HP <= 0) return; // 함정 밟고 죽음
        }
        
        if (tile == '*') // 몬스터 함정
        {
            AddLog("함정이다! 숨어있던 몬스터가 공격한다!");
            map[newX, newY] = ' '; // 함정 제거
            // 함정 몬스터 생성 (맵 리스트와 무관)
            StartBattle(new Monster("함정 거미", 0, 0, 25, 4, 0, 'S', 20));
            return; // 이동은 하지 않음
        }

        // 일반 몬스터와 충돌
        Monster? target = monsters.Find(m => m.X == newX && m.Y == newY);
        if (target != null)
        {
            StartBattle(target); // 전투 시작
            return; 
        }

        player.X = newX;
        player.Y = newY;
        
        ProcessMonsterTurn_World();
    }

    // 전투 커맨드 입력 처리
    private void ProcessBattleInput(ConsoleKeyInfo key)
    {
        if (currentBattleMonster == null)
        {
            currentState = GameState.World; 
            return;
        }

        switch (key.Key)
        {
            case ConsoleKey.D1: // 1. 기본 공격
                AttackMonster(player, currentBattleMonster);
                
                if (currentBattleMonster.HP <= 0) WinBattle(); 
                else ProcessMonsterTurn_Battle();
                break;
            
            case ConsoleKey.D2: // 2. 스킬
                AddLog("사용할 스킬을 선택하세요: [Q], [W], [E] (뒤로가기: B)");
                currentState = GameState.Battle_SkillSelect; 
                break;
                
            case ConsoleKey.D3: // 3. 아이템
                AddLog("아이템 가방이 비어있습니다!");
                break;
                
            case ConsoleKey.D4: // 4. 후퇴
                FleeBattle();
                break;
        }
    }

    // 스킬 선택 입력 처리
    private void ProcessSkillSelectInput(ConsoleKeyInfo key)
    {
        bool skillUsed = false;
        switch (key.Key)
        {
            case ConsoleKey.Q:
                skillUsed = UseSkill(0); // Q (첫 번째 스킬)
                break;
            case ConsoleKey.W:
                skillUsed = UseSkill(1); // W (두 번째 스킬)
                break;
            case ConsoleKey.E:
                skillUsed = UseSkill(2); // E (세 번째 스킬)
                break;
            case ConsoleKey.B:
                currentState = GameState.Battle; // B (뒤로가기)
                AddLog("행동을 선택하세요.");
                break;
        }

        if (skillUsed)
        {
            currentState = GameState.Battle; 
            if (currentBattleMonster != null && currentBattleMonster.HP > 0)
            {
                ProcessMonsterTurn_Battle(); // 몬스터 턴 진행
            }
            else if (currentBattleMonster != null && currentBattleMonster.HP <= 0)
            {
                WinBattle(); 
            }
        }
    }
    
    // [핵심 수정] 스킬 사용 로직 (Player.cs의 SkillData 사용)
    private bool UseSkill(int skillIndex)
    {
        if (currentBattleMonster == null) return false;
        if (skillIndex >= player.Skills.Count)
        {
            AddLog("해당 슬롯에 스킬이 없습니다.");
            return false;
        }
        
        SkillData skill = player.Skills[skillIndex]; // Player.cs에서 스킬 정보 가져오기
        int mpCost = skill.MpCost;
        
        if (player.MP < mpCost) 
        { 
            AddLog("MP가 부족합니다!"); 
            return false; 
        }
        
        player.MP -= mpCost; // MP 선 소모
        int damage = 0;
        
        // 스킬 이름에 따라 로직 수행 (Game.cs가 로직 담당)
        switch (skill.Name)
        {
            // --- 전사 ---
            case "파워 스트라이크":
                damage = (player.ATK + player.STR) * 2 - currentBattleMonster.DEF;
                AddLog($"전사: 파워 스트라이크! {damage}의 데미지!");
                break;
            case "방패 치기":
                damage = player.DEF * 2 - currentBattleMonster.DEF;
                AddLog($"전사: 방패 치기! {damage}의 데미지!");
                break;
            case "사기 진작":
                player.ATK += 2; // (임시 버프)
                AddLog($"전사: 사기 진작! 공격력이 2 증가했다!");
                return true; // 몬스터 턴으로
            
            // --- 마법사 ---
            case "파이어볼":
                damage = player.INT * 3 - currentBattleMonster.DEF;
                AddLog($"마법사: 파이어볼! {damage}의 데미지!");
                break;
            case "힐":
                int heal = player.INT * 2;
                player.HP = Math.Min(player.MaxHP, player.HP + heal);
                AddLog($"마법사: 힐! HP를 {heal}만큼 회복!");
                return true; // 몬스터 턴으로
            case "매직 미사일":
                damage = player.INT + 5 - currentBattleMonster.DEF;
                AddLog($"마법사: 매직 미사일! {damage}의 데미지!");
                break;

            // --- 도적 ---
            case "백스탭":
                damage = (player.ATK + player.DEX) * 2 - currentBattleMonster.DEF;
                AddLog($"도적: 백스탭! {damage}의 데미지!");
                break;
            case "독 찌르기":
                damage = player.DEX; 
                AddLog($"도적: 독 찌르기! {damage}의 데미지!");
                break;
            case "퀵 어택":
                damage = player.ATK + player.DEX - currentBattleMonster.DEF;
                AddLog($"도적: 퀵 어택! {damage}의 데미지!");
                break;
        }

        if (damage < 0) damage = 0;
        currentBattleMonster.HP -= damage;
        return true; // 스킬 사용 성공
    }

    // 전투 승리 처리
    private void WinBattle()
    {
        if (currentBattleMonster == null) return;
        
        AddLog($"{currentBattleMonster.Name}을(를) 처리했습니다!");
        int expGained = currentBattleMonster.EXPReward;
        AddLog($"경험치를 {expGained} 획득했다!");
        
        // 맵에 있던 몬스터만 리스트에서 제거
        monsters.Remove(currentBattleMonster); 
        
        currentBattleMonster = null;
        currentState = GameState.World; 
        
        if (player.AddExperience(expGained))
        {
            AddLog($"LEVEL UP! {player.Level}레벨이 되었습니다!");
        }
    }

    // 후퇴 처리
    private void FleeBattle()
    {
        AddLog("무사히 도망쳤습니다!");
        // (신규) 함정 몬스터였을 경우를 대비해 몬스터 리스트에서 제거 시도
        if(currentBattleMonster != null)
        {
            monsters.Remove(currentBattleMonster);
        }
        currentBattleMonster = null; 
        currentState = GameState.World; 
    }

    // 몬스터 턴 (전투 시)
    private void ProcessMonsterTurn_Battle()
    {
        if (currentBattleMonster == null) return;

        var monster = currentBattleMonster;
        AddLog($"{monster.Name}의 턴!");
        int damage = Math.Max(0, monster.ATK - player.DEF);
        player.HP -= damage;
        AddLog($"{monster.Name}이(가) 플레이어를 공격! {damage}의 데미지!");
    }
    
    // 몬스터 턴 (월드, AI 이동)
    private void ProcessMonsterTurn_World()
    {
        foreach (var monster in monsters.ToList()) 
        {
            int move = rand.Next(0, 5); // 0:가만히, 1:W, 2:S, 3:A, 4:D
            
            int newX = monster.X;
            int newY = monster.Y;

            switch (move)
            {
                case 1: newY--; break;
                case 2: newY++; break;
                case 3: newX--; break;
                case 4: newX++; break;
                default: continue; 
            }

            if (newX <= 0 || newX >= MapWidth -1 || newY <= 0 || newY >= MapHeight - 1) continue;
            if (map[newX, newY] != ' ') continue; // 벽, 함정 등으로는 이동 불가
            if (player.X == newX && player.Y == newY) continue;
            if (monsters.Any(m => m != monster && m.X == newX && m.Y == newY)) continue;
            
            monster.X = newX;
            monster.Y = newY;
        }
    }

    // 전투 데미지 계산 로직
    private void AttackMonster(Player attacker, Monster target)
    {
        int damage = 0;

        switch (attacker.Class)
        {
            case PlayerClass.Warrior:
                damage = (attacker.ATK + attacker.STR + attacker.WeaponAttack) - target.DEF;
                AddLog("시스템 방어자: 물리적 오류(충돌) 방어!");
                break;
            case PlayerClass.Wizard:
                float intMultiplier = 1.0f + (attacker.INT / 100.0f);
                int magicDamage = (int)(attacker.WeaponAttack + (attacker.ATK * intMultiplier));
                damage = magicDamage - target.DEF;
                AddLog("버그 수정자: 주문으로 오류 수정!");
                break;
            case PlayerClass.Rogue:
                float dexMultiplier = 1.0f + (attacker.DEX / 100.0f);
                int initialDamage = (int)(attacker.WeaponAttack + (attacker.ATK * dexMultiplier)) - target.DEF;
                damage = initialDamage;
                float totalCritChance = attacker.CritChance + (attacker.DEX / 1000.0f); 
                if (rand.NextDouble() < totalCritChance)
                {
                    damage = (int)(initialDamage * 1.5); 
                    AddLog("정보 수집가: 핵심 데이터(크리티컬)!");
                }
                break;
        }

        if (damage < 0) damage = 0;
        target.HP -= damage;
        AddLog($"{attacker.Class}이(가) {target.Name}에게 {damage}의 데미지를 입혔습니다!");
    }
}