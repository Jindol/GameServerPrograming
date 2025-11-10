//
// ASCIIQuest_S/ASCIIQuest_S.cs (스레드 충돌 및 논리 오류 수정 버전)
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

// ASCIIQuest_G의 Game.cs에 있던 게임 상태 Enum
internal enum GameState
{
    World,  // 맵 탐험
    Battle, // 전투 메인 메뉴
    Battle_SkillSelect, // 전투 스킬 선택
    GameOver // 게임 오버
}

class MUDServer
{
    private TcpListener listener;
    private Dictionary<int, ClientSession> clients = new Dictionary<int, ClientSession>();
    private Random random = new Random();
    
    // [수정] 스레드 동기화를 위한 잠금 객체
    private object gameLock = new object();

    // --- ASCIIQuest_G (Game.cs)에서 가져온 게임 상태 변수 ---
    private Dictionary<int, Player> gamePlayers = new Dictionary<int, Player>();
    private List<Monster> monsters = new List<Monster>();
    private char[,] map = null!;
    private List<string> logMessages = new List<string>();
    private GameState currentState = GameState.World;
    private Monster? currentBattleMonster = null;
    private Player? currentPlayerTurn = null;

    private const int MapWidth = 40;
    private const int MapHeight = 20;
    private const int InfoBoxWidth = 34;
    private const int LogBoxHeight = 10;

    // ANSI 색상 코드
    private const string C_RESET = "\u001b[0m";
    private const string C_RED = "\u001b[31m";
    private const string C_GREEN = "\u001b[32m";
    private const string C_YELLOW = "\u001b[33m";
    private const string C_BLUE = "\u001b[34m";

    private static readonly Regex AnsiRegex = new Regex(@"\x1B\[[0-9;]*m", RegexOptions.Compiled);
    // --- (여기까지) Game.cs에서 가져온 변수 ---


    public MUDServer(int port)
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Console.WriteLine("Server started... (ASCIIQuest_G Integrated)");

        InitializeMap();
        InitializeMonsters();
        AddLog("ASCII 미궁 서버가 열렸습니다. 2명의 플레이어를 기다립니다.");

        StartAcceptingClients();
    }

    private void StartAcceptingClients()
    {
        listener.BeginAcceptTcpClient(new AsyncCallback(AcceptClientCallback), null);
    }

    private void AcceptClientCallback(IAsyncResult ar)
    {
        TcpClient tcpClient;
        try
        {
            tcpClient = listener.EndAcceptTcpClient(ar);
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("Listener closed.");
            return;
        }

        // [수정] client 변수를 lock 바깥에서 선언
        ClientSession client; 

        lock (gameLock)
        {
            if (clients.Count >= 2)
            {
                Console.WriteLine("Max players reached. Rejecting new connection.");
                byte[] rejectMsg = Encoding.UTF8.GetBytes("서버가 가득 찼습니다. 나중에 다시 시도해주세요.|\n");
                tcpClient.GetStream().Write(rejectMsg, 0, rejectMsg.Length);
                tcpClient.Close();

                StartAcceptingClients();
                return;
            }

            int clientId = tcpClient.Client.RemoteEndPoint.GetHashCode();
            
            // [수정] 변수에 ClientSession 객체를 할당
            client = new ClientSession(clientId, tcpClient); 
            clients[clientId] = client;
            
            Console.WriteLine($"Client {clientId} connected. Awaiting nickname...");
        
            // 닉네임 요청 (프롬프트 추가)
            SendMessage(client, "사용할 닉네임을 입력하세요:\n> ");
        } // [수정] 여기서 lock 해제

        StartAcceptingClients();
        
        // [핵심 수정] 딕셔너리를 다시 조회(clients[...])하는 대신,
        // 위에서 생성한 'client' 객체를 직접 전달합니다.
        StartReceivingData(client); 
    }

    private void StartReceivingData(ClientSession client)
    {
        Thread thread = new Thread(() =>
        {
            try
            {
                // [수정] 닉네임/직업 선택 단계에서는 여기서 ReadLine()을 직접 처리합니다.
                // 1. 닉네임 받기
                while (client.TcpClient.Connected && client.State == ClientState.ChoosingNickname)
                {
                    string data = client.Reader.ReadLine();
                    if (data == null) throw new Exception("연결 끊김");
                    HandleCommand(data, client.ClientId); // 닉네임 처리
                }

                // 2. 직업 받기
                while (client.TcpClient.Connected && client.State == ClientState.ChoosingClass)
                {
                    string data = client.Reader.ReadLine();
                    if (data == null) throw new Exception("연결 끊김");
                    HandleCommand(data, client.ClientId); // 직업 처리
                }

                // 3. 게임 플레이 중
                while (client.TcpClient.Connected && client.State == ClientState.Playing)
                {
                    string data = client.Reader.ReadLine();
                    if (data != null)
                    {
                        HandleCommand(data, client.ClientId);
                    }
                    else
                    {
                        HandleClientDisconnect(client.ClientId);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving data from {client.ClientId}: {ex.Message}");
                HandleClientDisconnect(client.ClientId);
            }
        });
        thread.IsBackground = true;
        thread.Start();
    }


    private void HandleClientDisconnect(int clientId)
    {
        // [수정] 공유 자원(clients, gamePlayers, logMessages 등) 접근 전 lock
        lock (gameLock)
        {
            if (!clients.ContainsKey(clientId)) return;

            ClientSession client = clients[clientId];
            clients.Remove(clientId);

            Player gamePlayer = null;
            if (gamePlayers.ContainsKey(clientId))
            {
                gamePlayer = gamePlayers[clientId];
                gamePlayers.Remove(clientId);
            }

            string username = (gamePlayer != null) ? gamePlayer.Username : $"Client {clientId}";
            Console.WriteLine($"{username} disconnected.");

            if (clients.Count < 2)
            {
                if (currentState != GameState.GameOver)
                {
                    BroadcastMessage($"{username} (이)가 접속을 종료했습니다. 파티가 해산됩니다.", true);
                    ResetGame(); // ResetGame도 내부에서 lock을 잡음
                }
            }

            client.Reader.Close();
            client.Writer.Close();
            client.TcpClient.Close();
        } // [수정] lock 해제
    }

    private void ResetGame()
    {
        // [수정] 게임의 모든 상태를 리셋하므로 lock
        lock (gameLock)
        {
            Console.WriteLine("Resetting game state, waiting for new players...");
            logMessages.Clear();
            monsters.Clear();
            gamePlayers.Clear();

            currentState = GameState.World;
            currentBattleMonster = null;
            currentPlayerTurn = null;

            InitializeMap();
            InitializeMonsters();
            AddLog("ASCII 미궁 서버가 재시작되었습니다. 2명의 플레이어를 기다립니다.");

            foreach (var client in clients.Values)
            {
                client.State = ClientState.ChoosingNickname;
                client.GamePlayer = null;
                client.TempNickname = null;
                client.NicknameBuffer.Clear();
                SendMessage(client, "다른 플레이어가 떠났습니다. 닉네임을 다시 입력하세요:\n> ");
            }
        } // [수정] lock 해제
    }


    private void HandleCommand(string data, int clientId)
    {
        // [수정] 모든 게임 로직/상태 변경의 진입점이므로 메서드 전체를 lock
        lock (gameLock)
        {
            if (!clients.ContainsKey(clientId)) return;

            ClientSession client = clients[clientId];
            string command = data.Trim();

            // --- 1. 닉네임 선택 상태 (서버 버퍼링) ---
            if (client.State == ClientState.ChoosingNickname)
            {
                string key = command.ToUpper();
                string currentPrompt = "사용할 닉네임을 입력하세요:\n> ";

                if (key == "ENTER")
                {
                    string nickname = client.NicknameBuffer.ToString().Trim();

                    if (string.IsNullOrWhiteSpace(nickname) || nickname.Length > 10 || nickname.Contains(" "))
                    {
                        client.NicknameBuffer.Clear();
                        SendMessage(client, "닉네임은 1~10자리의 공백 없는 문자여야 합니다. 다시 입력하세요:\n> ");
                        return; // [수정] return 추가
                    }

                    if (gamePlayers.Values.Any(p => p.Username.Equals(nickname, StringComparison.OrdinalIgnoreCase)) ||
                        clients.Values.Any(c => c.ClientId != clientId && c.TempNickname != null && c.TempNickname.Equals(nickname, StringComparison.OrdinalIgnoreCase)))
                    {
                        client.NicknameBuffer.Clear();
                        SendMessage(client, "이미 사용 중이거나 선택 중인 닉네임입니다. 다시 입력하세요:\n> ");
                        return; // [수정] return 추가
                    }

                    client.TempNickname = nickname;
                    client.State = ClientState.ChoosingClass;

                    string classSelectionMsg = $"반갑습니다, {nickname}님. 직업을 선택하세요:\n" +
                                                "1. Warrior (시스템 방어자)\n" +
                                                "2. Wizard (버그 수정자)\n" +
                                                "3. Rogue (정보 수집가)\n" +
                                                "(키보드 1, 2, 3 입력)";
                    SendMessage(client, classSelectionMsg);
                }
                else if (key == "BACKSPACE")
                {
                    if (client.NicknameBuffer.Length > 0)
                    {
                        client.NicknameBuffer.Remove(client.NicknameBuffer.Length - 1, 1);
                    }
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
                }
                else if (key.Length == 1 && (char.IsLetterOrDigit(key[0])))
                {
                    if (client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(key);
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
                }
                else if (key.StartsWith("D") && key.Length == 2 && char.IsDigit(key[1])) // D1, D2..
                {
                    if (client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(key[1]);
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
                }
                else if (key.StartsWith("NUMPAD") && key.Length == 7) // NUMPAD1
                {
                    if (client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(key[6]);
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
                }
                else if (key == "SPACEBAR")
                {
                    if (client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(" ");
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
                }
                return; // [수정] 닉네임 입력 상태에서는 항상 로직 종료
            }


            // --- 2. 직업 선택 상태 ---
            // [수정] else if 로 변경하여 닉네임 상태와 분리
            else if (client.State == ClientState.ChoosingClass)
            {
                PlayerClass selectedClass;
                string key = command.ToUpper();

                switch (key)
                {
                    case "D1": case "NUMPAD1":
                        selectedClass = PlayerClass.Warrior; break;
                    case "D2": case "NUMPAD2":
                        selectedClass = PlayerClass.Wizard; break;
                    case "D3": case "NUMPAD3":
                        selectedClass = PlayerClass.Rogue; break;
                    default:
                        string classSelectionMsg = $"잘못된 선택입니다. 1, 2, 3 중 입력.\n" +
                                                    $"반갑습니다, {client.TempNickname}님. 직업을 선택하세요:\n" +
                                                    "1. Warrior (시스템 방어자)\n" +
                                                    "2. Wizard (버그 수정자)\n" +
                                                    "3. Rogue (정보 수집가)\n" +
                                                    "(키보드 1, 2, 3 입력)";
                        SendMessage(client, classSelectionMsg);
                        return;
                }

                Player gamePlayer = new Player(selectedClass, client.TempNickname);
                gamePlayer.ClientId = clientId;
                (gamePlayer.X, gamePlayer.Y) = GetRandomSpawnPosition();
                gamePlayers[clientId] = gamePlayer;
                client.GamePlayer = gamePlayer;
                client.State = ClientState.Playing;

                SendMessage(client, $"당신은 '{selectedClass}'입니다. 다른 플레이어를 기다립니다...");
                Console.WriteLine($"[DEBUG] {gamePlayer.Username} (Client {clientId})가 직업 선택 완료. State=Playing으로 변경.");

                if (clients.Count == 2)
                {
                    Console.WriteLine("--- [DEBUG] 2명 접속됨. 상태 확인 ---");
                    try
                    {
                        var p1 = clients.Values.ElementAt(0);
                        var p2 = clients.Values.ElementAt(1);
                        Console.WriteLine($"[DEBUG] P1 ({p1.TempNickname}) State: {p1.State}");
                        Console.WriteLine($"[DEBUG] P2 ({p2.TempNickname}) State: {p2.State}");
                        Console.WriteLine($"[DEBUG] clients.Count == 2: {clients.Count == 2}");
                        Console.WriteLine($"[DEBUG] All(Playing)?: {clients.Values.All(c => c.State == ClientState.Playing)}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"[DEBUG] 로그 출력 중 오류: {e.Message}");
                    }
                    Console.WriteLine("-----------------------------------");
                }
                else
                {
                    Console.WriteLine($"[DEBUG] 현재 접속 인원: {clients.Count}명. 2명 대기 중.");
                    // 1명일 때는 대기 화면만 표시 (월드 화면은 감춤)
                    SendMessage(client, GetWaitingDisplay(gamePlayer));
                }


                if (clients.Count == 2 && clients.Values.All(c => c.State == ClientState.Playing))
                {
                    Console.WriteLine("[DEBUG] 조건 충족! 게임 시작! BroadcastWorldState() 호출.");
                    Console.WriteLine("Both players ready. Starting ASCIIQuest!");
                    AddLog("두 명의 플레이어가 파티를 맺었습니다. 모험을 시작합니다!");
                    currentState = GameState.World;
                    currentPlayerTurn = gamePlayers.Values.First();
                    // [수정] 게임 화면을 즉시 브로드캐스트
                    BroadcastWorldState();
                }
                else
                {
                    Console.WriteLine("[DEBUG] 조건 불충족. 게임 시작 대기 중.");
                }
                return; // [수정] 직업 선택 로직 후에는 항상 로직 종료
            }

            // --- 3. 플레이 중 상태 (키 입력 처리) ---
            // [수정] else if 로 변경하여 위 상태들과 분리
            else if (client.State == ClientState.Playing)
            {
                if (currentPlayerTurn == null || client.GamePlayer != currentPlayerTurn)
                {
                    return; // 턴이 아님
                }

                Player actingPlayer = client.GamePlayer;
                string key = command.ToUpper();

                switch (currentState)
                {
                    case GameState.World:
                        bool turnEnded = ProcessWorldCommand(actingPlayer, key);
                        if (turnEnded)
                        {
                            if (currentState == GameState.World)
                            {
                                ProcessMonsterTurn_World();
                                SwitchTurn();
                                BroadcastWorldState();
                            }
                        }
                        break;

                    case GameState.Battle:
                        ProcessBattleCommand(actingPlayer, key);
                        break;

                    case GameState.Battle_SkillSelect:
                        ProcessSkillSelectCommand(actingPlayer, key);
                        break;
                }
            }
        } // [수정] lock(gameLock) 종료
    }

    private void SwitchTurn()
    {
        if (gamePlayers.Count == 0) return;

        var playerList = gamePlayers.Values.ToList();
        if (playerList.Count == 0) return;

        int currentIndex = -1;
        if (currentPlayerTurn != null)
        {
            currentIndex = playerList.IndexOf(currentPlayerTurn);
        }

        for (int i = 1; i <= playerList.Count; i++)
        {
            Player nextPlayer = playerList[(currentIndex + i) % playerList.Count];
            if (nextPlayer.HP > 0)
            {
                currentPlayerTurn = nextPlayer;
                return;
            }
        }
        currentPlayerTurn = null;
    }

    private bool ProcessWorldCommand(Player player, string key)
    {
        switch (key)
        {
            case "W":
            case "UPARROW":
                return ProcessPlayerMove(player, "up");

            case "S":
            case "DOWNARROW":
                return ProcessPlayerMove(player, "down");

            case "A":
            case "LEFTARROW":
                return ProcessPlayerMove(player, "left");

            case "D":
            case "RIGHTARROW":
                return ProcessPlayerMove(player, "right");

            case "I":
                AddLog($"--- {player.Username} ({player.Class}) 상태 ---");
                AddLog($"LV:{player.Level} HP:{player.HP}/{player.MaxHP} MP:{player.MP}/{player.MaxMP}");
                AddLog($"ATK:{player.ATK} DEF:{player.DEF} STR:{player.STR} INT:{player.INT} DEX:{player.DEX}");
                BroadcastWorldState(); // 내부에서 2인 체크
                return false;

            default:
                return false;
        }
    }

    private bool ProcessPlayerMove(Player player, string direction)
    {
        int newX = player.X;
        int newY = player.Y;

        switch (direction)
        {
            case "up": newY--; break;
            case "down": newY++; break;
            case "left": newX--; break;
            case "right": newX++; break;
        }

        if (newX < 0 || newX >= MapWidth || newY < 0 || newY >= MapHeight)
        {
            AddLog("더 이상 갈 수 없는 곳입니다.");
            BroadcastWorldState();
            return false;
        }

        char tile = map[newX, newY];
        if (tile == '█')
        {
            AddLog("벽에 부딪혔습니다.");
            BroadcastWorldState();
            return false;
        }

        if (tile == '^')
        {
            player.HP -= 5;
            AddLog($"{player.Username}(이)가 날카로운 함정을 밟았다! (HP -5)");
            map[newX, newY] = ' ';
            if (player.HP <= 0) HandlePlayerDeath(player);
        }

        if (tile == '*')
        {
            AddLog("함정이다! 숨어있던 몬스터가 공격한다!");
            map[newX, newY] = ' ';
            StartBattle(new Monster("함정 거미", 0, 0, 25, 4, 0, 'S', 20));
            return true;
        }

        Monster? target = monsters.Find(m => m.X == newX && m.Y == newY);
        if (target != null)
        {
            StartBattle(target);
            return true;
        }

        if (gamePlayers.Values.Any(p => p != player && p.X == newX && p.Y == newY))
        {
            AddLog("다른 플레이어와 겹칠 수 없습니다.");
            BroadcastWorldState();
            return false;
        }

        player.X = newX;
        player.Y = newY;
        AddLog($"{player.Username}(이)가 ({newX}, {newY})로 이동.");
        return true;
    }

    private void ProcessBattleCommand(Player player, string key)
    {
        if (currentBattleMonster == null)
        {
            currentState = GameState.World;
            BroadcastWorldState();
            return;
        }

        switch (key)
        {
            case "D1":
            case "NUMPAD1":
                AttackMonster(player, currentBattleMonster);
                if (currentBattleMonster.HP <= 0) WinBattle();
                else EndPlayerBattleTurn();
                break;

            case "D2":
            case "NUMPAD2":
                currentState = GameState.Battle_SkillSelect;
                AddLog("사용할 스킬을 선택하세요: [Q], [W], [E] (뒤로가기: B)");
                BroadcastBattleState();
                break;

            case "D3":
            case "NUMPAD3":
                AddLog("아이템 가방이 비어있습니다!");
                BroadcastBattleState();
                break;

            case "D4":
            case "NUMPAD4":
                FleeBattle();
                break;
            default:
                break;
        }
    }

    private void ProcessSkillSelectCommand(Player player, string key)
    {
        bool skillUsed = false;
        switch (key)
        {
            case "Q": skillUsed = UseSkill(player, 0); break;
            case "W": skillUsed = UseSkill(player, 1); break;
            case "E": skillUsed = UseSkill(player, 2); break;
            case "B":
                currentState = GameState.Battle;
                AddLog("행동을 선택하세요.");
                BroadcastBattleState();
                break;
            default:
                break;
        }

        if (skillUsed)
        {
            currentState = GameState.Battle;
            if (currentBattleMonster != null && currentBattleMonster.HP > 0)
            {
                EndPlayerBattleTurn();
            }
            else if (currentBattleMonster != null && currentBattleMonster.HP <= 0)
            {
                WinBattle();
            }
        }
    }

    private void EndPlayerBattleTurn()
    {
        if (currentBattleMonster == null || currentBattleMonster.HP <= 0)
        {
            if (currentBattleMonster != null) WinBattle();
            return;
        }

        ProcessMonsterTurn_Battle();

        if (gamePlayers.Values.All(p => p.HP <= 0))
        {
            HandlePlayerDeath(gamePlayers.Values.First(p => p.HP <= 0));
        }
        else
        {
            SwitchTurn();
            BroadcastBattleState();
        }
    }


    private void InitializeMap()
    {
        map = new char[MapWidth, MapHeight];
        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                if (x == 0 || x == MapWidth - 1 || y == 0 || y == MapHeight - 1)
                    map[x, y] = '█';
                else
                    map[x, y] = ' ';
            }
        }
        for (int y = 5; y < 15; y++) map[15, y] = '█';
        for (int x = 25; x < 35; x++) map[x, 8] = '█';
        map[10, 5] = '^';
        map[12, 12] = '^';
        map[30, 10] = '*';
    }

    private void InitializeMonsters()
    {
        monsters = new List<Monster>
        {
            new Monster("데이터 덩어리", 10, 10, 50, 5, 2, 'M', 50),
            new Monster("고블린", 20, 15, 30, 3, 1, 'G', 30),
            new Monster("슬라임", 30, 5, 20, 2, 0, 'S', 20)
        };
    }

    private void AddLog(string message)
    {
        logMessages.Add(message ?? string.Empty);
        // 로그가 박스의 표시 가능한 줄 수를 초과하지 않도록 앞에서부터 삭제
        // LogBoxHeight: 박스 총 높이, 내부 영역 = 높이 - 2, 그 중 첫 줄은 헤더이므로 실제 로그 라인 = 내부 - 1
        int visibleLogLines = Math.Max(1, LogBoxHeight - 3);
        while (logMessages.Count > visibleLogLines)
        {
            logMessages.RemoveAt(0);
        }
    }

    private void StartBattle(Monster monster)
    {
        AddLog($"야생의 {monster.Name}이(가) 나타났다!");
        currentBattleMonster = monster;
        currentState = GameState.Battle;

        if (currentPlayerTurn == null || currentPlayerTurn.HP <= 0)
        {
            currentPlayerTurn = gamePlayers.Values.FirstOrDefault(p => p.HP > 0);
        }

        BroadcastBattleState();
    }

    private void WinBattle()
    {
        if (currentBattleMonster == null) return;

        AddLog($"{currentBattleMonster.Name}을(를) 처리했습니다!");
        int expGained = currentBattleMonster.EXPReward;
        AddLog($"경험치를 {expGained} 획득했다!");

        monsters.Remove(currentBattleMonster);

        currentBattleMonster = null;
        currentState = GameState.World;

        foreach (var player in gamePlayers.Values)
        {
            if (player.HP > 0)
            {
                if (player.AddExperience(expGained))
                {
                    AddLog($"LEVEL UP! {player.Username}({player.Class})(이)가 {player.Level}레벨이 되었습니다!");
                }
            }
            else
            {
                player.HP = player.MaxHP / 4;
                AddLog($"{player.Username}(이)가 전투 승리로 부활합니다!");
            }
        }

        if (currentPlayerTurn == null || currentPlayerTurn.HP <= 0)
        {
            currentPlayerTurn = gamePlayers.Values.FirstOrDefault(p => p.HP > 0);
        }

        BroadcastWorldState();
    }

    private void FleeBattle()
    {
        AddLog("무사히 도망쳤습니다!");
        if (currentBattleMonster != null)
        {
            if (currentBattleMonster.X == 0 && currentBattleMonster.Y == 0) // 함정 몬스터
            {
                monsters.Remove(currentBattleMonster);
            }
        }
        currentBattleMonster = null;
        currentState = GameState.World;

        if (currentPlayerTurn == null || currentPlayerTurn.HP <= 0)
        {
            currentPlayerTurn = gamePlayers.Values.FirstOrDefault(p => p.HP > 0);
        }

        BroadcastWorldState();
    }

    private void ProcessMonsterTurn_Battle()
    {
        if (currentBattleMonster == null) return;

        var alivePlayers = gamePlayers.Values.Where(p => p.HP > 0).ToList();
        if (alivePlayers.Count == 0) return;

        Player targetPlayer = alivePlayers[random.Next(alivePlayers.Count)];

        var monster = currentBattleMonster;
        AddLog($"{monster.Name}의 턴!");
        int damage = Math.Max(0, monster.ATK - targetPlayer.DEF);
        targetPlayer.HP -= damage;
        AddLog($"{monster.Name}이(가) {targetPlayer.Username}에게 {damage}의 데미지!");

        if (targetPlayer.HP <= 0)
        {
            HandlePlayerDeath(targetPlayer);
        }
    }

    private void HandlePlayerDeath(Player deadPlayer)
    {
        AddLog($"{deadPlayer.Username}({deadPlayer.Class})(이)가 쓰러졌다...");

        if (gamePlayers.Values.All(p => p.HP <= 0))
        {
            AddLog("파티가 전멸했습니다... 게임을 리셋합니다.");
            BroadcastMessage("--- GAME OVER --- \n 3초 후 재시작합니다...", true);
            currentState = GameState.GameOver;

            Timer resetTimer = null;
            resetTimer = new Timer((_) => {
                // [수정] 타이머 콜백(다른 스레드)에서 ResetGame을 호출하므로 lock
                lock (gameLock)
                {
                    ResetGame();
                }
                resetTimer?.Dispose();
            }, null, 3000, Timeout.Infinite);
        }
        else
        {
            if (currentState != GameState.Battle)
            {
                AddLog($"{deadPlayer.Username}(이)가 부활을 기다립니다. (전투 승리 시 부활)");
            }
            else
            {
                AddLog($"{deadPlayer.Username}(이)가 전투에서 쓰러졌습니다!");
            }
        }
    }


    private void ProcessMonsterTurn_World()
    {
        foreach (var monster in monsters.ToList())
        {
            int move = random.Next(0, 5);
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

            if (newX <= 0 || newX >= MapWidth - 1 || newY <= 0 || newY >= MapHeight - 1) continue;
            if (map[newX, newY] != ' ') continue;
            if (gamePlayers.Values.Any(p => p.X == newX && p.Y == newY)) continue;
            if (monsters.Any(m => m != monster && m.X == newX && m.Y == newY)) continue;

            monster.X = newX;
            monster.Y = newY;
        }
    }

    private bool UseSkill(Player player, int skillIndex)
    {
        if (currentBattleMonster == null) return false;
        if (skillIndex >= player.Skills.Count)
        {
            AddLog("해당 슬롯에 스킬이 없습니다.");
            return false;
        }

        SkillData skill = player.Skills[skillIndex];
        int mpCost = skill.MpCost;

        if (player.MP < mpCost)
        {
            AddLog("MP가 부족합니다!");
            return false;
        }

        player.MP -= mpCost;
        int damage = 0;
        string logPrefix = $"{player.Username}:";

        switch (skill.Name)
        {
            case "파워 스트라이크":
                damage = (player.ATK + player.STR) * 2 - currentBattleMonster.DEF;
                AddLog($"{logPrefix} 파워 스트라이크! {damage}의 데미지!");
                break;
            case "방패 치기":
                damage = player.DEF * 2 - currentBattleMonster.DEF;
                AddLog($"{logPrefix} 방패 치기! {damage}의 데미지!");
                break;
            case "사기 진작":
                player.ATK += 2;
                AddLog($"{logPrefix} 사기 진작! 공격력이 2 증가!");
                return true;
            case "파이어볼":
                damage = player.INT * 3 - currentBattleMonster.DEF;
                AddLog($"{logPrefix} 파이어볼! {damage}의 데미지!");
                break;
            case "힐":
                int heal = player.INT * 2;
                player.HP = Math.Min(player.MaxHP, player.HP + heal);
                AddLog($"{logPrefix} 힐! HP를 {heal}만큼 회복!");
                return true;
            case "매직 미사일":
                damage = player.INT + 5 - currentBattleMonster.DEF;
                AddLog($"{logPrefix} 매직 미사일! {damage}의 데미지!");
                break;
            case "백스탭":
                damage = (player.ATK + player.DEX) * 2 - currentBattleMonster.DEF;
                AddLog($"{logPrefix} 백스탭! {damage}의 데미지!");
                break;
            case "독 찌르기":
                damage = player.DEX;
                AddLog($"{logPrefix} 독 찌르기! {damage}의 데미지!");
                break;
            case "퀵 어택":
                damage = player.ATK + player.DEX - currentBattleMonster.DEF;
                AddLog($"{logPrefix} 퀵 어택! {damage}의 데미지!");
                break;
        }

        if (damage < 0) damage = 0;
        currentBattleMonster.HP -= damage;
        return true;
    }

    private void AttackMonster(Player attacker, Monster target)
    {
        int damage = 0;
        switch (attacker.Class)
        {
            case PlayerClass.Warrior:
                damage = (attacker.ATK + attacker.STR + attacker.WeaponAttack) - target.DEF;
                break;
            case PlayerClass.Wizard:
                float intMultiplier = 1.0f + (attacker.INT / 100.0f);
                int magicDamage = (int)(attacker.WeaponAttack + (attacker.ATK * intMultiplier));
                damage = magicDamage - target.DEF;
                break;
            case PlayerClass.Rogue:
                float dexMultiplier = 1.0f + (attacker.DEX / 100.0f);
                int initialDamage = (int)(attacker.WeaponAttack + (attacker.ATK * dexMultiplier)) - target.DEF;
                damage = initialDamage;
                float totalCritChance = attacker.CritChance + (attacker.DEX / 1000.0f);
                if (random.NextDouble() < totalCritChance)
                {
                    damage = (int)(initialDamage * 1.5);
                    AddLog($"{attacker.Username}: 핵심 데이터(크리티컬)!");
                }
                break;
        }

        if (damage < 0) damage = 0;
        target.HP -= damage;
        AddLog($"{attacker.Username}({attacker.Class})(이)가 {target.Name}에게 {damage}의 데미지를 입혔습니다!");
    }


    private string GetWorldDisplay(Player player)
    {
        // 공유 화면(맵/로그)은 동일, 정보 패널은 뷰어별 개인화
        var topLines = BuildWorldTopLayout(player, out int totalWidth);
        var logBoxLines = BuildLogBox(totalWidth, "(입력: W,A,S,D / 상태: I)");

        return string.Join("\n", topLines.Concat(logBoxLines));
    }

    private string GetBattleDisplay(Player player)
    {
        int totalWidth = MapWidth + 2 + 1 + InfoBoxWidth;

        var battleStageLines = BuildBattleStageBox(totalWidth);
        var statusLines = BuildBattleStatusBox(player, totalWidth);
        var logBoxLines = BuildLogBox(totalWidth, "(행동: 1,2,3,4 | 스킬: Q,W,E | 뒤로가기: B)");

        return string.Join("\n", battleStageLines.Concat(statusLines).Concat(logBoxLines));
    }

    private List<string> BuildWorldTopLayout(Player viewer, out int totalWidth)
    {
        int mapBoxWidth = MapWidth + 2;
        int mapBoxHeight = MapHeight + 2;

        var mapBoxLines = BuildBox(mapBoxWidth, mapBoxHeight, "ASCII 미궁", BuildMapContentShared());
        var infoBoxLines = BuildBox(InfoBoxWidth, mapBoxHeight, "플레이어 정보", BuildInfoContentFor(viewer));

        totalWidth = mapBoxWidth + 1 + InfoBoxWidth;
        var combined = new List<string>(mapBoxHeight);
        for (int i = 0; i < mapBoxHeight; i++)
        {
            combined.Add(mapBoxLines[i] + " " + infoBoxLines[i]);
        }
        return combined;
    }

    private List<string> BuildMapContentShared()
    {
        char[,] tempMap = (char[,])map.Clone();

        foreach (var m in monsters)
        {
            if (m.X >= 0 && m.X < MapWidth && m.Y >= 0 && m.Y < MapHeight)
            {
                tempMap[m.X, m.Y] = m.Icon;
            }
        }

        // 두 명의 플레이어를 고정 아이콘으로 표기: 1, 2 (같은 맵 공유)
        var playersOrdered = gamePlayers.Values.OrderBy(p => p.ClientId).ToList();
        for (int idx = 0; idx < playersOrdered.Count; idx++)
        {
            var p = playersOrdered[idx];
            if (p.HP > 0 && p.X >= 0 && p.X < MapWidth && p.Y >= 0 && p.Y < MapHeight)
            {
                tempMap[p.X, p.Y] = (idx == 0) ? '1' : (idx == 1 ? '2' : 'P');
            }
        }

        var rows = new List<string>(MapHeight);
        for (int y = 0; y < MapHeight; y++)
        {
            var sb = new StringBuilder();
            for (int x = 0; x < MapWidth; x++)
            {
                char tile = tempMap[x, y];
                string cell = tile.ToString();

                if (tile == '1')
                    cell = $"{C_GREEN}@{C_RESET}";
                else if (tile == '2')
                    cell = $"{C_YELLOW}@{C_RESET}";
                else if (tile == 'M' || tile == 'G' || tile == 'S' || tile == '*' || tile == '^')
                    cell = $"{C_RED}{tile}{C_RESET}";
                else
                    cell = tile.ToString();

                sb.Append(cell);
            }
            rows.Add(sb.ToString());
        }
        return rows;
    }

    private List<string> BuildInfoContentFor(Player viewer)
    {
        var playersOrdered = gamePlayers.Values.OrderBy(p => p.ClientId).ToList();
        if (viewer == null)
        {
            viewer = playersOrdered.FirstOrDefault() ?? gamePlayers.Values.FirstOrDefault();
        }

        var info = new List<string>();
        string viewerColor = GetPlayerColor(viewer, playersOrdered);
        info.Add($"닉네임: {viewerColor}{viewer.Username}{C_RESET}");
        info.Add($"직업: {viewer.Class}");
        info.Add($"레벨: {viewer.Level}");

        string viewerTurnMark = GetTurnMark(viewer, viewer);
        string hpLine = $"HP: {viewer.HP}/{viewer.MaxHP}";
        if (!string.IsNullOrEmpty(viewerTurnMark)) hpLine = $"{hpLine} {viewerTurnMark}";
        info.Add(hpLine.Trim());
        info.Add("   " + BuildBarColored(Math.Min(12, InfoBoxWidth - 8), viewer.HP, viewer.MaxHP, C_RED));

        info.Add($"MP: {viewer.MP}/{viewer.MaxMP}");
        info.Add("   " + BuildBarColored(Math.Min(12, InfoBoxWidth - 8), viewer.MP, viewer.MaxMP, C_BLUE));

        // EXP 표시
        info.Add($"EXP: {viewer.EXP}/{viewer.EXPNext}");
        info.Add("   " + BuildBarColored(Math.Min(12, InfoBoxWidth - 8), viewer.EXP, viewer.EXPNext, C_GREEN));

        info.Add($"STR:{viewer.STR}  INT:{viewer.INT}");
        info.Add($"DEX:{viewer.DEX}  ATK:{viewer.ATK}");
        info.Add($"DEF:{viewer.DEF}");
        info.Add(string.Empty);

        info.Add("--- 파티 ---");
        for (int idx = 0; idx < playersOrdered.Count; idx++)
        {
            var member = playersOrdered[idx];
            string color = idx == 0 ? C_GREEN : (idx == 1 ? C_YELLOW : "");
            string colorReset = string.IsNullOrEmpty(color) ? "" : C_RESET;
            string mark = GetTurnMark(viewer, member);
            string header = $"{color}{member.Username}{colorReset}({member.Class})";
            if (!string.IsNullOrEmpty(mark)) header += $" {mark}";
            info.Add(header);
            info.Add($"LV:{member.Level}  HP:{member.HP}/{member.MaxHP}");
            info.Add($"MP:{member.MP}/{member.MaxMP}");
        }

        return info;
    }

    private List<string> BuildBattleStageBox(int totalWidth)
    {
        var content = BuildBattleArtContent(Math.Max(1, totalWidth - 2));
        int height = Math.Max(8, content.Count + 2);
        return BuildBox(totalWidth, height, "Battle Stage", content);
    }

    private List<string> BuildBattleStatusBox(Player player, int totalWidth)
    {
        var content = new List<string>();

        if (currentBattleMonster == null)
        {
            content.Add("전투 대상이 없습니다.");
        }
        else if (player.HP <= 0)
        {
            content.Add("당신은 쓰러져 있습니다.");
        }
        else if (currentPlayerTurn == player)
        {
            if (currentState == GameState.Battle)
            {
                content.Add("[1] 기본 공격");
                content.Add("[2] 스킬");
                content.Add("[3] 아이템");
                content.Add("[4] 후퇴");
            }
            else if (currentState == GameState.Battle_SkillSelect)
            {
                string[] keys = { "Q", "W", "E" };
                for (int i = 0; i < player.Skills.Count && i < keys.Length; i++)
                {
                    var skill = player.Skills[i];
                    string mpComment = player.MP >= skill.MpCost ? string.Empty : " (MP 부족)";
                    content.Add($"[{keys[i]}] {skill.Name} (MP {skill.MpCost}){mpComment}");
                }
                content.Add("[B] 뒤로가기");
            }
        }
        else
        {
            content.Add($"{currentPlayerTurn?.Username} 차례를 기다리는 중...");
        }

        content.Add(string.Empty);
        content.Add($"HP: {player.HP}/{player.MaxHP}");
        int barWidth = Math.Max(6, Math.Min(24, totalWidth - 10));
        content.Add("   " + BuildBarColored(barWidth, player.HP, player.MaxHP, C_RED));
        content.Add($"MP: {player.MP}/{player.MaxMP}");
        content.Add("   " + BuildBarColored(barWidth, player.MP, player.MaxMP, C_BLUE));
        content.Add($"STR:{player.STR}  INT:{player.INT}  DEX:{player.DEX}");
        content.Add($"ATK:{player.ATK}  DEF:{player.DEF}");
        content.Add(string.Empty);
        content.Add("--- 파티 ---");
        foreach (var member in gamePlayers.Values)
        {
            string mark = GetTurnMark(player, member);
            string header = $"{member.Username}({member.Class})";
            if (!string.IsNullOrEmpty(mark)) header += $" {mark}";
            content.Add(header);
            content.Add($"LV:{member.Level}  HP:{member.HP}/{member.MaxHP}");
            content.Add($"MP:{member.MP}/{member.MaxMP}");
        }

        int heightNeeded = Math.Max(12, content.Count + 2);
        return BuildBox(totalWidth, heightNeeded, "Player Status & Actions", content);
    }

    private List<string> BuildBattleArtContent(int contentWidth)
    {
        var content = new List<string>();
        if (contentWidth <= 0)
        {
            content.Add(string.Empty);
            return content;
        }

        if (currentBattleMonster == null)
        {
            content.Add("전투가 종료되었습니다.");
            return content;
        }

        string[] playerArt = { "  @  ", " /|\\ ", " / \\ " };
        string[] monsterArt = { "/----\\", "| M  M |", "|  --  |", "\\----/" };

        int playerArtWidth = playerArt.Max(l => l.Length);
        int monsterArtWidth = monsterArt.Max(l => l.Length);

        int playerX = 2;
        int monsterX = contentWidth - monsterArtWidth - 2;
        if (monsterX <= playerX + playerArtWidth)
        {
            monsterX = Math.Min(contentWidth - monsterArtWidth, playerX + playerArtWidth + 8);
        }
        monsterX = Math.Max(playerX + playerArtWidth + 2, monsterX);
        if (monsterX + monsterArtWidth > contentWidth)
        {
            monsterX = Math.Max(playerX + playerArtWidth + 2, contentWidth - monsterArtWidth);
        }

        int artHeight = Math.Max(playerArt.Length, monsterArt.Length);
        for (int i = 0; i < artHeight; i++)
        {
            char[] row = Enumerable.Repeat(' ', contentWidth).ToArray();
            if (i < playerArt.Length)
            {
                string segment = playerArt[i];
                for (int j = 0; j < segment.Length && playerX + j < contentWidth; j++)
                {
                    row[playerX + j] = segment[j];
                }
            }
            if (i < monsterArt.Length)
            {
                string segment = monsterArt[i];
                for (int j = 0; j < segment.Length && monsterX + j < contentWidth; j++)
                {
                    row[monsterX + j] = segment[j];
                }
            }
            content.Add(new string(row));
        }

        content.Add(string.Empty);
        string hpLine = $"{currentBattleMonster.Name} HP: {currentBattleMonster.HP}/{currentBattleMonster.MaxHP}";
        content.Add(CenterText(hpLine, contentWidth));
        int barWidth = Math.Max(6, Math.Min(24, contentWidth - 6));
        content.Add(CenterText(BuildBarColored(barWidth, currentBattleMonster.HP, currentBattleMonster.MaxHP, C_RED), contentWidth));

        return content;
    }

    private List<string> BuildLogBox(int totalWidth, string headerLine)
    {
        int boxHeight = LogBoxHeight;
        int innerHeight = Math.Max(1, boxHeight - 2);
        var content = new List<string>
        {
            headerLine ?? string.Empty
        };

        int logsToSkip = Math.Max(0, logMessages.Count - (innerHeight - 1));
        foreach (var log in logMessages.Skip(logsToSkip))
        {
            content.Add(log);
        }

        return BuildBox(totalWidth, boxHeight, "Log", content);
    }

    private List<string> BuildBox(int width, int height, string title, IList<string> contentLines)
    {
        width = Math.Max(2, width);
        height = Math.Max(2, height);

        var lines = new List<string>(height);
        lines.Add(BuildBorder(width, '╔', '═', '╗', title));

        int innerHeight = height - 2;
        for (int i = 0; i < innerHeight; i++)
        {
            string content = (contentLines != null && i < contentLines.Count) ? contentLines[i] ?? string.Empty : string.Empty;
            string fitted = PadOrTrimVisible(content, width - 2);
            lines.Add("║" + fitted + "║");
        }

        lines.Add(BuildBorder(width, '╚', '═', '╝'));
        return lines;
    }

    private string BuildBorder(int width, char left, char fill, char right, string? title = null)
    {
        char[] result = Enumerable.Repeat(fill, width).ToArray();
        result[0] = left;
        result[width - 1] = right;

        if (!string.IsNullOrEmpty(title))
        {
            string text = $" {title} ";
            int start = Math.Max(1, (width - text.Length) / 2);
            for (int i = 0; i < text.Length && (start + i) < width - 1; i++)
            {
                result[start + i] = text[i];
            }
        }

        return new string(result);
    }

    private string CenterText(string text, int width)
    {
        if (width <= 0) return string.Empty;
        if (string.IsNullOrEmpty(text)) return new string(' ', width);
        int visLen = VisibleLength(text);
        if (visLen >= width) return PadOrTrimVisible(text, width);

        int padding = (width - visLen) / 2;
        return new string(' ', padding) + text + new string(' ', width - padding - visLen);
    }

    private string BuildBar(int width, int current, int max)
    {
        width = Math.Max(0, width);
        if (width == 0) return "[]";
        if (max <= 0) max = 1;

        double ratio = current / (double)max;
        int filled = Clamp((int)Math.Round(ratio * width), 0, width);
        int empty = width - filled;
        return "[" + new string('█', filled) + new string('░', empty) + "]";
    }

    private string BuildBarColored(int width, int current, int max, string colorCode)
    {
        string bar = BuildBar(width, current, max);
        // 대괄호는 기본색, 내부 바만 색상
        int visLen = bar.Length; // bar에는 ANSI 없음
        if (visLen < 2) return bar;
        string inner = bar.Substring(1, visLen - 2);
        return "[" + colorCode + inner + C_RESET + "]";
    }

    private int VisibleLength(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        return AnsiRegex.Replace(s, "").Length;
    }

    private string PadOrTrimVisible(string s, int targetWidth)
    {
        if (targetWidth <= 0) return string.Empty;
        if (string.IsNullOrEmpty(s)) return new string(' ', targetWidth);

        // Trim by visible width
        int vis = 0;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; )
        {
            if (s[i] == '\u001b')
            {
                // copy ANSI sequence as-is
                var m = AnsiRegex.Match(s, i);
                if (m.Success && m.Index == i)
                {
                    sb.Append(m.Value);
                    i += m.Length;
                    continue;
                }
            }
            if (vis >= targetWidth) break;
            sb.Append(s[i]);
            vis++;
            i++;
        }
        // pad
        if (vis < targetWidth)
        {
            sb.Append(new string(' ', targetWidth - vis));
        }
        // ensure reset at end for safety
        return sb.ToString() + C_RESET;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private string GetTurnMark(Player viewer, Player target)
    {
        if (currentPlayerTurn == null || target != currentPlayerTurn) return string.Empty;
        return target == viewer ? "<- (Your Turn)" : "<- (Turn)";
    }

    private string GetPlayerColor(Player p, List<Player> ordered)
    {
        int idx = Math.Max(0, ordered.IndexOf(p));
        return idx == 0 ? C_GREEN : (idx == 1 ? C_YELLOW : C_RESET);
    }

    private void BroadcastWorldState()
    {
        Console.WriteLine($"[DEBUG] BroadcastWorldState() 호출됨. clients.Count = {clients.Count}");
        foreach (var client in clients.Values)
        {
            Console.WriteLine($"[DEBUG] Client {client.ClientId}: State={client.State}, GamePlayer={client.GamePlayer != null}");
            if (client.GamePlayer != null && client.State == ClientState.Playing)
            {
                if (clients.Count == 2 && clients.Values.All(c => c.State == ClientState.Playing))
                {
                    // 두 클라이언트에게 동일 화면 전송
                    string display = GetWorldDisplay(client.GamePlayer);
                    Console.WriteLine($"[DEBUG] Client {client.ClientId}에게 게임 화면 전송 (길이: {display.Length})");
                    SendMessage(client, display);
                }
                else
                {
                    // 2명이 갖춰지지 않으면 대기 화면만 전송
                    string waiting = GetWaitingDisplay(client.GamePlayer);
                    Console.WriteLine($"[DEBUG] Client {client.ClientId}에게 대기 화면 전송 (길이: {waiting.Length})");
                    SendMessage(client, waiting);
                }
            }
        }
    }

    private void BroadcastBattleState()
    {
        foreach (var client in clients.Values)
        {
            if (client.GamePlayer != null && client.State == ClientState.Playing)
            {
                if (clients.Count == 2 && clients.Values.All(c => c.State == ClientState.Playing))
            {
                SendMessage(client, GetBattleDisplay(client.GamePlayer));
                }
                else
                {
                    SendMessage(client, GetWaitingDisplay(client.GamePlayer));
                }
            }
        }
    }

    private void BroadcastMessage(string message, bool includeNewline = true)
    {
        Console.WriteLine($"Broadcasting: {message}");
        foreach (var client in clients.Values)
        {
            SendMessage(client, message, includeNewline);
        }
    }

    private void SendMessage(ClientSession client, string message, bool includeNewline = true)
    {
        try
        {
            // [수정] 클라이언트가 |를 기준으로 Split하므로 \n을 |로 변경
            // [핵심] CRLF 환경에서 \r이 남아 커서를 줄 처음으로 이동시키는 문제가 있어 제거
            string singleLineMessage = message.Replace("\r", "").Replace('\n', '|');
            client.Writer.WriteLine(singleLineMessage); // WriteLine이 \n을 추가함
            client.Writer.Flush(); // [수정] 즉시 전송 보장
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message to {client.ClientId}: {ex.Message}");
            HandleClientDisconnect(client.ClientId);
        }
    }

    private (int x, int y) GetRandomSpawnPosition()
    {
        while (true)
        {
            int x = random.Next(1, MapWidth - 1);
            int y = random.Next(1, MapHeight - 1);

            if (map[x, y] == ' ' &&
                !monsters.Any(m => m.X == x && m.Y == y) &&
                !gamePlayers.Values.Any(p => p.X == x && p.Y == y))
            {
                return (x, y);
            }
        }
    }

    // --- 대기 화면 ---
    private string GetWaitingDisplay(Player viewer)
    {
        int mapBoxWidth = MapWidth + 2;
        int mapBoxHeight = MapHeight + 2;
        int totalWidth = mapBoxWidth + 1 + InfoBoxWidth;

        var content = new List<string>();
        content.Add("두 명의 플레이어가 필요합니다.");
        content.Add("다른 플레이어를 기다리는 중...");
        content.Add(string.Empty);
        content.Add($"현재 접속: {clients.Count} / 2");

        var waitBox = BuildBox(totalWidth, 8, "Waiting For Party", content);
        var fillerTop = new string(' ', totalWidth);
        var fillerBottom = new string(' ', totalWidth);
        var lines = new List<string>();
        lines.Add(fillerTop);
        lines.AddRange(waitBox);
        lines.Add(fillerBottom);
        return string.Join("\n", lines);
    }
}

internal enum ClientState { ChoosingNickname, ChoosingClass, Playing }
class ClientSession
{
    public int ClientId { get; }
    public TcpClient TcpClient { get; }
    public ClientState State { get; set; }
    public Player GamePlayer { get; set; }
    public string TempNickname { get; set; }
    public StreamWriter Writer { get; }
    public StreamReader Reader { get; }
    public StringBuilder NicknameBuffer { get; } = new StringBuilder();

    public ClientSession(int clientId, TcpClient tcpClient)
    {
        ClientId = clientId;
        TcpClient = tcpClient;
        State = ClientState.ChoosingNickname;
        GamePlayer = null;
        TempNickname = null;

        NetworkStream stream = tcpClient.GetStream();
        Writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        Reader = new StreamReader(stream, Encoding.UTF8);
    }
}


// (신규) ASCIIQuest_G/Monster.cs
public class Monster
{
    public string Name { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int HP { get; set; }
    public int MaxHP { get; set; }
    public int ATK { get; set; }
    public int DEF { get; set; }
    public char Icon { get; set; }
    public int EXPReward { get; set; }

    public Monster(string name, int x, int y, int maxHP, int atk, int def, char icon, int expReward)
    {
        Name = name;
        X = x;
        Y = y;
        MaxHP = HP = maxHP;
        ATK = atk;
        DEF = def;
        Icon = icon;
        EXPReward = expReward;
    }
}


// (신규) ASCIIQuest_G/Player.cs
public class SkillData
{
    public string Name { get; set; }
    public int MpCost { get; set; }

    public SkillData(string name, int mpCost)
    {
        Name = name;
        MpCost = mpCost;
    }
}

public enum PlayerClass
{
    Warrior,
    Wizard,
    Rogue
}

public class Player
{
    public int ClientId { get; set; }
    public string Username { get; set; }

    public int X { get; set; }
    public int Y { get; set; }

    public int Level { get; private set; }
    public int EXP { get; private set; }
    public int EXPNext { get; private set; }

    public PlayerClass Class { get; private set; }
    public int HP { get; set; }
    public int MaxHP { get; set; }
    public int MP { get; set; }
    public int MaxMP { get; set; }
    public int ATK { get; set; }
    public int DEF { get; set; }
    public int STR { get; set; }
    public int INT { get; set; }
    public int DEX { get; set; }

    public int WeaponAttack { get; set; }
    public float CritChance { get; set; }

    public List<SkillData> Skills { get; private set; }

    public Player(PlayerClass playerClass, string username)
    {
        Class = playerClass;
        Username = username;
        Skills = new List<SkillData>();
        SetInitialStats();
    }

    private void SetInitialStats()
    {
        Level = 1;
        EXP = 0;
        EXPNext = 100;

        Skills.Clear();

        switch (Class)
        {
            case PlayerClass.Warrior:
                MaxHP = HP = 30; MaxMP = MP = 10;
                ATK = 4; DEF = 4; STR = 8; INT = 2; DEX = 2;
                WeaponAttack = 1; STR += 1;
                Skills.Add(new SkillData("파워 스트라이크", 5));
                Skills.Add(new SkillData("방패 치기", 3));
                Skills.Add(new SkillData("사기 진작", 8));
                break;
            case PlayerClass.Wizard:
                MaxHP = HP = 20; MaxMP = MP = 20;
                ATK = 2; DEF = 2; STR = 2; INT = 12; DEX = 2;
                WeaponAttack = 1; INT += 1; MaxMP += 5;
                Skills.Add(new SkillData("파이어볼", 8));
                Skills.Add(new SkillData("힐", 10));
                Skills.Add(new SkillData("매직 미사일", 4));
                break;
            case PlayerClass.Rogue:
                MaxHP = HP = 25; MaxMP = MP = 12;
                ATK = 3; DEF = 3; STR = 2; INT = 2; DEX = 13;
                WeaponAttack = 1; DEX += 1; CritChance = 0.05f;
                Skills.Add(new SkillData("백스탭", 7));
                Skills.Add(new SkillData("독 찌르기", 5));
                Skills.Add(new SkillData("퀵 어택", 3));
                break;
        }
    }

    public bool AddExperience(int expAmount)
    {
        EXP += expAmount;
        bool leveledUp = false;

        while (EXP >= EXPNext)
        {
            EXP -= EXPNext;
            Level++;
            leveledUp = true;
            LevelUpStats();
            EXPNext = (int)(EXPNext * 1.5);
        }
        return leveledUp;
    }

    private void LevelUpStats()
    {
        switch (Class)
        {
            case PlayerClass.Warrior:
                MaxHP += 5; STR += 2; DEF += 1;
                break;
            case PlayerClass.Wizard:
                MaxHP += 2; MaxMP += 5; INT += 2;
                break;
            case PlayerClass.Rogue:
                MaxHP += 3; ATK += 1; DEX += 2;
                break;
        }
        HP = MaxHP;
        MP = MaxMP;
    }
}

class Program
{
    static void Main(string[] args)
    {
        int port = 12345;
        MUDServer server = new MUDServer(port);
        Console.WriteLine($"ASCIIQuest 2-Player Server is running on port {port}.");

        while (true)
        {
            Thread.Sleep(1000);
        }
    }
}