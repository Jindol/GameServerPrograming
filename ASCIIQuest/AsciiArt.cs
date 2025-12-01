// AsciiArt.cs

// System.IO: Path.Combine, File.Exists, File.ReadLines 등 파일 입출력 기능을 위해 사용
using System;
using System.IO; 
// System.Text: UTF-8 인코딩으로 파일을 읽기 위해 사용
using System.Text; 
// System.Collections.Generic: Dictionary와 List를 사용하기 위해 필요
using System.Collections.Generic; 
using System.Linq; 

/// <summary>
/// 게임에 사용되는 모든 아스키 아트(.txt 파일)를 로드하고 관리하는 정적(static) 클래스입니다.
/// '리소스 매니저' 역할을 하며, 게임 시작 시 모든 아트를 메모리에 미리 로드합니다.
/// </summary>
public static class AsciiArt
{
    /// <summary>
    /// 아스키 아트 .txt 파일들이 저장된 "Art" 폴더의 절대 경로입니다.
    /// (예: C:\MyGame\bin\Debug\net6.0\Art)
    /// </summary>
    private static readonly string ArtDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Art");

    // --- 아트를 저장할 정적 변수들 ---
    
    // 플레이어 직업별 아트 배열
    private static string[] Player_Warrior { get; set; }
    private static string[] Player_Wizard  { get; set; }
    private static string[] Player_Rogue   { get; set; }

    /// <summary>
    /// 몬스터 ID(string)를 몬스터 아트(string[])와 매핑하는 딕셔너리입니다.
    /// (예: "slime" 키 -> 슬라임 아트 배열)
    /// </summary>
    private static Dictionary<string, string[]> monsterArtLibrary = new Dictionary<string, string[]>();
    
    // 몬스터 아트 파일을 찾지 못했을 때 대신 표시할 기본 아트입니다.
    private static string[] artNotFound; 

    // 상자 아트 배열 (닫힘/열림)
    private static string[] Chest_Closed { get; set; }
    private static string[] Chest_Open { get; set; }

    // 각종 타이틀 아트 배열
    private static string[] Title_ChooseClass { get; set; }
    private static string[] Title_Main { get; set; }
    private static string[] Title_GameClear { get; set; }

    private static string[] Title_GameOver { get; set; }

    private static string[] Stage_1 { get; set; }
    private static string[] Stage_2 { get; set; }
    private static string[] Stage_3 { get; set; }
    
    private static string[] Name_Stage_1 { get; set; }
    private static string[] Name_Stage_2 { get; set; }
    private static string[] Name_Stage_3 { get; set; }

    // 숫자 아스키 아트를 저장하는 배열
    private static string[] Number_0 { get; set; }
    private static string[] Number_1 { get; set; }
    private static string[] Number_2 { get; set; }
    private static string[] Number_3 { get; set; }
    private static string[] Number_4 { get; set; }
    private static string[] Number_5 { get; set; }
    private static string[] Number_6 { get; set; }
    private static string[] Number_7 { get; set; }
    private static string[] Number_8 { get; set; }
    private static string[] Number_9 { get; set; }
    
    // 기호 아스키 아트를 저장하는 배열
    private static string[] Symbol_Plus { get; set; }
    private static string[] Symbol_Minus { get; set; }
    private static string[] Symbol_Miss { get; set; }
    private static string[] Symbol_Critical { get; set; }

    private static string[] Icon_Error { get; set; }
    private static string[] Text_Error { get; set; }

    private static string[] Text_Game { get; set; }
    private static string[] Text_Start { get; set; }

    private static string[] Text_Trap { get; set; }      // 함정
    private static string[] Text_Warning { get; set; }   // 필드 보스
    private static string[] Text_Fatal { get; set; }     // 스테이지 보스

    private static string[] Title_Intro { get; set; }
    private static string[] Title_Ending { get; set; }

    /// <summary>
    /// 숫자/기호(char)를 해당 아스키 아트(string[])와 매핑하는 딕셔너리입니다.
    /// (예: '0' 키 -> Number_0 배열)
    /// </summary>
    private static Dictionary<char, string[]> numberArtLibrary = new Dictionary<char, string[]>();

    // --- 직업 선택창 전용 위치 보정값 ---
    // (이 값들은 Game.cs의 DrawClassConfirmation에서 사용됩니다.)
    public static (float X, float Y) WarriorSelectOffset { get; } = (-0.05f, 0f);  
    public static (float X, float Y) WizardSelectOffset  { get; } = (0f, -0.05f); 
    public static (float X, float Y) RogueSelectOffset   { get; } = (0f, 0f);

    /// <summary>
    /// .txt 파일을 읽고, 탭을 공백으로 변환하며, "자동 왼쪽 정렬(정규화)"을 수행하는 헬퍼 메서드입니다.
    /// </summary>
    /// <param name="fileName">"Art" 폴더 내의 .txt 파일 이름 (예: "player_warrior.txt")</param>
    /// <returns>정규화된 아스키 아트 문자열 배열</returns>
    private static string[] LoadArt(string fileName)
    {
        // "Art" 폴더 경로와 파일 이름을 조합하여 전체 파일 경로를 생성합니다.
        string filePath = Path.Combine(ArtDirectory, fileName);
        try
        {
            // 파일이 존재하지 않으면 오류 메시지를 담은 배열을 반환합니다.
            if (!File.Exists(filePath))
            {
                return new string[] { "Error:", fileName, "not found." };
            }

            // 1. 임시 리스트에 파일을 읽어들입니다.
            List<string> lines = new List<string>();
            // .txt 파일을 UTF-8 인코딩으로 한 줄씩 읽습니다.
            foreach (string line in File.ReadLines(filePath, Encoding.UTF8))
            {
                // 탭(\t) 문자가 포함되어 있다면, 일관성을 위해 공백 4칸으로 모두 치환합니다.
                lines.Add(line.Replace("\t", "    "));
            }

            // 2. "최소 들여쓰기" 값을 계산합니다. (아트 정규화의 핵심)
            // (파일 내에서 가장 왼쪽에 있는 텍스트를 찾아 기준으로 삼습니다.)
            int minIndent = int.MaxValue;
            foreach (string line in lines)
            {
                // 비어있지 않은(공백이 아닌 문자가 있는) 줄만 검사합니다.
                if (line.Trim().Length > 0)
                {
                    // 이 줄의 왼쪽에 있는 공백(들여쓰기)이 몇 칸인지 계산합니다.
                    int indent = 0;
                    while (indent < line.Length && line[indent] == ' ')
                    {
                        indent++;
                    }
                    
                    // 현재 줄의 들여쓰기가 지금까지 찾은 '최소 들여쓰기'보다 작으면 갱신합니다.
                    if (indent < minIndent)
                    {
                        minIndent = indent;
                    }
                }
            }

            // (파일이 아예 비어있거나, 공백으로만 이루어진 경우 minIndent가 갱신되지 않았으므로 0으로 설정)
            if (minIndent == int.MaxValue)
            {
                minIndent = 0;
            }

            // 3. 모든 줄에서 "최소 들여쓰기"만큼 공백을 제거합니다.
            // (아트 블록 전체를 왼쪽으로 정렬시킵니다.)
            string[] normalizedLines = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                // 줄의 길이가 최소 들여쓰기 값보다 긴 경우에만 자릅니다.
                if (lines[i].Length >= minIndent)
                {
                    // 예: minIndent가 2일 때 -> "  / \"  ->  "/ \"
                    normalizedLines[i] = lines[i].Substring(minIndent);
                }
                else
                {
                    // (빈 줄이거나, 들여쓰기만 있던 줄은 그냥 빈 문자열로 처리)
                    normalizedLines[i] = ""; 
                }
            }

            // 정규화된(왼쪽 정렬된) 아트 배열을 반환합니다.
            return normalizedLines;
        }
        catch (Exception ex)
        {
            // 파일 읽기 실패 시 오류 메시지를 반환합니다.
            return new string[] { "Error:", ex.Message };
        }
    }

    /// <summary>
    /// 정적 생성자(Static Constructor)입니다.
    /// 프로그램에서 'AsciiArt' 클래스를 처음 참조할 때 "단 한 번만" 자동으로 호출됩니다.
    /// 모든 .txt 아트를 미리 메모리에 로드(Pre-loading)하는 역할을 합니다.
    /// </summary>
    static AsciiArt()
    {
        // 플레이어 아트 로드
        Player_Warrior    = LoadArt("player_warrior.txt");
        Player_Wizard     = LoadArt("player_wizard.txt");
        Player_Rogue      = LoadArt("player_rogue.txt");
        
        // 상자/타이틀 아트 로드
        Chest_Closed = LoadArt("chest.txt");
        Chest_Open   = LoadArt("chest_open.txt");
        Title_ChooseClass = LoadArt("ChooseClass.txt");
        Title_Main = LoadArt("Title.txt");
        Title_GameClear = LoadArt("CLEAR.txt");

        // [핵심] 몬스터 아트 라이브러리 로드
        // (MonsterData.cs의 몬스터 ID "키"와 .txt 파일 이름이 일치해야 합니다.)
        
        // 아트 로드 실패 시 표시할 기본 아트
        artNotFound = new string[] { "Art Not Found" };
        
        // Stage 1
        monsterArtLibrary["slime"] = LoadArt("monster_slime.txt");
        monsterArtLibrary["goblin"] = LoadArt("monster_goblin.txt");
        monsterArtLibrary["little_devil"] = LoadArt("monster_1.txt");
        monsterArtLibrary["fb_memory_leak"] = LoadArt("fb_memory_leak.txt");

        // Stage 2
        monsterArtLibrary["skeleton"] = LoadArt("monster_skeleton.txt");
        monsterArtLibrary["orc"] = LoadArt("monster_orc.txt");
        monsterArtLibrary["bat"] = LoadArt("monster_bat.txt");
        monsterArtLibrary["fb_rogue_process"] = LoadArt("fb_rogue_process.txt");

        // Stage 3
        monsterArtLibrary["golem"] = LoadArt("monster_golem_d.txt"); 
        monsterArtLibrary["dragon_whelp"] = LoadArt("monster_dragon_whelp.txt");
        monsterArtLibrary["corrupted_file"] = LoadArt("monster_corrupted_file.txt"); 
        monsterArtLibrary["fb_unhandled_exception"] = LoadArt("fb_unhandled_exception.txt"); 
        
        // Bosses
        monsterArtLibrary["boss_golem"] = LoadArt("monster_golem_b.txt"); 
        monsterArtLibrary["boss_lich"] = LoadArt("boss_lich.txt");
        monsterArtLibrary["boss_kernel"] = LoadArt("boss_kernel_dragon.txt");

        monsterArtLibrary["mimic"] = LoadArt("mimic.txt");

        // 숫자 및 기호 아트 로드
        Number_0 = LoadArt("number_0.txt");
        Number_1 = LoadArt("number_1.txt");
        Number_2 = LoadArt("number_2.txt");
        Number_3 = LoadArt("number_3.txt");
        Number_4 = LoadArt("number_4.txt");
        Number_5 = LoadArt("number_5.txt");
        Number_6 = LoadArt("number_6.txt");
        Number_7 = LoadArt("number_7.txt");
        Number_8 = LoadArt("number_8.txt");
        Number_9 = LoadArt("number_9.txt");
        Symbol_Plus  = LoadArt("symbol_plus.txt");
        Symbol_Minus = LoadArt("symbol_minus.txt");
        Symbol_Miss  = LoadArt("symbol_miss.txt");
        Symbol_Critical = LoadArt("symbol_critical.txt");

        // [신규] 에러 아트 로드
        Icon_Error = LoadArt("ERROR.txt");
        Text_Error = LoadArt("ERROR_TEXT.txt");
        Text_Trap    = LoadArt("TEXT_TRAP.txt");
        Text_Warning = LoadArt("TEXT_WARNING.txt");
        Text_Fatal   = LoadArt("TEXT_FATAL.txt");

        Text_Game = LoadArt("GAME.txt");
        Text_Start = LoadArt("START.txt");

        Title_GameOver = LoadArt("GAME_OVER.txt");

        Stage_1 = LoadArt("STAGE_1.txt");
        Stage_2 = LoadArt("STAGE_2.txt");
        Stage_3 = LoadArt("STAGE_3.txt");

        Name_Stage_1 = LoadArt("NAME_STAGE_1.txt");
        Name_Stage_2 = LoadArt("NAME_STAGE_2.txt");
        Name_Stage_3 = LoadArt("NAME_STAGE_3.txt");

        Title_Intro = LoadArt("TITLE_INTRO.txt");
        Title_Ending = LoadArt("TITLE_ENDING.txt");

        // [핵심] 숫자/기호 딕셔너리에 매핑
        // (Game.cs의 DrawAsciiNumber가 이 딕셔너리를 사용합니다.)
        numberArtLibrary['0'] = Number_0;
        numberArtLibrary['1'] = Number_1;
        numberArtLibrary['2'] = Number_2;
        numberArtLibrary['3'] = Number_3;
        numberArtLibrary['4'] = Number_4;
        numberArtLibrary['5'] = Number_5;
        numberArtLibrary['6'] = Number_6;
        numberArtLibrary['7'] = Number_7;
        numberArtLibrary['8'] = Number_8;
        numberArtLibrary['9'] = Number_9;
        numberArtLibrary['+'] = Symbol_Plus;
        numberArtLibrary['-'] = Symbol_Minus;
        numberArtLibrary['M'] = Symbol_Miss; // (MISS 표시는 'M' 키로 매핑)

        // 딕셔너리에 없는 문자가 요청될 경우 사용할 기본 '?' 아트
        numberArtLibrary['?'] = new string[] { "  ?  ", " ? ? ", "  ?  ", "  ?  ", "     " };
    }

    /// <summary>
    /// 지정된 플레이어 직업(pClass)에 맞는 아트를 반환합니다.
    /// </summary>
    public static string[] GetPlayerArt(PlayerClass pClass)
    {
        switch (pClass)
        {
            case PlayerClass.Warrior:
                return Player_Warrior;
            case PlayerClass.Wizard:
                return Player_Wizard;
            case PlayerClass.Rogue:
                return Player_Rogue;
            default:
                // 예외적인 경우, 기본값으로 전사 아트를 반환합니다.
                return Player_Warrior;
        }
    }

    /// <summary>
    /// 지정된 몬스터 ID(monsterId)에 맞는 아트를 딕셔너리에서 찾아 반환합니다.
    /// </summary>
    public static string[] GetMonsterArt(string monsterId)
    {
        // 1. 딕셔너리에서 monsterId 키로 아트를 찾습니다.
        if (monsterArtLibrary.TryGetValue(monsterId, out string[]? art))
        {
            // 2. 찾으면 해당 아트를 반환합니다.
            return art;
        }
        
        // 3. 못 찾았을 경우, 대체용으로 "slime" 아트를 시도합니다.
        if (monsterArtLibrary.TryGetValue("slime", out string[]? fallbackArt))
        {
            return fallbackArt;
        }
        
        // 4. "slime" 아트마저 없으면, 로드 실패용 "artNotFound" 아트를 반환합니다.
        return artNotFound;
    }

    /// <summary>
    /// 상자의 상태(isOpen)에 따라 닫힌 상자 또는 열린 상자 아트를 반환합니다.
    /// </summary>
    public static string[] GetChestArt(bool isOpen)
    {
        return isOpen ? Chest_Open : Chest_Closed;
    }

    /// <summary>
    /// 직업 선택창에서 사용할 직업별 '위치 보정값'(X, Y 비율)을 반환합니다.
    /// </summary>
    public static (float X, float Y) GetClassSelectOffset(PlayerClass pClass)
    {
        switch (pClass)
        {
            case PlayerClass.Warrior: return WarriorSelectOffset;
            case PlayerClass.Wizard: return WizardSelectOffset;
            case PlayerClass.Rogue: return RogueSelectOffset;
            default: return (0f, 0f);
        }
    }
    
    /// <summary>
    /// 직업 선택창의 타이틀 아트를 반환합니다.
    /// </summary>
    public static string[] GetChooseClassTitleArt()
    {
        return Title_ChooseClass;
    }

    /// <summary>
    /// 메인 메뉴의 타이틀 아트를 반환합니다.
    /// </summary>
    public static string[] GetMainTitleArt()
    {
        return Title_Main;
    }

    /// <summary>
    /// 숫자/기호 아트가 매핑된 딕셔너리 전체를 반환합니다. (Game.cs의 DrawAsciiNumber에서 사용)
    /// </summary>
    public static Dictionary<char, string[]> GetNumberArtLibrary()
    {
        return numberArtLibrary;
    }

    /// <summary>
    /// 크리티컬 히트 연출용 아트를 반환합니다.
    /// </summary>
    public static string[] GetCriticalArt()
    {
        return Symbol_Critical;
    }

    /// <summary>
    /// 게임 클리어(엔딩) 화면의 타이틀 아트를 반환합니다.
    /// </summary>
    public static string[] GetGameClearArt()
    {
        return Title_GameClear;
    }

    public static string[] GetGameOverArt()
    {
        return Title_GameOver;
    }

    public static string[] GetErrorIcon() { return Icon_Error; }
    public static string[] GetErrorText() { return Text_Error; }

    public static string[] GetTrapText() { return Text_Trap; }
    public static string[] GetWarningText() { return Text_Warning; }
    public static string[] GetFatalText() { return Text_Fatal; }

        public static string[] GetIntroTitleArt() { return Title_Intro; }
    public static string[] GetEndingTitleArt() { return Title_Ending; }

    public static string[] GetStageNumberArt(int stage)
    {
        switch (stage)
        {
            case 1: return Stage_1;
            case 2: return Stage_2;
            case 3: return Stage_3;
            default: return Stage_1;
        }
    }

    public static string[] GetStageNameArt(int stage)
    {
        switch (stage)
        {
            case 1: return Name_Stage_1;
            case 2: return Name_Stage_2;
            case 3: return Name_Stage_3;
            default: return Name_Stage_1;
        }
    }

    public static string[] GetGameTextArt() { return Text_Game; }
    public static string[] GetStartTextArt() { return Text_Start; }

}