using System;
using System.Collections.Generic;
using DarkMyst.Combat.Model;
using DarkMyst.Combat.Runtime;

namespace DarkMyst.Combat
{
    /// <summary>
    /// Runs a battle and returns the event log.
    /// <para>
    /// The engine has no rendering, no timing and no dependency on Unity. Animation speed can
    /// therefore never change an outcome: the client plays back a log the simulator already
    /// finished producing. The same assembly runs on the server, which is what makes a battle
    /// verifiable when a reward is at stake.
    /// </para>
    /// <para>
    /// Determinism rules obeyed throughout: integer arithmetic only, a fixed iteration order
    /// everywhere (side then slot), and a single explicit RNG whose draws happen in a defined
    /// order.
    /// </para>
    /// </summary>
    public sealed class BattleSimulator
    {
        private readonly CombatRules _rules;
        private readonly DeterministicRandom _rng;
        private readonly List<RuntimeUnit> _attackers = new List<RuntimeUnit>();
        private readonly List<RuntimeUnit> _defenders = new List<RuntimeUnit>();
        private readonly List<BattleEvent> _events = new List<BattleEvent>();

        private int _round;
        private int _sequence;

        private BattleSimulator(BattleRequest request)
        {
            _rules = (request.Rules ?? CombatRules.Default).Clone();
            _rng = new DeterministicRandom(request.Seed);
            BuildTeam(request.Attacker, TeamSide.Attacker, _attackers);
            BuildTeam(request.Defender, TeamSide.Defender, _defenders);
        }

        /// <summary>Runs the battle described by <paramref name="request"/>.</summary>
        /// <exception cref="ArgumentException">The request is not a valid battle setup.</exception>
        public static BattleResult Run(BattleRequest request)
        {
            Validate(request);
            return new BattleSimulator(request).Execute(request);
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private static void Validate(BattleRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!string.IsNullOrEmpty(request.RulesVersion) && request.RulesVersion != CombatRules.Version)
            {
                throw new ArgumentException(
                    "Battle requests rule set " + request.RulesVersion + " but this build implements "
                    + CombatRules.Version + ". Replaying an old battle needs an engine of that version.",
                    nameof(request));
            }

            ValidateTeam(request.Attacker, nameof(request.Attacker));
            ValidateTeam(request.Defender, nameof(request.Defender));
        }

        private static void ValidateTeam(TeamDefinition team, string name)
        {
            if (team == null || team.Units == null || team.Units.Count == 0)
            {
                throw new ArgumentException(name + " has no units.", name);
            }

            if (team.Units.Count > Formation.SlotCount)
            {
                throw new ArgumentException(
                    name + " has " + team.Units.Count + " units but only "
                    + Formation.SlotCount + " slots exist.", name);
            }

            var seen = new HashSet<int>();
            bool leaderPresent = false;
            foreach (UnitDefinition unit in team.Units)
            {
                if (unit.Slot < 0 || unit.Slot >= Formation.SlotCount)
                {
                    throw new ArgumentException(
                        name + ": slot " + unit.Slot + " is outside 0.." + (Formation.SlotCount - 1) + ".", name);
                }

                if (!seen.Add(unit.Slot))
                {
                    throw new ArgumentException(name + ": slot " + unit.Slot + " is used twice.", name);
                }

                if (unit.Stats.MaxHp <= 0)
                {
                    throw new ArgumentException(
                        name + ": unit '" + unit.InstanceId + "' has no HP.", name);
                }

                if (!HasUsableTurnAction(unit))
                {
                    throw new ArgumentException(
                        name + ": unit '" + unit.InstanceId + "' has no guaranteed OnAction skill. "
                        + "Every unit needs one OnAction skill with 100% activation and no cooldown, "
                        + "otherwise it can end up with nothing to do on its turn.", name);
                }

                if (unit.Slot == team.LeaderSlot)
                {
                    leaderPresent = true;
                }
            }

            if (!leaderPresent)
            {
                throw new ArgumentException(
                    name + ": leader slot " + team.LeaderSlot + " is empty.", name);
            }
        }

        private static bool HasUsableTurnAction(UnitDefinition unit)
        {
            if (unit.Skills == null)
            {
                return false;
            }

            foreach (SkillDefinition skill in unit.Skills)
            {
                if (skill.Trigger == TriggerKind.OnAction
                    && !skill.IsLeaderSkill
                    && skill.ActivationChancePerMille >= 1000
                    && skill.CooldownRounds <= 0
                    && skill.InitialCooldownRounds <= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BuildTeam(TeamDefinition team, TeamSide side, List<RuntimeUnit> into)
        {
            var ordered = new List<UnitDefinition>(team.Units);
            ordered.Sort((a, b) => a.Slot.CompareTo(b.Slot));

            foreach (UnitDefinition definition in ordered)
            {
                var unit = new RuntimeUnit
                {
                    Definition = definition,
                    Ref = new UnitRef(side, definition.Slot),
                    Row = Formation.RowOf(definition.Slot),
                    IsLeader = definition.Slot == team.LeaderSlot,
                    Hp = definition.Stats.MaxHp,
                    Alive = true
                };

                foreach (SkillDefinition skill in definition.Skills)
                {
                    if (skill.InitialCooldownRounds > 0)
                    {
                        unit.Cooldowns[skill.Id] = skill.InitialCooldownRounds;
                    }
                }

                into.Add(unit);
            }
        }

        // ------------------------------------------------------------------
        // Main loop
        // ------------------------------------------------------------------

        private BattleResult Execute(BattleRequest request)
        {
            Emit(BattleEventKind.BattleStarted, note: "seed=" + request.Seed);

            foreach (RuntimeUnit unit in AllUnitsInOrder())
            {
                FireTrigger(TriggerKind.OnBattleStart, unit, null, 0);
            }

            BattleEndReason endReason = BattleEndReason.RoundLimit;
            bool ended = false;
            int roundsRun = 0;

            for (_round = 1; _round <= _rules.MaxRounds && !ended; _round++)
            {
                roundsRun = _round;
                Emit(BattleEventKind.RoundStarted);

                List<RuntimeUnit> order = ComputeTurnOrder();

                foreach (RuntimeUnit unit in order)
                {
                    if (unit.Alive)
                    {
                        FireTrigger(TriggerKind.OnRoundStart, unit, null, 0);
                    }
                }

                foreach (RuntimeUnit unit in order)
                {
                    if (TryResolveEnd(out endReason))
                    {
                        ended = true;
                        break;
                    }

                    if (unit.Alive)
                    {
                        TakeTurn(unit);
                    }
                }

                if (ended || TryResolveEnd(out endReason))
                {
                    ended = true;
                    continue;
                }

                EndOfRoundTicks(order);

                if (TryResolveEnd(out endReason))
                {
                    ended = true;
                    continue;
                }

                ExpireDurations();
                Emit(BattleEventKind.RoundEnded);
            }

            _round = roundsRun;
            if (!ended)
            {
                endReason = BattleEndReason.RoundLimit;
            }

            BattleOutcome outcome = ResolveOutcome(endReason);
            Emit(BattleEventKind.BattleEnded, note: outcome + "/" + endReason);

            var result = new BattleResult
            {
                Outcome = outcome,
                EndReason = endReason,
                Rounds = roundsRun,
                Seed = request.Seed,
                RulesVersion = CombatRules.Version,
                ContentVersion = request.ContentVersion,
                RngCalls = _rng.CallCount,
                Events = _events,
                FinalUnits = Snapshot()
            };
            result.Checksum = result.ComputeChecksum();
            return result;
        }

        /// <summary>
        /// Speed decides the order, recomputed every round so a speed buff takes effect from
        /// the next round. Ties break on side then slot, never on RNG: two identical teams must
        /// still produce one reproducible log.
        /// </summary>
        private List<RuntimeUnit> ComputeTurnOrder()
        {
            var order = new List<RuntimeUnit>();
            foreach (RuntimeUnit unit in AllUnitsInOrder())
            {
                if (unit.Alive)
                {
                    order.Add(unit);
                }
            }

            order.Sort((a, b) =>
            {
                int bySpeed = EffectiveStat(b, Stat.Speed).CompareTo(EffectiveStat(a, Stat.Speed));
                if (bySpeed != 0)
                {
                    return bySpeed;
                }

                int bySide = a.Ref.Side.CompareTo(b.Ref.Side);
                return bySide != 0 ? bySide : a.Ref.Slot.CompareTo(b.Ref.Slot);
            });

            return order;
        }

        private void TakeTurn(RuntimeUnit unit)
        {
            Emit(BattleEventKind.TurnStarted, source: unit.Ref);

            if (unit.StunRounds > 0)
            {
                unit.StunRounds--;
                if (unit.StunRounds == 0)
                {
                    unit.StunImmuneRounds = _rules.StunImmuneRoundsAfterStun;
                }

                Emit(BattleEventKind.TurnSkipped, source: unit.Ref, note: "stunned");
                Emit(BattleEventKind.TurnEnded, source: unit.Ref);
                return;
            }

            FireTrigger(TriggerKind.OnBeforeAction, unit, null, 0);

            if (unit.Alive)
            {
                SkillDefinition action = ChooseTurnAction(unit);
                if (action != null)
                {
                    ExecuteSkill(unit, action, null, 0);
                }
            }

            // Stun immunity is spent by taking a turn, not by the clock. Counting rounds
            // instead would let a stunner acting before the victim re-apply before the
            // immunity ever had a chance to block anything.
            if (unit.StunImmuneRounds > 0)
            {
                unit.StunImmuneRounds--;
            }

            Emit(BattleEventKind.TurnEnded, source: unit.Ref);
        }

        /// <summary>
        /// Picks the first eligible OnAction skill in authoring order. Content therefore reads
        /// top-down as "use this if you can, otherwise this, otherwise the basic attack", which
        /// is how designers already think about it.
        /// </summary>
        private SkillDefinition ChooseTurnAction(RuntimeUnit unit)
        {
            foreach (SkillDefinition skill in unit.Definition.Skills)
            {
                if (skill.Trigger != TriggerKind.OnAction)
                {
                    continue;
                }

                if (skill.IsLeaderSkill && !unit.IsLeader)
                {
                    continue;
                }

                if (GetCooldown(unit, skill.Id) > 0)
                {
                    continue;
                }

                if (!_rng.Chance(skill.ActivationChancePerMille))
                {
                    continue;
                }

                return skill;
            }

            return null;
        }

        private void FireTrigger(TriggerKind kind, RuntimeUnit owner, RuntimeUnit triggerSource, int depth)
        {
            if (depth >= _rules.MaxReactionDepth)
            {
                return;
            }

            // Death rattles are the one trigger a downed unit still gets.
            if (!owner.Alive && kind != TriggerKind.OnDeath)
            {
                return;
            }

            foreach (SkillDefinition skill in owner.Definition.Skills)
            {
                if (skill.Trigger != kind)
                {
                    continue;
                }

                if (skill.IsLeaderSkill && !owner.IsLeader)
                {
                    continue;
                }

                if (GetCooldown(owner, skill.Id) > 0)
                {
                    continue;
                }

                if (!_rng.Chance(skill.ActivationChancePerMille))
                {
                    continue;
                }

                ExecuteSkill(owner, skill, triggerSource, depth);
            }
        }

        private void ExecuteSkill(RuntimeUnit caster, SkillDefinition skill, RuntimeUnit triggerSource, int depth)
        {
            Emit(BattleEventKind.SkillActivated, source: caster.Ref, skillId: skill.Id);

            if (skill.CooldownRounds > 0)
            {
                caster.Cooldowns[skill.Id] = skill.CooldownRounds;
            }

            foreach (SkillEffect effect in skill.Effects)
            {
                ApplyEffect(caster, skill, effect, triggerSource, depth);
            }
        }

        // ------------------------------------------------------------------
        // Effects
        // ------------------------------------------------------------------

        private void ApplyEffect(
            RuntimeUnit caster,
            SkillDefinition skill,
            SkillEffect effect,
            RuntimeUnit triggerSource,
            int depth)
        {
            List<RuntimeUnit> targets = SelectTargets(caster, effect, triggerSource);

            foreach (RuntimeUnit target in targets)
            {
                if (!_rng.Chance(effect.ChancePerMille))
                {
                    Emit(BattleEventKind.StatusResisted, caster.Ref, target.Ref,
                        skillId: skill.Id, statusId: effect.StatusId);
                    continue;
                }

                switch (effect.Kind)
                {
                    case EffectKind.Damage:
                        ApplyDamageEffect(caster, skill, effect, target, depth);
                        break;
                    case EffectKind.Heal:
                        ApplyHeal(caster, target, ScaledAmount(caster, effect), skill.Id);
                        break;
                    case EffectKind.Shield:
                        ApplyShield(caster, target, effect, skill.Id);
                        break;
                    case EffectKind.StatModifier:
                    case EffectKind.DamageOverTime:
                    case EffectKind.HealOverTime:
                    case EffectKind.Taunt:
                        ApplyStatus(caster, target, effect, skill.Id);
                        break;
                    case EffectKind.Stun:
                        ApplyStun(caster, target, effect, skill.Id);
                        break;
                    case EffectKind.Cleanse:
                        RemoveStatuses(caster, target, effect, beneficial: false, skillId: skill.Id);
                        break;
                    case EffectKind.Dispel:
                        RemoveStatuses(caster, target, effect, beneficial: true, skillId: skill.Id);
                        break;
                    case EffectKind.Revive:
                        ApplyRevive(caster, target, effect, skill.Id);
                        break;
                    default:
                        throw new NotSupportedException("Unhandled effect kind " + effect.Kind + ".");
                }
            }
        }

        private void ApplyDamageEffect(
            RuntimeUnit caster,
            SkillDefinition skill,
            SkillEffect effect,
            RuntimeUnit target,
            int depth)
        {
            int totalDealt = 0;
            int hits = effect.Hits < 1 ? 1 : effect.Hits;

            for (int hit = 0; hit < hits && target.Alive; hit++)
            {
                Stat scalingStat = effect.ScalingStat ?? DamageMath.DefaultScalingStat(effect.DamageKind);
                int attackStat = EffectiveStat(caster, scalingStat);

                int mitigationStat = 0;
                int rowPerMille = 1000;
                int affinityPerMille = 1000;

                if (effect.DamageKind != DamageKind.True)
                {
                    mitigationStat = EffectiveStat(target, DamageMath.DefaultMitigationStat(effect.DamageKind));
                    rowPerMille = DamageMath.RowMultiplierPerMille(effect.DamageKind, target.Row, _rules);
                    affinityPerMille = DamageMath.AffinityMultiplierPerMille(
                        caster.Definition.Affinity, target.Definition.Affinity, _rules);
                }

                bool critical = effect.CanCrit && _rng.Chance(EffectiveStat(caster, Stat.CritRate));
                int critPerMille = critical
                    ? DamageMath.CritMultiplierPerMille(EffectiveStat(caster, Stat.CritDamage), _rules)
                    : 1000;

                int variancePerMille = _rng.NextInt(_rules.VarianceMinPerMille, _rules.VarianceMaxPerMille + 1);

                int amount = DamageMath.Compute(
                    attackStat, mitigationStat, effect.PowerPerMille,
                    rowPerMille, affinityPerMille, critPerMille, variancePerMille, _rules);

                amount += effect.FlatAmount;

                totalDealt += DealDamage(caster, target, amount, critical, skill.Id, effect.StatusId, depth);
            }

            if (effect.LifestealPerMille > 0 && totalDealt > 0)
            {
                ApplyHeal(caster, caster, (int)((long)totalDealt * effect.LifestealPerMille / 1000L), skill.Id);
            }

            if (totalDealt > 0 && target.Alive)
            {
                FireTrigger(TriggerKind.OnAfterDamaged, target, caster, depth + 1);
            }
        }

        /// <summary>Applies damage through shields, emits the events, and handles death.</summary>
        /// <returns>Damage actually removed from HP plus shields.</returns>
        private int DealDamage(
            RuntimeUnit source,
            RuntimeUnit target,
            int amount,
            bool critical,
            string skillId,
            string statusId,
            int depth)
        {
            if (!target.Alive || amount <= 0)
            {
                return 0;
            }

            int absorbed = ConsumeShields(target, ref amount);
            if (absorbed > 0)
            {
                Emit(BattleEventKind.ShieldAbsorbed, source.Ref, target.Ref,
                    skillId: skillId, amount: absorbed, hpAfter: target.Hp);
            }

            if (amount > 0)
            {
                target.Hp -= amount;
                if (target.Hp < 0)
                {
                    target.Hp = 0;
                }
            }

            Emit(BattleEventKind.Damaged, source.Ref, target.Ref,
                skillId: skillId, statusId: statusId, amount: amount,
                critical: critical, hpAfter: target.Hp);

            if (target.Hp == 0)
            {
                HandleDown(target, source, depth);
            }

            return absorbed + amount;
        }

        private int ConsumeShields(RuntimeUnit target, ref int amount)
        {
            int absorbed = 0;
            while (amount > 0 && target.Shields.Count > 0)
            {
                ShieldInstance shield = target.Shields[0];
                int take = shield.Remaining < amount ? shield.Remaining : amount;
                shield.Remaining -= take;
                amount -= take;
                absorbed += take;

                if (shield.Remaining <= 0)
                {
                    target.Shields.RemoveAt(0);
                }
            }

            return absorbed;
        }

        private void HandleDown(RuntimeUnit unit, RuntimeUnit killer, int depth)
        {
            unit.Alive = false;
            Emit(BattleEventKind.UnitDowned, killer?.Ref, unit.Ref, hpAfter: 0);

            FireTrigger(TriggerKind.OnDeath, unit, killer, depth + 1);

            // Statuses and shields do not survive the unit. A revive brings back a clean slate.
            unit.Statuses.Clear();
            unit.Shields.Clear();
            unit.StunRounds = 0;

            foreach (RuntimeUnit ally in TeamOf(unit.Ref.Side))
            {
                if (ally.Alive)
                {
                    FireTrigger(TriggerKind.OnAllyDown, ally, unit, depth + 1);
                }
            }
        }

        private void ApplyHeal(RuntimeUnit source, RuntimeUnit target, int amount, string skillId)
        {
            if (!target.Alive || amount <= 0)
            {
                return;
            }

            int before = target.Hp;
            target.Hp += amount;
            if (target.Hp > target.MaxHp)
            {
                target.Hp = target.MaxHp;
            }

            int healed = target.Hp - before;
            if (healed <= 0)
            {
                return;
            }

            Emit(BattleEventKind.Healed, source.Ref, target.Ref,
                skillId: skillId, amount: healed, hpAfter: target.Hp);
        }

        private void ApplyShield(RuntimeUnit caster, RuntimeUnit target, SkillEffect effect, string skillId)
        {
            if (!target.Alive)
            {
                return;
            }

            int amount = ScaledAmount(caster, effect);
            if (amount <= 0)
            {
                return;
            }

            target.Shields.Add(new ShieldInstance
            {
                Id = effect.StatusId ?? skillId,
                Remaining = amount,
                RemainingRounds = effect.DurationRounds,
                Source = caster.Ref
            });

            Emit(BattleEventKind.StatusApplied, caster.Ref, target.Ref,
                skillId: skillId, statusId: effect.StatusId ?? skillId, amount: amount, hpAfter: target.Hp);
        }

        private void ApplyStun(RuntimeUnit caster, RuntimeUnit target, SkillEffect effect, string skillId)
        {
            if (!target.Alive)
            {
                return;
            }

            if (target.StunImmuneRounds > 0)
            {
                Emit(BattleEventKind.StatusResisted, caster.Ref, target.Ref,
                    skillId: skillId, statusId: effect.StatusId ?? "stun", note: "stun immune");
                return;
            }

            int rounds = effect.DurationRounds < 1 ? 1 : effect.DurationRounds;
            if (rounds > target.StunRounds)
            {
                target.StunRounds = rounds;
            }

            Emit(BattleEventKind.StatusApplied, caster.Ref, target.Ref,
                skillId: skillId, statusId: effect.StatusId ?? "stun", amount: rounds);
        }

        private void ApplyRevive(RuntimeUnit caster, RuntimeUnit target, SkillEffect effect, string skillId)
        {
            if (target.Alive)
            {
                return;
            }

            int hp = (int)((long)target.MaxHp * effect.AmountPerMille / 1000L);
            if (hp < 1)
            {
                hp = 1;
            }

            target.Alive = true;
            target.Hp = hp;

            // A revived unit is not inserted into the current round's turn order, so it acts
            // from the next round. That keeps "revive then immediately act" out of the game.
            Emit(BattleEventKind.UnitRevived, caster.Ref, target.Ref,
                skillId: skillId, amount: hp, hpAfter: hp);
        }

        private void ApplyStatus(RuntimeUnit caster, RuntimeUnit target, SkillEffect effect, string skillId)
        {
            if (!target.Alive || string.IsNullOrEmpty(effect.StatusId))
            {
                return;
            }

            bool beneficial = IsBeneficial(effect);
            int tickAmount = 0;
            if (effect.Kind == EffectKind.DamageOverTime || effect.Kind == EffectKind.HealOverTime)
            {
                tickAmount = ScaledAmount(caster, effect);
                if (tickAmount < 1)
                {
                    tickAmount = 1;
                }
            }

            StatusInstance existing = null;
            if (effect.StackRule != StackRule.Independent)
            {
                for (int i = 0; i < target.Statuses.Count; i++)
                {
                    if (target.Statuses[i].Id == effect.StatusId)
                    {
                        existing = target.Statuses[i];
                        break;
                    }
                }
            }

            if (existing != null)
            {
                switch (effect.StackRule)
                {
                    case StackRule.Ignore:
                        return;

                    case StackRule.Stack:
                        if (existing.Stacks < existing.MaxStacks)
                        {
                            existing.Stacks++;
                        }

                        existing.RemainingRounds = effect.DurationRounds;
                        existing.TickAmount = tickAmount > existing.TickAmount ? tickAmount : existing.TickAmount;
                        break;

                    default: // Refresh
                        existing.RemainingRounds = effect.DurationRounds;
                        if (Math.Abs(effect.AmountPerMille) > Math.Abs(existing.AmountPerMille))
                        {
                            existing.AmountPerMille = effect.AmountPerMille;
                        }

                        existing.TickAmount = tickAmount > existing.TickAmount ? tickAmount : existing.TickAmount;
                        break;
                }

                Emit(BattleEventKind.StatusRefreshed, caster.Ref, target.Ref,
                    skillId: skillId, statusId: effect.StatusId, amount: existing.Stacks);
                return;
            }

            target.Statuses.Add(new StatusInstance
            {
                Id = effect.StatusId,
                Kind = effect.Kind,
                ModifiedStat = effect.ModifiedStat,
                AmountPerMille = effect.AmountPerMille,
                TickAmount = tickAmount,
                RemainingRounds = effect.DurationRounds,
                Stacks = 1,
                MaxStacks = effect.MaxStacks < 1 ? 1 : effect.MaxStacks,
                StackRule = effect.StackRule,
                Source = caster.Ref,
                IsBeneficial = beneficial
            });

            Emit(BattleEventKind.StatusApplied, caster.Ref, target.Ref,
                skillId: skillId, statusId: effect.StatusId, amount: 1);
        }

        /// <summary>
        /// A status is a buff when it helps the unit carrying it. Cleanse removes debuffs,
        /// Dispel removes buffs, so this classification decides what each one can strip.
        /// </summary>
        private static bool IsBeneficial(SkillEffect effect)
        {
            switch (effect.Kind)
            {
                case EffectKind.HealOverTime:
                    return true;
                case EffectKind.DamageOverTime:
                case EffectKind.Taunt:
                    return false;
                case EffectKind.StatModifier:
                    return effect.AmountPerMille >= 0;
                default:
                    return false;
            }
        }

        private void RemoveStatuses(
            RuntimeUnit caster, RuntimeUnit target, SkillEffect effect, bool beneficial, string skillId)
        {
            if (!target.Alive)
            {
                return;
            }

            int budget = effect.RemoveCount < 1 ? 1 : effect.RemoveCount;
            for (int i = 0; i < target.Statuses.Count && budget > 0;)
            {
                StatusInstance status = target.Statuses[i];
                if (status.IsBeneficial == beneficial)
                {
                    target.Statuses.RemoveAt(i);
                    budget--;
                    Emit(BattleEventKind.StatusRemoved, caster.Ref, target.Ref,
                        skillId: skillId, statusId: status.Id);
                    continue;
                }

                i++;
            }
        }

        /// <summary>Scales an effect's amount off the caster: <c>stat * amount / 1000 + flat</c>.</summary>
        private int ScaledAmount(RuntimeUnit caster, SkillEffect effect)
        {
            Stat stat = effect.ScalingStat ?? Stat.Magic;
            int value = (int)((long)EffectiveStat(caster, stat) * effect.AmountPerMille / 1000L);
            return value + effect.FlatAmount;
        }

        // ------------------------------------------------------------------
        // Targeting
        // ------------------------------------------------------------------

        private List<RuntimeUnit> SelectTargets(RuntimeUnit caster, SkillEffect effect, RuntimeUnit triggerSource)
        {
            var result = new List<RuntimeUnit>();

            switch (effect.Target)
            {
                case TargetSelector.Self:
                    if (caster.Alive)
                    {
                        result.Add(caster);
                    }

                    return result;

                case TargetSelector.TriggerSource:
                    if (triggerSource != null && triggerSource.Alive)
                    {
                        result.Add(triggerSource);
                    }

                    return result;

                case TargetSelector.AllAllies:
                    return LivingUnits(caster.Ref.Side);

                case TargetSelector.AllEnemies:
                    return LivingUnits(Opposite(caster.Ref.Side));

                case TargetSelector.LowestSlotDownedAlly:
                {
                    foreach (RuntimeUnit unit in TeamOf(caster.Ref.Side))
                    {
                        if (!unit.Alive)
                        {
                            result.Add(unit);
                            break;
                        }
                    }

                    return result;
                }
            }

            bool targetsEnemies = effect.Target == TargetSelector.RandomEnemy
                || effect.Target == TargetSelector.FrontRowEnemy
                || effect.Target == TargetSelector.LowestHpEnemy
                || effect.Target == TargetSelector.HighestAttackEnemy;

            TeamSide side = targetsEnemies ? Opposite(caster.Ref.Side) : caster.Ref.Side;
            List<RuntimeUnit> candidates = LivingUnits(side);
            if (candidates.Count == 0)
            {
                return result;
            }

            if (targetsEnemies && !effect.IgnoresTaunt)
            {
                var taunting = new List<RuntimeUnit>();
                foreach (RuntimeUnit unit in candidates)
                {
                    if (unit.IsTaunting)
                    {
                        taunting.Add(unit);
                    }
                }

                if (taunting.Count > 0)
                {
                    candidates = taunting;
                }
            }

            switch (effect.Target)
            {
                case TargetSelector.FrontRowEnemy:
                {
                    var front = new List<RuntimeUnit>();
                    foreach (RuntimeUnit unit in candidates)
                    {
                        if (unit.Row == Row.Front)
                        {
                            front.Add(unit);
                        }
                    }

                    // Once the front line is gone the back line is exposed.
                    return PickRandom(front.Count > 0 ? front : candidates, effect.MaxTargets);
                }

                case TargetSelector.LowestHpEnemy:
                case TargetSelector.LowestHpAlly:
                {
                    RuntimeUnit best = candidates[0];
                    foreach (RuntimeUnit unit in candidates)
                    {
                        if (unit.Hp < best.Hp)
                        {
                            best = unit;
                        }
                    }

                    result.Add(best);
                    return result;
                }

                case TargetSelector.HighestAttackEnemy:
                {
                    RuntimeUnit best = candidates[0];
                    int bestValue = EffectiveStat(best, Stat.Attack);
                    foreach (RuntimeUnit unit in candidates)
                    {
                        int value = EffectiveStat(unit, Stat.Attack);
                        if (value > bestValue)
                        {
                            best = unit;
                            bestValue = value;
                        }
                    }

                    result.Add(best);
                    return result;
                }

                default: // RandomEnemy, RandomAlly
                    return PickRandom(candidates, effect.MaxTargets);
            }
        }

        /// <summary>
        /// Picks up to <paramref name="count"/> distinct units. Always returns them in slot
        /// order so the log reads consistently regardless of draw order.
        /// </summary>
        private List<RuntimeUnit> PickRandom(List<RuntimeUnit> candidates, int count)
        {
            if (count < 1)
            {
                count = 1;
            }

            if (candidates.Count <= count)
            {
                return new List<RuntimeUnit>(candidates);
            }

            var pool = new List<RuntimeUnit>(candidates);
            var picked = new List<RuntimeUnit>();
            for (int i = 0; i < count; i++)
            {
                int index = _rng.NextInt(0, pool.Count);
                picked.Add(pool[index]);
                pool.RemoveAt(index);
            }

            picked.Sort(CompareByAddress);
            return picked;
        }

        // ------------------------------------------------------------------
        // Round end
        // ------------------------------------------------------------------

        /// <summary>
        /// Regeneration resolves before poison, in turn order. Documented rather than
        /// incidental: a unit on 100 HP with a 200 regen and a 250 poison survives the round.
        /// </summary>
        private void EndOfRoundTicks(List<RuntimeUnit> order)
        {
            foreach (RuntimeUnit unit in order)
            {
                if (!unit.Alive)
                {
                    continue;
                }

                foreach (StatusInstance status in new List<StatusInstance>(unit.Statuses))
                {
                    if (status.Kind != EffectKind.HealOverTime)
                    {
                        continue;
                    }

                    int amount = status.TickAmount * status.Stacks;
                    int before = unit.Hp;
                    unit.Hp = unit.Hp + amount > unit.MaxHp ? unit.MaxHp : unit.Hp + amount;
                    if (unit.Hp != before)
                    {
                        Emit(BattleEventKind.StatusTicked, status.Source, unit.Ref,
                            statusId: status.Id, amount: unit.Hp - before, hpAfter: unit.Hp);
                    }
                }
            }

            foreach (RuntimeUnit unit in order)
            {
                if (!unit.Alive)
                {
                    continue;
                }

                foreach (StatusInstance status in new List<StatusInstance>(unit.Statuses))
                {
                    if (status.Kind != EffectKind.DamageOverTime || !unit.Alive)
                    {
                        continue;
                    }

                    int amount = status.TickAmount * status.Stacks;
                    unit.Hp -= amount;
                    if (unit.Hp < 0)
                    {
                        unit.Hp = 0;
                    }

                    Emit(BattleEventKind.StatusTicked, status.Source, unit.Ref,
                        statusId: status.Id, amount: amount, hpAfter: unit.Hp);

                    if (unit.Hp == 0)
                    {
                        HandleDown(unit, FindUnit(status.Source), 0);
                    }
                }
            }
        }

        private void ExpireDurations()
        {
            foreach (RuntimeUnit unit in AllUnitsInOrder())
            {
                for (int i = unit.Statuses.Count - 1; i >= 0; i--)
                {
                    StatusInstance status = unit.Statuses[i];
                    status.RemainingRounds--;
                    if (status.RemainingRounds <= 0)
                    {
                        unit.Statuses.RemoveAt(i);
                        Emit(BattleEventKind.StatusExpired, target: unit.Ref, statusId: status.Id);
                    }
                }

                for (int i = unit.Shields.Count - 1; i >= 0; i--)
                {
                    ShieldInstance shield = unit.Shields[i];
                    shield.RemainingRounds--;
                    if (shield.RemainingRounds <= 0)
                    {
                        unit.Shields.RemoveAt(i);
                        Emit(BattleEventKind.StatusExpired, target: unit.Ref, statusId: shield.Id);
                    }
                }

                if (unit.Cooldowns.Count > 0)
                {
                    var keys = new List<string>(unit.Cooldowns.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    foreach (string key in keys)
                    {
                        int value = unit.Cooldowns[key] - 1;
                        unit.Cooldowns[key] = value < 0 ? 0 : value;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Outcome
        // ------------------------------------------------------------------

        private bool TryResolveEnd(out BattleEndReason reason)
        {
            bool attackersStanding = AnyAlive(_attackers);
            bool defendersStanding = AnyAlive(_defenders);

            if (!defendersStanding)
            {
                reason = BattleEndReason.DefenderWiped;
                return true;
            }

            if (!attackersStanding)
            {
                reason = BattleEndReason.AttackerWiped;
                return true;
            }

            reason = BattleEndReason.RoundLimit;
            return false;
        }

        /// <summary>
        /// On the round limit the side holding the larger share of its total HP wins. An exact
        /// tie is a Draw; the surrounding game decides what a Draw is worth (PvE treats it as a
        /// loss for the attacker).
        /// </summary>
        private BattleOutcome ResolveOutcome(BattleEndReason reason)
        {
            if (reason == BattleEndReason.DefenderWiped)
            {
                return BattleOutcome.AttackerVictory;
            }

            if (reason == BattleEndReason.AttackerWiped)
            {
                return BattleOutcome.DefenderVictory;
            }

            int attackerShare = HpSharePerMille(_attackers);
            int defenderShare = HpSharePerMille(_defenders);

            if (attackerShare > defenderShare)
            {
                return BattleOutcome.AttackerVictory;
            }

            return attackerShare < defenderShare ? BattleOutcome.DefenderVictory : BattleOutcome.Draw;
        }

        private static int HpSharePerMille(List<RuntimeUnit> team)
        {
            long hp = 0;
            long maxHp = 0;
            foreach (RuntimeUnit unit in team)
            {
                hp += unit.Hp;
                maxHp += unit.MaxHp;
            }

            return maxHp == 0 ? 0 : (int)(hp * 1000L / maxHp);
        }

        private List<UnitSnapshot> Snapshot()
        {
            var snapshots = new List<UnitSnapshot>();
            foreach (RuntimeUnit unit in AllUnitsInOrder())
            {
                snapshots.Add(new UnitSnapshot
                {
                    Ref = unit.Ref,
                    InstanceId = unit.Definition.InstanceId,
                    CharacterId = unit.Definition.CharacterId,
                    Hp = unit.Hp,
                    MaxHp = unit.MaxHp,
                    Alive = unit.Alive
                });
            }

            return snapshots;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private int EffectiveStat(RuntimeUnit unit, Stat stat) => unit.EffectiveStat(stat, _rules);

        private int GetCooldown(RuntimeUnit unit, string skillId)
        {
            int value;
            return unit.Cooldowns.TryGetValue(skillId, out value) ? value : 0;
        }

        private static TeamSide Opposite(TeamSide side)
            => side == TeamSide.Attacker ? TeamSide.Defender : TeamSide.Attacker;

        private List<RuntimeUnit> TeamOf(TeamSide side)
            => side == TeamSide.Attacker ? _attackers : _defenders;

        private List<RuntimeUnit> LivingUnits(TeamSide side)
        {
            var living = new List<RuntimeUnit>();
            foreach (RuntimeUnit unit in TeamOf(side))
            {
                if (unit.Alive)
                {
                    living.Add(unit);
                }
            }

            return living;
        }

        private IEnumerable<RuntimeUnit> AllUnitsInOrder()
        {
            foreach (RuntimeUnit unit in _attackers)
            {
                yield return unit;
            }

            foreach (RuntimeUnit unit in _defenders)
            {
                yield return unit;
            }
        }

        private RuntimeUnit FindUnit(UnitRef reference)
        {
            foreach (RuntimeUnit unit in TeamOf(reference.Side))
            {
                if (unit.Ref.Slot == reference.Slot)
                {
                    return unit;
                }
            }

            return null;
        }

        private static bool AnyAlive(List<RuntimeUnit> team)
        {
            foreach (RuntimeUnit unit in team)
            {
                if (unit.Alive)
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareByAddress(RuntimeUnit a, RuntimeUnit b)
        {
            int bySide = a.Ref.Side.CompareTo(b.Ref.Side);
            return bySide != 0 ? bySide : a.Ref.Slot.CompareTo(b.Ref.Slot);
        }

        private void Emit(
            BattleEventKind kind,
            UnitRef? source = null,
            UnitRef? target = null,
            string skillId = null,
            string statusId = null,
            int amount = 0,
            bool critical = false,
            int hpAfter = -1,
            string note = null)
        {
            _events.Add(new BattleEvent
            {
                Kind = kind,
                Sequence = _sequence++,
                Round = _round,
                Source = source,
                Target = target,
                SkillId = skillId,
                StatusId = statusId,
                Amount = amount,
                Critical = critical,
                TargetHpAfter = hpAfter,
                Note = note
            });
        }
    }
}
