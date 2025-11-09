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
    
    // (신규) 처치 시 보상 경험치
    public int EXPReward { get; set; }

    public Monster(string name, int x, int y, int hp, int atk, int def, char icon, int expReward)
    {
        Name = name;
        X = x;
        Y = y;
        MaxHP = HP = hp;
        ATK = atk;
        DEF = def;
        Icon = icon;
        EXPReward = expReward; // (신규)
    }
}