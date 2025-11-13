// AsciiArt.cs
using System;
using System.IO; 
using System.Text; 
using System.Collections.Generic; // List
using System.Linq; // Min()

public static class AsciiArt
{
    private static readonly string ArtDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Art");

    // 아트를 저장할 정적 변수들
    private static string[] Player_Warrior { get; set; }
    private static string[] Player_Wizard  { get; set; }
    private static string[] Player_Rogue   { get; set; }

    private static string[] Little_Devil    { get; set; }
    private static string[] Slime           { get; set; }
    private static string[] Goblin           { get; set; }
    private static string[] Monster_DataGolem { get; set; }
    private static string[] Boss_DataGolem { get; set; }
    
    private static string[] Chest_Closed { get; set; }
    private static string[] Chest_Open { get; set; }

    private static string[] Title_ChooseClass { get; set; }

    private static string[] Title_Main { get; set; }

    public static (float X, float Y) WarriorSelectOffset { get; } = (-0.05f, 0f);  
    
    // (기존: -2) -> (변경: -0.15f) (위쪽으로 15%)
    public static (float X, float Y) WizardSelectOffset  { get; } = (0f, -0.05f); 
    
    // (기존: -8) -> (변경: -0.2f) (왼쪽으로 20%)
    public static (float X, float Y) RogueSelectOffset   { get; } = (-0.1f, 0f);


    // --- [핵심 수정] ---
    // .txt 파일을 읽고 "정규화" (왼쪽 정렬)하는 새 LoadArt 메서드
    private static string[] LoadArt(string fileName)
    {
        string filePath = Path.Combine(ArtDirectory, fileName);
        try
        {
            if (!File.Exists(filePath))
            {
                return new string[] { "Error:", fileName, "not found." };
            }

            // 1. 탭을 공백 4칸으로 변환하여 임시 리스트에 읽기
            List<string> lines = new List<string>();
            foreach (string line in File.ReadLines(filePath, Encoding.UTF8))
            {
                // 탭이 있다면 공백 4개로 교체
                lines.Add(line.Replace("\t", "    "));
            }

            // 2. "최소 들여쓰기" 계산 (아트가 깨지는 핵심 원인)
            // (파일 내에서 가장 왼쪽에 있는 줄을 찾음)
            int minIndent = int.MaxValue;
            foreach (string line in lines)
            {
                // 비어있지 않은 줄만 검사
                if (line.Trim().Length > 0)
                {
                    // 이 줄의 왼쪽 공백 수 계산
                    int indent = 0;
                    while (indent < line.Length && line[indent] == ' ')
                    {
                        indent++;
                    }
                    
                    // 가장 작은 들여쓰기 값을 저장
                    if (indent < minIndent)
                    {
                        minIndent = indent;
                    }
                }
            }

            // (파일이 비어있거나 공백으로만 이루어진 경우)
            if (minIndent == int.MaxValue)
            {
                minIndent = 0;
            }

            // 3. 모든 줄에서 "최소 들여쓰기"만큼 공백 제거
            // (아트 블록 전체를 왼쪽으로 정렬)
            string[] normalizedLines = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                // 최소 들여쓰기 값보다 긴 줄만 자르기
                if (lines[i].Length >= minIndent)
                {
                    // 예: minIndent 2 -> "  / \" -> "/ \"
                    normalizedLines[i] = lines[i].Substring(minIndent);
                }
                else
                {
                    // 빈 줄이나, 들여쓰기만 있던 줄
                    normalizedLines[i] = ""; 
                }
            }

            return normalizedLines; // 정규화된(왼쪽 정렬된) 아트 반환
        }
        catch (Exception ex)
        {
            return new string[] { "Error:", ex.Message };
        }
    }

    // 정적 생성자 (LoadArt를 호출)
    static AsciiArt()
    {
        Player_Warrior    = LoadArt("player_warrior.txt");
        Player_Wizard     = LoadArt("player_wizard.txt");
        Player_Rogue      = LoadArt("player_rogue.txt");
        
        Little_Devil      = LoadArt("monster_1.txt");
        Slime             = LoadArt("monster_slime.txt");
        Goblin            = LoadArt("monster_goblin.txt");
        Monster_DataGolem = LoadArt("monster_golem_d.txt");
        Boss_DataGolem = LoadArt("monster_golem_b.txt");
        
        Chest_Closed = LoadArt("chest.txt");
        Chest_Open = LoadArt("chest_open.txt");
        Title_ChooseClass = LoadArt("ChooseClass.txt");
        Title_Main = LoadArt("Title.txt");
    }

    // --- (이하 GetPlayerArt, GetMonsterArt 메서드는 변경 없음) ---

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
                return Player_Warrior;
        }
    }

    public static string[] GetMonsterArt(char icon)
    {
        switch (icon)
        {
            case 'L': // 고블린
                return Little_Devil;
            case 'G': // 고블린
                return Goblin;
            case 'S': // 슬라임
                return Slime;
            case 'B': // 보스
                return Boss_DataGolem;
            case 'D': // 데이터 덩어리
            default:
                return Monster_DataGolem;
        }
    }

    public static string[] GetChestArt(bool isOpen)
    {
        return isOpen ? Chest_Open : Chest_Closed;
    }

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
    public static string[] GetChooseClassTitleArt()
    {
        return Title_ChooseClass;
    }

    public static string[] GetMainTitleArt()
    {
        return Title_Main;
    }
}