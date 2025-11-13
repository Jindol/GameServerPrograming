// StatModifier.cs

// 장비가 올려줄 수 있는 스탯의 종류
public enum StatType
{
    // 기본 스탯
    HP,
    MP,
    ATK,
    DEF,
    STR,
    INT,
    DEX,
    CritChance,
    EXPGain, 
    
    // 스킬 강화
    PowerStrikeDamage,
    FireballDamage,
    HealAmount,
    BackstabDamage,     
    PoisonStabDamage,   
    QuickAttackDamage,
    
    // [신규] 2번 요청 (전사, 마법사 추가 스킬)
    ShieldBashDamage,
    MagicMissileDamage,
    
    // [신규] 3번 요청 (직업별 특수 효과)
    DamageReflectChance, // 전사: 피해 반사
    ManaRefundChance,    // 마법사: 마나 환급
    LifeStealPercent,     // 도적: 생명력 흡수

    ResourceCostReduction, // [신규] 공용: 자원 소모 감소
    StunChance,            // [신규] 전사: 기절 확률
    ManaShieldConversion,  // [신규] 마법사: 마력 보호막
    BleedChance            // [신규] 도적: 출혈 확률
}

// 스탯 적용 방식
public enum ModifierType
{
    Flat,       // 고정값 (예: HP + 10)
    Percent     // 비율 (예: ATK + 0.05 (5%))
}

public class StatModifier
{
    public StatType Stat { get; private set; }
    public float Value { get; private set; }
    public ModifierType Type { get; private set; }

    public StatModifier(StatType stat, float value, ModifierType type = ModifierType.Flat)
    {
        Stat = stat;
        Value = value;
        Type = type;
    }

    // 장비 설명을 위한 문자열
    public string GetDescription()
    {
        string statName = Stat.ToString();
        string valueStr;

        if (Type == ModifierType.Percent)
        {
            // [수정] 크리티컬/특수효과는 F0 (소수점 없이), 스킬데미지는 F1 (소수점 1자리)
            if (Stat == StatType.EXPGain || 
                Stat == StatType.DamageReflectChance || Stat == StatType.ManaRefundChance || 
                Stat == StatType.LifeStealPercent ||
                // [신규]
                Stat == StatType.ResourceCostReduction || Stat == StatType.StunChance ||
                Stat == StatType.ManaShieldConversion || Stat == StatType.BleedChance
                )
            {
                valueStr = $"+{(Value * 100):F0}%"; // 예: +5%
            }
            else
            {
                valueStr = $"+{(Value * 100):F1}%"; // 예: +15.5%
            }
        }
        else
        {
            valueStr = $"+{Value:F0}"; // 예: +10
        }

        // 스탯 이름 한글화 (신규 스탯 추가)
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

            // [신규]
            case StatType.ShieldBashDamage: statName = "방패 치기 데미지"; break;
            case StatType.MagicMissileDamage: statName = "매직 미사일 데미지"; break;
            
            case StatType.DamageReflectChance: statName = "피해 반사 확률"; break;
            case StatType.ManaRefundChance: statName = "마나 환급 확률"; break;
            case StatType.LifeStealPercent: statName = "생명력 흡수"; break;

            case StatType.ResourceCostReduction: statName = "자원 소모 감소"; break;
            case StatType.StunChance: statName = "기절 확률"; break;
            case StatType.ManaShieldConversion: statName = "마력 보호막 전환율"; break;
            case StatType.BleedChance: statName = "출혈 확률"; break;
        }

        return $"{statName} {valueStr}";
    }
}