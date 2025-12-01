// MultiplayerBattleManager.cs
using System;
using System.Text.Json;

/// <summary>
/// 멀티플레이 전투 로직과 싱글플레이 연출을 연결하는 관리자 클래스입니다.
/// </summary>
public static class MultiplayerBattleManager
{
    // --- 1. [공격자] 로컬 플레이어 행동 처리 ---

    /// <summary>
    /// 기본 공격을 수행하고 패킷을 전송합니다. (더블 어택 버그 수정됨)
    /// </summary>
    public static void PerformLocalAttack(Game game, Player player, Monster target)
    {
        // 1. 데미지 계산 (싱글플레이 공식 사용)
        // 주의: Game.AttackMonster는 데미지 계산만 하고 HP를 깎지 않도록 수정된 버전을 사용해야 합니다.
        // 현재 Game.cs 구조상 AttackMonster 로직을 여기서 일부 재현하거나 Game의 헬퍼를 씁니다.
        
        // (싱글플레이 AttackMonster 로직 복원)
        bool isCrit = false;
        bool isMiss = false;

        // 회피 공식
        if (new Random().NextDouble() < 0.05) // 5% 확률
        {
            isMiss = true;
        }

        int damage = 0;
        if (!isMiss)
        {
            if (target.MonsterId == "mimic") damage = 1;
            else
            {
                // 직업별 데미지 공식
                switch (player.Class)
                {
                    case PlayerClass.Warrior:
                        damage = ApplyDefense(player.ATK + player.STR, target.DEF);
                        break;
                    case PlayerClass.Wizard:
                        float intMultiplier = 1.0f + (player.INT / 100.0f);
                        damage = Math.Max(1, (int)(player.ATK * intMultiplier)); // 마법은 방어 무시
                        break;
                    case PlayerClass.Rogue:
                        float dexMultiplier = 1.0f + (player.DEX / 100.0f);
                        int rawDmg = (int)(player.ATK * dexMultiplier);
                        if (new Random().NextDouble() < player.CritChance) { rawDmg = (int)(rawDmg * 1.5); isCrit = true; }
                        damage = ApplyDefense(rawDmg, target.DEF);
                        break;
                }
            }
        }

        // 2. 로컬(내 화면) 적용
        if (isMiss)
        {
            game.AddLog("[나] 공격이 빗나갔습니다!");
            game.StartAnimation(target, "M", () => game.EndMyTurn());
        }
        else
        {
            target.HP -= damage;
            string critMsg = isCrit ? " (치명타!)" : "";
            game.AddLog($"[나] {target.Name} 공격! {damage} 피해!{critMsg}");
            
            // [중요] 상태이상(기절/출혈) 확률 발동 체크 (싱글플레이 ApplyOnHitEffects 로직)
            ApplyPassiveEffects(game, player, target);

            game.StartAnimation(target, $"-{damage}", () => 
            {
                if (target.HP <= 0) game.WinMultiplayerBattle();
                else game.EndMyTurn();
            });
        }

        // 3. 패킷 전송
        var data = new BattleActionData
        {
            ActionType = 0, // 공격
            Damage = damage,
            IsCrit = isCrit,
            SkillName = "" // 기본 공격
        };
        NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleAction, Data = JsonSerializer.Serialize(data) });
    }

    /// <summary>
    /// 스킬을 사용하고 패킷을 전송합니다.
    /// </summary>
    public static void PerformLocalSkill(Game game, Player player, Monster target, Skill skill)
    {
        // 1. 자원 소모 및 쿨타임 적용
        skill.CurrentCooldown = skill.Cooldown;

        // [핵심 수정] 변수 선언을 분기문(if) 바깥, 가장 위쪽에서 해야 합니다.
        // 이 변수(rawValue)는 공격일 땐 데미지가 되고, 힐일 땐 회복량이 됩니다.
        int rawValue = skill.CalculateDamage(player, target);
        
        var data = new BattleActionData { ActionType = 1, SkillName = skill.Name };

        // [A] 공격 스킬인 경우
        if (skill.IsDamagingSkill)
        {
            int finalDmg = 0;
            bool isCrit = false;

            // 도적 '파열' 스킬: 1.5배 보정 및 크리티컬 처리
            if (skill is Eviscerate) 
            { 
                rawValue = (int)(rawValue * 1.5f); 
                isCrit = true; 
            }

            // 방어력 적용 여부
            if (target.MonsterId == "mimic") 
            {
                finalDmg = 1;
            }
            else if (IsIgnoreDefenseSkill(skill.Name)) 
            {
                finalDmg = Math.Max(1, rawValue);
            }
            else 
            {
                finalDmg = ApplyDefense(rawValue, target.DEF);
            }

            // 로컬 데이터(내 화면) 적용
            target.HP -= finalDmg;
            game.AddLog($"[나] {skill.Name}! {finalDmg} 데미지!");
            
            // 상태이상 적용
            ApplySkillSideEffects(game, skill.Name, player, target);

            // 패킷 데이터 설정
            data.Damage = finalDmg;
            data.IsCrit = isCrit;

            // 애니메이션 재생
            game.StartAnimation(target, $"-{finalDmg}", () => {
                // (흡혈 등 후처리)
                if (skill is Execution || player.GetStatBonus(StatType.LifeStealPercent, ModifierType.Percent) > 0)
                {
                    int heal = (skill is Execution) ? (int)(finalDmg * 0.5f) : (int)(finalDmg * player.GetStatBonus(StatType.LifeStealPercent, ModifierType.Percent));
                    if(heal > 0) {
                        player.HP = Math.Min(player.MaxHP, player.HP + heal);
                        game.StartBuffAnimation(player, heal, ConsoleColor.Red, () => {
                            game.CheckForManaRefund(skill.MpCost);
                            if (target.HP <= 0) game.WinMultiplayerBattle(); else game.EndMyTurn();
                        });
                        return;
                    }
                }
                game.CheckForManaRefund(skill.MpCost);
                if (target.HP <= 0) game.WinMultiplayerBattle(); else game.EndMyTurn();
            });
        }
        // [B] 버프/힐 스킬인 경우
        else if (skill.IsBuffSkill) 
        {
            int amount = rawValue; // 위에서 계산한 값 그대로 사용
            
            // [핵심] 버프/힐량은 음수로 보내서 공격이 아님을 패킷에 표시
            data.Damage = -amount; 
            
            game.AddLog($"[나] {skill.Name} 사용!");
            
            // 로컬 연출 색상 결정
            ConsoleColor color = ConsoleColor.Blue; 
            if (skill is Heal) color = ConsoleColor.Green;
            if (skill is IronWill) color = ConsoleColor.Gray; // 전사 버프 회색

            // 애니메이션 재생
            game.StartBuffAnimation(player, amount, color, () => {
                game.CheckForManaRefund(skill.MpCost);
                game.EndMyTurn();
            });
        }

        // 패킷 전송
        NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleAction, Data = JsonSerializer.Serialize(data) });
    }
    /// <summary>
    /// 아이템을 사용하고 패킷을 전송합니다.
    /// </summary>
    public static bool PerformLocalItem(Game game, Player player, Consumable item)
    {
        bool success = player.UseConsumable(item.CType, item.Rarity, game);
        
        if (success)
        {
            int amount = item.Value; 
            
            var data = new BattleActionData 
            { 
                ActionType = 2, 
                Damage = -amount, 
                SkillName = item.Name 
            };
            NetworkManager.Instance.Send(new Packet { Type = PacketType.BattleAction, Data = JsonSerializer.Serialize(data) });

            ConsoleColor color = (item.CType == ConsumableType.HealthPotion) ? ConsoleColor.Red : ConsoleColor.Blue;
            game.StartBuffAnimation(player, amount, color, () => game.EndMyTurn());
        }

        return success; // [신규] 결과 반환
    }


    // --- 2. [수신자] 상대방 행동 처리 ---

    /// <summary>
    /// 상대방의 행동 패킷을 받아 내 화면에 연출합니다.
    /// </summary>
    public static void OnReceiveBattleAction(Game game, BattleActionData data)
    {
        Monster target = game.CurrentBattleMonster;

        // 1. 공격(0) 또는 스킬(1) 처리
        if (data.ActionType == 0 || data.ActionType == 1)
        {
            // ---------------------------------------------------------
            // [A] 힐 / 버프 스킬 처리 (데미지가 음수이거나 특정 스킬명)
            // ---------------------------------------------------------
            // (음수 데미지는 확실히 버프/힐로 간주)
            if (data.Damage < 0 || data.SkillName == "강철의 의지" || data.SkillName == "Heal" || data.SkillName == "힐")
            {
                int amount = Math.Abs(data.Damage); // 절대값으로 변환 (ex: -5 -> 5)
                Player ally = game.OtherPlayer;
                if (ally == null) return;

                // [1] 전사 방어 버프 (강철의 의지)
                if (data.SkillName == "강철의 의지")
                {
                    // [핵심] 동료의 TempDefBuff에 값을 적용 -> DEF가 올라감 -> 피격 데미지 감소
                    ally.TempDefBuff = amount;
                    // 상태이상 지속시간 설정 (UI 표시는 안되더라도 로직상 필요하다면)
                    ally.StatusEffects[StatType.DEF] = 5; 

                    game.AddLog($"동료가 {data.SkillName} 사용! (방어력 +{amount})");
                    
                    // [요청 반영] 회색(Gray) 연출
                    game.StartBuffAnimation(ally, amount, ConsoleColor.Gray, () => { });
                }
                // [2] 마법사 힐 (Heal)
                else if (data.SkillName == "Heal" || data.SkillName == "힐")
                {
                    // [핵심] 체력 동기화
                    ally.HP = Math.Min(ally.MaxHP, ally.HP + amount);
                    
                    game.AddLog($"동료가 {data.SkillName} 사용! (HP +{amount})");
                    
                    // [요청 반영] 초록색(Green) 연출
                    game.StartBuffAnimation(ally, amount, ConsoleColor.Green, () => { });
                }
                // [3] 그 외 버프 (예: 마나 보호막 등)
                else
                {
                    // 기본 처리
                    game.AddLog($"동료가 {data.SkillName} 사용! (수치 {amount})");
                    game.StartBuffAnimation(ally, amount, ConsoleColor.Blue, () => { });
                }
                
                return; // 버프/힐 처리가 끝났으니 리턴
            }

            // ---------------------------------------------------------
            // [B] 공격 스킬 처리
            // ---------------------------------------------------------
            if (target == null) return;

            // 빗나감
            if (data.Damage == 0 && data.ActionType == 0)
            {
                game.AddLog("동료의 공격이 빗나갔습니다!");
                game.StartAnimation(target, "M", () => { });
                return;
            }

            // 데미지 적용
            int dmg = Math.Max(0, data.Damage);
            target.HP -= dmg;

            string actionName = (data.ActionType == 1) ? data.SkillName : "공격";
            string critMsg = data.IsCrit ? " (치명타!)" : "";
            game.AddLog($"동료의 {actionName}! {dmg} 피해!{critMsg}");

            // 상태이상 동기화
            if (data.ActionType == 1 && !string.IsNullOrEmpty(data.SkillName))
                ApplySkillSideEffects(game, data.SkillName, game.OtherPlayer, target);
            
            if (data.ActionType == 0)
                ApplyPassiveEffects(game, game.OtherPlayer, target);

            // 처형 스킬 특수 연출 (공격 -> 회복)
            if (data.SkillName == "처형")
            {
                game.StartAnimation(target, $"-{dmg}", () => 
                {
                    int heal = (int)(dmg * 0.5f);
                    Player ally = game.OtherPlayer;
                    if (ally != null) ally.HP = Math.Min(ally.MaxHP, ally.HP + heal);
                    
                    game.StartBuffAnimation(ally, heal, ConsoleColor.Red, () => 
                    {
                        if (target.HP <= 0) game.WinMultiplayerBattle();
                    });
                });
            }
            else
            {
                game.StartAnimation(target, $"-{dmg}", () => 
                {
                    if (target.HP <= 0) game.WinMultiplayerBattle();
                });
            }
        }
        // 2. 아이템 사용(2) 처리
        else if (data.ActionType == 2)
        {
            // (기존 아이템 로직 유지)
            int healAmount = Math.Abs(data.Damage);
            string itemName = data.SkillName;
            Player ally = game.OtherPlayer;

            if (ally != null)
            {
                if (itemName.Contains("HP") || itemName.Contains("Health"))
                    ally.HP = Math.Min(ally.MaxHP, ally.HP + healAmount);
                else if (itemName.Contains("MP") || itemName.Contains("Mana"))
                    ally.MP = Math.Min(ally.MaxMP, ally.MP + healAmount);
            }
            
            game.AddLog($"동료가 {itemName}을(를) 사용했습니다.");
            ConsoleColor color = (itemName.Contains("MP") || itemName.Contains("Mana")) ? ConsoleColor.Blue : ConsoleColor.Red;
            
            game.StartBuffAnimation(ally, healAmount, color, () => { });
        }
    }


    // --- 3. 공용 헬퍼 메서드 ---

    // 방어력 적용 공식
    private static int ApplyDefense(int rawDmg, int def)
    {
        // Game.cs의 상수 DEFENSE_CONSTANT = 30 가정
        float reduction = (float)def / (def + 30);
        return Math.Max(1, (int)(rawDmg * (1.0f - reduction)));
    }

    // 방어 무시 스킬 목록
    private static bool IsIgnoreDefenseSkill(string name)
    {
        return name == "독 찌르기" || name == "파이어볼" || name == "매직 미사일" || name == "메테오" || name == "처형";
    }

    // 스킬별 상태이상 적용 (싱글/멀티 공용)
    private static void ApplySkillSideEffects(Game game, string skillName, Player caster, Monster target)
    {
        if (skillName == "처형") 
        {
            target.AddStatusEffect(StatType.StunChance, 1);
            game.AddLog("적 기절!");
        }
        else if (skillName == "파열") 
        {
            target.AddStatusEffect(StatType.StrongPoison, 3);
            target.StrongPoisonDamagePerTurn = caster.DEX * 3;
            game.AddLog("맹독 주입!");
        }
        else if (skillName == "메테오") 
        {
            target.AddStatusEffect(StatType.AtkDebuff, 3);
            game.AddLog("공격력 하락!");
        }
        else if (skillName == "독 찌르기") 
        {
            target.AddStatusEffect(StatType.PoisonStabDamage, 5);
            target.PoisonDamagePerTurn = Math.Max(1, caster.DEX / 2);
            game.AddLog("독 주입!");
        }
        else if (skillName == "강철의 의지")
        {
            // IronWill은 CalculateDamage에서 이미 적용되지만, 
            // 원격 플레이어의 경우 시각적 효과나 데이터 동기화를 위해 필요할 수 있음.
            // (다만 Player 데이터는 PlayerInfo 패킷으로 덮어씌워질 수 있으니 주의)
        }
    }

    // 패시브 효과 (기절, 출혈 등) 적용
    private static void ApplyPassiveEffects(Game game, Player attacker, Monster target)
    {
        // 전사 기절
        float stunChance = attacker.GetStatBonus(StatType.StunChance, ModifierType.Percent);
        if (new Random().NextDouble() < stunChance)
        {
            target.AddStatusEffect(StatType.StunChance, 1);
            game.AddLog("전사 특성: 적을 기절시켰습니다!");
        }

        // 도적 출혈
        float bleedChance = attacker.GetStatBonus(StatType.BleedChance, ModifierType.Percent);
        if (new Random().NextDouble() < bleedChance)
        {
            target.BleedDamagePerTurn = Math.Max(1, attacker.DEX / 2);
            target.AddStatusEffect(StatType.BleedChance, 3);
            game.AddLog("도적 특성: 출혈 효과 적용!");
        }
    }

    // ========================================================================
    // [신규] 3. 호스트 전용: 몬스터 턴 처리 (상태이상 -> 공격 연쇄 로직)
    // ========================================================================

    /// <summary>
    /// 호스트가 몬스터의 턴을 시작합니다. (맹독 -> 독 -> 출혈 -> 기절/공격 순서)
    /// </summary>
    public static void ProcessMonsterTurn_Host(Game game)
    {
        Monster monster = game.CurrentBattleMonster;
        if (monster == null) return;

        // Step 1: 맹독 처리부터 시작
        DoStrongPoison(game, monster);
    }

    private static void DoStrongPoison(Game game, Monster monster)
    {
        // 맹독 체크
        if (monster.StatusEffects.GetValueOrDefault(StatType.StrongPoison, 0) > 0)
        {
            // 데미지 계산
            int dmg = (monster.MonsterId == "mimic") ? 1 : monster.StrongPoisonDamagePerTurn;
            monster.HP -= dmg;
            
            // 턴 차감
            monster.StatusEffects[StatType.StrongPoison]--;
            if (monster.StatusEffects[StatType.StrongPoison] == 0) monster.RemoveStatusEffect(StatType.StrongPoison);

            // 패킷 전송 (ActionType 10: 맹독)
            SendEnemyActionPacket(10, dmg, false, "맹독");

            // 로컬 연출 후 다음 단계(독)로
            game.AddLog($"[적] 맹독 피해! {dmg} 데미지!");
            game.customBlinkColor = ConsoleColor.Magenta; // 보라색
            game.StartAnimation(monster, $"-{dmg}", () => 
            {
                game.customBlinkColor = ConsoleColor.Black; // 색상 초기화
                if (monster.HP <= 0) game.WinMultiplayerBattle();
                else DoPoison(game, monster);
            });
        }
        else
        {
            DoPoison(game, monster); // 없으면 바로 다음 단계
        }
    }

    private static void DoPoison(Game game, Monster monster)
    {
        if (monster.StatusEffects.GetValueOrDefault(StatType.PoisonStabDamage, 0) > 0)
        {
            int dmg = (monster.MonsterId == "mimic") ? 1 : monster.PoisonDamagePerTurn;
            monster.HP -= dmg;

            monster.StatusEffects[StatType.PoisonStabDamage]--;
            if (monster.StatusEffects[StatType.PoisonStabDamage] == 0) monster.RemoveStatusEffect(StatType.PoisonStabDamage);

            // 패킷 전송 (ActionType 11: 독)
            SendEnemyActionPacket(11, dmg, false, "독");

            game.AddLog($"[적] 독 피해! {dmg} 데미지!");
            game.customBlinkColor = ConsoleColor.Magenta;
            game.StartAnimation(monster, $"-{dmg}", () => 
            {
                game.customBlinkColor = ConsoleColor.Black;
                if (monster.HP <= 0) game.WinMultiplayerBattle();
                else DoBleed(game, monster);
            });
        }
        else
        {
            DoBleed(game, monster);
        }
    }

    private static void DoBleed(Game game, Monster monster)
    {
        if (monster.StatusEffects.GetValueOrDefault(StatType.BleedChance, 0) > 0)
        {
            int dmg = (monster.MonsterId == "mimic") ? 1 : monster.BleedDamagePerTurn;
            monster.HP -= dmg;

            monster.StatusEffects[StatType.BleedChance]--;
            if (monster.StatusEffects[StatType.BleedChance] == 0) monster.RemoveStatusEffect(StatType.BleedChance);

            // 패킷 전송 (ActionType 12: 출혈)
            SendEnemyActionPacket(12, dmg, false, "출혈");

            game.AddLog($"[적] 출혈 피해! {dmg} 데미지!");
            game.customBlinkColor = ConsoleColor.DarkRed;
            game.StartAnimation(monster, $"-{dmg}", () => 
            {
                game.customBlinkColor = ConsoleColor.Black;
                if (monster.HP <= 0) game.WinMultiplayerBattle();
                else DoMonsterAction(game, monster);
            });
        }
        else
        {
            DoMonsterAction(game, monster);
        }
    }

    private static void DoMonsterAction(Game game, Monster monster)
    {
        // 1. 공격력 감소 디버프 (기존 유지)
        if (monster.StatusEffects.ContainsKey(StatType.AtkDebuff))
        {
            if (monster.StatusEffects[StatType.AtkDebuff] > 0)
            {
                monster.StatusEffects[StatType.AtkDebuff]--;
                if (monster.StatusEffects[StatType.AtkDebuff] == 0) 
                {
                    monster.RemoveStatusEffect(StatType.AtkDebuff);
                    // [신규] 공격력 회복 패킷 전송 (ActionType 14)
                    SendEnemyActionPacket(14, 0, false, "공격력 회복");
                    game.AddLog($"{monster.Name}의 공격력이 회복되었습니다.");
                }
            }
        }

        // 2. 기절 체크 (기존 유지)
        if (monster.StatusEffects.GetValueOrDefault(StatType.StunChance, 0) > 0)
        {
            monster.StatusEffects[StatType.StunChance]--;
            if (monster.StatusEffects[StatType.StunChance] == 0) monster.RemoveStatusEffect(StatType.StunChance);

            SendEnemyActionPacket(13, 0, false, "기절");
            game.AddLog($"[적] {monster.Name}은(는) 기절해서 움직일 수 없다!");
            
            System.Threading.Timer timer = null;
            timer = new System.Threading.Timer((_) => {
                game.ResumeTurnAfterEnemyAction();
                NetworkManager.Instance.IsDirty = true; 
                timer.Dispose();
            }, null, 1000, System.Threading.Timeout.Infinite);
            return;
        }

        // 3. 타겟 결정 (기존 유지)
        bool isAmHost = NetworkManager.Instance.IsHost;
        // (타겟 결정 로직 기존과 동일)
        bool myDeath = game.player.IsDead;
        bool otherDeath = (game.OtherPlayer != null && game.OtherPlayer.IsDead);
        bool otherAbsent = (game.OtherPlayer == null || game.OtherPlayer.IsWaitingAtPortal);

        // 실제 타겟 가능 여부 (호스트/게스트 기준)
        bool hostAlive, guestAlive;

        if (isAmHost)
        {
            hostAlive = !myDeath;
            guestAlive = !otherDeath && !otherAbsent;
        }
        else // 내가 게스트라면
        {
            hostAlive = !otherDeath && !otherAbsent;
            guestAlive = !myDeath;
        }

        // 타겟 선정
        bool targetHost; // true면 호스트 공격, false면 게스트 공격

        if (!hostAlive && !guestAlive) return; // 둘 다 없으면 중단
        else if (!hostAlive) targetHost = false; // 호스트 없으면 게스트 공격
        else if (!guestAlive) targetHost = true; // 게스트 없으면 호스트 공격
        else targetHost = new Random().Next(0, 2) == 0; // 둘 다 있으면 랜덤

        // ------------------------------------------------------------------
        // [타겟 객체 가져오기]
        // targetHost 플래그에 따라 실제 맞을 Player 객체를 찾습니다.
        // 내가 호스트고 targetHost=true면 -> 나(game.player)
        // 내가 게스트고 targetHost=false면 -> 나(game.player)
        // ------------------------------------------------------------------
        Player targetObj;
        if (isAmHost)
            targetObj = targetHost ? game.player : game.OtherPlayer;
        else
            targetObj = targetHost ? game.OtherPlayer : game.player;

        // 방어력/회피율 가져오기
        int targetDef = (targetObj != null) ? targetObj.DEF : 0;
        int targetDex = (targetObj != null) ? targetObj.DEX : 0;

        // 4. 회피 및 데미지 계산 (기존 로직 유지)
        bool isMiss = false;
        if (targetObj != null)
        {
            double evasion = targetDex * 0.0075;
            if (new Random().NextDouble() < evasion) isMiss = true;
        }

        int finalDamage = 0;
        if (!isMiss) finalDamage = ApplyDefense(monster.ATK, targetDef);
        else finalDamage = 0;

        // 5. 패킷 전송
        var data = new BattleActionData 
        { 
            ActionType = 3, 
            Damage = finalDamage, 
            IsTargetHost = targetHost // 계산된 타겟 정보 전송
        };
        string json = JsonSerializer.Serialize(data);
        NetworkManager.Instance.Send(new Packet { Type = PacketType.EnemyAction, Data = json });

        // 6. 로컬 처리
        OnReceiveEnemyAction(game, data); 
    }

    // [헬퍼] 적 행동 패킷 전송
    private static void SendEnemyActionPacket(int actionType, int damage, bool isTargetHost, string skillName = "")
    {
        var data = new BattleActionData 
        { 
            ActionType = actionType, 
            Damage = damage, 
            IsTargetHost = isTargetHost,
            SkillName = skillName
        };
        string json = JsonSerializer.Serialize(data);
        NetworkManager.Instance.Send(new Packet { Type = PacketType.EnemyAction, Data = json });
    }


    // ========================================================================
    // [신규] 4. 공용: 적 행동 패킷 수신 처리 (HandleEnemyAction 대체)
    // ========================================================================

    public static void OnReceiveEnemyAction(Game game, BattleActionData data)
    {
        game.UpdateLastBattleActionTime();

        Monster monster = game.CurrentBattleMonster;
        if (monster == null) return;

        switch (data.ActionType)
        {
            case 10: // 맹독
                monster.HP -= data.Damage;
                
                // [핵심 수정] 게스트도 턴 차감 및 제거 수행
                if (monster.StatusEffects.ContainsKey(StatType.StrongPoison))
                {
                    monster.StatusEffects[StatType.StrongPoison]--;
                    if (monster.StatusEffects[StatType.StrongPoison] <= 0) 
                        monster.RemoveStatusEffect(StatType.StrongPoison);
                }

                game.AddLog($"[적] 맹독 피해! {data.Damage} 데미지!");
                game.customBlinkColor = ConsoleColor.Magenta;
                game.StartAnimation(monster, $"-{data.Damage}", () => {
                    game.customBlinkColor = ConsoleColor.Black;
                    if (monster.HP <= 0) game.WinMultiplayerBattle();
                });
                break;

            case 11: // 독
                monster.HP -= data.Damage;

                // [핵심 수정] 턴 차감
                if (monster.StatusEffects.ContainsKey(StatType.PoisonStabDamage))
                {
                    monster.StatusEffects[StatType.PoisonStabDamage]--;
                    if (monster.StatusEffects[StatType.PoisonStabDamage] <= 0) 
                        monster.RemoveStatusEffect(StatType.PoisonStabDamage);
                }

                game.AddLog($"[적] 독 피해! {data.Damage} 데미지!");
                game.customBlinkColor = ConsoleColor.Magenta;
                game.StartAnimation(monster, $"-{data.Damage}", () => {
                    game.customBlinkColor = ConsoleColor.Black;
                    if (monster.HP <= 0) game.WinMultiplayerBattle();
                });
                break;

            case 12: // 출혈
                monster.HP -= data.Damage;

                // [핵심 수정] 턴 차감
                if (monster.StatusEffects.ContainsKey(StatType.BleedChance))
                {
                    monster.StatusEffects[StatType.BleedChance]--;
                    if (monster.StatusEffects[StatType.BleedChance] <= 0) 
                        monster.RemoveStatusEffect(StatType.BleedChance);
                }

                game.AddLog($"[적] 출혈 피해! {data.Damage} 데미지!");
                game.customBlinkColor = ConsoleColor.DarkRed;
                game.StartAnimation(monster, $"-{data.Damage}", () => {
                    game.customBlinkColor = ConsoleColor.Black;
                    if (monster.HP <= 0) game.WinMultiplayerBattle();
                });
                break;

            case 13: // 기절
                // [기존 코드 유지] (기절은 이미 수정되어 있었음)
                game.AddLog($"[적] {monster.Name}은(는) 기절해서 움직일 수 없다!");
                
                if (monster.StatusEffects.ContainsKey(StatType.StunChance))
                {
                    monster.StatusEffects[StatType.StunChance]--;
                    if (monster.StatusEffects[StatType.StunChance] <= 0)
                        monster.RemoveStatusEffect(StatType.StunChance);
                }

                if (!NetworkManager.Instance.IsHost)
                {
                    System.Threading.Timer t = null;
                    t = new System.Threading.Timer((_) => {
                        game.ResumeTurnAfterEnemyAction();
                        NetworkManager.Instance.IsDirty = true; 
                        t.Dispose();
                    }, null, 1000, System.Threading.Timeout.Infinite);
                }
                break;

            case 14: // [신규] 공격력 회복 (메테오 디버프 해제)
                if (monster.StatusEffects.ContainsKey(StatType.AtkDebuff))
                {
                    monster.RemoveStatusEffect(StatType.AtkDebuff);
                }
                game.AddLog($"{monster.Name}의 공격력이 회복되었습니다.");
                break;

            case 3: // 기본 공격
                ProcessEnemyAttack(game, monster, data);
                break;
        }
    }

    // 적의 기본 공격 처리 (기존 로직 분리)
   private static void ProcessEnemyAttack(Game game, Monster monster, BattleActionData data)
    {
        bool amITarget = (NetworkManager.Instance.IsHost == data.IsTargetHost);
        Player target = amITarget ? game.player : game.OtherPlayer;
        string targetName = amITarget ? "나" : "동료";

        int finalDamage = data.Damage; // 호스트가 보낸 값 (0이면 MISS)

        // [핵심 수정] 로컬 회피 계산 로직 삭제!
        // 호스트가 이미 계산해서 보냈으므로 무조건 따름

        // 1. 회피(MISS) 처리
        if (finalDamage == 0)
        {
            game.AddLog($"{monster.Name}의 공격이 빗나갔습니다!"); // (동료/나 구분 가능)
            game.StartAnimation(target, "M", () => { game.ResumeTurnAfterEnemyAction(); });
            return;
        }

        // 2. 피격 처리
        game.AddLog($"{monster.Name}가 {targetName}를 공격! {finalDamage} 피해!");
        
        target.HP -= finalDamage;
        if (target.HP < 0) target.HP = 0;

        game.customBlinkColor = ConsoleColor.DarkRed;

        game.StartAnimation(target, $"-{finalDamage}", () => 
        {
            if (game.player.HP <= 0 && amITarget) 
            {
                // 게임 오버는 RunGameLoop에서 체크
            }
            else
            {
                game.ResumeTurnAfterEnemyAction();
            }
        });
    }
}