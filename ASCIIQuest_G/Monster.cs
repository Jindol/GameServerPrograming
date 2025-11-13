// Monster.cs
public class Monster
{
    public string Name { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int HP { get; set; }
    public int MaxHP { get; set; }
    public int ATK { get; set; }
    public int DEF { get; set; } 
    public char Icon { get; set; } 
    
    public int EXPReward { get; set; }

    // [신규] 몬스터가 자신의 아트 Y좌표를 가짐
    public int ArtOffsetY { get; set; }
    public int ArtOffsetX { get; set; } // [신규]

    public int OriginalMaxHP { get; private set; }
    public int OriginalATK { get; private set; }
    public int OriginalDEF { get; private set; }
    public int OriginalEXPReward { get; private set; }


    public Dictionary<StatType, int> StatusEffects { get; private set; }
    public int PoisonDamagePerTurn { get; set; } = 0;
    public int BleedDamagePerTurn { get; set; } = 0; // [신규] 출혈 데미지
    // [수정] 생성자에 artOffsetY 추가
    public Monster(string name, int x, int y, int hp, int atk, int def, char icon, int expReward, int artOffsetY, int artOffsetX)
    {
        Name = name;
        X = x;
        Y = y;
        MaxHP = HP = hp;
        ATK = atk;
        DEF = def;
        Icon = icon;
        EXPReward = expReward;
        ArtOffsetY = artOffsetY;
        ArtOffsetX = artOffsetX; // [신규]

        OriginalMaxHP = hp;
        OriginalATK = atk;
        OriginalDEF = def;
        OriginalEXPReward = expReward;

        StatusEffects = new Dictionary<StatType, int>();
    }
}