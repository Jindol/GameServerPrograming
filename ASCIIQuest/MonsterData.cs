// MonsterData.cs
using System;
// Dictionary<TKey, TValue>, List<T>를 사용하기 위해 필요합니다.
using System.Collections.Generic;

/// <summary>
/// 몬스터의 '원본 스탯' 청사진(Blueprint)을 정의하는 순수 데이터 클래스(POCO)입니다.
/// 이 클래스는 데이터베이스(MonsterDB)에 '저장'되기 위한 용도이며,
/// 실제 게임에 등장하는 몬스터 인스턴스(객체)인 'Monster.cs'와는 구별됩니다.
/// </summary>
public class MonsterStats
{
    // --- 몬스터의 원본(기본) 스탯 ---
    public string Name { get; set; } = string.Empty;   
    public int MaxHP { get; set; }
    public int ATK { get; set; }
    public int DEF { get; set; } 
    public char Icon { get; set; } 
    public int EXPReward { get; set; }
    
    // --- 전투 화면 아트 보정값 ---
    public int ArtOffsetY { get; set; } 
    public int ArtOffsetX { get; set; } 
}


/// <summary>
/// [핵심] 정적(static) 클래스로, 몬스터의 '원본 스탯' 데이터베이스이자
/// 몬스터 '인스턴스(객체)'를 생성하는 'Factory(공장)' 역할을 합니다.
/// </summary>
public static class MonsterDB
{
    /// <summary>
    /// [핵심 데이터베이스] 모든 몬스터의 'ID'(string)를 '원본 스탯 청사진'(MonsterStats)에 매핑하는 중앙 딕셔너리입니다.
    /// 'CreateMonster' 메서드는 이 딕셔너리에서 ID를 검색하여 몬스터를 생성합니다.
    /// </summary>
   private static Dictionary<string, MonsterStats> allMonsters = new Dictionary<string, MonsterStats>
    {
        // --- Stage 1 (ASCII 미궁) ---
        // (ID)           (이름)              (아이콘) (HP) (ATK) (DEF) (경험치) (아트 Y보정) (아트 X보정)
        { "slime", new MonsterStats { Name = "데이터 찌꺼기", Icon = 'S', MaxHP = 65, ATK = 6, DEF = 2, EXPReward = 70, ArtOffsetY = 0, ArtOffsetX = 0 } },
        { "goblin", new MonsterStats { Name = "로그 환영", Icon = 'G', MaxHP = 60, ATK = 7, DEF = 4, EXPReward = 75, ArtOffsetY = 0, ArtOffsetX = 0 } },
        { "little_devil", new MonsterStats { Name = "버그 조각", Icon = 'L', MaxHP = 50, ATK = 8, DEF = 2, EXPReward = 70, ArtOffsetY = 0, ArtOffsetX = 0 } },
        
        // Stage 1 필드 보스
        { "fb_memory_leak", new MonsterStats { Name = "메모리 누수", Icon = 'F', MaxHP = 180, ATK = 14, DEF = 7, EXPReward = 300, ArtOffsetY = 0, ArtOffsetX = 0 } },

        // --- Stage 2 (데이터 동굴) ---
        { "skeleton", new MonsterStats { Name = "망가진 포인터", Icon = 'K', MaxHP = 100, ATK = 13, DEF = 7, EXPReward = 150, ArtOffsetY = 0, ArtOffsetX = 0 } },
        { "orc", new MonsterStats { Name = "과부하된 프로세스", Icon = 'O', MaxHP = 130, ATK = 15, DEF = 5, EXPReward = 190, ArtOffsetY = 0, ArtOffsetX = 0 } },
        { "bat", new MonsterStats { Name = "노이즈 비트", Icon = 'V', MaxHP = 80, ATK = 17, DEF = 4, EXPReward = 170, ArtOffsetY = 0, ArtOffsetX = 0 } },

        // Stage 2 필드 보스
        { "fb_rogue_process", new MonsterStats { Name = "로그 프로세스", Icon = 'F', MaxHP = 400, ATK = 24, DEF = 13, EXPReward = 800, ArtOffsetY = 0, ArtOffsetX = 0 } },

        // --- Stage 3 (코어) ---
        { "golem", new MonsterStats { Name = "커널 가드", Icon = 'D', MaxHP = 170, ATK = 18, DEF = 12, EXPReward = 320, ArtOffsetY = 0, ArtOffsetX = 0 } }, 
        { "dragon_whelp", new MonsterStats { Name = "프로토콜 와이번", Icon = 'R', MaxHP = 220, ATK = 21, DEF = 10, EXPReward = 410, ArtOffsetY = 0, ArtOffsetX = 0 } }, 
        { "corrupted_file", new MonsterStats { Name = "손상된 파일", Icon = 'C', MaxHP = 190, ATK = 19, DEF = 13, EXPReward = 360, ArtOffsetY = 0, ArtOffsetX = 0 } }, 

        // Stage 3 필드 보스
        { "fb_unhandled_exception", new MonsterStats { Name = "처리되지 않은 예외", Icon = 'F', MaxHP = 900, ATK = 30, DEF = 22, EXPReward = 2000, ArtOffsetY = 0, ArtOffsetX = 0 } },
        
        // --- 메인 보스 ---
        { "boss_golem", new MonsterStats { Name = "코드의 수호자", Icon = 'B', MaxHP = 600, ATK = 25, DEF = 18, EXPReward = 1500, ArtOffsetY = -2, ArtOffsetX = 0 } },
        { "boss_lich", new MonsterStats { Name = "부패한 관리자", Icon = 'B', MaxHP = 1150, ATK = 40, DEF = 24, EXPReward = 5000, ArtOffsetY = -2, ArtOffsetX = 0 } },
        { "boss_kernel", new MonsterStats { Name = "태초의 오류", Icon = 'B', MaxHP = 2800, ATK = 55, DEF = 35, EXPReward = 15000, ArtOffsetY = -2, ArtOffsetX = 0 } },
    
        { "mimic", new MonsterStats { Name = "미믹", Icon = 'M', MaxHP = 9, ATK = 10, DEF = 999, EXPReward = 150, ArtOffsetY = 2, ArtOffsetX = 0 } }
    };

    /// <summary>
    /// 스테이지(int)별로 스폰될 수 있는 일반 몬스터 ID(string 리스트)의 '스폰 풀(Pool)'을 정의합니다.
    /// (CreateRandomMonster 메서드가 이 딕셔너리를 참조합니다.)
    /// </summary>
    private static Dictionary<int, List<string>> stageMonsterIds = new Dictionary<int, List<string>>
    {
        { 1, new List<string> { "slime", "goblin", "little_devil" } },
        { 2, new List<string> { "skeleton", "orc", "bat" } },
        { 3, new List<string> { "golem", "dragon_whelp", "corrupted_file" } }
    };
    
    /// <summary>
    /// 스테이지(int)별 '메인 보스'의 ID를 매핑합니다.
    /// </summary>
    private static Dictionary<int, string> stageBossIds = new Dictionary<int, string>
    {
        { 1, "boss_golem" },
        { 2, "boss_lich" },
        { 3, "boss_kernel" }
    };

    /// <summary>
    /// 스테이지(int)별 '필드 보스'의 ID를 매핑합니다.
    /// </summary>
    private static Dictionary<int, string> stageFieldBossIds = new Dictionary<int, string>
    {
        { 1, "fb_memory_leak" },
        { 2, "fb_rogue_process" },
        { 3, "fb_unhandled_exception" }
    };

    /// <summary>
    /// [핵심 Factory Method] 몬스터 ID를 기반으로 실제 'Monster' 인스턴스(객체)를 생성합니다.
    /// </summary>
    /// <param name="id">생성할 몬스터의 ID (예: "slime")</param>
    /// <param name="x">몬스터가 스폰될 맵 X좌표</param>
    /// <param name="y">몬스터가 스폰될 맵 Y좌표</param>
    /// <returns>생성된 Monster 객체</returns>
    public static Monster CreateMonster(string id, int x, int y)
    {
        // 1. 'allMonsters' 딕셔너리에서 요청된 ID로 몬스터의 '원본 스탯(stats)'을 찾습니다.
        if (allMonsters.TryGetValue(id, out MonsterStats? stats))
        {
            // 2. 찾은 스탯(설계도)을 기반으로 새 'Monster' 객체(인스턴스)를 생성하여 반환합니다.
            return new Monster(stats.Name, x, y, stats.MaxHP, stats.ATK, stats.DEF, stats.Icon, stats.EXPReward, stats.ArtOffsetY, stats.ArtOffsetX, id);
        }
        
        // 3. [Fallback] 만약 딕셔너리에 ID가 없으면, 게임이 멈추는 것을 방지하기 위해
        //    'slime' 몬스터를 대신 생성하여 반환합니다.
        return CreateMonster("slime", x, y);
    }
    
    /// <summary>
    /// 지정된 스테이지(stage)의 '스폰 풀'에서 무작위 몬스터 1마리를 생성합니다.
    /// (Game.cs의 SpawnMonsters, Trap.cs의 Trigger 등에서 사용됩니다.)
    /// </summary>
    /// <param name="x">스폰될 X좌표</param>
    /// <param name="y">스폰될 Y좌표</param>
    /// <param name="rand">무작위 선택에 사용할 Random 객체</param>
    /// <param name="stage">몬스터 스폰 풀을 결정할 현재 스테이지 번호</param>
    /// <returns>생성된 무작위 Monster 객체</returns>
    public static Monster CreateRandomMonster(int x, int y, Random rand, int stage)
    {
        // 1. 'stageMonsterIds' 딕셔너리에서 현재 스테이지의 몬스터 ID 리스트(keys)를 가져옵니다.
        if (!stageMonsterIds.TryGetValue(stage, out var keys))
        {
            // 2. [Fallback] 만약 딕셔너리에 해당 스테이지 정보가 없으면, 1스테이지 리스트를 대신 사용합니다.
            keys = stageMonsterIds[1]; 
        }
        
        // 3. 가져온 리스트(keys)에서 몬스터 ID 하나를 무작위로 선택합니다.
        string randomId = keys[rand.Next(keys.Count)];
        
        // 4. 선택된 ID로 'CreateMonster'를 호출하여 실제 인스턴스를 반환합니다.
        return CreateMonster(randomId, x, y);
    }

    /// <summary>
    /// 지정된 스테이지(stage)의 '메인 보스'를 생성합니다.
    /// (Game.cs의 InitializeMap에서 호출됩니다.)
    /// </summary>
    /// <param name="x">보스가 스폰될 X좌표 (보스방 중앙)</param>
    /// <param name="y">보스가 스폰될 Y좌표 (보스방 중앙)</param>
    /// <param name="stage">생성할 보스를 결정할 현재 스테이지 번호</param>
    /// <returns>생성된 Boss Monster 객체</returns>
    public static Monster CreateBoss(int x, int y, int stage)
    {
        // 1. 'stageBossIds' 딕셔너리에서 현재 스테이지의 보스 ID를 가져옵니다.
        if (!stageBossIds.TryGetValue(stage, out var bossId))
        {
            // 2. [Fallback] 유효하지 않은 스테이지일 경우, 1스테이지 보스 ID를 대신 사용합니다.
            bossId = stageBossIds[1]; 
        }
        
        // 3. 해당 ID로 'CreateMonster'를 호출하여 보스 인스턴스를 반환합니다.
        return CreateMonster(bossId, x, y);
    }

    /// <summary>
    /// 지정된 스테이지의 '필드 보스' ID를 반환합니다.
    /// (Game.cs의 SpawnFieldBosses 메서드에서 이 ID를 받아 CreateMonster를 호출합니다.)
    /// </summary>
    public static string GetFieldBossIdForStage(int stage)
    {
        if (!stageFieldBossIds.TryGetValue(stage, out var bossId))
        {
            bossId = stageFieldBossIds[1]; 
        }
        return bossId;
    }
}