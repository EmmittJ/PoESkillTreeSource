﻿using System;
using System.Collections.Generic;
using System.Linq;
using PoESkillTree.Computation.Builders.Values;
using PoESkillTree.Computation.Common;
using PoESkillTree.Computation.Common.Builders;
using PoESkillTree.Computation.Common.Builders.Stats;
using PoESkillTree.Computation.Common.Builders.Values;
using PoESkillTree.Computation.Common.Parsing;
using PoESkillTree.GameModel.PassiveTree;

namespace PoESkillTree.Computation.Builders.Stats
{
    public class PassiveTreeBuilders : StatBuildersBase, IPassiveTreeBuilders
    {
        private readonly PassiveTreeDefinition _tree;

        public PassiveTreeBuilders(IStatFactory statFactory, PassiveTreeDefinition tree) : base(statFactory)
        {
            _tree = tree;
        }

        public IStatBuilder NodeSkilled(ushort nodeId) => FromIdentity($"{nodeId}.Skilled", typeof(bool));

        public ValueBuilder TotalInModifierSourceJewelRadius(IStatBuilder stat)
            => new ValueBuilder(new ValueBuilderImpl(
                ps => BuildInModifierSourceJewelRadiusValue(ps, stat, _ => new Constant(true)),
                c => TotalInModifierSourceJewelRadius(stat.Resolve(c))));

        public ValueBuilder AllocatedInModifierSourceJewelRadius(IStatBuilder stat)
            => new ValueBuilder(new ValueBuilderImpl(
                ps => BuildInModifierSourceJewelRadiusValue(ps, stat, v => v),
                c => AllocatedInModifierSourceJewelRadius(stat.Resolve(c))));

        public ValueBuilder UnallocatedInModifierSourceJewelRadius(IStatBuilder stat)
            => new ValueBuilder(new ValueBuilderImpl(
                ps => BuildInModifierSourceJewelRadiusValue(ps, stat,
                    v => new ConditionalValue(c => !v.Calculate(c).IsTrue(), $"!{v}")),
                c => UnallocatedInModifierSourceJewelRadius(stat.Resolve(c))));

        private IValue BuildInModifierSourceJewelRadiusValue(
            BuildParameters parameters, IStatBuilder stat, Func<IValue, IValue> condition)
        {
            return GetNodesInRadius(parameters)
                .Select(GetValue)
                .Aggregate((l, r) => l + r)
                .Build(parameters);

            ValueBuilder GetValue(PassiveNodeDefinition d)
                => stat.AsPassiveNodePropertyFor(d.Id).Value
                    .If(condition(NodeSkilled(d.Id).Value.Build(parameters)));
        }

        public IStatBuilder MultipliedAttributeForNodesInModifierSourceJewelRadius(
            IStatBuilder sourceAttribute, IStatBuilder targetAttribute)
            => new StatBuilder(StatFactory,
                new MultipliedAttributeForEachNodeInRadiusStatBuilder(GetNodesInRadius, sourceAttribute, targetAttribute));

        private IEnumerable<PassiveNodeDefinition> GetNodesInRadius(BuildParameters parameters)
        {
            var modifierSource = GetJewelSource(parameters);
            return _tree.GetNodesInRadius(modifierSource.PassiveNodeId, modifierSource.Radius);
        }

        private static ModifierSource.Local.Jewel GetJewelSource(BuildParameters parameters)
        {
            var modifierSource = parameters.ModifierSource;
            if (modifierSource is ModifierSource.Global globalSource)
                modifierSource = globalSource.LocalSource;
            if (modifierSource is ModifierSource.Local.Jewel jewelSource)
                return jewelSource;
            throw new ParseException(
                "IPassiveTreeBuilders.*InModifierSourceJewelRadius can only be used with a source of type ModifierSource.Local.Jewel");
        }
    }
}