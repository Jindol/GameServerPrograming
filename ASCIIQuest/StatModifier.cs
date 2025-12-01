// StatModifier.cs

/// <summary>
/// [핵심] 장비가 올려줄 수 있는 '모든 종류의' 스탯 옵션을 정의하는 마스터 열거형(enum)입니다.
/// ItemDB.cs의 'Stat_Pools'에서 이 리스트를 참조하여 직업별 스탯 풀을 구성합니다.
/// </summary>
public enum StatType
{
    // --- 1. 기본 스탯 (Flat) ---
    // (Player.cs의 base 스탯에 직접 더해지는 값)
    HP,  // 최대 HP
    MP,  // 최대 MP
    ATK, // 공격력
    DEF, // 방어력
    STR, // 힘
    INT, // 지능
    DEX, // 민첩

    // --- 2. 특수 스탯 (Percent) ---
    CritChance, // (현재 코드에서는 사용되지 않음, DEX가 CritChance를 계산)
    EXPGain,    // 경험치 획득량 %

    // --- 3. 스킬 강화 스탯 (Percent) ---
    // (Skill.cs의 CalculateDamage에서 이 보너스를 참조)
    PowerStrikeDamage,  // (전사) 파워 스트라이크 데미지 %
    ShieldBashDamage,   // (전사) 방패 치기 데미지 %
    FireballDamage,     // (마법사) 파이어볼 데미지 %
    MagicMissileDamage, // (마법사) 매직 미사일 데미지 %
    HealAmount,         // (마법사) 힐 회복량 %
    BackstabDamage,     // (도적) 백스탭 데미지 %
    PoisonStabDamage,   // (도적) 독 찌르기 데미지 %
    QuickAttackDamage,  // (도적) 퀵 어택 데미지 %

    ExecutionDamage,  // 전사: 처형
    MeteorDamage,     // 마법사: 메테오
    EviscerateDamage, // 도적: 파열
    
    // --- 4. 직업별 특수 효과 (Percent) ---
    // (Game.cs의 전투 로직에서 이 보너스를 참조)
    DamageReflectChance, // (전사) 피해 반사 확률 %
    StunChance,          // (전사) 기절 확률 %
    ManaRefundChance,    // (마법사) 마나 환급 확률 %
    ManaShieldConversion,  // (마법사) 마력 보호막 전환율 %
    LifeStealPercent,    // (도적) 생명력 흡수 %
    BleedChance,         // (도적) 출혈 확률 %
    StrongPoison,           // 로그: 맹독 (기존 독과 중첩 가능)
    AtkDebuff,               // 마법사: 공격력 감소

    // --- 5. 공용 유틸리티 효과 (Percent) ---
    ResourceCostReduction, // (공용) 스킬 MP 소모 감소 %
}

/// <summary>
/// 스탯 옵션이 적용되는 방식을 정의합니다. (고정값, 비율)
/// </summary>
public enum ModifierType
{
    /// <summary>
    /// 고정값으로 적용됩니다. (예: HP +10, STR +2)
    /// </summary>
    Flat,
    
    /// <summary>
    /// 비율(퍼센트)로 적용됩니다. (예: ATK +0.05 (5%), EXPGain +0.1 (10%))
    /// </summary>
    Percent
}

/// <summary>
/// 아이템에 부여되는 '단일 스탯 옵션' 하나를 정의하는 순수 데이터 클래스입니다.
/// (예: {Stat: StatType.HP, Value: 10, Type: ModifierType.Flat})
/// Equipment.cs는 이 객체들을 'List<StatModifier>' 형태로 포함(Composition)합니다.
/// </summary>
public class StatModifier
{
    /// <summary>
    /// 이 옵션이 영향을 주는 스탯의 '종류' (예: StatType.HP)
    /// </summary>
    public StatType Stat { get; private set; }
    
    /// <summary>
    /// 이 옵션의 '수치' (Flat이면 +10, Percent면 +0.05)
    /// </summary>
    public float Value { get; private set; }
    
    /// <summary>
    /// 이 옵션의 '적용 방식' (Flat 또는 Percent)
    /// </summary>
    public ModifierType Type { get; private set; }

    /// <summary>
    /// 새 스탯 옵션 인스턴스를 생성합니다.
    /// (ItemDB.cs의 GetRandomModifier 메서드에서 호출됩니다.)
    /// </summary>
    /// <param name="stat">스탯 종류</param>
    /// <param name="value">스탯 수치</param>
    /// <param name="type">적용 방식 (기본값: Flat)</param>
    public StatModifier(StatType stat, float value, ModifierType type = ModifierType.Flat)
    {
        Stat = stat;
        Value = value;
        Type = type;
    }

    /// <summary>
    /// [핵심] 이 스탯 옵션 객체(데이터)를
    /// 인벤토리, 아이템 비교창 등 UI에 표시할 '한글 문자열'로 '번역' 및 '가공'합니다.
    /// </summary>
    /// <returns>UI에 표시될 최종 문자열 (예: "최대 HP +10", "경험치 획득 +5%")</returns>
    public string GetDescription()
    {
        string statName = Stat.ToString(); // (기본값: "HP", "FireballDamage" 등)
        string valueStr;

        // 1. 값(Value) 포맷팅
        if (Type == ModifierType.Percent)
        {
            // 1a. 특수 효과 % 스탯 (소수점 없이, 예: +5%)
            if (Stat == StatType.EXPGain || 
                Stat == StatType.DamageReflectChance || Stat == StatType.ManaRefundChance || 
                Stat == StatType.LifeStealPercent ||
                Stat == StatType.ResourceCostReduction || Stat == StatType.StunChance ||
                Stat == StatType.ManaShieldConversion || Stat == StatType.BleedChance
                )
            {
                // (Value 0.05 -> "+5%")
                valueStr = $"+{(Value * 100):F0}%";
            }
            // 1b. 스킬 데미지 % 스탯 (소수점 1자리, 예: +15.5%)
            else
            {
                // (Value 0.155 -> "+15.5%")
                valueStr = $"+{(Value * 100):F1}%";
            }
        }
        else // (Type == ModifierType.Flat)
        {
            // 1c. 고정값(Flat) 스탯 (소수점 없이, 예: +10)
            valueStr = $"+{Value:F0}";
        }

        // 2. 스탯 이름(StatType) '번역' (Enum -> 한글)
        switch (Stat)
        {
            case StatType.HP: statName = "최대 HP"; break;
            case StatType.MP: statName = "최대 MP"; break;
            case StatType.ATK: statName = "공격력"; break;
            case StatType.DEF: statName = "방어력"; break;
            case StatType.EXPGain: statName = "경험치 획득"; break;
            
            // 스킬
            case StatType.PowerStrikeDamage: statName = "파워 스트라이크 데미지"; break;
            case StatType.FireballDamage: statName = "파이어볼 데미지"; break;
            case StatType.HealAmount: statName = "힐 회복량"; break;
            case StatType.BackstabDamage: statName = "백스탭 데미지"; break; 
            case StatType.PoisonStabDamage: statName = "독 찌르기 데미지"; break; 
            case StatType.QuickAttackDamage: statName = "퀵 어택 데미지"; break; 
            case StatType.ShieldBashDamage: statName = "방패 치기 데미지"; break;
            case StatType.MagicMissileDamage: statName = "매직 미사일 데미지"; break;
            case StatType.ExecutionDamage: statName = "처형 데미지"; break;
            case StatType.MeteorDamage: statName = "메테오 데미지"; break;
            case StatType.EviscerateDamage: statName = "파열 데미지"; break;
            
            // 특수 효과
            case StatType.DamageReflectChance: statName = "피해 반사 확률"; break;
            case StatType.ManaRefundChance: statName = "마나 환급 확률"; break;
            case StatType.LifeStealPercent: statName = "생명력 흡수"; break;
            case StatType.ResourceCostReduction: statName = "자원 소모 감소"; break;
            case StatType.StunChance: statName = "기절 확률"; break;
            case StatType.ManaShieldConversion: statName = "마력 보호막 전환율"; break;
            case StatType.BleedChance: statName = "출혈 확률"; break;
            
            // (STR, INT, DEX는 기본값(ToString())을 그대로 사용)
        }

        // 3. 번역된 이름과 가공된 값을 조합하여 반환
        return $"{statName} {valueStr}"; // (예: "최대 HP +10")
    }
}