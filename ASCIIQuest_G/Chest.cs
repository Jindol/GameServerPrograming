// Chest.cs
using System;
using System.Collections.Generic;

public class Chest
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public char Icon { get; private set; }
    public bool IsOpen { get; private set; }
    public ConsoleColor Color { get; private set; }

    // 함정 및 빈 상자 확률
    private const double CHEST_TRAP_CHANCE = 0.15;  // 15%
    private const double CHEST_EMPTY_CHANCE = 0.10; // 10%

    private const double CHEST_EQUIPMENT_CHANCE = 0.40;

    public Chest(int x, int y)
    {
        X = x;
        Y = y;
        Icon = '$'; // 상자 아이콘
        Color = ConsoleColor.Yellow;
        IsOpen = false;
    }

    // 상자가 열렸을 때 맵 타일과 아이콘을 변경하는 헬퍼
    private void SetIsOpen(Game game)
    {
        IsOpen = true;
        Icon = '_'; // (아이콘은 더 이상 사용되지 않지만, 상태 구분을 위해 둠)
        Color = ConsoleColor.DarkGray;
        
        // [핵심 수정] 맵의 해당 위치 타일을 '.' (바닥)으로 영구 변경
        game.UpdateMapTile(X, Y, '.'); 
    }

    // F키로 상자를 여는 메인 로직
    public void Open(Player player, Game game, Random rand, int currentStage)  
    {
        if (IsOpen)
        {
            game.AddLog("이미 비어있는 상자입니다.");
            return;
        }

        game.AddLog("상자를 엽니다...");
        SetIsOpen(game);

        // 1. 함정 발동 (15%)
        if (rand.NextDouble() < CHEST_TRAP_CHANCE)
        {
            game.AddLog("함정이다! 상자에서 몬스터가 튀어나왔다!");
            game.StartBattle(MonsterDB.CreateRandomMonster(0, 0, rand, currentStage), true);
            return;
        }

        // 2. 꽝 (빈 상자) (10%)
        if (rand.NextDouble() < CHEST_EMPTY_CHANCE)
        {
            game.AddLog("상자가 비어있습니다.");
            return;
        }

        // 3. 아이템 획득 (75%)
        int lootCount = 1;
        if (rand.NextDouble() < 0.10) lootCount = 3;      // 10% (전체 7.5%)
        else if (rand.NextDouble() < 0.25) lootCount = 2; // 15% (전체 11.25%)
        // (나머지 75%는 1개)

        List<Item> foundItems = new List<Item>();
        for (int i = 0; i < lootCount; i++)
        {
            if (rand.NextDouble() < CHEST_EQUIPMENT_CHANCE) 
            {
                foundItems.Add(ItemDB.GenerateRandomEquipment(player.Class, rand, false, currentStage));
            }
            else // 60% 확률로 소비템
            {
                foundItems.Add(ItemDB.CreateRandomConsumable(rand, false, currentStage));
            }
        }

        // Game.cs에 아이템 처리 위임
        game.ProcessChestLoot(foundItems);
    }
}