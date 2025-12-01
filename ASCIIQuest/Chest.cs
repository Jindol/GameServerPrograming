// Chest.cs
using System;
using System.Collections.Generic;

/// <summary>
/// 맵에 배치되는 '상자' 오브젝트를 정의하는 클래스입니다.
/// 플레이어가 상호작용(F키)하면 열리며, 함정이거나 아이템을 제공합니다.
/// </summary>
public class Chest
{
    // --- 상자의 속성(Properties) ---
    
    /// <summary>
    /// 상자의 맵 X좌표
    /// </summary>
    public int X { get; private set; }
    
    /// <summary>
    /// 상자의 맵 Y좌표
    /// </summary>
    public int Y { get; private set; }
    
    /// <summary>
    /// 맵에 표시될 아이콘 (예: '$')
    /// </summary>
    public char Icon { get; private set; }
    
    /// <summary>
    /// 상자가 이미 열렸는지 여부를 나타내는 플래그
    /// </summary>
    public bool IsOpen { get; private set; }
    
    /// <summary>
    /// 맵에 표시될 아이콘의 색상 (예: ConsoleColor.Yellow)
    /// </summary>
    public ConsoleColor Color { get; private set; }

    // --- 확률 상수 ---
    
    // 상자가 함정일 확률 (15%)
    private const double CHEST_TRAP_CHANCE = 0.15;
    // 상자가 비어있을 확률 (10%)
    private const double CHEST_EMPTY_CHANCE = 0.10;
    // (100% - 15% - 10% = 75% 확률로 아이템 획득)

    // 아이템 획득 시, 해당 아이템이 '장비'일 확률 (기본 40%)
    private const double CHEST_EQUIPMENT_CHANCE = 0.40;

    /// <summary>
    /// 지정된 좌표(x, y)에 새 상자 인스턴스를 생성합니다.
    /// </summary>
    public Chest(int x, int y)
    {
        X = x;
        Y = y;
        Icon = '$'; // 닫힌 상자 아이콘
        Color = ConsoleColor.Yellow;
        IsOpen = false; // 생성 시에는 항상 닫힌 상태
    }

    /// <summary>
    /// 상자가 열렸을 때 호출되는 내부 헬퍼 메서드입니다.
    /// 상자의 상태를 '열림'으로 변경하고, 맵 타일을 '.'(바닥)으로 영구히 변경합니다.
    /// </summary>
    /// <param name="game">맵 타일 변경을 위해 Game 엔진 참조가 필요합니다.</param>
    private void SetIsOpen(Game game)
    {
        IsOpen = true;
        Icon = '_'; // (더 이상 사용되진 않지만, 열린 상태임을 명시)
        Color = ConsoleColor.DarkGray;
        
        // [핵심] Game.cs에 요청하여 맵의 '$' 아이콘을 '.'(바닥)으로 덮어씁니다.
        // 이를 통해 플레이어가 열린 상자 위로 지나갈 수 있게 됩니다.
        game.UpdateMapTile(X, Y, '.'); 
    }

    /// <summary>
    /// 플레이어가 상자를 여는 메인 로직입니다. (Game.cs의 TryOpenChest에서 호출됨)
    /// 확률에 따라 (1.함정) -> (2.꽝) -> (3.아이템) 순서로 검사합니다.
    /// </summary>
    /// <param name="player">아이템 획득 주체 (현재는 장비 생성 시 직업 참조용)</param>
    /// <param name="game">전투 시작(StartBattle) 또는 아이템 획득(ProcessChestLoot) 처리를 위임할 Game 엔진</param>
    /// <param name="rand">확률 계산에 사용할 Random 객체</param>
    /// <param name="currentStage">현재 스테이지 (함정 몬스터 및 아이템 등급 스케일링에 사용)</param>
    public void Open(Player player, Game game, Random rand, int currentStage)  
    {
        // 이미 열린 상자라면 아무것도 하지 않고 종료합니다.
        if (IsOpen)
        {
            game.AddLog("이미 비어있는 상자입니다.");
            return;
        }

        game.AddLog("상자를 엽니다...");
        // 상자 상태를 '열림'으로 변경하고 맵 타일을 '.'으로 바꿉니다.
        SetIsOpen(game);

        // 1. 함정 발동 검사 (15% 확률)
        if (rand.NextDouble() < CHEST_TRAP_CHANCE)
        {
            game.AddLog("함정이다! 상자가 몬스터로 변했다!");
            
            // [수정] 무작위 몬스터 대신 '미믹' 생성 (ID: "mimic")
            Monster mimic = MonsterDB.CreateMonster("mimic", 0, 0);
            
            // (CreateMonster는 기본 스탯으로 생성하므로, Game.StartBattle에서 스테이지에 맞게 조정됨)
            game.StartBattle(mimic, true); 
            return;
        }

        // 2. 꽝 (빈 상자) 검사 (10% 확률)
        if (rand.NextDouble() < CHEST_EMPTY_CHANCE)
        {
            game.AddLog("상자가 비어있습니다.");
            return; // 아이템 획득 로직을 실행하지 않고 즉시 종료
        }

        // 3. 아이템 획득 (위의 확률을 모두 피한 나머지 75% 확률)
        
        // 3a. 획득할 아이템 개수 결정 (기본 1개, 확률적으로 2~3개)
        int lootCount = 1;
        if (rand.NextDouble() < 0.10) lootCount = 3;      // (10% 확률로 3개)
        else if (rand.NextDouble() < 0.25) lootCount = 2; // (25% 확률로 2개)
        // (나머지 65% 확률로 1개)

        // 3b. 스테이지별 장비 드랍률 계산 (40% -> 50% -> 60%)
        double equipmentChance = CHEST_EQUIPMENT_CHANCE + ((currentStage - 1) * 0.10);

        List<Item> foundItems = new List<Item>();
        for (int i = 0; i < lootCount; i++)
        {
            // 3c. (equipmentChance) 확률로 장비 생성
            if (rand.NextDouble() < equipmentChance) 
            {
                // ItemDB에 장비 생성을 요청 (ItemDB는 currentStage에 맞춰 품질을 조절)
                foundItems.Add(ItemDB.GenerateRandomEquipment(player.Class, rand, false, currentStage));
            }
            else // (100% - equipmentChance) 확률로 소비템 생성
            {
                foundItems.Add(ItemDB.CreateRandomConsumable(rand, false, currentStage));
            }
        }

        // 4. [관심사 분리] 획득한 아이템 리스트(foundItems)의 처리를 Game 엔진에 위임합니다.
        // (Game.cs는 이 리스트를 받아 LootDrop, LootSummary 창을 띄웁니다.)
        game.ProcessChestLoot(foundItems);
    }

    // [신규] 네트워크 동기화용 강제 열기 메서드
    public void ForceOpen(Game game)
    {
        if (IsOpen) return;
        
        // 내부 private 메서드였던 SetIsOpen 로직을 수행
        IsOpen = true;
        // Icon = '_'; // (필요하다면)
        // Color = ConsoleColor.DarkGray; // (필요하다면)
        
        // 맵 타일 변경
        game.UpdateMapTile(X, Y, '.');
    }
}