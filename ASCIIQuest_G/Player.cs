// Player.cs

// (신규) 스킬의 기본 데이터를 저장하는 클래스
public class SkillData
{
    public string Name { get; set; }
    public int MpCost { get; set; }

    public SkillData(string name, int mpCost)
    {
        Name = name;
        MpCost = mpCost;
    }
}

public enum PlayerClass
{
    Warrior,
    Wizard,
    Rogue
}

public class Player
{
    // [신규] 네트워크에서 플레이어를 구분하기 위한 ID
    public int Id { get; set; } 

    // 위치
    public int X { get; set; }
    public int Y { get; set; }

    // 성장 스탯
    public int Level { get; private set; }
    public int EXP { get; private set; }
    public int EXPNext { get; private set; }

    // 기본 스탯
    public PlayerClass Class { get; private set; }
    public int HP { get; set; }
    public int MaxHP { get; set; }
    public int MP { get; set; }
    public int MaxMP { get; set; }
    public int ATK { get; set; }
    public int DEF { get; set; }
    public int STR { get; set; }
    public int INT { get; set; }
    public int DEX { get; set; }

    // 장비 (간단하게)
    public int WeaponAttack { get; set; }
    public float CritChance { get; set; }
    
    // (신규) 스킬 리스트
    public List<SkillData> Skills { get; private set; }

    // 생성자
    public Player(PlayerClass playerClass)
    {
        Class = playerClass;
        Skills = new List<SkillData>(); // 리스트 초기화
        SetInitialStats();
    }

    // 초기 스탯 설정
    private void SetInitialStats()
    {
        Level = 1;
        EXP = 0;
        EXPNext = 100; 

        Skills.Clear(); // 스킬 리스트 초기화

        switch (Class)
        {
            case PlayerClass.Warrior:
                MaxHP = HP = 30; MaxMP = MP = 10;
                ATK = 4; DEF = 4; STR = 8; INT = 2; DEX = 2;
                WeaponAttack = 1; STR += 1; 
                // (신규) 전사 스킬
                Skills.Add(new SkillData("파워 스트라이크", 5));
                Skills.Add(new SkillData("방패 치기", 3));
                Skills.Add(new SkillData("사기 진작", 8));
                break;
            case PlayerClass.Wizard:
                MaxHP = HP = 20; MaxMP = MP = 20;
                ATK = 2; DEF = 2; STR = 2; INT = 12; DEX = 2;
                WeaponAttack = 1; INT += 1; MaxMP += 5; 
                // (신규) 마법사 스킬
                Skills.Add(new SkillData("파이어볼", 8));
                Skills.Add(new SkillData("힐", 10));
                Skills.Add(new SkillData("매직 미사일", 4));
                break;
            case PlayerClass.Rogue:
                MaxHP = HP = 25; MaxMP = MP = 12;
                ATK = 3; DEF = 3; STR = 2; INT = 2; DEX = 13;
                WeaponAttack = 1; DEX += 1; CritChance = 0.05f; 
                // (신규) 도적 스킬
                Skills.Add(new SkillData("백스탭", 7));
                Skills.Add(new SkillData("독 찌르기", 5));
                Skills.Add(new SkillData("퀵 어택", 3));
                break;
        }
    }

    // 경험치 획득 및 레벨 업 처리
    public bool AddExperience(int expAmount)
    {
        EXP += expAmount;
        bool leveledUp = false;

        while (EXP >= EXPNext)
        {
            EXP -= EXPNext;
            Level++;
            leveledUp = true;
            LevelUpStats();
            EXPNext = (int)(EXPNext * 1.5);
        }
        return leveledUp;
    }

    // 레벨 업 시 직업별 스탯 성장
    private void LevelUpStats()
    {
        switch (Class)
        {
            case PlayerClass.Warrior:
                MaxHP += 5; STR += 2; DEF += 1;
                break;
            case PlayerClass.Wizard:
                MaxHP += 2; MaxMP += 5; INT += 2;
                break;
            case PlayerClass.Rogue:
                MaxHP += 3; ATK += 1; DEX += 2;
                break;
        }
        HP = MaxHP;
        MP = MaxMP;
    }
}