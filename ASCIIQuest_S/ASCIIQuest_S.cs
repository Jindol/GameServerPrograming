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
    private List<Chest> chests = new List<Chest>();
    private List<Trap> traps = new List<Trap>();
    private List<Rectangle> rooms = new List<Rectangle>();
    private Rectangle bossRoom;
    private char[,] map = null!;
    private List<string> logMessages = new List<string>();
    private GameState currentState = GameState.World;
    private Monster? currentBattleMonster = null;
    private Player? currentPlayerTurn = null;
    private int currentStage = 1;

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
            
            Console.WriteLine($"[DEBUG] Client {clientId} connected. Total clients: {clients.Count}. Awaiting nickname...");
        
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
                    if (data == null) 
                    {
                        Console.WriteLine($"[DEBUG] Client {client.ClientId} ReadLine() returned null");
                        throw new Exception("연결 끊김");
                    }
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
            chests.Clear();
            traps.Clear();
            gamePlayers.Clear();

            currentState = GameState.World;
            currentBattleMonster = null;
            currentPlayerTurn = null;
            currentStage = 1;

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

            // 콘솔 크기 업데이트 처리
            if (command.StartsWith("CONSOLESIZE:"))
            {
                string[] parts = command.Split(':');
                if (parts.Length == 3 && int.TryParse(parts[1], out int width) && int.TryParse(parts[2], out int height))
                {
                    client.WindowWidth = Math.Max(80, width); // 최소 너비 80
                    client.WindowHeight = Math.Max(20, height); // 최소 높이 20
                    // 크기 변경 시 즉시 화면 갱신
                    if (client.State == ClientState.Playing && client.GamePlayer != null)
                    {
                        BroadcastWorldState();
                    }
                }
                return;
            }

            // --- 1. 닉네임 선택 상태 (서버 버퍼링) ---
            if (client.State == ClientState.ChoosingNickname)
            {
                string key = command.ToUpper();
                string currentPrompt = "사용할 닉네임을 입력하세요:\n> ";

                // [수정] ENTER, Return 모두 처리 (클라이언트가 보낼 수 있는 모든 형태)
                // ConsoleKey.Enter.ToString()은 "Enter"를 반환하지만, ToUpper()로 "ENTER"가 됨
                if (key == "ENTER" || key == "RETURN")
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
                    Console.WriteLine($"[DEBUG] 닉네임 입력 완료: '{nickname}'");

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
                    // [수정] 일반 문자 키 처리 (클라이언트가 KeyChar로 보냄)
                    if (client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(key);
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
                }
                else if (key.StartsWith("D") && key.Length == 2 && char.IsDigit(key[1])) // D1, D2..
                {
                    // [수정] 숫자 키패드 처리 (D1, D2 등)
                    if (client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(key[1]);
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
                }
                else if (key.StartsWith("NUMPAD") && key.Length == 7) // NUMPAD1
                {
                    // [수정] 숫자 키패드 처리 (NUMPAD1 등)
                    if (client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(key[6]);
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
                }
                else if (key == "SPACEBAR")
                {
                    if (client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(" ");
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
                }
                else
                {
                    // 인식되지 않은 키는 무시
                }
                return; // [수정] 닉네임 입력 상태에서는 항상 로직 종료
            }


            // --- 2. 직업 선택 상태 ---
            // [수정] else if 로 변경하여 닉네임 상태와 분리
            else if (client.State == ClientState.ChoosingClass)
            {
                PlayerClass selectedClass;
                string key = command.ToUpper();
                bool isValidSelection = false;

                // [수정] 일반 문자 키('1', '2', '3')와 특수 키(D1, NumPad1 등) 모두 처리
                switch (key)
                {
                    case "1": case "D1": case "NUMPAD1":
                        selectedClass = PlayerClass.Warrior;
                        isValidSelection = true;
                        break;
                    case "2": case "D2": case "NUMPAD2":
                        selectedClass = PlayerClass.Wizard;
                        isValidSelection = true;
                        break;
                    case "3": case "D3": case "NUMPAD3":
                        selectedClass = PlayerClass.Rogue;
                        isValidSelection = true;
                        break;
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
                Console.WriteLine($"[DEBUG] 직업 선택 완료: '{gamePlayer.Username}' - {selectedClass}");

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
                // 인벤토리 열기 (간단 버전)
                AddLog($"--- {player.Username} ({player.Class}) 상태 ---");
                AddLog($"LV:{player.Level} HP:{player.HP}/{player.MaxHP} MP:{player.MP}/{player.MaxMP}");
                AddLog($"ATK:{player.ATK} DEF:{player.DEF} STR:{player.STR} INT:{player.INT} DEX:{player.DEX}");
                BroadcastWorldState();
                return false;
                
            case "E":
                // 인벤토리 열기 (향후 구현)
                AddLog("인벤토리 기능은 향후 구현 예정입니다.");
                BroadcastWorldState();
                return false;
                
            case "F":
                // 상자 열기
                TryOpenChest(player);
                BroadcastWorldState();
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

        // 함정 체크
        Trap? trap = traps.Find(t => t.X == newX && t.Y == newY && !t.IsTriggered);
        if (trap != null)
        {
            trap.Trigger();
            if (trap.Type == TrapType.Damage)
            {
                player.HP -= 5;
                AddLog($"{player.Username}(이)가 날카로운 함정을 밟았다! (HP -5)");
                map[newX, newY] = ' ';
                if (player.HP <= 0) HandlePlayerDeath(player);
            }
            else if (trap.Type == TrapType.Battle)
            {
                AddLog("함정이다! 숨어있던 몬스터가 공격한다!");
                map[newX, newY] = ' ';
                StartBattle(new Monster("함정 거미", 0, 0, 25, 4, 0, 'S', 20));
                return true;
            }
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
            case "1":
            case "D1":
            case "NUMPAD1":
                AttackMonster(player, currentBattleMonster);
                if (currentBattleMonster.HP <= 0) WinBattle();
                else EndPlayerBattleTurn();
                break;

            case "2":
            case "D2":
            case "NUMPAD2":
                currentState = GameState.Battle_SkillSelect;
                AddLog("사용할 스킬을 선택하세요: [Q], [W], [E] (뒤로가기: B)");
                BroadcastBattleState();
                break;

            case "3":
            case "D3":
            case "NUMPAD3":
                // 아이템 사용 메뉴
                if (player.ConsumableInventory.Count == 0)
                {
                    AddLog("아이템 가방이 비어있습니다!");
                    BroadcastBattleState();
                }
                else
                {
                    // 간단 버전: 첫 번째 HP 물약 또는 MP 물약 사용
                    var hpPotions = player.ConsumableInventory.Where(c => c.CType == ConsumableType.HealthPotion).ToList();
                    var mpPotions = player.ConsumableInventory.Where(c => c.CType == ConsumableType.ManaPotion).ToList();
                    
                    if (hpPotions.Count > 0 && player.HP < player.MaxHP)
                    {
                        player.UseConsumable(ConsumableType.HealthPotion, hpPotions[0].Rarity);
                        AddLog($"{player.Username}(이)가 HP 물약을 사용했습니다!");
                        EndPlayerBattleTurn();
                    }
                    else if (mpPotions.Count > 0 && player.MP < player.MaxMP)
                    {
                        player.UseConsumable(ConsumableType.ManaPotion, mpPotions[0].Rarity);
                        AddLog($"{player.Username}(이)가 MP 물약을 사용했습니다!");
                        EndPlayerBattleTurn();
                    }
                    else
                    {
                        AddLog("사용할 수 있는 아이템이 없습니다.");
                        BroadcastBattleState();
                    }
                }
                break;

            case "4":
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
        // 맵 초기화
        map = new char[MapWidth, MapHeight];
        rooms.Clear();
        monsters.Clear();
        traps.Clear();
        chests.Clear();
        
        // 전체를 벽으로 채움
        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                map[x, y] = '█';
            }
        }

        // 스테이지별 맵 생성 파라미터
        int maxRooms, minRoomSize, maxRoomSize, monsterCount, damageTraps, battleTraps, chestCount;
        double lRoomChance = 0.25;

        switch (currentStage)
        {
            case 2: // Stage 2: 데이터 동굴
                maxRooms = 40; minRoomSize = 6; maxRoomSize = 12;
                monsterCount = 25; damageTraps = 15; battleTraps = 10; chestCount = 7;
                lRoomChance = 0.50;
                break;
            case 3: // Stage 3: 커널 코어
                maxRooms = 20; minRoomSize = 15; maxRoomSize = 25;
                monsterCount = 15; damageTraps = 10; battleTraps = 20; chestCount = 10;
                lRoomChance = 0.10;
                break;
            case 1: // Stage 1: ASCII 미궁
            default:
                maxRooms = 30; minRoomSize = 10; maxRoomSize = 20;
                monsterCount = 20; damageTraps = 15; battleTraps = 10; chestCount = 5;
                lRoomChance = 0.25;
                break;
        }
        
        // 안전 검사: maxRoomSize가 minRoomSize보다 작으면 조정
        if (maxRoomSize < minRoomSize)
        {
            maxRoomSize = minRoomSize + 1;
        }

        // 보스 방 생성
        int bossRoomW = Math.Min(25, MapWidth / 4);
        int bossRoomH = Math.Min(15, MapHeight / 2);
        int bossRoomX = MapWidth - bossRoomW - 2;
        int bossRoomY = (MapHeight - bossRoomH) / 2;
        bossRoom = new Rectangle(bossRoomX, bossRoomY, bossRoomW, bossRoomH);
        CreateRoom(bossRoom, false);
        rooms.Add(bossRoom);

        // 일반 방 생성
        for (int i = 0; i < maxRooms - 1; i++)
        {
            Rectangle newRoom;
            Rectangle attachedRoom = new Rectangle(0, 0, 0, 0);
            bool isLShaped = false;
            bool overlap;
            int attempts = 0;

            do
            {
                attempts++;
                overlap = false;
                isLShaped = false;

                if (random.NextDouble() < lRoomChance)
                {
                    isLShaped = true;
                    int maxW1 = Math.Max(minRoomSize + 1, maxRoomSize);
                    int maxH1 = Math.Max(minRoomSize + 1, maxRoomSize);
                    int w1 = random.Next(minRoomSize, maxW1);
                    int h1 = random.Next(minRoomSize, maxH1);
                    int maxX1 = Math.Max(2, MapWidth - w1 - 1);
                    int maxY1 = Math.Max(2, MapHeight - h1 - 1);
                    if (maxX1 <= 1) maxX1 = 2;
                    if (maxY1 <= 1) maxY1 = 2;
                    int x1 = random.Next(1, maxX1);
                    int y1 = random.Next(1, maxY1);
                    newRoom = new Rectangle(x1, y1, w1, h1);

                    int w2, h2, x2, y2;
                    if (random.Next(0, 2) == 0) // 가로 팔
                    {
                        int minW2 = Math.Max(1, minRoomSize / 2);
                        int maxW2 = Math.Max(minW2 + 1, maxRoomSize);
                        w2 = random.Next(minW2, maxW2);
                        h2 = h1;
                        x2 = (random.Next(0, 2) == 0) ? (x1 - w2) : (x1 + w1);
                        y2 = y1;
                    }
                    else // 세로 팔
                    {
                        w2 = w1;
                        int minH2 = Math.Max(1, minRoomSize / 2);
                        int maxH2 = Math.Max(minH2 + 1, maxRoomSize);
                        h2 = random.Next(minH2, maxH2);
                        x2 = x1;
                        y2 = (random.Next(0, 2) == 0) ? (y1 - h2) : (y1 + h1);
                    }
                    attachedRoom = new Rectangle(x2, y2, w2, h2);

                    if (attachedRoom.Left < 1 || attachedRoom.Right >= MapWidth - 1 ||
                        attachedRoom.Top < 1 || attachedRoom.Bottom >= MapHeight - 1)
                    {
                        overlap = true;
                        continue;
                    }
                    if (rooms.Any(r => r.Intersects(newRoom) || r.Intersects(attachedRoom)))
                    {
                        overlap = true;
                        continue;
                    }
                }
                else
                {
                    int maxW = Math.Max(minRoomSize + 1, maxRoomSize + 1);
                    int w = random.Next(minRoomSize, maxW);
                    int h_variation = (int)(w * 0.5);
                    int minH = Math.Max(minRoomSize, w - h_variation);
                    int maxH = Math.Min(maxRoomSize, w + h_variation);
                    int maxH2 = Math.Max(minH + 1, maxH + 1);
                    int h = random.Next(minH, maxH2);

                    int maxX = Math.Max(2, MapWidth - w - 1);
                    int maxY = Math.Max(2, MapHeight - h - 1);
                    if (maxX <= 1) maxX = 2;
                    if (maxY <= 1) maxY = 2;
                    int x = random.Next(1, maxX);
                    int y = random.Next(1, maxY);
                    newRoom = new Rectangle(x, y, w, h);

                    if (rooms.Any(r => r.Intersects(newRoom)))
                    {
                        overlap = true;
                        continue;
                    }
                }
            } while (overlap && attempts < 100);

            if (!overlap)
            {
                CreateRoom(newRoom, true);
                rooms.Add(newRoom);

                if (isLShaped)
                {
                    CreateRoom(attachedRoom, true);
                    CreateHorizontalTunnel(newRoom.Center.x, attachedRoom.Center.x, newRoom.Center.y);
                    CreateVerticalTunnel(newRoom.Center.y, attachedRoom.Center.y, attachedRoom.Center.x);
                }
            }
        }

        // 맵 구조 정리
        var boss = rooms[0];
        rooms = rooms.Skip(1).OrderBy(r => r.Center.x).ToList();
        rooms.Insert(0, boss);
        rooms.Add(boss);

        // 터널 생성
        for (int i = 1; i < rooms.Count; i++)
        {
            var (prevX, prevY) = rooms[i - 1].Center;
            var (currX, currY) = rooms[i].Center;
            CreateHorizontalTunnel(prevX, currX, prevY);
            CreateVerticalTunnel(prevY, currY, currX);
        }

        // 보스 스폰
        (int bossX, int bossY) = bossRoom.Center;
        monsters.Add(new Monster("데이터 골렘", bossX, bossY, 500, 20, 15, 'B', 1500));

        // 함정, 몬스터, 상자 스폰
        SpawnTraps(TrapType.Damage, '^', damageTraps);
        SpawnTraps(TrapType.Battle, '*', battleTraps);
        SpawnMonsters(monsterCount);
        SpawnChests(chestCount);
    }
    
    private void CreateRoom(Rectangle room, bool addObstacles)
    {
        // 방의 바닥을 파기
        for (int y = room.Y + 1; y < room.Bottom; y++)
        {
            for (int x = room.X + 1; x < room.Right; x++)
            {
                if (x > 0 && x < MapWidth && y > 0 && y < MapHeight)
                    map[x, y] = '.';
            }
        }

        if (addObstacles && room.Width >= 7 && room.Height >= 7)
        {
            int obstacleCount = (room.Width * room.Height) / 50;
            for (int i = 0; i < obstacleCount; i++)
            {
                if (random.Next(0, 2) == 0) // 기둥
                {
                    int minPillarX = room.Left + 2;
                    int maxPillarX = Math.Max(minPillarX + 1, room.Right - 2);
                    int minPillarY = room.Top + 2;
                    int maxPillarY = Math.Max(minPillarY + 1, room.Bottom - 2);
                    
                    if (maxPillarX > minPillarX && maxPillarY > minPillarY)
                    {
                        int pillarX = random.Next(minPillarX, maxPillarX);
                        int pillarY = random.Next(minPillarY, maxPillarY);
                        if (pillarX > 0 && pillarX < MapWidth && pillarY > 0 && pillarY < MapHeight)
                        {
                            map[pillarX, pillarY] = '█';
                            if (pillarX + 1 < MapWidth) map[pillarX + 1, pillarY] = '█';
                            if (pillarY + 1 < MapHeight) map[pillarX, pillarY + 1] = '█';
                        }
                    }
                }
                else // 벽
                {
                    int maxLineLength = Math.Max(4, room.Width / 3);
                    int lineLength = random.Next(3, Math.Max(4, maxLineLength));
                    int lineX, lineY;
                    if (random.Next(0, 2) == 0) // 가로 벽
                    {
                        int minLineX = room.Left + 2;
                        int maxLineX = Math.Max(minLineX + 1, room.Right - lineLength - 1);
                        int minLineY = room.Top + 2;
                        int maxLineY = Math.Max(minLineY + 1, room.Bottom - 2);
                        
                        if (maxLineX > minLineX && maxLineY > minLineY)
                        {
                            lineX = random.Next(minLineX, maxLineX);
                            lineY = random.Next(minLineY, maxLineY);
                            for (int x = 0; x < lineLength; x++)
                                if (lineX + x < MapWidth && lineX + x > 0 && lineY > 0 && lineY < MapHeight)
                                    map[lineX + x, lineY] = '█';
                        }
                    }
                    else // 세로 벽
                    {
                        int minLineX = room.Left + 2;
                        int maxLineX = Math.Max(minLineX + 1, room.Right - 2);
                        int minLineY = room.Top + 2;
                        int maxLineY = Math.Max(minLineY + 1, room.Bottom - lineLength - 1);
                        
                        if (maxLineX > minLineX && maxLineY > minLineY)
                        {
                            lineX = random.Next(minLineX, maxLineX);
                            lineY = random.Next(minLineY, maxLineY);
                            for (int y = 0; y < lineLength; y++)
                                if (lineY + y < MapHeight && lineY + y > 0 && lineX > 0 && lineX < MapWidth)
                                    map[lineX, lineY + y] = '█';
                        }
                    }
                }
            }
        }
    }
    
    private void CreateHorizontalTunnel(int x1, int x2, int y)
    {
        for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
        {
            if (x > 0 && x < MapWidth - 1 && y > 1 && y < MapHeight - 2)
            {
                map[x, y - 1] = '.'; map[x, y] = '.'; map[x, y + 1] = '.';
            }
        }
    }
    
    private void CreateVerticalTunnel(int y1, int y2, int x)
    {
        for (int y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
        {
            if (x > 1 && x < MapWidth - 2 && y > 0 && y < MapHeight - 1)
            {
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
            x = random.Next(1, MapWidth - 1);
            y = random.Next(1, MapHeight - 1);
            attempts++;
            if (attempts > 500) return (1, 1);
            if (!allowInBossRoom && bossRoom.Contains(x, y)) { continue; }
        }
        while (map[x, y] != '.' ||
               gamePlayers.Values.Any(p => p.X == x && p.Y == y) ||
               monsters.Any(m => m.X == x && m.Y == y) ||
               traps.Any(t => t.X == x && t.Y == y) ||
               chests.Any(c => c.X == x && c.Y == y));
        return (x, y);
    }
    
    private void SpawnTraps(TrapType type, char icon, int count)
    {
        for (int i = 0; i < count; i++)
        {
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
            string[] monsterNames = { "데이터 덩어리", "고블린", "슬라임" };
            char[] monsterIcons = { 'M', 'G', 'S' };
            int[] monsterHPs = { 50, 30, 20 };
            int[] monsterATKs = { 5, 3, 2 };
            int[] monsterDEFs = { 2, 1, 0 };
            int[] monsterEXPs = { 50, 30, 20 };
            
            int idx = random.Next(monsterNames.Length);
            monsters.Add(new Monster(monsterNames[idx], x, y, monsterHPs[idx], monsterATKs[idx], monsterDEFs[idx], monsterIcons[idx], monsterEXPs[idx]));
        }
    }
    
    private void SpawnChests(int count)
    {
        int chestsSpawned = 0;
        foreach (var room in rooms.Skip(1).Where(r => !r.Equals(bossRoom)))
        {
            if (chestsSpawned >= count) break;
            if (random.NextDouble() < 0.5)
            {
                var corner = GetRandomCornerInRoom(room);
                if (corner.HasValue)
                {
                    chests.Add(new Chest(corner.Value.x, corner.Value.y));
                    chestsSpawned++;
                }
            }
        }
        
        // 남은 상자는 랜덤 위치에 스폰
        while (chestsSpawned < count)
        {
            var (x, y) = GetRandomEmptyPosition(allowInBossRoom: false);
            chests.Add(new Chest(x, y));
            chestsSpawned++;
        }
    }
    
    private (int x, int y)? GetRandomCornerInRoom(Rectangle room)
    {
        List<(int x, int y)> corners = new List<(int x, int y)>();
        (int x, int y)[] cornerPoints = new[]
        {
            (room.Left + 1, room.Top + 1),
            (room.Right - 1, room.Top + 1),
            (room.Left + 1, room.Bottom - 1),
            (room.Right - 1, room.Bottom - 1)
        };

        foreach (var (x, y) in cornerPoints)
        {
            if (x > 0 && x < MapWidth && y > 0 && y < MapHeight && map[x, y] == '.')
            {
                if ((x - 1 >= 0 && map[x - 1, y] == '█' && y - 1 >= 0 && map[x, y - 1] == '█') ||
                    (x + 1 < MapWidth && map[x + 1, y] == '█' && y - 1 >= 0 && map[x, y - 1] == '█') ||
                    (x - 1 >= 0 && map[x - 1, y] == '█' && y + 1 < MapHeight && map[x, y + 1] == '█') ||
                    (x + 1 < MapWidth && map[x + 1, y] == '█' && y + 1 < MapHeight && map[x, y + 1] == '█'))
                {
                    corners.Add((x, y));
                }
            }
        }

        if (corners.Count == 0) return null;
        return corners[random.Next(corners.Count)];
    }

    private void InitializeMonsters()
    {
        // InitializeMap()에서 이미 몬스터를 스폰하므로 여기서는 초기화만 수행
        // (필요시 추가 몬스터 스폰 가능)
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

        // 아이템 드롭
        List<Item> drops = ItemDB.GenerateAllDrops(gamePlayers.Values.First().Class, random, currentStage);
        foreach (var item in drops)
        {
            foreach (var player in gamePlayers.Values.Where(p => p.HP > 0))
            {
                player.AddItem(item);
                AddLog($"{player.Username}(이)가 {item.Name}을(를) 획득했습니다!");
            }
        }

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
    
    // [신규] 상자 열기 시도
    private void TryOpenChest(Player player)
    {
        Chest? chest = chests.Find(c => c.X == player.X && c.Y == player.Y && !c.IsOpen);
        if (chest == null)
        {
            AddLog("열 수 있는 상자가 없습니다.");
            return;
        }
        
        chest.Open();
        AddLog($"{player.Username}(이)가 상자를 엽니다...");
        
        // 아이템 드롭
        if (random.NextDouble() < 0.15) // 함정 확률
        {
            AddLog("함정이다! 상자에서 몬스터가 튀어나왔다!");
            StartBattle(new Monster("함정 거미", 0, 0, 25, 4, 0, 'S', 20));
            return;
        }
        
        if (random.NextDouble() < 0.10) // 빈 상자
        {
            AddLog("상자가 비어있습니다.");
            return;
        }
        
        // 아이템 획득
        List<Item> drops = ItemDB.GenerateAllDrops(player.Class, random, currentStage);
        foreach (var item in drops)
        {
            player.AddItem(item);
            AddLog($"{player.Username}(이)가 {item.Name}을(를) 획득했습니다!");
        }
        
        map[chest.X, chest.Y] = '.';
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
                player.ATK += 2; // (임시 버프)
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
        // 플레이어의 클라이언트 세션 찾기
        ClientSession client = clients.Values.FirstOrDefault(c => c.GamePlayer == player);
        if (client == null)
        {
            // 기본값 사용
            var defaultTopLines = BuildWorldTopLayout(player, 120, 30, out int defaultTotalWidth);
            var defaultLogBoxLines = BuildLogBox(defaultTotalWidth, "(입력: W,A,S,D / 상태: I)");
            return string.Join("\n", defaultTopLines.Concat(defaultLogBoxLines));
        }

        // 동적 레이아웃 계산
        int screenWidth = client.WindowWidth;
        int screenHeight = client.WindowHeight;
        
        // 로그와 정보 박스는 고정 크기
        int worldLogHeight = 10; // 고정 높이
        int worldInfoWidth = 35; // 고정 너비
        int worldLogWidth = screenWidth - worldInfoWidth; // 나머지 공간 (공백 없이 바로 붙임)
        
        // 맵은 로그/정보 영역을 제외한 나머지 전체 공간 사용
        int worldMapHeight = screenHeight - worldLogHeight;
        
        // 최소 크기 보장
        worldMapHeight = Math.Max(10, worldMapHeight);
        worldLogHeight = Math.Max(5, worldLogHeight);
        worldInfoWidth = Math.Max(30, worldInfoWidth);
        worldLogWidth = Math.Max(30, worldLogWidth);
        
        // worldInfoX는 실제로 사용되지 않지만 호환성을 위해 유지
        int worldInfoX = worldLogWidth;

        var topLines = BuildWorldTopLayout(player, screenWidth, worldMapHeight, worldLogWidth, worldLogHeight, worldInfoX, worldInfoWidth, out int totalWidth);
        // BuildWorldTopLayout에서 이미 로그와 정보를 함께 배치했으므로 추가 호출 불필요
        return string.Join("\n", topLines);
    }

    private string GetBattleDisplay(Player player)
    {
        // 플레이어의 클라이언트 세션 찾기
        ClientSession client = clients.Values.FirstOrDefault(c => c.GamePlayer == player);
        int totalWidth = (client != null) ? client.WindowWidth : (MapWidth + 2 + 1 + InfoBoxWidth);
        
        // 배틀 레이아웃: 스테이지, 상태, 로그를 세로로 배치
        int battleStageHeight = 12;
        int statusHeight = 20;
        int logHeight = 10;
        
        var battleStageLines = BuildBattleStageBox(totalWidth);
        var statusLines = BuildBattleStatusBox(player, totalWidth);
        var logBoxLines = BuildLogBox(totalWidth, logHeight, "(행동: 1,2,3,4 | 스킬: Q,W,E | 뒤로가기: B)");

        return string.Join("\n", battleStageLines.Concat(statusLines).Concat(logBoxLines));
    }

    private List<string> BuildWorldTopLayout(Player viewer, int screenWidth, int worldMapHeight, int worldLogWidth, int worldLogHeight, int worldInfoX, int worldInfoWidth, out int totalWidth)
    {
        // 맵 박스 크기 계산 (전체 화면 너비 사용)
        int mapBoxWidth = screenWidth;
        int mapBoxHeight = worldMapHeight;

        // 카메라 뷰포트 계산 (플레이어 중심)
        // 박스 테두리(좌우 각 1칸) 제외하여 정확히 박스 내부 크기와 일치
        int viewportWidth = mapBoxWidth - 2; // 좌우 테두리 제외
        int viewportHeight = mapBoxHeight - 2; // 상하 테두리 제외
        
        int cameraX = viewer.X - (viewportWidth / 2);
        int cameraY = viewer.Y - (viewportHeight / 2);
        cameraX = Math.Max(0, Math.Min(cameraX, MapWidth - viewportWidth));
        cameraY = Math.Max(0, Math.Min(cameraY, MapHeight - viewportHeight));

        var mapContent = BuildMapContentWithCamera(cameraX, cameraY, viewportWidth, viewportHeight);
        var mapBoxLines = BuildBox(mapBoxWidth, mapBoxHeight, "ASCII 미궁", mapContent);
        
        // 정보 박스는 맵 박스 아래에 배치 (ASCIIQuest_G 방식)
        // 박스 크기 고정을 위해 maxHeight 지정
        var infoBoxLines = BuildBox(worldInfoWidth, worldLogHeight, "플레이어 정보", BuildInfoContentFor(viewer, worldInfoWidth, worldLogHeight - 2));

        totalWidth = screenWidth;
        var combined = new List<string>(worldMapHeight + worldLogHeight);
        
        // 맵 박스 추가
        for (int i = 0; i < mapBoxHeight; i++)
        {
            combined.Add(mapBoxLines[i]);
        }
        
        // 로그 박스 생성
        var logBoxLines = BuildLogBox(worldLogWidth, worldLogHeight, "(입력: W,A,S,D / 상태: I)");
        
        // 로그와 정보 박스를 같은 줄에 배치 (공백 없이 바로 붙임)
        for (int i = 0; i < worldLogHeight; i++)
        {
            string logLine = (i < logBoxLines.Count) ? logBoxLines[i] : new string(' ', worldLogWidth);
            string infoLine = (i < infoBoxLines.Count) ? infoBoxLines[i] : new string(' ', worldInfoWidth);
            // 로그 박스와 정보 박스를 공백 없이 바로 붙임
            combined.Add(logLine + infoLine);
        }
        
        return combined;
    }

    // 오버로드: 기본값 사용 (하위 호환성)
    private List<string> BuildWorldTopLayout(Player viewer, int screenWidth, int screenHeight, out int totalWidth)
    {
        int worldMapHeight = (screenHeight * 2) / 3;
        int worldLogY = worldMapHeight;
        int worldLogHeight = screenHeight - worldLogY;
        int worldInfoX = (screenWidth * 3) / 5;
        int worldLogWidth = worldInfoX;
        int worldInfoWidth = screenWidth - worldInfoX;
        return BuildWorldTopLayout(viewer, screenWidth, worldMapHeight, worldLogWidth, worldLogHeight, worldInfoX, worldInfoWidth, out totalWidth);
    }

    private string BuildLogLine(int lineIndex, int logWidth, int logHeight)
    {
        int maxLines = Math.Max(1, logHeight - 3);
        int logCount = logMessages.Count;
        int logIndex = logCount - maxLines + lineIndex;
        string logLine = "";
        if (logIndex >= 0 && logIndex < logMessages.Count)
        {
            logLine = logMessages[logIndex];
        }
        // 로그 박스 내부 너비 (테두리 제외)
        int contentWidth = Math.Max(0, logWidth - 4);
        string displayLine = PadOrTrimVisible(logLine, contentWidth);
        return displayLine;
    }

    private List<string> BuildMapContentWithCamera(int cameraX, int cameraY, int viewportWidth, int viewportHeight)
    {
        var rows = new List<string>(viewportHeight);
        
        for (int y = 0; y < viewportHeight; y++)
        {
            var sb = new StringBuilder();
            for (int x = 0; x < viewportWidth; x++)
            {
                int mapX = cameraX + x;
                int mapY = cameraY + y;
                
                char tile = ' ';
                if (mapX >= 0 && mapX < MapWidth && mapY >= 0 && mapY < MapHeight)
                {
                    tile = map[mapX, mapY];
                }
                
                // 몬스터 표시
                foreach (var m in monsters)
                {
                    if (m.X == mapX && m.Y == mapY && m.X >= 0 && m.X < MapWidth && m.Y >= 0 && m.Y < MapHeight)
                    {
                        tile = m.Icon;
                        break;
                    }
                }
                
                // 상자 표시
                foreach (var chest in chests)
                {
                    if (!chest.IsOpen && chest.X == mapX && chest.Y == mapY && chest.X >= 0 && chest.X < MapWidth && chest.Y >= 0 && chest.Y < MapHeight)
                    {
                        tile = chest.Icon;
                        break;
                    }
                }
                
                // 함정 표시
                foreach (var trap in traps)
                {
                    if (!trap.IsTriggered && trap.X == mapX && trap.Y == mapY && trap.X >= 0 && trap.X < MapWidth && trap.Y >= 0 && trap.Y < MapHeight)
                    {
                        tile = trap.Icon;
                        break;
                    }
                }
                
                // 플레이어 표시
                var playersOrdered = gamePlayers.Values.OrderBy(p => p.ClientId).ToList();
                for (int idx = 0; idx < playersOrdered.Count; idx++)
                {
                    var p = playersOrdered[idx];
                    if (p.HP > 0 && p.X == mapX && p.Y == mapY && p.X >= 0 && p.X < MapWidth && p.Y >= 0 && p.Y < MapHeight)
                    {
                        tile = (idx == 0) ? '1' : (idx == 1 ? '2' : 'P');
                        break;
                    }
                }
                
                string cell = tile.ToString();
                if (tile == '1')
                    cell = $"{C_GREEN}@{C_RESET}";
                else if (tile == '2')
                    cell = $"{C_YELLOW}@{C_RESET}";
                else if (tile == 'M' || tile == 'G' || tile == 'S' || tile == '*' || tile == '^')
                    cell = $"{C_RED}{tile}{C_RESET}";
                else if (tile == '█')
                    cell = tile.ToString(); // 벽은 기본 색상
                else if (tile == '.')
                    cell = tile.ToString(); // 바닥은 기본 색상
                
                sb.Append(cell);
            }
            rows.Add(sb.ToString());
        }
        return rows;
    }

    private List<string> BuildInfoContentFor(Player viewer, int infoWidth = 34, int maxHeight = 0)
    {
        var playersOrdered = gamePlayers.Values.OrderBy(p => p.ClientId).ToList();
        if (viewer == null)
        {
            viewer = playersOrdered.FirstOrDefault() ?? gamePlayers.Values.FirstOrDefault();
        }

        var info = new List<string>();
        
        // 파티 정보를 먼저 표시 (모든 플레이어 정보)
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
            
            // maxHeight가 지정되어 있고 초과하면 중단
            if (maxHeight > 0 && info.Count >= maxHeight - 2) // 여유 공간 확보
            {
                break;
            }
        }
        
        info.Add(string.Empty);
        
        // 자기 정보를 간소화하여 표시
        string viewerColor = GetPlayerColor(viewer, playersOrdered);
        info.Add($"나: {viewerColor}{viewer.Username}{C_RESET}");
        string viewerTurnMark = GetTurnMark(viewer, viewer);
        string hpLine = $"HP: {viewer.HP}/{viewer.MaxHP}";
        if (!string.IsNullOrEmpty(viewerTurnMark)) hpLine = $"{hpLine} {viewerTurnMark}";
        info.Add(hpLine.Trim());
        info.Add("   " + BuildBarColored(Math.Min(12, infoWidth - 8), viewer.HP, viewer.MaxHP, C_RED));
        info.Add($"MP: {viewer.MP}/{viewer.MaxMP}");
        info.Add("   " + BuildBarColored(Math.Min(12, infoWidth - 8), viewer.MP, viewer.MaxMP, C_BLUE));
        
        // maxHeight가 지정되어 있으면 빈 줄로 채움 (박스 크기 고정)
        if (maxHeight > 0)
        {
            while (info.Count < maxHeight - 1) // -1은 하단 테두리
            {
                info.Add(string.Empty);
            }
        }

        return info;
    }

    private List<string> BuildBattleStageBox(int totalWidth)
    {
        var content = BuildBattleArtContent(Math.Max(1, totalWidth - 2));
        int height = 12; // 고정 높이
        // 내용이 많아도 박스 크기는 고정
        if (content.Count > height - 2)
        {
            // 최근 내용만 표시
            int innerHeight = height - 2;
            var limitedContent = content.Skip(Math.Max(0, content.Count - innerHeight)).Take(innerHeight).ToList();
            content = limitedContent;
        }
        // 빈 줄로 채워서 고정 크기 유지
        while (content.Count < height - 2)
        {
            content.Add(string.Empty);
        }
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
        content.Add($"EXP: {player.EXP}/{player.EXPNext}");
        content.Add("   " + BuildBarColored(barWidth, player.EXP, player.EXPNext, C_GREEN));
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

        int heightNeeded = 20; // 고정 높이
        // 내용이 많아도 박스 크기는 고정
        if (content.Count > heightNeeded - 2)
        {
            // 최근 내용만 표시
            int innerHeight = heightNeeded - 2;
            var limitedContent = content.Skip(Math.Max(0, content.Count - innerHeight)).Take(innerHeight).ToList();
            content = limitedContent;
        }
        // 빈 줄로 채워서 고정 크기 유지
        while (content.Count < heightNeeded - 2)
        {
            content.Add(string.Empty);
        }
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

    // 오버로드: 동적 크기 사용
    private List<string> BuildLogBox(int logWidth, int logHeight, string headerLine)
    {
        int innerHeight = Math.Max(1, logHeight - 3); // 헤더 라인 제외
        var content = new List<string>
        {
            headerLine ?? string.Empty
        };

        // 최근 로그만 표시 (박스 크기 고정)
        int logsToSkip = Math.Max(0, logMessages.Count - innerHeight);
        int logCount = 0;
        int contentWidth = logWidth - 4; // 테두리 제외
        foreach (var log in logMessages.Skip(logsToSkip))
        {
            if (logCount >= innerHeight) break; // 박스 크기를 넘지 않도록
            // 로그 내용을 박스 너비에 맞게 자르기
            string trimmedLog = PadOrTrimVisible(log, contentWidth);
            content.Add(trimmedLog);
            logCount++;
        }
        
        // 남은 공간은 빈 줄로 채움 (박스 크기 고정)
        while (content.Count < logHeight - 1) // -1은 하단 테두리
        {
            content.Add(string.Empty);
        }

        return BuildBox(logWidth, logHeight, "Log", content);
    }

    private List<string> BuildBox(int width, int height, string title, IList<string> contentLines)
    {
        width = Math.Max(2, width);
        height = Math.Max(2, height);

        var lines = new List<string>(height);
        lines.Add(BuildBorder(width, '╔', '═', '╗', title));

        int innerHeight = height - 2; // 상단/하단 테두리 제외
        // contentLines가 많아도 박스 크기는 고정
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
        // 맵의 빈 공간(.)에서 스폰
        while (true)
        {
            int x = random.Next(1, MapWidth - 1);
            int y = random.Next(1, MapHeight - 1);

            if (map[x, y] == '.' &&
                !monsters.Any(m => m.X == x && m.Y == y) &&
                !gamePlayers.Values.Any(p => p.X == x && p.Y == y) &&
                !traps.Any(t => t.X == x && t.Y == y) &&
                !chests.Any(c => c.X == x && c.Y == y))
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
    public int WindowWidth { get; set; } = 120; // 기본값
    public int WindowHeight { get; set; } = 30; // 기본값

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
// 서버에서는 SkillData를 사용하지만, 실제 게임에서는 Skill 클래스를 사용
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
    
    // [신규] 인벤토리 시스템
    public Dictionary<EquipmentSlot, Equipment> EquippedGear { get; private set; }
    public List<Consumable> ConsumableInventory { get; private set; }
    public List<Item> Inventory { get; private set; } // 모든 아이템 저장

    public Player(PlayerClass playerClass, string username)
    {
        Class = playerClass;
        Username = username;
        Skills = new List<SkillData>();
        EquippedGear = new Dictionary<EquipmentSlot, Equipment>();
        ConsumableInventory = new List<Consumable>();
        Inventory = new List<Item>();
        SetInitialStats();
    }

    private void SetInitialStats()
    {
        Level = 1;
        EXP = 0;
        EXPNext = 100;

        Skills.Clear();
        EquippedGear.Clear();
        ConsumableInventory.Clear();
        Inventory.Clear();

        // 초기 소비 아이템 추가
        AddConsumable(new Consumable("[Common] 조악한 HP 물약", ItemRarity.Common, ConsumableType.HealthPotion, 20));
        AddConsumable(new Consumable("[Common] 조악한 HP 물약", ItemRarity.Common, ConsumableType.HealthPotion, 20));
        AddConsumable(new Consumable("[Common] 조악한 MP 물약", ItemRarity.Common, ConsumableType.ManaPotion, 10));

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
    
    // [신규] 장비 장착
    public Equipment? EquipItem(Equipment newItem)
    {
        Equipment? oldItem = null;
        EquippedGear.TryGetValue(newItem.Slot, out oldItem);
        EquippedGear[newItem.Slot] = newItem;
        
        // 스탯 재계산 (간단 버전)
        RecalculateStats();
        return oldItem;
    }
    
    private void RecalculateStats()
    {
        // 장비 스탯 보너스 적용 (간단 버전)
        // 실제 구현은 GetStatBonus를 사용하여 계산
    }
    
    // [신규] 스탯 보너스 계산
    public float GetStatBonus(StatType stat, ModifierType type)
    {
        float bonus = 0;
        foreach (var equip in EquippedGear.Values)
        {
            foreach (var mod in equip.Modifiers)
            {
                if (mod.Stat == stat && mod.Type == type)
                {
                    bonus += mod.Value;
                }
            }
        }
        return bonus;
    }
    
    // [신규] 소비 아이템 추가
    public void AddConsumable(Consumable item)
    {
        ConsumableInventory.Add(item);
        Inventory.Add(item);
    }
    
    // [신규] 아이템 추가
    public void AddItem(Item item)
    {
        Inventory.Add(item);
        if (item is Consumable consumable)
        {
            ConsumableInventory.Add(consumable);
        }
    }
    
    // [신규] 소비 아이템 사용
    public bool UseConsumable(ConsumableType cType, ItemRarity rarity)
    {
        Consumable? itemToUse = ConsumableInventory.FirstOrDefault(item => item.CType == cType && item.Rarity == rarity);
        if (itemToUse == null) return false;
        
        bool success = false;
        switch (cType)
        {
            case ConsumableType.HealthPotion:
                if (HP < MaxHP)
                {
                    HP = Math.Min(MaxHP, HP + itemToUse.Value);
                    success = true;
                }
                break;
            case ConsumableType.ManaPotion:
                if (MP < MaxMP)
                {
                    MP = Math.Min(MaxMP, MP + itemToUse.Value);
                    success = true;
                }
                break;
        }
        
        if (success)
        {
            ConsumableInventory.Remove(itemToUse);
            Inventory.Remove(itemToUse);
        }
        return success;
    }
}

// ========== ASCIIQuest_G 클래스들 추가 ==========

// Rectangle 구조체 (맵 생성용)
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

// Item.cs
public enum ItemRarity
{
    Common, Rare, Unique, Legendary
}

public enum ItemType
{
    Equipment, Consumable
}

public abstract class Item
{
    public string Name { get; protected set; }
    public ItemRarity Rarity { get; protected set; }
    public ItemType Type { get; protected set; }

    public Item(string name, ItemRarity rarity, ItemType type)
    {
        Name = name;
        Rarity = rarity;
        Type = type;
    }
}

// Equipment.cs
public enum EquipmentSlot
{
    Weapon, Head, Armor, Gloves, Boots
}

public class Equipment : Item
{
    public EquipmentSlot Slot { get; private set; }
    public PlayerClass RequiredClass { get; private set; }
    public List<StatModifier> Modifiers { get; private set; }

    public Equipment(string name, ItemRarity rarity, EquipmentSlot slot, PlayerClass requiredClass) 
        : base(name, rarity, ItemType.Equipment)
    {
        Slot = slot;
        RequiredClass = requiredClass;
        Modifiers = new List<StatModifier>();
    }

    public void AddModifier(StatModifier modifier)
    {
        Modifiers.Add(modifier);
    }
}

// Consumable.cs
public enum ConsumableType
{
    HealthPotion, ManaPotion
}

public class Consumable : Item
{
    public ConsumableType CType { get; private set; }
    public int Value { get; private set; }

    public Consumable(string name, ItemRarity rarity, ConsumableType cType, int value) 
        : base(name, rarity, ItemType.Consumable)
    {
        CType = cType;
        Value = value;
    }
}

// StatModifier.cs
public enum StatType
{
    HP, MP, ATK, DEF, STR, INT, DEX, CritChance, EXPGain,
    PowerStrikeDamage, FireballDamage, HealAmount, BackstabDamage,
    PoisonStabDamage, QuickAttackDamage, ShieldBashDamage, MagicMissileDamage,
    DamageReflectChance, ManaRefundChance, LifeStealPercent,
    ResourceCostReduction, StunChance, ManaShieldConversion, BleedChance
}

public enum ModifierType
{
    Flat, Percent
}

public class StatModifier
{
    public StatType Stat { get; private set; }
    public float Value { get; private set; }
    public ModifierType Type { get; private set; }

    public StatModifier(StatType stat, float value, ModifierType type = ModifierType.Flat)
    {
        Stat = stat;
        Value = value;
        Type = type;
    }

    public string GetDescription()
    {
        string statName = Stat.ToString();
        string valueStr;

        if (Type == ModifierType.Percent)
        {
            valueStr = $"+{(Value * 100):F1}%";
        }
        else
        {
            valueStr = $"+{Value:F0}";
        }

        switch (Stat)
        {
            case StatType.HP: statName = "최대 HP"; break;
            case StatType.MP: statName = "최대 MP"; break;
            case StatType.ATK: statName = "공격력"; break;
            case StatType.DEF: statName = "방어력"; break;
            case StatType.EXPGain: statName = "경험치 획득"; break;
            case StatType.PowerStrikeDamage: statName = "파워 스트라이크 데미지"; break;
            case StatType.FireballDamage: statName = "파이어볼 데미지"; break;
            case StatType.HealAmount: statName = "힐 회복량"; break;
            case StatType.BackstabDamage: statName = "백스탭 데미지"; break;
            case StatType.PoisonStabDamage: statName = "독 찌르기 데미지"; break;
            case StatType.QuickAttackDamage: statName = "퀵 어택 데미지"; break;
            case StatType.ShieldBashDamage: statName = "방패 치기 데미지"; break;
            case StatType.MagicMissileDamage: statName = "매직 미사일 데미지"; break;
            case StatType.DamageReflectChance: statName = "피해 반사 확률"; break;
            case StatType.ManaRefundChance: statName = "마나 환급 확률"; break;
            case StatType.LifeStealPercent: statName = "생명력 흡수"; break;
            case StatType.ResourceCostReduction: statName = "자원 소모 감소"; break;
            case StatType.StunChance: statName = "기절 확률"; break;
            case StatType.ManaShieldConversion: statName = "마력 보호막 전환율"; break;
            case StatType.BleedChance: statName = "출혈 확률"; break;
        }

        return $"{statName} {valueStr}";
    }
}

// Chest.cs
public class Chest
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public char Icon { get; private set; }
    public bool IsOpen { get; private set; }

    public Chest(int x, int y)
    {
        X = x;
        Y = y;
        Icon = '$';
        IsOpen = false;
    }

    public void Open()
    {
        IsOpen = true;
        Icon = '_';
    }
}

// Trap.cs
public enum TrapType
{
    Damage, Battle
}

public class Trap
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public TrapType Type { get; private set; }
    public char Icon { get; private set; }
    public bool IsTriggered { get; private set; }

    public Trap(int x, int y, TrapType type, char icon)
    {
        X = x;
        Y = y;
        Type = type;
        Icon = icon;
        IsTriggered = false;
    }

    public void Trigger()
    {
        IsTriggered = true;
    }
}

// ItemDB.cs (간소화 버전)
public static class ItemDB
{
    private static readonly Dictionary<PlayerClass, Dictionary<EquipmentSlot, string>> baseItemNames = new()
    {
        { PlayerClass.Warrior, new() {
            { EquipmentSlot.Weapon, "검" }, { EquipmentSlot.Head, "투구" }, { EquipmentSlot.Armor, "갑옷" },
            { EquipmentSlot.Gloves, "건틀릿" }, { EquipmentSlot.Boots, "장화" }
        }},
        { PlayerClass.Wizard, new() {
            { EquipmentSlot.Weapon, "지팡이" }, { EquipmentSlot.Head, "모자" }, { EquipmentSlot.Armor, "로브" },
            { EquipmentSlot.Gloves, "장갑" }, { EquipmentSlot.Boots, "신발" }
        }},
        { PlayerClass.Rogue, new() {
            { EquipmentSlot.Weapon, "단검" }, { EquipmentSlot.Head, "두건" }, { EquipmentSlot.Armor, "경갑" },
            { EquipmentSlot.Gloves, "가죽 장갑" }, { EquipmentSlot.Boots, "부츠" }
        }}
    };

    public static List<Item> GenerateAllDrops(PlayerClass playerClass, Random rand, int stage = 1)
    {
        List<Item> drops = new List<Item>();
        double equipmentDropChance = 0.15 + ((stage - 1) * 0.05);
        
        if (rand.NextDouble() < equipmentDropChance)
        {
            drops.Add(GenerateRandomEquipment(playerClass, rand, false, stage));
        }
        
        if (rand.NextDouble() < 0.40)
        {
            drops.Add(CreateRandomConsumable(rand, false, stage));
        }

        return drops;
    }

    public static Equipment GenerateRandomEquipment(PlayerClass playerClass, Random rand, bool isBossDrop = false, int stage = 1)
    {
        ItemRarity rarity = isBossDrop ? ItemRarity.Rare : (ItemRarity)rand.Next(0, 4);
        Array slots = Enum.GetValues(typeof(EquipmentSlot));
        EquipmentSlot slot = (EquipmentSlot)slots.GetValue(rand.Next(slots.Length))!;
        
        string baseName = baseItemNames[playerClass][slot];
        string name = $"[{rarity}] {baseName}";
        
        Equipment equip = new Equipment(name, rarity, slot, playerClass);
        
        // 간단한 스탯 추가
        if (slot == EquipmentSlot.Weapon)
        {
            equip.AddModifier(new StatModifier(StatType.ATK, rand.Next(2, 5) * (1 + (int)rarity), ModifierType.Flat));
        }
        else
        {
            equip.AddModifier(new StatModifier(StatType.DEF, rand.Next(1, 4) * (1 + (int)rarity), ModifierType.Flat));
        }
        
        return equip;
    }

    public static Consumable CreateRandomConsumable(Random rand, bool isBossDrop = false, int stage = 1)
    {
        ItemRarity rarity = isBossDrop ? ItemRarity.Rare : (ItemRarity)rand.Next(0, 4);
        ConsumableType type = (rand.Next(0, 2) == 0) ? ConsumableType.HealthPotion : ConsumableType.ManaPotion;
        int baseValue = (type == ConsumableType.HealthPotion) ? 20 : 10;
        int value = (int)(baseValue * (1 + (int)rarity * 0.75));
        string prefix = rarity == ItemRarity.Common ? "조악한" : rarity == ItemRarity.Rare ? "쓸만한" : rarity == ItemRarity.Unique ? "정교한" : "신비로운";
        string baseName = (type == ConsumableType.HealthPotion) ? "HP 물약" : "MP 물약";
        string name = $"[{rarity}] {prefix} {baseName}";
        return new Consumable(name, rarity, type, value);
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