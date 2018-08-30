using System.Collections.Generic;
using System.Linq;
using EnumsNET;
using MoreLinq;
using PoESkillTree.Computation.Common;
using PoESkillTree.Computation.Common.Builders;
using PoESkillTree.Computation.Common.Builders.Conditions;
using PoESkillTree.Computation.Common.Builders.Damage;
using PoESkillTree.Computation.Common.Builders.Equipment;
using PoESkillTree.Computation.Common.Builders.Forms;
using PoESkillTree.Computation.Common.Builders.Modifiers;
using PoESkillTree.Computation.Common.Builders.Stats;
using PoESkillTree.Computation.Common.Builders.Values;
using PoESkillTree.GameModel.Items;
using PoESkillTree.GameModel.Skills;

namespace PoESkillTree.Computation.Parsing.SkillParsers
{
    public class ActiveSkillParser : IParser<Skill>
    {
        public delegate IParser<UntranslatedStatParserParameter> StatParserFactory(string statTranslationFileName);

        private readonly IBuilderFactories _builderFactories;
        private readonly IMetaStatBuilders _metaStatBuilders;
        private readonly StatParserFactory _statParserFactory;

        private readonly IModifierBuilder _modifierBuilder = new ModifierBuilder();
        private readonly ActiveSkillPreParser _preParser;
        private readonly ActiveSkillKeywordParser _keywordParser;

        public ActiveSkillParser(
            SkillDefinitions skillDefinitions, IBuilderFactories builderFactories, IMetaStatBuilders metaStatBuilders,
            StatParserFactory statParserFactory)
        {
            (_builderFactories, _metaStatBuilders, _statParserFactory) =
                (builderFactories, metaStatBuilders, statParserFactory);
            _preParser = new ActiveSkillPreParser(skillDefinitions, metaStatBuilders);
            _keywordParser = new ActiveSkillKeywordParser(builderFactories, metaStatBuilders);
        }

        public ParseResult Parse(Skill parameter)
        {
            var modifiers = new List<Modifier>();
            var (preParseResult, parsedStats) = _preParser.Parse(parameter);

            var definition = preParseResult.SkillDefinition;
            var activeSkill = definition.ActiveSkill;
            var level = definition.Levels[parameter.Level];

            var hitDamageSource = preParseResult.HitDamageSource;

            var isMainSkill = preParseResult.IsMainSkill.IsSet;

            void Add(IIntermediateModifier m) => modifiers.AddRange(Build(m));
            void AddGem(IIntermediateModifier m) => modifiers.AddRange(BuildGem(m));

            IReadOnlyList<Modifier> Build(IIntermediateModifier m)
                => m.Build(preParseResult.GlobalSource, Entity.Character);

            IReadOnlyList<Modifier> BuildGem(IIntermediateModifier m)
                => m.Build(preParseResult.GemSource, Entity.Character);

            if (hitDamageSource.HasValue)
            {
                Add(Modifier(_metaStatBuilders.SkillHitDamageSource,
                    Forms.TotalOverride, (int) hitDamageSource.Value, isMainSkill));
            }
            var usesMainHandCondition = isMainSkill;
            var usesOffHandCondition = isMainSkill.And(OffHand.Has(Tags.Weapon));
            if (activeSkill.ActiveSkillTypes.Contains(ActiveSkillType.RequiresDualWield))
                usesMainHandCondition = usesMainHandCondition.And(OffHand.Has(Tags.Weapon));
            else if (activeSkill.ActiveSkillTypes.Contains(ActiveSkillType.RequiresShield))
                usesMainHandCondition = usesMainHandCondition.And(OffHand.Has(Tags.Shield));
            if (activeSkill.WeaponRestrictions.Any())
            {
                usesMainHandCondition = usesMainHandCondition.And(
                    CreateWeaponRestrictionCondition(MainHand, activeSkill.WeaponRestrictions));
                usesOffHandCondition = usesOffHandCondition.And(
                    CreateWeaponRestrictionCondition(OffHand, activeSkill.WeaponRestrictions));
            }
            Add(Modifier(_metaStatBuilders.SkillUsesHand(AttackDamageHand.MainHand),
                Forms.TotalOverride, 1, usesMainHandCondition));
            if (!activeSkill.ActiveSkillTypes.Contains(ActiveSkillType.DoesNotUseOffHand))
            {
                Add(Modifier(_metaStatBuilders.SkillUsesHand(AttackDamageHand.OffHand),
                    Forms.TotalOverride, 1, usesOffHandCondition));
            }
            Add(Modifier(_metaStatBuilders.MainSkillId,
                Forms.TotalOverride, definition.NumericId, isMainSkill));

            var (newlyParsedModifiers, newlyParsedStats) = _keywordParser.Parse(parameter, preParseResult);
            modifiers.AddRange(newlyParsedModifiers);
            parsedStats = parsedStats.Concat(newlyParsedStats);

            if (hitDamageSource != DamageSource.Attack)
            {
                var castRateDamageSource = hitDamageSource ?? DamageSource.Spell;
                Add(Modifier(_builderFactories.StatBuilders.CastRate.With(castRateDamageSource),
                    Forms.BaseSet, 1000D / activeSkill.CastTime, isMainSkill));
            }

            if (activeSkill.TotemLifeMultiplier is double lifeMulti)
            {
                var totemLifeStat = _builderFactories.StatBuilders.Pool.From(Pool.Life)
                    .For(_builderFactories.EntityBuilders.Totem);
                Add(Modifier(totemLifeStat, Forms.PercentMore, (lifeMulti - 1) * 100, isMainSkill));
            }

            if (level.DamageEffectiveness.HasValue)
            {
                Add(Modifier(_metaStatBuilders.DamageBaseAddEffectiveness,
                    Forms.TotalOverride, level.DamageEffectiveness.Value, isMainSkill));
            }
            if (level.DamageMultiplier.HasValue)
            {
                Add(Modifier(_metaStatBuilders.DamageBaseSetEffectiveness,
                    Forms.TotalOverride, level.DamageMultiplier.Value, isMainSkill));
            }
            if (level.CriticalStrikeChance.HasValue && hitDamageSource.HasValue)
            {
                Add(Modifier(_builderFactories.ActionBuilders.CriticalStrike.Chance.With(hitDamageSource.Value),
                    Forms.BaseSet, level.CriticalStrikeChance.Value, isMainSkill));
            }

            if (level.ManaCost.HasValue)
            {
                Add(Modifier(_builderFactories.StatBuilders.Pool.From(Pool.Mana).Cost,
                    Forms.BaseSet, level.ManaCost.Value, isMainSkill));
            }
            if (level.Cooldown.HasValue)
            {
                Add(Modifier(_builderFactories.StatBuilders.Cooldown,
                    Forms.BaseSet, level.Cooldown.Value, isMainSkill));
            }

            if (parameter.GemGroup.HasValue)
            {
                AddGem(Modifier(_builderFactories.StatBuilders.Requirements.Level, Forms.BaseSet, level.RequiredLevel));
                if (level.RequiredDexterity > 0)
                {
                    AddGem(Modifier(_builderFactories.StatBuilders.Requirements.Dexterity,
                        Forms.BaseSet, level.RequiredDexterity));
                }
                if (level.RequiredIntelligence > 0)
                {
                    AddGem(Modifier(_builderFactories.StatBuilders.Requirements.Intelligence,
                        Forms.BaseSet, level.RequiredIntelligence));
                }
                if (level.RequiredStrength > 0)
                {
                    AddGem(Modifier(_builderFactories.StatBuilders.Requirements.Strength,
                        Forms.BaseSet, level.RequiredStrength));
                }
            }

            var qualityStats =
                level.QualityStats.Select(s => new UntranslatedStat(s.StatId, s.Value * parameter.Quality / 1000));
            var (parsedModifiers, remainingStats) = ParseWithoutTranslating(level.Stats, isMainSkill);
            parsedModifiers.ForEach(Add);

            remainingStats = remainingStats.Except(parsedStats);
            var isMainSkillValue = preParseResult.IsMainSkill.Value
                .Build(new BuildParameters(null, Entity.Character, default));
            var statParser = _statParserFactory(definition.StatTranslationFile);

            ParseResult Parse(IEnumerable<UntranslatedStat> stats)
            {
                var parserParameter = new UntranslatedStatParserParameter(preParseResult.LocalSource, stats);
                return ApplyCondition(statParser.Parse(parserParameter), isMainSkillValue);
            }

            var parseResults = new[] { ParseResult.Success(modifiers), Parse(qualityStats), Parse(remainingStats) };
            return ParseResult.Aggregate(parseResults);
        }

        private static IConditionBuilder CreateWeaponRestrictionCondition(
            IEquipmentBuilder hand, IEnumerable<ItemClass> weaponRestrictions)
            => weaponRestrictions.Select(hand.Has).Aggregate((l, r) => l.Or(r));

        private (IEnumerable<IIntermediateModifier> modifiers, IEnumerable<UntranslatedStat> unparsed)
            ParseWithoutTranslating(IEnumerable<UntranslatedStat> stats, IConditionBuilder isMainSkill)
        {
            var modifiers = new List<IIntermediateModifier>();
            var unparsedStats = new List<UntranslatedStat>();
            DamageType? hitDamageType = null;
            DamageSource? hitDamageSource = null;
            double hitDamageMinimum = 0D;
            double? hitDamageMaximum = null;
            foreach (var stat in stats)
            {
                var match = SkillStatIds.HitDamageRegex.Match(stat.StatId);
                if (match.Success)
                {
                    hitDamageSource = Enums.Parse<DamageSource>(match.Groups[1].Value, true);
                    hitDamageType = Enums.Parse<DamageType>(match.Groups[3].Value, true);
                    if (match.Groups[2].Value == "minimum")
                        hitDamageMinimum = stat.Value;
                    else
                        hitDamageMaximum = stat.Value;
                    continue;
                }
                match = SkillStatIds.DamageOverTimeRegex.Match(stat.StatId);
                if (match.Success)
                {
                    var type = Enums.Parse<DamageType>(match.Groups[1].Value, true);
                    var statBuilder = _builderFactories.DamageTypeBuilders.From(type).Damage
                        .WithSkills(DamageSource.OverTime);
                    modifiers.Add(Modifier(statBuilder,
                        Forms.BaseSet, stat.Value / 60D, isMainSkill));
                    continue;
                }
                if (stat.StatId == "base_skill_number_of_additional_hits")
                {
                    modifiers.Add(Modifier(_metaStatBuilders.SkillNumberOfHitsPerCast,
                        Forms.BaseAdd, CreateValue(stat.Value), isMainSkill));
                    continue;
                }
                if (stat.StatId == "skill_double_hits_when_dual_wielding")
                {
                    modifiers.Add(Modifier(_metaStatBuilders.SkillDoubleHitsWhenDualWielding,
                        Forms.TotalOverride, CreateValue(stat.Value), isMainSkill));
                    continue;
                }
                unparsedStats.Add(stat);
            }
            if (hitDamageMaximum.HasValue)
            {
                var statBuilder = _builderFactories.DamageTypeBuilders.From(hitDamageType.Value).Damage
                    .WithSkills(hitDamageSource.Value);
                var valueBuilder = _builderFactories.ValueBuilders.FromMinAndMax(CreateValue(hitDamageMinimum),
                    CreateValue(hitDamageMaximum.Value));
                modifiers.Add(Modifier(statBuilder, Forms.BaseSet, valueBuilder, isMainSkill));
            }
            return (modifiers, unparsedStats);
        }

        private ParseResult ApplyCondition(ParseResult result, IValue conditionalValue)
        {
            return result
                .ApplyToModifiers(m => new Modifier(m.Stats, m.Form, ApplyCondition(m.Value), m.Source));

            IValue ApplyCondition(IValue value)
                => new FunctionalValue(c => conditionalValue.Calculate(c).IsTrue() ? value.Calculate(c) : null,
                    $"{conditionalValue}.IsTrue ? {value} : null");
        }

        private IIntermediateModifier Modifier(
            IStatBuilder stat, IFormBuilder form, double value, IConditionBuilder condition)
            => Modifier(stat, form, CreateValue(value), condition);

        private IIntermediateModifier Modifier(
            IStatBuilder stat, IFormBuilder form, IValueBuilder value, IConditionBuilder condition)
            => _modifierBuilder.WithStat(stat).WithForm(form).WithValue(value).WithCondition(condition).Build();

        private IIntermediateModifier Modifier(IStatBuilder stat, IFormBuilder form, double value)
            => Modifier(stat, form, CreateValue(value));

        private IIntermediateModifier Modifier(IStatBuilder stat, IFormBuilder form, IValueBuilder value)
            => _modifierBuilder.WithStat(stat).WithForm(form).WithValue(value).Build();

        private IFormBuilders Forms => _builderFactories.FormBuilders;
        private IValueBuilder CreateValue(double value) => _builderFactories.ValueBuilders.Create(value);

        private IEquipmentBuilder MainHand => Equipment[ItemSlot.MainHand];
        private IEquipmentBuilder OffHand => Equipment[ItemSlot.OffHand];
        private IEquipmentBuilderCollection Equipment => _builderFactories.EquipmentBuilders.Equipment;
    }

    public struct Skill
    {
        public Skill(string id, int level, int quality, ItemSlot itemSlot, int socketIndex, int? gemGroup)
            => (Id, Level, Quality, ItemSlot, SocketIndex, GemGroup) =
                (id, level, quality, itemSlot, socketIndex, gemGroup);

        public string Id { get; }
        public int Level { get; }
        public int Quality { get; }

        public ItemSlot ItemSlot { get; }
        public int SocketIndex { get; }

        // Null: item inherent skill
        public int? GemGroup { get; }
    }
}