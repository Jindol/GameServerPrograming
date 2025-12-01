// Consumable.cs

/// <summary>
/// 소비 아이템의 종류를 정의합니다. (예: HP 물약, MP 물약)
/// </summary>
public enum ConsumableType
{
    HealthPotion,
    ManaPotion
}

/// <summary>
/// 'Item' 클래스를 상속받아, '소비 아이템'의 고유한 속성과 사용 로직을 정의합니다.
/// </summary>
public class Consumable : Item
{
    /// <summary>
    /// 이 소비 아이템의 종류(HP 물약, MP 물약 등)를 나타냅니다.
    /// </summary>
    public ConsumableType CType { get; private set; }
    
    /// <summary>
    /// [핵심] 이 아이템의 '최소' 회복 고정값입니다.
    /// (예: 20)
    /// </summary>
    public int Value { get; private set; } 

    /// <summary>
    /// 새 소비 아이템 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="name">아이템 이름 (예: "[Common] 저용량 HP 물약")</param>
    /// <param name="rarity">아이템 희귀도 (Common, Rare 등)</param>
    /// <param name="cType">아이템 종류 (HealthPotion, ManaPotion)</param>
    /// <param name="value">최소 회복 고정값 (예: 20)</param>
    public Consumable(string name, ItemRarity rarity, ConsumableType cType, int value) 
        // 부모 클래스(Item)의 생성자를 호출하여 기본 속성을 초기화합니다.
        : base(name, rarity, ItemType.Consumable)
    {
        CType = cType;
        Value = value; // 최소 회복량 저장
    }

    /// <summary>
    /// [핵심] 플레이어가 이 소비 아이템을 사용하는 메인 로직입니다.
    /// 퍼센트(%) 회복과 고정값(Value) 회복 중 더 유리한 값을 적용합니다.
    /// </summary>
    /// <param name="player">아이템 효과를 적용받을 Player 객체</param>
    /// <param name="game">로그 메시지(AddLog)를 출력할 Game 엔진</param>
    /// <returns>사용에 성공하면 true, (HP가 가득 차는 등) 실패하면 false를 반환합니다.</returns>
    public bool Use(Player player, Game game)
    {
        // 1. 아이템 희귀도(Rarity)에 따라 기본 회복 비율(percent)을 결정합니다.
        float percent = 0.25f; // 기본 Common 등급 (25%)
        switch (this.Rarity)
        {
            case ItemRarity.Common:    percent = 0.25f; break; // 25%
            case ItemRarity.Rare:      percent = 0.50f; break; // 50%
            case ItemRarity.Unique:    percent = 0.75f; break; // 75%
            case ItemRarity.Legendary: percent = 1.00f; break; // 100%
        }

        // 2. 아이템 종류(CType)에 따라 분기합니다.
        switch (CType)
        {
            case ConsumableType.HealthPotion:
                // 이미 HP가 가득 찼는지 확인합니다.
                if (player.HP >= player.MaxHP)
                {
                    game.AddLog("HP가 이미 가득 찼습니다.");
                    return false; // 사용 실패 (아이템이 소모되지 않음)
                }
                
                // [핵심] 하이브리드 회복 로직
                // 1. 플레이어 최대 HP 기준 '퍼센트(%) 회복량' 계산
                int percentHeal = (int)(player.MaxHP * percent);
                // 2. '퍼센트 회복량'과 아이템의 '최소 고정 회복량(Value)' 중 더 큰 값을 선택
                int finalHealAmount = Math.Max(percentHeal, this.Value); 
                
                // 3. 플레이어 HP를 회복시키되, MaxHP를 넘지 않도록 합니다.
                player.HP = Math.Min(player.MaxHP, player.HP + finalHealAmount);
                game.AddLog($"HP 회복 물약 사용! (HP +{finalHealAmount})");
                return true; // 사용 성공 (이후 Player.cs에서 아이템을 인벤토리에서 제거)

            case ConsumableType.ManaPotion:
                // 이미 MP가 가득 찼는지 확인합니다.
                if (player.MP >= player.MaxMP)
                {
                    game.AddLog("MP가 이미 가득 찼습니다.");
                    return false; 
                }
                
                // [핵심] 하이브리드 회복 로직 (HP와 동일)
                int percentHealMP = (int)(player.MaxMP * percent);
                int finalHealAmountMP = Math.Max(percentHealMP, this.Value); 

                player.MP = Math.Min(player.MaxMP, player.MP + finalHealAmountMP);
                game.AddLog($"MP 회복 물약 사용! (MP +{finalHealAmountMP})");
                return true; 
        }
        
        // (HealthPotion, ManaPotion 외의 CType이 추가될 경우, 여기까지 도달)
        return false;
    }
}