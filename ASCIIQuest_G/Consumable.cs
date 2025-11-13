// Consumable.cs

// 소비 아이템 종류
public enum ConsumableType
{
    HealthPotion,
    ManaPotion
}

public class Consumable : Item
{
    public ConsumableType CType { get; private set; }
    public int Value { get; private set; } // 회복량

    // --- [수정] 3번 요청: 이름 생성 로직을 ItemDB로 이전 ---
    // 생성자에서 'name'을 직접 받도록 변경
    public Consumable(string name, ItemRarity rarity, ConsumableType cType, int value) 
        : base(name, rarity, ItemType.Consumable)
    {
        CType = cType;
        Value = value;
    }

    // [삭제] 이름 생성 헬퍼 (ItemDB로 이동)
    // private static string GenerateName(...) 
    // --- [끝] ---

    // 소비 아이템 사용 로직 (변경 없음)
    public bool Use(Player player, Game game)
    {
        switch (CType)
        {
            // ... (기존과 동일) ...
            case ConsumableType.HealthPotion:
                if (player.HP >= player.MaxHP)
                {
                    game.AddLog("HP가 이미 가득 찼습니다.");
                    return false; 
                }
                player.HP = Math.Min(player.MaxHP, player.HP + Value);
                game.AddLog($"HP 회복 물약 사용! (HP +{Value})");
                return true; 

            case ConsumableType.ManaPotion:
                if (player.MP >= player.MaxMP)
                {
                    game.AddLog("MP가 이미 가득 찼습니다.");
                    return false; 
                }
                player.MP = Math.Min(player.MaxMP, player.MP + Value);
                game.AddLog($"MP 회복 물약 사용! (MP +{Value})");
                return true; 
        }
        return false;
    }
}