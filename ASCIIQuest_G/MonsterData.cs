// MonsterData.cs
using System;
using System.Collections.Generic;

// ... (MonsterStats 클래스는 변경 없음) ...
public class MonsterStats
{
    public string Name { get; set; } = string.Empty;   
     public int MaxHP { get; set; }
    public int ATK { get; set; }
    public int DEF { get; set; } 
    public char Icon { get; set; } 
    public int EXPReward { get; set; }
    public int ArtOffsetY { get; set; } 
    public int ArtOffsetX { get; set; } 
}


// 몬스터 데이터베이스 및 생성기
public static class MonsterDB
{
    // [신규] 1. 모든 몬스터의 '원본 스탯'을 ID별로 정의
    private static Dictionary<string, MonsterStats> allMonsters = new Dictionary<string, MonsterStats>
    {
        // --- Stage 1 (ASCII 미궁) ---
        {
            "slime", new MonsterStats { Name = "슬라임", Icon = 'S', MaxHP = 50, ATK = 4, DEF = 1, EXPReward = 50, ArtOffsetY = 4, ArtOffsetX = 0 } 
        },
        { 
            "goblin", new MonsterStats { Name = "악마", Icon = 'G', MaxHP = 45, ATK = 5, DEF = 3, EXPReward = 60, ArtOffsetY = 4, ArtOffsetX = 0 } 
        },
        { 
            "little_devil", new MonsterStats { Name = "소악마", Icon = 'L', MaxHP = 35, ATK = 6, DEF = 1, EXPReward = 50, ArtOffsetY = 4, ArtOffsetX = 0 } 
        },
        
        // --- [신규] Stage 2 (데이터 동굴) ---
        {
            "skeleton", new MonsterStats { Name = "스켈레톤", Icon = 'K', MaxHP = 80, ATK = 10, DEF = 5, EXPReward = 120, ArtOffsetY = 4, ArtOffsetX = 0 } 
        },
        {
            "orc", new MonsterStats { Name = "오크", Icon = 'O', MaxHP = 100, ATK = 12, DEF = 3, EXPReward = 150, ArtOffsetY = 4, ArtOffsetX = 0 } 
        },
        {
            "bat", new MonsterStats { Name = "박쥐", Icon = 'V', MaxHP = 60, ATK = 14, DEF = 2, EXPReward = 130, ArtOffsetY = 4, ArtOffsetX = 0 } 
        },

        // --- [신규] Stage 3 (코어) ---
        {
            "golem", new MonsterStats { Name = "골렘", Icon = 'D', MaxHP = 150, ATK = 18, DEF = 10, EXPReward = 300, ArtOffsetY = 4, ArtOffsetX = 0 } 
        },
        {
            "dragon_whelp", new MonsterStats { Name = "새끼용", Icon = 'R', MaxHP = 200, ATK = 22, DEF = 8, EXPReward = 400, ArtOffsetY = 4, ArtOffsetX = 0 } 
        },
        
        // --- 보스 ---
        {
            "boss_golem", new MonsterStats { Name = "데이터 골렘", Icon = 'B', MaxHP = 500, ATK = 20, DEF = 15, EXPReward = 1500, ArtOffsetY = 0, ArtOffsetX = 0 } 
        },
        {
            "boss_lich", new MonsterStats { Name = "리치 왕", Icon = 'B', MaxHP = 1000, ATK = 35, DEF = 20, EXPReward = 5000, ArtOffsetY = 0, ArtOffsetX = 0 } 
        },
        {
            "boss_kernel", new MonsterStats { Name = "커널 드래곤", Icon = 'B', MaxHP = 2500, ATK = 50, DEF = 30, EXPReward = 15000, ArtOffsetY = 0, ArtOffsetX = 0 } 
        }
    };

    // [신규] 2. 스테이지별 몬스터 리스트 (ID)
    private static Dictionary<int, List<string>> stageMonsterIds = new Dictionary<int, List<string>>
    {
        { 1, new List<string> { "slime", "goblin", "little_devil" } },
        { 2, new List<string> { "skeleton", "orc", "bat" } },
        { 3, new List<string> { "golem", "dragon_whelp" } }
    };
    
    // [신규] 3. 스테이지별 보스 (ID)
    private static Dictionary<int, string> stageBossIds = new Dictionary<int, string>
    {
        { 1, "boss_golem" },
        { 2, "boss_lich" },
        { 3, "boss_kernel" }
    };

    // [수정] 4. 몬스터 생성기 (ID 기반)
    public static Monster CreateMonster(string id, int x, int y)
    {
        if (allMonsters.TryGetValue(id, out MonsterStats? stats))
        {
            return new Monster(stats.Name, x, y, stats.MaxHP, stats.ATK, stats.DEF, stats.Icon, stats.EXPReward, stats.ArtOffsetY, stats.ArtOffsetX);
        }
        return CreateMonster("slime", x, y); // (Fallback)
    }
    
    // [수정] 5. 무작위 몬스터 생성 (stage 기반)
    public static Monster CreateRandomMonster(int x, int y, Random rand, int stage)
    {
        // 현재 스테이지에 맞는 몬스터 ID 리스트를 가져옴
        if (!stageMonsterIds.TryGetValue(stage, out var keys))
        {
            keys = stageMonsterIds[1]; // (Fallback to Stage 1)
        }
        
        string randomId = keys[rand.Next(keys.Count)];
        return CreateMonster(randomId, x, y);
    }

    // [수정] 6. 보스 생성 (stage 기반)
    public static Monster CreateBoss(int x, int y, int stage)
    {
        if (!stageBossIds.TryGetValue(stage, out var bossId))
        {
            bossId = stageBossIds[1]; // (Fallback to Stage 1)
        }
        return CreateMonster(bossId, x, y);
    }
}