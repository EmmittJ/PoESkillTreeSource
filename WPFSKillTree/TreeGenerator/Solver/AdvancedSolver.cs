﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using POESKillTree.SkillTreeFiles;
using POESKillTree.TreeGenerator.Algorithm.Model;
using POESKillTree.TreeGenerator.Genetic;
using POESKillTree.TreeGenerator.Model.PseudoAttributes;
using POESKillTree.TreeGenerator.Settings;

namespace POESKillTree.TreeGenerator.Solver
{
#if (PoESkillTree_UseSmallDec_ForAttributes)
    using CSharpGlobalCode.GlobalCode_ExperimentalCode;
#endif
    using SmallDigit =
#if (PoESkillTree_UseSmallDec_ForAttributes)
    SmallDec;
#else
    System.Single;
#endif
    using CSVScoreType =
#if (PoESkillTree_EnableAlternativeCSV&&PoESkillTree_UseSmallDec_ForAttributes)
    SmallDec;
#else
    System.Double;
#endif

    /// <summary>
    /// Implementation of AbstractGeneticSolver that tries to find optimal trees based on constraints.
    /// </summary>
    public class AdvancedSolver : AbstractGeneticSolver<AdvancedSolverSettings>
    {
        /// <summary>
        /// PseudoAttributeConstraint data object where the PseudoAttribute is converted into the
        /// applicable attributes and their conversion multiplier.
        /// </summary>
        private class ConvertedPseudoAttributeConstraint
        {
            /// <summary>
            /// </summary>
            public List<Tuple<string, SmallDigit>> Attributes { get; private set; }

            /// <summary>
            /// </summary>
            public Tuple<SmallDigit, double
#if (PoESkillTree_EnableMinimumValue)
            , SmallDigit
#endif
            > TargetWeightTuple
            { get; private set; }

            public ConvertedPseudoAttributeConstraint(List<Tuple<string, SmallDigit>> attributes, Tuple<SmallDigit, double
#if (PoESkillTree_EnableMinimumValue)
            , SmallDigit
#endif
            > tuple)
            {
                Attributes = attributes;
                TargetWeightTuple = tuple;
            }
        }

        /// <summary>
        /// Regex to search for Wildcards of the form '{number}' in attribute names.
        /// </summary>
        private static readonly Regex ContainsWildcardRegex = new Regex(@"{\d+}");

        /// <summary>
        /// Regex to test if an attribute is +# to Str/Int/Dex and the node can be a travel node.
        /// </summary>
        private static readonly Regex TravelNodeRegex = new Regex(@"\+# to (Strength|Intelligence|Dexterity)");

        // It doesn't gain anything from larger populations and more generations. Running more
        // generations has the upside that it doesn't take much longer because the GA reaches a point
        // where not many new DNAs are generated and it can use the cache nearly always.
        protected override int Generations
        {
            get { return 200; }
        }

        private const double PopMultiplier = 7;

        // Between 3 and 5 seems to be the optimal point. Anything higher or lower is worse. This is
        // assuming it is not problem specific.
        /// <summary>
        /// How many consecutive genes can be flipped at most by mutation. Utilizes the fact that
        /// consecutive genes mostly are node clusters that are taken together.
        /// </summary>
        private const int MaxMutateClusterSize = 4;

        /// <summary>
        /// Weight of the CSV of the used node count if it is higher than the allowed node count.
        /// </summary>
        private const double UsedNodeCountWeight = 5;

        /// <summary>
        /// Factor for the value calculated from the node difference if used node count is lower than the allowed node count.
        /// </summary>
        /// <remarks>
        /// A tree with less points spent should only better better if the csv satisfaction is not worse.
        /// Because of that this factor is really small.
        ///</remarks>
        private const double UsedNodeCountFactor = .0005;

        /// <summary>
        /// Factor by which weights get multiplied in the CSV calculation.
        /// </summary>
        private const double CsvWeightMultiplier = 10;

        /// <summary>
        /// Maps indexes of constraints (both attribute and pseudo attribute constraints) to their
        /// {Target, Weight}-Tuple.
        /// </summary>
        private Tuple<SmallDigit, double
#if (PoESkillTree_EnableMinimumValue)
        , SmallDigit
#endif
        >[] _attrConstraints;

#if (PoESkillTree_EnableAlternativeCSV)

        /// <summary>
        /// Names of the individual Attribute Constraints (use later to save/display the target
        /// pseudo-attribute totals for targets with minimum value)
        /// </summary>
        public List<string> _ConstraintNames;

#endif

        /// <summary>
        /// Dictionary that maps attribute names to the constraint numbers they apply to (as indexes
        /// of _attrConstraints).
        /// </summary>
        private Dictionary<string, List<int>> _attrNameLookup;

        /// <summary>
        /// Dictionary that maps attribute names and numbers (as indexes of _attrConstraints) to
        /// their conversion multipliers that gets applied when they are calculated.
        /// </summary>
        private Dictionary<Tuple<string, int>, SmallDigit> _attrConversionMultipliers;

        /// <summary>
        /// Pseudo-Dictionary that maps node ids to a list of their attributes as a pair of the
        /// constraint number and the value. Fits all possible ushorts (node ids) and is pretty
        /// sparse. Not contained ids have null as value.
        /// </summary>
        private List<Tuple<int, SmallDigit>>[] _nodeAttributes;

        /// <summary>
        /// Dictionary that saves which nodes (represented by their id) are travel nodes.
        /// </summary>
        private Dictionary<ushort, bool> _areTravelNodes;

        /// <summary>
        /// Contains all SkillNode-Ids contained in <see cref="AbstractSolver{TS}.TargetNodes"/>
        /// (contents from <see cref="GraphNode.Nodes"/>).
        /// </summary>
        private List<ushort> _fixedNodes;

        /// <summary>
        /// Array of the values for each attribute independent of the current calculated skill tree.
        /// </summary>
        private SmallDigit[] _fixedAttributes;

        protected override GeneticAlgorithmParameters GaParameters
        {
            get
            {
                return new GeneticAlgorithmParameters(
                    (int)(PopMultiplier * SearchSpace.Count),
                    SearchSpace.Count,
                    maxMutateClusterSize: MaxMutateClusterSize);
            }
        }

        /// <summary>
        /// Creates a new, uninitialized instance.
        /// </summary>
        /// <param name="tree">The (not null) skill tree in which to optimize.</param>
        /// <param name="settings">
        /// The (not null) settings that describe what the solver should do.
        /// </param>
        public AdvancedSolver(SkillTree tree, AdvancedSolverSettings settings)
            : base(tree, settings)
        {
            FinalHillClimbEnabled = true;
        }

        public override void Initialize()
        {
            // Evaluate the pseudo constraints into attribute lists.
            var convertedPseudos = EvalPseudoAttrConstraints();
            // Assign a number to each attribute and pseudo attribute constraint and link their names
            // to these numbers.
            FormalizeConstraints(Settings.AttributeConstraints, convertedPseudos);
            // Extract attributes from nodes and set travel nodes.
            ExtractNodeAttributes();

            base.Initialize();
        }

        /// <summary>
        /// Assigns a number to each attribute and pseudo attribute constraint, saves their weights
        /// and target values into _attrConstraints, saves the numbers for each name into
        /// _attrNameLookup and saves the conversion multipliers into _attrConversionMultipliers.
        /// </summary>
        /// <param name="attrConstraints">todo: describe attrConstraints parameter on FormalizeConstraints</param>
        /// <param name="pseudoConstraints">todo: describe pseudoConstraints parameter on FormalizeConstraints</param>
        private void FormalizeConstraints(Dictionary<string, Tuple<SmallDigit, double
#if (PoESkillTree_EnableMinimumValue)
        , SmallDigit
#endif
        >> attrConstraints, IReadOnlyCollection<ConvertedPseudoAttributeConstraint> pseudoConstraints)
        {
            //Slightly reduce number of calls needed by only calculating once (increase in speed more important than slight temporary increase of one byte)
            int TotalConstraintCount = attrConstraints.Count + pseudoConstraints.Count;
            _attrConstraints = new Tuple<SmallDigit, double
#if (PoESkillTree_EnableMinimumValue)
            , SmallDigit
#endif
            >[TotalConstraintCount];
            _attrNameLookup = new Dictionary<string, List<int>>(attrConstraints.Count);
            _attrConversionMultipliers = new Dictionary<Tuple<string, int>, SmallDigit>(attrConstraints.Count);
            _fixedAttributes = new SmallDigit[TotalConstraintCount];
#if (PoESkillTree_EnableAlternativeCSV)
            _ConstraintNames = new List<string>(TotalConstraintCount);
#endif
            var i = 0;
            foreach (var kvPair in attrConstraints)
            {
                _attrConstraints[i] = kvPair.Value;
                _attrNameLookup[kvPair.Key] = new List<int> { i };
                _attrConversionMultipliers[Tuple.Create(kvPair.Key, i)] = 1;
#if (PoESkillTree_EnableAlternativeCSV)
                _ConstraintNames.Add(kvPair.Key);
#endif
                i++;
            }
            foreach (var pseudo in pseudoConstraints)
            {
                _attrConstraints[i] = pseudo.TargetWeightTuple;
                foreach (var tuple in pseudo.Attributes)
                {
                    if (_attrNameLookup.ContainsKey(tuple.Item1))
                    {
                        _attrNameLookup[tuple.Item1].Add(i);
                    }
                    else
                    {
                        _attrNameLookup[tuple.Item1] = new List<int> { i };
                    }
                    _attrConversionMultipliers[Tuple.Create(tuple.Item1, i)] = tuple.Item2;
                }
                i++;
            }
        }

        protected override bool MustIncludeNodeGroup(SkillNode node)
        {
            // If the node has stats and is not a travel node, the group is included.
            return _nodeAttributes[node.Id].Count > 0 && !_areTravelNodes[node.Id];
        }

        protected override bool IncludeNodeInSearchGraph(SkillNode node)
        {
            // Keystones can only be included if they are check-tagged.
            return node.Type != NodeType.Keystone;
        }

        /// <summary>
        /// Extracts attributes from the skill tree nodes and fills _nodeAttributes and _areTravelNodes.
        /// </summary>
        private void ExtractNodeAttributes()
        {
            var skillNodes = SkillTree.Skillnodes;
            _nodeAttributes = new List<Tuple<int, SmallDigit>>[ushort.MaxValue];
            _areTravelNodes = new Dictionary<ushort, bool>(skillNodes.Count);
            foreach (var node in skillNodes)
            {
                var id = node.Key;
                var skillNode = node.Value;

                // Remove attributes that have no constraints. Replace attributes that have
                // constraints with a tuple of their number and the value. For attributes with more
                // than one value, the first one is selected, that is reasonable for the attributes
                // the skill tree currently has. Attributes without value are not supported. If a
                // constraint referencing an attribute without value slips through, it will break.
                _nodeAttributes[id] =
                    (from attr in SkillTree.ExpandHybridAttributes(skillNode.Attributes)
                     where _attrNameLookup.ContainsKey(attr.Key)
                     from constraint in _attrNameLookup[attr.Key]
                     let value = attr.Value[0] * _attrConversionMultipliers[Tuple.Create(attr.Key, constraint)]
                     select Tuple.Create(constraint, value))
                    .ToList();

                // Set if the node is a travel node.
                if (skillNode.Attributes.Count == 1 && TravelNodeRegex.IsMatch(skillNode.Attributes.Keys.First())
                    && skillNode.Attributes.Values.First().Any(v => (int)v == 10))
                {
                    _areTravelNodes[id] = true;
                }
                else
                {
                    _areTravelNodes[id] = false;
                }
            }
        }

        /// <summary>
        /// Evaluates <see cref="AdvancedSolverSettings.PseudoAttributeConstraints"/> and converts
        /// each PseudoAttribute into a list of the attribute names that evaluated to true and their
        /// conversion multiplier.
        /// </summary>
        private List<ConvertedPseudoAttributeConstraint> EvalPseudoAttrConstraints()
        {
            var keystones = from node in Settings.Checked
                            where node.Type == NodeType.Keystone
                            select node.Name;
            var conditionSettings = new ConditionSettings(Settings.Tags, Settings.OffHand, keystones.ToArray(), Settings.WeaponClass);

            var resolvedWildcardNames = new Dictionary<string, List<Tuple<string, string[]>>>();
            var convertedPseudos = new List<ConvertedPseudoAttributeConstraint>(Settings.PseudoAttributeConstraints.Count);

            foreach (var pair in Settings.PseudoAttributeConstraints)
            {
                var convAttrs = new List<Tuple<string, SmallDigit>>(pair.Key.Attributes.Count);
                foreach (var attr in pair.Key.Attributes)
                {
                    var name = attr.Name;
                    if (ContainsWildcardRegex.IsMatch(name))
                    {
                        // Wildcards are resolved by searching the skill tree attributes for each
                        // attribute that matches the attribute name ('{number}' replaced by '(.*)'
                        // for matching) and evaluating the attribute for each of those replacements.
                        if (!resolvedWildcardNames.ContainsKey(name))
                        {
                            var searchRegex = new Regex("^" + ContainsWildcardRegex.Replace(name, "(.*)") + "$");
                            resolvedWildcardNames[name] = (from a in SkillTree.AllAttributes
                                                           let match = searchRegex.Match(a)
                                                           where match.Success
                                                           select Tuple.Create(a, ExtractGroupValuesFromGroupCollection(match.Groups))).ToList();
                        }
                        convAttrs.AddRange(from replacement in resolvedWildcardNames[name]
                                           where attr.Evaluate(conditionSettings, replacement.Item2)
                                           select Tuple.Create(replacement.Item1, attr.ConversionMultiplier));
                    }
                    else if (attr.Evaluate(conditionSettings))
                    {
                        convAttrs.Add(Tuple.Create(name, attr.ConversionMultiplier));
                    }
                }

                var convPseudo = new ConvertedPseudoAttributeConstraint(convAttrs, pair.Value);
                convertedPseudos.Add(convPseudo);
            }

            return convertedPseudos;
        }

        /// <summary>
        /// Returns an Array of all captured substrings from the given GroupCollection except the
        /// first (which is the whole matched string).
        /// </summary>
        /// <param name="groups">todo: describe groups parameter on ExtractGroupValuesFromGroupCollection</param>
        private static string[] ExtractGroupValuesFromGroupCollection(GroupCollection groups)
        {
            var result = new string[groups.Count - 1];
            for (var i = 1; i < groups.Count; i++)
            {
                result[i - 1] = groups[i].Value;
            }
            return result;
        }

        protected override bool IsVariableTargetNode(GraphNode node)
        {
            // Nodes that have relevant attributes and are not travel nodes (+10 int/str/dex) can be
            // selected as target nodes.
            return _nodeAttributes[node.Id].Count > 0 && !_areTravelNodes[node.Id];
        }

        protected override void OnFinalSearchSpaceCreated()
        {
            // Merge attributes for nodes that were merged. Combine duplicate attributes per node.
            foreach (var node in AllNodes.Select(n => n.Id).Distinct())
            {
                // This node is contained in another, it will be removed from _nodeAttributes when
                // its parent is processed.
                if (NodeExpansionDictionary[node] == null) continue;

                var dict = new Dictionary<int, SmallDigit>();
                foreach (var containedNode in NodeExpansionDictionary[node])
                {
                    foreach (var tuple in _nodeAttributes[containedNode])
                    {
                        if (!dict.ContainsKey(tuple.Item1))
                        {
                            dict.Add(tuple.Item1, tuple.Item2);
                        }
                        else
                        {
                            dict[tuple.Item1] += tuple.Item2;
                        }
                    }
                    _nodeAttributes[containedNode] = null;
                }
                _nodeAttributes[node] = dict.Select(p => Tuple.Create(p.Key, p.Value)).ToList();
            }

            // Set fixed attributes from target nodes and Settings.InitialAttributes
            CreateFixedAttributes();
        }

        /// <summary>
        /// Sets the fixed attribute values from <see cref="AbstractSolver{T}.TargetNodes"/> and from
        /// <see cref="AdvancedSolverSettings.InitialAttributes"/>.
        /// </summary>
        private void CreateFixedAttributes()
        {
            _fixedNodes = TargetNodes.Select(n => n.Id).ToList();
            // Set start stats from start and target nodes.
            AddAttributes(_fixedNodes, _fixedAttributes);
            // Add the initial stats from the settings.
            foreach (var initialStat in Settings.InitialAttributes)
            {
                var name = initialStat.Key;
                if (_attrNameLookup.ContainsKey(name))
                {
                    foreach (var i in _attrNameLookup[name])
                    {
                        _fixedAttributes[i] += initialStat.Value * _attrConversionMultipliers[Tuple.Create(name, i)];
                    }
                }
            }
        }

        /// <summary>
        /// Adds all attributes of the given node ids to the given list.
        /// </summary>
        /// <param name="ids">todo: describe ids parameter on AddAttributes</param>
        /// <param name="to">todo: describe to parameter on AddAttributes</param>
        private void AddAttributes(IEnumerable<ushort> ids, IList<SmallDigit> to)
        {
            foreach (var id in ids)
            {
                foreach (var tuple in _nodeAttributes[id])
                {
                    to[tuple.Item1] += tuple.Item2;
                }
            }
        }

        protected override CSVScoreType FitnessFunction(HashSet<ushort> skilledNodes)
        {
            // Add stats of the MST-nodes and start stats.
            var totalStats = (SmallDigit[])_fixedAttributes.Clone();
            // Don't count the character start node.
            var usedNodeCount = skilledNodes.Select(n => NodeExpansionDictionary[n].Count).Sum() - UncountedNodes;
            var totalPoints = Settings.TotalPoints;
            skilledNodes.ExceptWith(_fixedNodes);
            AddAttributes(skilledNodes, totalStats);
            //Give zero point trees worst score
            if (usedNodeCount == 0)
            {
#if (PoESkillTree_UseSmallDec_ForAttributes)
                return SmallDec.Zero;
#else
                return 0.0;
#endif
            }


            ///<summary>
            /// Calculate constraint value for each stat and multiply them. (High score = better skilltree)
            ///</summary>
            CSVScoreType csvs
#if (PoESkillTree_UseSmallDec_ForAttributes)
            = SmallDec.Zero;
#elif (PoESkillTree_UseSmallDec_ForAttributes && POESkillTree_ScoreMethodV1)
            = 1.0;
#else
            = 0.0;
#endif

#if (PoESkillTree_EnableAlternativeCSV)
            var Multiplier
#if (PoESkillTree_UseSmallDec_ForAttributes)
            = SmallDec.Zero;
#else
            = 0.0;
#endif
#if (!POESkillTree_ScoreMethodV1)
            //Variables for alternative Scoring method
            ushort NumberOfAttributes = 0;
            ushort NumberOfPenalties = 0;
            //#if (DEBUG)
            //                Console.WriteLine("CVSV Info for Generator:");
            //#endif
#endif
#endif
            for (var i = 0; i < _attrConstraints.Length; i++)
            {
                var AttributeTargets = _attrConstraints[i];
#if (!PoESkillTree_EnableAlternativeCSV)
                csvs *= CalcCsv(totalStats[i], AttributeTargets.Item2, AttributeTargets.Item1);
#else
                var CurrentStatValue = totalStats[i];
#if (!PoESkillTree_EnableAlternativeCSV || POESkillTree_ScoreMethodV1)
                //If current stat is better than target value give slight bonus CSV Score value
                if (CurrentStatValue > AttributeTargets.Item1)
                {
                    Multiplier = CurrentStatValue / AttributeTargets.Item1;
                    Multiplier -= 1;
                    Multiplier /= 20;
                    Multiplier += 1;
                    CurrentStatValue =
#if (!PoESkillTree_UseSmallDec_ForAttributes)
                    (float)
#endif
                    Multiplier * AttributeTargets.Item1;
                    Multiplier =
#if (PoESkillTree_UseSmallDec_ForAttributes)
                    SmallDec.Exp(AttributeTargets.Item2 * CsvWeightMultiplier * (CurrentStatValue / AttributeTargets.Item1)) / SmallDec.Exp(AttributeTargets.Item2 * CsvWeightMultiplier);
#else
                    Math.Exp(AttributeTargets.Item2 * CsvWeightMultiplier * (CurrentStatValue / AttributeTargets.Item1)) / Math.Exp(AttributeTargets.Item2 * CsvWeightMultiplier);
#endif
                    csvs *= Multiplier;
                }
#if (PoESkillTree_EnableMinimumValue)
                else if (AttributeTargets.Item3 > 0)
                {
                    //if less than Minimum value reached give score value 4th of the effect to cvsv
                    Multiplier = CalcCsv(CurrentStatValue, AttributeTargets.Item2, AttributeTargets.Item1);

                    csvs *= Multiplier;
                }
#endif
                else
                {
                    Multiplier = CalcCsv(CurrentStatValue, AttributeTargets.Item2, AttributeTargets.Item1);
                    csvs *= Multiplier;
                }
#else
                //Alternative Scoring Method
                ++NumberOfAttributes;
                if (CurrentStatValue > AttributeTargets.Item1)
                {
                    Multiplier = CurrentStatValue / AttributeTargets.Item1;
                    Multiplier -= 1;
                    Multiplier /= 20;
                    Multiplier += 1;
                    csvs += CurrentStatValue * Multiplier;
                }
#if (PoESkillTree_EnableMinimumValue)
                else if (AttributeTargets.Item3 > 0)
                {
                    ++NumberOfPenalties;
                    csvs += CurrentStatValue/2;
                }
#endif
                else
                {
                    csvs += CurrentStatValue;
                }
                //#if (DEBUG)
                //                Console.WriteLine("AttributeName:" + _ConstraintNames[i]);
                //                Console.WriteLine("Current Value:" + CurrentStatValue.ToString());
                //                Console.WriteLine("Target Value:" + AttributeTargets.Item1.ToString());
                //#if (PoESkillTree_EnableMinimumValue)
                //                Console.WriteLine("Minimum Value to Target: " + AttributeTargets.Item3.ToString());
                //#endif
                //#endif
#endif
#endif
            }

#if (!PoESkillTree_EnableAlternativeCSV || POESkillTree_ScoreMethodV1)
            // Total points spent is another csv.
            if (usedNodeCount > totalPoints)
            {
                // If UsedNodeCount is higher than Settings.TotalPoints, it is calculated as a csv
                // with a weight of 5. (and lower = better)
                csvs *= CalcCsv(2 * totalPoints - usedNodeCount, UsedNodeCountWeight, totalPoints);
            }
            else if (usedNodeCount < totalPoints)
            {
                // If it is lower, apply it as a logarithmic factor.
                csvs *= 1 + UsedNodeCountFactor * Math.Log(totalPoints + 1 - usedNodeCount);
            }

            //#if (PoESkillTree_EnableAlternativeCSV && DEBUG)
            //            Console.WriteLine("Generated fitness CSVScore:" + csvs.ToString());
            //#endif
#else
            //Alternative Scoring Method
            if (NumberOfAttributes==0)
            {
#if (PoESkillTree_UseSmallDec_ForAttributes)
                return SmallDec.Zero;
#else
                return 0.0;
#endif
            }
            csvs /= NumberOfAttributes;
            if (usedNodeCount > totalPoints)
            {
                Multiplier = CalcCsv(2 * totalPoints - usedNodeCount, UsedNodeCountWeight, totalPoints);
#if (DEBUG)
                Console.WriteLine("CSV Score Multipier of " + Multiplier + " applied for using more nodes than total points target (used " + usedNodeCount + "/" + totalPoints + " nodes).");
#endif
                csvs *= Multiplier;
            }
            else
            {
                Multiplier = 1 + UsedNodeCountFactor * Math.Log(totalPoints + 1 - usedNodeCount);
#if (DEBUG)
                Console.WriteLine("CSV Score Multipier of " + Multiplier + " applied for using less nodes than total points target (used " + usedNodeCount + "/" + totalPoints + " nodes).");
#endif
                csvs *= Multiplier;
            }
            if(NumberOfPenalties>0)
            {
                Multiplier = NumberOfPenalties;
                Multiplier /= NumberOfAttributes;
                Multiplier = 1 - Multiplier;
#if (DEBUG)
                Console.WriteLine("CSV Score Multipier of " + Multiplier + " applied for failing to reach minimum values for "+ NumberOfPenalties + " target values." );
#endif
            }
#endif


            // Make sure the fitness is not < 0 (can't happen with the current implementation anyway).
            return Math.Max(csvs, 0);
        }

#if (PoESkillTree_EnableAlternativeCSV && PoESkillTree_UseSmallDec_ForAttributes)
        private static SmallDec CalcCsv(SmallDec CurrentValue, SmallDec weight, SmallDec target)
        {
            CurrentValue = SmallDec.Min(CurrentValue, target);
            return SmallDec.Exp(weight * CsvWeightMultiplier * (CurrentValue / target)) / SmallDec.Exp(weight * CsvWeightMultiplier);
        }

#else

        private static double CalcCsv(SmallDigit CurrentValue, double weight, SmallDigit target)
        {
            // Don't go higher than the target value.
#if (PoESkillTree_UseSmallDec_ForAttributes)
            CurrentValue = SmallDec.Min(CurrentValue, target);
            return Math.Exp(weight * CsvWeightMultiplier * (double)(CurrentValue / target)) / Math.Exp(weight * CsvWeightMultiplier);
#else
            CurrentValue = Math.Min(CurrentValue, target);
            return Math.Exp(weight * CsvWeightMultiplier * (CurrentValue / target)) / Math.Exp(weight * CsvWeightMultiplier);
#endif
        }

#endif
    }
}