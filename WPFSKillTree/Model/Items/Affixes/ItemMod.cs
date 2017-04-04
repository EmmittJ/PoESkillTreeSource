﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using POESKillTree.Model.Items.Enums;

namespace POESKillTree.Model.Items.Affixes
{
    using CSharpGlobalCode.GlobalCode_ExperimentalCode;
    public class ItemMod
    {
        public enum ValueColoring
        {
            White = 0,
            LocallyAffected = 1,

            Fire = 4,
            Cold = 5,
            Lightning = 6,
            Chaos = 7
        }


        public ItemModTier ParentTier { get; }

        public string Attribute { get; }

#if (PoESkillTree_UseSmallDec_ForAttributes && PoESkillTree_UseSmallDec_ForGeneratorBars)
        public List<SmallDec> Value { get; set; }
#else
        public List<float> Value { get; set; }
#endif

        public List<ValueColoring> ValueColor { get; set; }

        public bool IsLocal { get; }

        public ItemMod(ItemType itemType, string attribute, Regex numberfilter, IEnumerable<ValueColoring> valueColor = null)
        {
            Value = (from Match match in numberfilter.Matches(attribute)
#if (PoESkillTree_UseSmallDec_ForAttributes && PoESkillTree_UseSmallDec_ForGeneratorBars)
                     select SmallDec.Parse(match.Value, CultureInfo.InvariantCulture))
#else
                     select float.Parse(match.Value, CultureInfo.InvariantCulture))
#endif
                     .ToList();
            Attribute = numberfilter.Replace(attribute, "#");
            IsLocal = DetermineLocal(itemType, Attribute);
            ValueColor = valueColor == null ? new List<ValueColoring>() : new List<ValueColoring>(valueColor);
        }

        public ItemMod(ItemType itemType, string attribute, ItemModTier parentTier = null)
        {
            IsLocal = DetermineLocal(itemType, attribute);
            Attribute = attribute;
            ParentTier = parentTier;
#if (PoESkillTree_UseSmallDec_ForAttributes && PoESkillTree_UseSmallDec_ForGeneratorBars)
            Value = new List<SmallDec>();
#else
            Value = new List<float>();
#endif
            ValueColor = new List<ValueColoring>();
        }

        private ItemMod(ItemMod other)
        {
            IsLocal = other.IsLocal;
            Attribute = other.Attribute;
            ParentTier = other.ParentTier;
            ValueColor = other.ValueColor.ToList();
        }

        // Returns true if property/mod is local, false otherwise.
        private static bool DetermineLocal(ItemType itemType, string attr)
        {
            if (attr == "#% reduced Attribute Requirements"
                || attr.Contains("+# to Level of Socketed "))
                return true;
            var group = itemType.Group();
            // Chance to Block is only local on shields.
            if (attr == "+#% Chance to Block")
                return group == ItemGroup.Shield;
            switch (group)
            {
                case ItemGroup.Amulet:
                case ItemGroup.Ring:
                case ItemGroup.Belt:
                case ItemGroup.Unknown:
                case ItemGroup.Quiver:
                case ItemGroup.Jewel:
                case ItemGroup.Gem:
                    // These item types have no local mods.
                    return false;
                case ItemGroup.OneHandedWeapon:
                case ItemGroup.TwoHandedWeapon:
                    return attr == "#% increased Attack Speed"
                           || attr == "#% increased Accuracy Rating"
                           || attr == "+# to Accuracy Rating"
                           || attr.StartsWith("Adds ") && (attr.EndsWith(" Damage") || attr.EndsWith(" Damage in Main Hand") || attr.EndsWith(" Damage in Off Hand"))
                           || attr == "#% increased Physical Damage"
                           || attr == "#% increased Critical Strike Chance"
                           || attr.Contains("Damage Leeched as")
                           || attr.Contains("Critical Strike Chance with this Weapon")
                           || attr.Contains("Critical Strike Damage Multiplier with this Weapon");
                case ItemGroup.Shield:
                case ItemGroup.BodyArmour:
                case ItemGroup.Boots:
                case ItemGroup.Gloves:
                case ItemGroup.Helmet:
                    return (attr.Contains("Armour") && !attr.EndsWith("Armour against Projectiles"))
                        || attr.Contains("Evasion Rating")
                        || (attr.Contains("Energy Shield") && !attr.EndsWith("Energy Shield Recharge"));
                default:
                    throw new NotSupportedException("Someone forgot to add a switch case.");
            }
        }

        public ItemMod Sum(ItemMod m)
        {
            return new ItemMod(m)
            {
                Value = Value.Zip(m.Value, (f1, f2) => f1 + f2).ToList()
            };
        }

        private string InsertValues(string into, ref int index)
        {
            var indexCopy = index;
            var result = Regex.Replace(into, "#", m => Value[indexCopy++].ToString("###0.##", CultureInfo.InvariantCulture));
            index = indexCopy;
            return result;
        }

        private JArray[] ValueTokensToJArrays(IEnumerable<string> tokens)
        {
            var valueIndex = 0;
            return tokens.Select(t => new JArray(InsertValues(t, ref valueIndex), ValueColor[valueIndex - 1])).ToArray();
        }

        public JToken ToJobject(bool asMod = false)
        {
            if (asMod)
            {
                var index = 0;
                return new JValue(InsertValues(Attribute, ref index));
            }

            const string allowedTokens = @"(#|#%|\+#%|#-#|#/#)";
            string name;
            var tokens = new List<string>();
            int displayMode;
            if (Value == null || Value.Count == 0)
            {
                name = Attribute;
                displayMode = 0;
            }
            else if (Regex.IsMatch(Attribute, @"^[^#]*: (" + allowedTokens + @"(, |$))+"))
            {
                // displayMode 0 is for the form `Attribute = name + ": " + values.Join(", ")`
                name = Regex.Replace(Attribute, @"(: |, )" + allowedTokens + @"(?=, |$)", m =>
                {
                    tokens.Add(m.Value.TrimStart(',', ':', ' '));
                    return "";
                });
                displayMode = 0;
            }
            else
            {
                // displayMode 3 is for the form `Attribute = name.Replace("%i" with values[i])`
                var matchIndex = 0;
                name = Regex.Replace(Attribute, @"(?<=^|\s)" + allowedTokens + @"(?=$|\s|,)", m =>
                {
                    tokens.Add(m.Value);
                    return "%" + matchIndex++;
                });
                displayMode = 3;
            }
            return new JObject
            {
                {"name", name}, {"values", new JArray(ValueTokensToJArrays(tokens))}, {"displayMode", displayMode}
            };
        }
    }

    /// <summary>
    /// Extension methods for <see cref="IEnumerable{T}"/>s of <see cref="ItemMod"/>s.
    /// </summary>
    public static class ItemModExtensions
    {
        /// <summary>
        /// Returns the value at index <paramref name="valueIndex"/> of the first ItemMod in <paramref name="mods"/> whose
        /// attribute equals <paramref name="attribute"/>, or <paramref name="defaultValue"/> if there is no such ItemMod.
        /// </summary>
#if (PoESkillTree_UseSmallDec_ForAttributes && PoESkillTree_UseSmallDec_ForGeneratorBars)
        public static SmallDec First(this IEnumerable<ItemMod> mods, string attribute, int valueIndex, SmallDec defaultValue)
#else
        public static float First(this IEnumerable<ItemMod> mods, string attribute, int valueIndex, float defaultValue)
#endif
        {
            return mods.Where(p => p.Attribute == attribute).Select(p => p.Value[valueIndex]).DefaultIfEmpty(defaultValue).First();
        }

        /// <summary>
        /// Returns true and writes the value at index <paramref name="valueIndex"/> of the first ItemMod in <paramref name="mods"/> whose
        /// attribute equals <paramref name="attribute"/> into <paramref name="value"/>, or returns false and writes 0 into <paramref name="value"/>
        /// if there is no such ItemMod.
        /// </summary>
#if (PoESkillTree_UseSmallDec_ForAttributes)
        public static bool TryGetValue(this IEnumerable<ItemMod> mods, string attribute, int valueIndex, out SmallDec value)
#else
        public static bool TryGetValue(this IEnumerable<ItemMod> mods, string attribute, int valueIndex, out float value)
#endif
        {
            var mod = mods.FirstOrDefault(p => p.Attribute == attribute);
            if (mod == null)
            {
#if (PoESkillTree_UseSmallDec_ForAttributes)
                value = SmallDec.Zero;
#else
                value = default(float);
#endif
                return false;
            }
            else
            {
                value = mod.Value[valueIndex];
                return true;
            }
        }
#if (PoESkillTree_UseSmallDec_ForAttributes && !PoESkillTree_UseSmallDec_ForGeneratorBars)
        public static bool TryGetValue(this IEnumerable<ItemMod> mods, string attribute, int valueIndex, out float value)
        {
            var mod = mods.FirstOrDefault(p => p.Attribute == attribute);
            if (mod == null)
            {
                value = default(float);
                return false;
            }
            else
            {
                value = mod.Value[valueIndex];
                return true;
            }
        }
#endif
    }
}