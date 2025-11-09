//
// ASCIIQuest_S/ASCIIQuest_S.cs (수정된 코드)
//
using System;
using System.Collections.Generic;
using System.IO; 
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

        if (clients.Count >= 2)
        {
            Console.WriteLine("Max players reached. Rejecting new connection.");
            byte[] rejectMsg = Encoding.UTF8.GetBytes("서버가 가득 찼습니다. 나중에 다시 시도해주세요.\n");
            tcpClient.GetStream().Write(rejectMsg, 0, rejectMsg.Length);
            tcpClient.Close();

            StartAcceptingClients();
            return;
        }

        int clientId = tcpClient.Client.RemoteEndPoint.GetHashCode();
        ClientSession client = new ClientSession(clientId, tcpClient);
        clients[clientId] = client;

        Console.WriteLine($"Client {clientId} connected. Awaiting nickname...");

        // 닉네임 요청 (프롬프트 추가)
        SendMessage(client, "사용할 닉네임을 입력하세요:\n> ");

        StartAcceptingClients();
        StartReceivingData(client); 
    }

    private void StartReceivingData(ClientSession client)
    {
        Thread thread = new Thread(() =>
        {
            try
            {
                while (client.TcpClient.Connected)
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
                ResetGame();
            }
        }

        client.Reader.Close();
        client.Writer.Close();
        client.TcpClient.Close();
    }

    private void ResetGame()
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
            client.NicknameBuffer.Clear(); // [신규] 닉네임 버퍼 초기화
            SendMessage(client, "다른 플레이어가 떠났습니다. 닉네임을 다시 입력하세요:\n> ");
        }
    }


    private void HandleCommand(string data, int clientId)
    {
        if (!clients.ContainsKey(clientId)) return;

        ClientSession client = clients[clientId];
        string command = data.Trim(); 
        
        // --- [1번 문제 수정] 닉네임 선택 상태 (서버 버퍼링) ---
        if (client.State == ClientState.ChoosingNickname)
        {
            string key = command.ToUpper();
            string currentPrompt = "사용할 닉네임을 입력하세요:\n> ";

            if (key == "ENTER")
            {
                string nickname = client.NicknameBuffer.ToString().Trim();
                
                // 닉네임 유효성 검사
                if (string.IsNullOrWhiteSpace(nickname) || nickname.Length > 10 || nickname.Contains(" "))
                {
                    client.NicknameBuffer.Clear();
                    SendMessage(client, "닉네임은 1~10자리의 공백 없는 문자여야 합니다. 다시 입력하세요:\n> ");
                    return;
                }

                // 닉네임 중복 확인
                if (gamePlayers.Values.Any(p => p.Username.Equals(nickname, StringComparison.OrdinalIgnoreCase)) ||
                    clients.Values.Any(c => c.ClientId != clientId && c.TempNickname != null && c.TempNickname.Equals(nickname, StringComparison.OrdinalIgnoreCase)))
                {
                    client.NicknameBuffer.Clear();
                    SendMessage(client, "이미 사용 중이거나 선택 중인 닉네임입니다. 다시 입력하세요:\n> ");
                    return;
                }

                // 닉네임 통과
                client.TempNickname = nickname; 
                client.State = ClientState.ChoosingClass; 

                string classSelectionMsg = $"반갑습니다, {nickname}님. 직업을 선택하세요:\n" +
                                            "1. Warrior (시스템 방어자)\n" +
                                            "2. Wizard (버그 수정자)\n" +
                                            "3. Rogue (정보 수집가)\n" +
                                            "(키보드 1, 2, 3 입력)";
                SendMessage(client, classSelectionMsg);
            }
            else if (key == "BACKSPACE") // 백스페이스 처리
            {
                if (client.NicknameBuffer.Length > 0)
                {
                    client.NicknameBuffer.Remove(client.NicknameBuffer.Length - 1, 1);
                }
                SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
            }
            // 일반 글자/숫자 키 처리
            else if (key.Length == 1 && (char.IsLetterOrDigit(key[0])))
            {
                if(client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(key);
                SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
            }
            else if (key.StartsWith("D") && key.Length == 2 && char.IsDigit(key[1])) // D1, D2..
            {
                if(client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(key[1]);
                SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
            }
            else if (key.StartsWith("NUMPAD") && key.Length == 7) // NUMPAD1
            {
                if(client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(key[6]);
                SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
            }
            else if (key == "SPACEBAR") // 스페이스바
            {
                    if(client.NicknameBuffer.Length < 10) client.NicknameBuffer.Append(" ");
                    SendMessage(client, currentPrompt + client.NicknameBuffer.ToString());
            }
            // 그 외 (W, A, S, D, ArrowKeys 등)는 무시
            return; 
        }


        // --- [2번 문제 수정] 직업 선택 상태 ---
        if (client.State == ClientState.ChoosingClass)
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
                    // [FIX] 잘못된 입력 시 목록 다시 전송
                    string classSelectionMsg = $"잘못된 선택입니다. 1, 2, 3 중 입력.\n" +
                                                $"반갑습니다, {client.TempNickname}님. 직업을 선택하세요:\n" +
                                                "1. Warrior (시스템 방어자)\n" +
                                                "2. Wizard (버그 수정자)\n" +
                                                "3. Rogue (정보 수집가)\n" +
                                                "(키보드 1, 2, 3 입력)";
                    SendMessage(client, classSelectionMsg);
                    return; // 
            }
            
            Player gamePlayer = new Player(selectedClass, client.TempNickname); 
            gamePlayer.ClientId = clientId; 
            (gamePlayer.X, gamePlayer.Y) = GetRandomSpawnPosition();
            gamePlayers[clientId] = gamePlayer; 
            client.GamePlayer = gamePlayer;      
            client.State = ClientState.Playing;  

            SendMessage(client, $"당신은 '{selectedClass}'입니다. 다른 플레이어를 기다립니다...");
            Console.WriteLine($"Client {clientId} ({gamePlayer.Username}) selected {selectedClass}.");

            if (clients.Count == 2 && clients.Values.All(c => c.State == ClientState.Playing))
            {
                Console.WriteLine("Both players ready. Starting ASCIIQuest!");
                AddLog("두 명의 플레이어가 파티를 맺었습니다. 모험을 시작합니다!");
                currentState = GameState.World;
                currentPlayerTurn = gamePlayers.Values.First();
                BroadcastWorldState();
            }
            return;
        }

        // --- [3번 문제 수정] 플레이 중 상태 (키 입력 처리) ---
        if (client.State == ClientState.Playing)
        {
            if (currentPlayerTurn == null || client.GamePlayer != currentPlayerTurn)
            {
                return; // 턴이 아님
            }

            Player actingPlayer = client.GamePlayer; 
            string key = command.ToUpper(); // 클라이언트가 보낸 ConsoleKey 문자열

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
    }

    private void SwitchTurn()
    {
        if (gamePlayers.Count == 0) return; 

        var playerList = gamePlayers.Values.ToList();
        if (playerList.Count == 0) return;

        int currentIndex = -1;
        if(currentPlayerTurn != null)
        {
            currentIndex = playerList.IndexOf(currentPlayerTurn);
        }
        
        for(int i=1; i <= playerList.Count; i++)
        {
            Player nextPlayer = playerList[(currentIndex + i) % playerList.Count];
            if(nextPlayer.HP > 0)
            {
                currentPlayerTurn = nextPlayer;
                return;
            }
        }
        currentPlayerTurn = null; // 살아있는 플레이어 없음
    }

    // [수정] 'move' 명령어 대신 'W', 'A', 'S', 'D' 키를 직접 처리
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

            case "I": // [신규] 상태 보기
                AddLog($"--- {player.Username} ({player.Class}) 상태 ---");
                AddLog($"LV:{player.Level} HP:{player.HP}/{player.MaxHP} MP:{player.MP}/{player.MaxMP}");
                AddLog($"ATK:{player.ATK} DEF:{player.DEF} STR:{player.STR} INT:{player.INT} DEX:{player.DEX}");
                BroadcastWorldState(); // 로그 갱신을 위해 브로드캐스트
                return false; // 턴 소모 안함

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
            BroadcastWorldState(); // [신규] 로그 갱신
            return false;
        }

        char tile = map[newX, newY];
        if (tile == '█')
        {
            AddLog("벽에 부딪혔습니다.");
            BroadcastWorldState(); // [신규] 로그 갱신
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
             BroadcastWorldState(); // [신규] 로그 갱신
             return false;
        }

        player.X = newX;
        player.Y = newY;
        AddLog($"{player.Username}(이)가 ({newX}, {newY})로 이동.");
        return true; 
    }

    // [수정] '1', '2', '3', '4' 키를 직접 처리
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

    // [수정] 'Q', 'W', 'E', 'B' 키를 직접 처리
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
             if(currentBattleMonster != null) WinBattle();
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
        logMessages.Add(message);
        if (logMessages.Count > 10) 
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
                ResetGame();
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
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("--- ASCII 미궁 ---");

        char[,] tempMap = (char[,])map.Clone();

        foreach (var m in monsters)
        {
            if (m.X >= 0 && m.X < MapWidth && m.Y >= 0 && m.Y < MapHeight)
                tempMap[m.X, m.Y] = m.Icon;
        }
        
        foreach (var p in gamePlayers.Values)
        {
            if (p.HP > 0) 
            {
                 if (p.X >= 0 && p.X < MapWidth && p.Y >= 0 && p.Y < MapHeight)
                    tempMap[p.X, p.Y] = (p == player) ? '@' : 'P'; 
            }
        }

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                sb.Append(tempMap[x, y]);
            }
            sb.AppendLine();
        }

        sb.AppendLine("--- 파티 정보 ---");
        foreach (var p in gamePlayers.Values)
        {
            string turnMark = (p == currentPlayerTurn) ? "<- (Turn)" : "";
            if (p == player) turnMark = (p == currentPlayerTurn) ? "<- (Your Turn)" : turnMark;
            
            string hpStatus = (p.HP <= 0) ? "[DEAD]" : $"HP: {p.HP}/{p.MaxHP}";
            sb.AppendLine($"[{p.Username}({p.Class}) | LV:{p.Level}] {hpStatus} | MP: {p.MP}/{p.MaxMP} {turnMark}");
        }

        sb.AppendLine("--- Log --- (입력: W,A,S,D / 상태: I)");
        foreach (var log in logMessages)
        {
            sb.AppendLine(log);
        }
        sb.AppendLine("-----------");

        return sb.ToString();
    }

    private string GetBattleDisplay(Player player)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("--- BATTLE STAGE ---");

        if (currentBattleMonster == null)
        {
            sb.AppendLine("전투가 종료되었습니다.");
            return sb.ToString();
        }

        sb.AppendLine($"          {currentBattleMonster.Name} [HP: {currentBattleMonster.HP}/{currentBattleMonster.MaxHP}]");
        sb.AppendLine(@"              /----\");
        sb.AppendLine(@"             | M  M |");
        sb.AppendLine(@"             |  --  |");
        sb.AppendLine(@"              \----/");
        sb.AppendLine();

        sb.AppendLine("--- 파티 정보 ---");
        foreach (var p in gamePlayers.Values)
        {
            string turnMark = (p == currentPlayerTurn) ? "<- (Turn)" : "";
            if (p == player) turnMark = (p == currentPlayerTurn) ? "<- (Your Turn)" : turnMark;

            string hpStatus = (p.HP <= 0) ? "[DEAD]" : $"HP: {p.HP}/{p.MaxHP}";
            sb.AppendLine($"[{p.Username}({p.Class}) | LV:{p.Level}] {hpStatus} | MP: {p.MP}/{p.MaxMP} {turnMark}");
        }

        if (player == currentPlayerTurn && player.HP > 0) 
        {
            sb.AppendLine("--- 행동 선택 ---");
            if (currentState == GameState.Battle)
            {
                sb.AppendLine("[1] 기본 공격");
                sb.AppendLine("[2] 스킬");
                sb.AppendLine("[3] 아이템");
                sb.AppendLine("[4] 후퇴");
            }
            else if (currentState == GameState.Battle_SkillSelect)
            {
                if (player.Skills.Count > 0) sb.AppendLine($"[Q] {player.Skills[0].Name} (MP {player.Skills[0].MpCost})");
                if (player.Skills.Count > 1) sb.AppendLine($"[W] {player.Skills[1].Name} (MP {player.Skills[1].MpCost})");
                if (player.Skills.Count > 2) sb.AppendLine($"[E] {player.Skills[2].Name} (MP {player.Skills[2].MpCost})");
                sb.AppendLine("[B] 뒤로가기");
            }
        }
        else if (player.HP <= 0)
        {
             sb.AppendLine("--- 당신은 쓰러져있습니다 ---");
        }
        else
        {
            sb.AppendLine($"--- {currentPlayerTurn?.Username}의 턴을 기다리는 중 ---");
        }

        sb.AppendLine("--- Log ---");
        foreach (var log in logMessages)
        {
            sb.AppendLine(log);
        }
        sb.AppendLine("-----------");
        return sb.ToString();
    }

    private void BroadcastWorldState()
    {
        foreach (var client in clients.Values)
        {
            if (client.GamePlayer != null && client.State == ClientState.Playing)
            {
                SendMessage(client, GetWorldDisplay(client.GamePlayer));
            }
        }
    }

    private void BroadcastBattleState()
    {
        foreach (var client in clients.Values)
        {
            if (client.GamePlayer != null && client.State == ClientState.Playing)
            {
                SendMessage(client, GetBattleDisplay(client.GamePlayer));
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
            if (includeNewline)
            {
                client.Writer.WriteLine(message); 
            }
            else
            {
                client.Writer.Write(message); 
            }
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
}

// [1번 문제 수정] 닉네임 버퍼 추가
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
    public StringBuilder NicknameBuffer { get; } = new StringBuilder(); // [신규]

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