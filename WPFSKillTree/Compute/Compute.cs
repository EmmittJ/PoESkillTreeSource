﻿using System;
using System.Collections.Generic;
using POESKillTree.Localization;
using POESKillTree.Model;
using POESKillTree.ViewModels;
using System.Linq;
using POESKillTree.Model.Items;
using POESKillTree.SkillTreeFiles;
using static POESKillTree.Compute.ComputeGlobal;

namespace POESKillTree.Compute
{
    /* Known issues:
     * - No support for minions, traps, mines and totems.
     * - No support for Vaal gems.
     * - Mana regeneration shows sometimes incorrect value (0.1 difference from in-game value).
     * - Damage type ranges shows sometimes incorrect value affecting overall DPS (1 difference from in-game value, occurs with damage conversions).
     * - Damage per Second shows sometimes incorrect value (0.1 difference from in-game value).
     * - Spell Critical Strike chance shows sometimes incorrect value (0.1 difference from in-game value).
     * - Estimated chance to Evade Attacks shows sometimes incorrect value.
     * - Cast gems (Herald of Ice, Herald of Thunder) have their quality bonuses applied in inactive state.
     * - Chance to Hit shows sometimes incorrect value affecting overall DPS.
     */
    public class Computation
    {


        // Equipped items.
        public List<Item> Items;
        // Main hand weapon.
        public Weapon MainHand;
        // Off hand weapon or quiver/shield.
        public Weapon OffHand;
        // The flag whether character is dual wielding.
        public bool IsDualWielding;
        // The flag whether character is wielding a shield.
        public bool IsWieldingShield => (MainHand?.Is(WeaponType.Shield) ?? false) || (OffHand?.Is(WeaponType.Shield) ?? false);
        // The flag whether character is wielding a staff.
        public bool IsWieldingStaff => MainHand?.Is(WeaponType.Staff) ?? false;
        public bool EldritchBattery => Global.ContainsKey("Converts all Energy Shield to Mana");
        // Character level.
        public int Level;
        // Equipment attributes.
        public AttributeSet Equipment = new AttributeSet();
        // All global attributes (includes tree, equipment, implicit).
        public AttributeSet Global = new AttributeSet();
        // Implicit attributes derived from base attributes and level (e.g. Life, Mana).
        public AttributeSet Implicit = new AttributeSet();
        // Skill tree attributes (includes base attributes).
        public AttributeSet Tree = new AttributeSet();


        // Skill tree keystones.
        public bool Acrobatics;
        public bool AvatarOfFire;
        public bool BloodMagic;
        public bool ChaosInoculation;
        public bool IronGrip;
        public bool IronReflexes;
        public bool NecromanticAegis;
        public bool ResoluteTechnique;
        public bool VaalPact;
        public bool ZealotsOath;

        // Chance to Evade = 1 - Attacker's Accuracy / ( Attacker's Accuracy + (Defender's Evasion / 4) ^ 0.8 )
        // @see http://pathofexile.gamepedia.com/Evasion
        public float ChanceToEvade(int level, float evasionRating)
        {
            if (level == 0)
                return 0; //throw exception?

            if (Global.ContainsKey("Cannot Evade enemy Attacks"))
                return 0; // The modifier can be from other source than Unwavering Stance.

            int maa = MonsterAverageAccuracy[level];

            return RoundValue((float)(1 - maa / (maa + Math.Pow(evasionRating / 4, 0.8))) * 100, 0);
        }

        // Chance to Hit = Attacker's Accuracy / ( Attacker's Accuracy + (Defender's Evasion / 4) ^ 0.8 )
        // Chance to hit can never be lower than 5%, nor higher than 95%.
        // @see http://pathofexile.gamepedia.com/Accuracy
        public float ChanceToHit(int level, float accuracyRating)
        {
            // the maximum level considered for this is 80 if it is the same for estimated physical damage reduction
            int mae = MonsterAverageEvasion[Math.Min(level, 80) - 1]; // XXX: For some reason this works.

            float chance = (float)(accuracyRating / (accuracyRating + Math.Pow(mae / 4, 0.8))) * 100;
            if (chance < 5f) chance = 5f;
            else if (chance > 95f) chance = 95f;

            return chance;
        }

        // Computes core attributes.
        private void ComputeCoreAttributes()
        {
            float strength = 0;
            float dexterity = 0;
            float intelligence = 0;

            // Citrine Amulet
            if (Global.ContainsKey("+# to Strength and Dexterity"))
            {
                strength += Global["+# to Strength and Dexterity"][0];
                dexterity += Global["+# to Strength and Dexterity"][0];

                Global.Remove("+# to Strength and Dexterity");
            }

            // Agate Amulet
            if (Global.ContainsKey("+# to Strength and Intelligence"))
            {
                strength += Global["+# to Strength and Intelligence"][0];
                intelligence += Global["+# to Strength and Intelligence"][0];

                Global.Remove("+# to Strength and Intelligence");
            }

            // Turquoise Amulet
            if (Global.ContainsKey("+# to Dexterity and Intelligence"))
            {
                dexterity += Global["+# to Dexterity and Intelligence"][0];
                intelligence += Global["+# to Dexterity and Intelligence"][0];

                Global.Remove("+# to Dexterity and Intelligence");
            }

            if (Global.ContainsKey("+# to all Attributes"))
            {
                strength += Global["+# to all Attributes"][0];
                dexterity += Global["+# to all Attributes"][0];
                intelligence += Global["+# to all Attributes"][0];

                Global.Remove("+# to all Attributes");
            }

            if (strength != 0)
                Global["+# to Strength"][0] += strength;
            if (dexterity != 0)
                Global["+# to Dexterity"][0] += dexterity;
            if (intelligence != 0)
                Global["+# to Intelligence"][0] += intelligence;
        }

        // Computes defensive statistics.
        public List<ListGroup> GetDefensiveAttributes()
        {
            AttributeSet ch = new AttributeSet();
            AttributeSet def = new AttributeSet();


            // Character attributes.
            ch["Strength: #"] = Global["+# to Strength"];
            ch["Dexterity: #"] = Global["+# to Dexterity"];
            ch["Intelligence: #"] = Global["+# to Intelligence"];

            float life;
            if (ChaosInoculation)
                life = Global["Maximum Life becomes #, Immune to Chaos Damage"][0];
            else
            {
                life = Global["+# to maximum Life"][0];
                if (Global.ContainsKey("#% increased maximum Life"))
                    life = IncreaseValueByPercentage(life, Global["#% increased maximum Life"][0]);
            }
            ch["Life: #"] = new List<float>() { RoundValue(life, 0) };

            float lessArmourAndES = 0;
            if (Acrobatics)
                lessArmourAndES += Global["#% Chance to Dodge Attacks. #% less Armour and Energy Shield, #% less Chance to Block Spells and Attacks"][1];

            var stats = EnergyShield(lessArmourAndES);
            float es = stats.EnergyShield;
            float shieldArmour = stats.ShieldArmor;
            float shieldEvasion = stats.ShieldEvasion;
            float mana = stats.Mana;
            float incMana = stats.IncreasedMana;

            if (BloodMagic)
                mana = 0;

            ch["Mana: #"] = new List<float>() { RoundValue(mana, 0) };
            ch["Maximum Energy Shield: #"] = new List<float>() { RoundValue(es, 0) };
            def = def.Merge(ArmorAndEvasion(lessArmourAndES, shieldArmour, shieldEvasion));

            // Dodge Attacks and Spells.
            def = def.Merge(Dodge());

            // Energy Shield Recharge per Second.
            // @see http://pathofexile.gamepedia.com/Energy_shield
            def = def.Merge(EnergyShieldRegen(es));

            // Life Regeneration.
            def = def.Merge(LifeRegen(life, es));

            // Mana Regeneration.
            def = def.Merge(ManaRegen(mana));

            // Shield, Staff and Dual Wielding detection.
            bool hasShield = OffHand.IsShield();

            def = def.Merge(Resistances(Difficulty.Normal, hasShield));
            def = def.Merge(Block(hasShield));
            def = def.Merge(HitAvoidance());

            List<ListGroup> groups = new List<ListGroup>();
            groups.Add(new ListGroup(L10n.Message("Character"), ch));
            groups.Add(new ListGroup(L10n.Message("Defense"), def));

            return groups;
        }

        public EnergyShieldStats EnergyShield(float lessArmourAndES)
        {
            float mana = Global.GetOrDefault("+# to maximum Mana");
            float incMana = 0;
            incMana = Global.GetOrDefault("#% increased maximum Mana");

            float es = Global.GetOrDefault("+# to maximum Energy Shield");
            // Add maximum shield from items.
            es += Global.GetOrDefault("Energy Shield: #");

            float incES = Global.GetOrDefault("#% increased maximum Energy Shield");

            // More % maximum shield from tree and items.
            float moreES = Global.GetOrDefault("#% more maximum Energy Shield");

            // Equipped Shield bonuses.
            float incArmourShield = Global.GetOrDefault("#% increased Armour from equipped Shield");
            float incESShield = Global.GetOrDefault("#% increased Energy Shield from equipped Shield");
            float incDefencesShield = Global.GetOrDefault("#% increased Defences from equipped Shield");
            float shieldArmour = 0;
            float shieldEvasion = 0;
            float shieldES = 0;
            if (incDefencesShield > 0 || incArmourShield > 0 || incESShield > 0)
            {
                List<float> value = OffHand.GetValues("Armour: #");
                if (value.Count > 0)
                    shieldArmour += PercentOfValue(value[0], incArmourShield + incDefencesShield);

                value = OffHand.GetValues("Evasion Rating: #");
                if (value.Count > 0)
                    shieldEvasion += PercentOfValue(value[0], incDefencesShield);

                value = OffHand.GetValues("Energy Shield: #");
                if (value.Count > 0)
                    shieldES += PercentOfValue(value[0], incESShield + incDefencesShield);
            }

            // ( Mana * %mana increases ) + ( ES * ( %ES increases + %mana increases ) * ( %ES more ) )
            // ES to Mana conversion mod (old Eldritch Battery).
            if (EldritchBattery)
            {
                es = IncreaseValueByPercentage(es, incES + incMana);
                es += shieldES;
                if (moreES > 0)
                    es = IncreaseValueByPercentage(es, moreES);
                if (lessArmourAndES > 0)
                    es = IncreaseValueByPercentage(es, -lessArmourAndES);

                mana = IncreaseValueByPercentage(mana, incMana) + es;
                es = 0;
            }
            else
            {
                mana = IncreaseValueByPercentage(mana, incMana);
                es = IncreaseValueByPercentage(es, incES);
                es += shieldES;
                if (moreES > 0)
                    es = IncreaseValueByPercentage(es, moreES);
                if (lessArmourAndES > 0)
                    es = IncreaseValueByPercentage(es, -lessArmourAndES);
            }

            return new EnergyShieldStats
            {
                EnergyShield = es,
                ShieldArmor = shieldArmour,
                ShieldEvasion = shieldEvasion,
                Mana = mana,
                IncreasedMana = incMana
            };
        }
        public struct EnergyShieldStats
        {
            public float EnergyShield { get; set; }
            public float ShieldArmor { get; set; }
            public float ShieldEvasion { get; set; }
            public float Mana { get; set; }
            public float IncreasedMana { get; set; }
        }

        public AttributeSet ArmorAndEvasion(float lessArmourAndES, float shieldArmour, float shieldEvasion)
        {
            var def = new AttributeSet();
            // Evasion Rating from level, tree and items.
            float evasion = Global.GetOrDefault("Evasion Rating: #");
            evasion += Global.GetOrDefault("+# to Evasion Rating");
            // Increase % from dexterity, tree and items.
            float incEvasion = Global.GetOrDefault("#% increased Evasion Rating");
            float incEvasionAndArmour = Global.GetOrDefault("#% increased Evasion Rating and Armour");

            float armour = Global.GetOrDefault("Armour: #") + Global.GetOrDefault("+# to Armour");
            float armourProjectile = 0;
            // Armour from items.
            float incArmour = 0;
            float incArmourProjectile = 0;
            incArmour += Global.GetOrDefault("#% increased Armour");
            incArmourProjectile += Global.GetOrDefault("#% increased Armour against Projectiles");
            // Enable armour against projectile calculations once there is some Armour against Projectiles modifier.
            if (incArmourProjectile != 0)
                armourProjectile = armour;

            // Final Armour = Base Evasion * ( 1 + % increased Evasion Rating + % increased Armour + % increased Evasion Rating and Armour )
            //              + Base Armour  * ( 1 + % increased Armour                              + % increased Evasion Rating and Armour )
            // @see http://pathofexile.gamepedia.com/Iron_Reflexes
            if (IronReflexes)
            {
                // Substract "#% increased Evasion Rating" from Dexterity (it's not being applied).
                incEvasion -= Implicit.GetOrDefault("#% increased Evasion Rating");
                armour = IncreaseValueByPercentage(armour, incArmour + incEvasionAndArmour) + IncreaseValueByPercentage(evasion, incEvasion + incArmour + incEvasionAndArmour);
                armour += shieldArmour + shieldEvasion;
                if (armourProjectile > 0)
                {
                    armourProjectile = IncreaseValueByPercentage(armourProjectile, incArmour + incArmourProjectile + incEvasionAndArmour) + IncreaseValueByPercentage(evasion, incEvasion + incArmour + incEvasionAndArmour);
                    armourProjectile += shieldArmour + shieldEvasion;
                }
                evasion = 0;
            }
            else
            {
                evasion = IncreaseValueByPercentage(evasion, incEvasion + incEvasionAndArmour) + shieldEvasion;
                armour = IncreaseValueByPercentage(armour, incArmour + incEvasionAndArmour) + shieldArmour;
                if (armourProjectile > 0)
                    armourProjectile = IncreaseValueByPercentage(armourProjectile, incArmour + incArmourProjectile + incEvasionAndArmour) + shieldArmour;
            }
            if (lessArmourAndES > 0)
            {
                armour = IncreaseValueByPercentage(armour, -lessArmourAndES);
                if (armourProjectile > 0)
                    armourProjectile = IncreaseValueByPercentage(armourProjectile, -lessArmourAndES);
            }

            if (armour > 0)
            {
                def["Armour: #"] = new List<float>() { RoundValue(armour, 0) };
                def["Estimated Physical Damage reduction: #%"] = new List<float>() { RoundValue(PhysicalDamageReduction(Level, RoundValue(armour, 0)), 0) };
            }
            if (armourProjectile > 0)
            {
                def["Armour against Projectiles: #"] = new List<float>() { RoundValue(armourProjectile, 0) };
                def["Estimated Physical Damage reduction against Projectiles: #%"] = new List<float>() { RoundValue(PhysicalDamageReduction(Level, RoundValue(armourProjectile, 0)), 0) };
            }
            if (evasion > 0)
                def["Evasion Rating: #"] = new List<float>() { RoundValue(evasion, 0) };

            float chanceToEvade = ChanceToEvade(Level, RoundValue(evasion, 0));
            if (chanceToEvade > 0)
            {
                // Arrow Dancing keystone.
                float chanceToEvadeMelee = chanceToEvade, chanceToEvadeProjectile = chanceToEvade;

                chanceToEvadeMelee = IncreaseValueByPercentage(chanceToEvadeMelee, -Global.GetOrDefault("#% less chance to Evade Melee Attacks"));
                chanceToEvadeMelee = IncreaseValueByPercentage(chanceToEvadeMelee, Global.GetOrDefault("#% more chance to Evade Melee Attacks"));
                chanceToEvadeProjectile = IncreaseValueByPercentage(chanceToEvadeProjectile, -Global.GetOrDefault("#% less chance to Evade Projectile Attacks"));
                chanceToEvadeProjectile = IncreaseValueByPercentage(chanceToEvadeProjectile, Global.GetOrDefault("#% more chance to Evade Projectile Attacks"));

                // Chance cannot be less than 5% and more than 95%.
                if (chanceToEvadeMelee < 5f) chanceToEvadeMelee = 5f;
                else if (chanceToEvadeMelee > 95f) chanceToEvadeMelee = 95f;
                if (chanceToEvadeProjectile < 5f) chanceToEvadeProjectile = 5f;
                else if (chanceToEvadeProjectile > 95f) chanceToEvadeProjectile = 95f;

                if (chanceToEvadeMelee == chanceToEvadeProjectile)
                    def["Estimated chance to Evade Attacks: #%"] = new List<float>() { RoundValue(chanceToEvadeMelee, 0) };
                else
                {
                    def["Estimated chance to Evade Melee Attacks: #%"] = new List<float>() { RoundValue(chanceToEvadeMelee, 0) };
                    def["Estimated chance to Evade Projectile Attacks: #%"] = new List<float>() { RoundValue(chanceToEvadeProjectile, 0) };
                }
            }
            return def;
        }

        private AttributeSet Dodge()
        {
            var def = new AttributeSet();
            float chanceToDodgeAttacks = 0;
            float chanceToDodgeSpells = 0;
            if (Acrobatics)
                chanceToDodgeAttacks += Global["#% Chance to Dodge Attacks. #% less Armour and Energy Shield, #% less Chance to Block Spells and Attacks"][0];
            if (Global.ContainsKey("#% additional chance to Dodge Attacks"))
                chanceToDodgeAttacks += Global["#% additional chance to Dodge Attacks"][0];
            if (Global.ContainsKey("#% Chance to Dodge Spell Damage"))
                chanceToDodgeSpells += Global["#% Chance to Dodge Spell Damage"][0];
            if (chanceToDodgeAttacks > 0)
                def["Chance to Dodge Attacks: #%"] = new List<float>() { chanceToDodgeAttacks };
            if (chanceToDodgeSpells > 0)
                def["Chance to Dodge Spells: #%"] = new List<float>() { chanceToDodgeSpells };
            return def;
        }

        private AttributeSet EnergyShieldRegen(float es)
        {
            var def = new AttributeSet();
            if (es > 0)
            {
                def["Maximum Energy Shield: #"] = new List<float>() { RoundValue(es, 0) };

                float esRecharge = RoundValue(es, 0) / 5; // By default, energy shield recharges at a rate equal to a fifth of the character's maximum energy shield per second.
                if (Global.ContainsKey("#% increased Energy Shield Recharge Rate"))
                    esRecharge = esRecharge * (1 + Global["#% increased Energy Shield Recharge Rate"][0] / 100);
                def["Energy Shield Recharge per Second: #"] = new List<float>() { RoundValue(esRecharge, 1) };

                float esDelay = 2; // By default, the delay period for energy shield to begin to recharge is 2 seconds.
                float esOccurrence = 0;
                esOccurrence += Global.GetOrDefault("#% faster start of Energy Shield Recharge");
                esOccurrence -= Global.GetOrDefault("#% slower start of Energy Shield Recharge");
                esDelay = esDelay * 100 / (100 + esOccurrence); // 200 / (100 + r)
                if (esOccurrence != 0)
                    def["Energy Shield Recharge Occurrence modifier: " + (esOccurrence > 0 ? "+" : "") + "#%"] = new List<float>() { esOccurrence };
                def["Energy Shield Recharge Delay: #s"] = new List<float>() { RoundValue(esDelay, 1) };
            }
            return def;
        }

        private AttributeSet LifeRegen(float life, float es)
        {
            var def = new AttributeSet();
            float lifeRegen = 0;
            float lifeRegenFlat = 0;
            if (Global.ContainsKey("#% of Life Regenerated per second"))
                lifeRegen += Global["#% of Life Regenerated per second"][0];
            if (Global.ContainsKey("# Life Regenerated per second"))
                lifeRegenFlat += Global["# Life Regenerated per second"][0];

            if (VaalPact)
                lifeRegen = 0;

            if (ZealotsOath)
            {
                if (es > 0 && lifeRegen + lifeRegenFlat > 0)
                    def["Energy Shield Regeneration per Second: #"] = new List<float>() { RoundValue(PercentOfValue(RoundValue(es, 0), lifeRegen), 1) + lifeRegenFlat };
            }
            else
            {
                if (!ChaosInoculation && lifeRegen + lifeRegenFlat > 0)
                    def["Life Regeneration per Second: #"] = new List<float>() { RoundValue(PercentOfValue(RoundValue(life, 0), lifeRegen), 1) + lifeRegenFlat };
            }
            return def;
        }

        private AttributeSet ManaRegen(float mana)
        {
            var def = new AttributeSet();
            if (mana > 0)
            {
                float manaRegen = PercentOfValue(RoundValue(mana, 0), 1.75f);
                // manaRegen += ClarityManaRegenerationPerSecond; // Clarity provides flat mana regeneration bonus.
                float incManaRegen = 0;
                if (Global.ContainsKey("#% increased Mana Regeneration Rate"))
                    incManaRegen += Global["#% increased Mana Regeneration Rate"][0];
                manaRegen = IncreaseValueByPercentage(manaRegen, incManaRegen);
                def["Mana Regeneration per Second: #"] = new List<float>() { RoundValue(manaRegen, 1) };
            }
            return def;
        }

        public AttributeSet Resistances(Difficulty diff, bool hasShield)
        {
            var def = new AttributeSet();
            def["Fire Resistance: #% (#%)"] = GetResistance("Fire", diff, hasShield);
            def["Cold Resistance: #% (#%)"] = GetResistance("Cold", diff, hasShield);
            def["Lightning Resistance: #% (#%)"] = GetResistance("Lightning", diff, hasShield);
            def["Chaos Resistance: #% (#%)"] = GetResistance("Chaos", diff, hasShield);
            return def;
        }

        public List<float> GetResistance(string type, Difficulty diff, bool hasShield)
        {
            float max = 75;
            float res = 0;
            if (diff == Difficulty.Cruel)
                res = -20;
            else if (diff == Difficulty.Merciless)
                res = -60;

            bool isChaos = type == "Chaos";

            string normal = $"+#% to {type} Resistance";
            if (Global.ContainsKey(normal))
                res += Global[normal][0];

            string allres = "+#% to all Elemental Resistances";
            if (Global.ContainsKey(allres) && !isChaos)
                res += Global[allres][0];

            string allResShield = "+#% Elemental Resistances while holding a Shield";
            if (hasShield && Global.ContainsKey(allResShield) && !isChaos)
                res += Global[allResShield][0];

            var toMatch1 = $"\\+#% to {type} and (.+) Resistances";
            var matches = Global.Matches(toMatch1);
            foreach (var m in matches)
                res += m.Value[0];

            var toMatch2 = $"\\+#% to (.+) and {type} Resistances";
            var matches2 = Global.Matches(toMatch2);
            foreach (var m in matches2)
                res += m.Value[0];

            string maxResText = $"+#% to maximum {type} Resistance";
            if (Global.ContainsKey(maxResText))
                max += Global[maxResText][0];

            if (isChaos && ChaosInoculation)
                max = res = 100;

            return new List<float>() { MaximumValue(res, max), res };
        }

        public float GetMaxBlock()
        {
            return 75 + Global.GetOrDefault("+#% to maximum Block Chance");
        }

        public AttributeSet Block(bool hasShield)
        {
            var block = new AttributeSet();
            // Chance to Block Attacks and Spells.
            // Block chance is capped at 75%. The chance to block spells is also capped at 75%.
            // @see http://pathofexile.gamepedia.com/Blocking
            var maxBlock = GetMaxBlock();
            float maxChanceBlockAttacks = maxBlock;
            float maxChanceBlockSpells = maxBlock;
            float maxChanceBlockProjectiles = maxBlock;
            float chanceBlockAttacks = 0;
            float chanceBlockSpells = 0;
            float chanceBlockProjectiles = 0;

            if (hasShield)
                chanceBlockAttacks = OffHand.GetValues("Chance to Block: #%").FirstOrDefault();
            else if (IsWieldingStaff)
                chanceBlockAttacks = MainHand.GetValues("#% Chance to Block").FirstOrDefault();
            else if (IsDualWielding)
                chanceBlockAttacks += 15; // When dual wielding, the base chance to block is 15% no matter which weapons are used.

            if (hasShield)
            {
                chanceBlockAttacks += Global.GetOrDefault("#% additional Chance to Block with Shields");
                chanceBlockSpells += Global.GetOrDefault("#% additional Chance to Block Spells with Shields");
            }
            if (IsWieldingStaff)
                chanceBlockAttacks += Global.GetOrDefault("#% additional Block Chance With Staves");
            if (IsDualWielding)
                chanceBlockAttacks += Global.GetOrDefault("#% additional Block Chance while Dual Wielding");
            if (IsDualWielding || hasShield)
                chanceBlockAttacks += Global.GetOrDefault("#% additional Block Chance while Dual Wielding or holding a Shield");

            chanceBlockSpells += PercentOfValue(chanceBlockAttacks, Global.GetOrDefault("#% of Block Chance applied to Spells"));

            if (hasShield)
                chanceBlockProjectiles = chanceBlockAttacks + Global.GetOrDefault("+#% additional Block Chance against Projectiles");
            if (Acrobatics)
            {
                float lessChanceBlock = Global["#% Chance to Dodge Attacks. #% less Armour and Energy Shield, #% less Chance to Block Spells and Attacks"][2];
                chanceBlockAttacks = IncreaseValueByPercentage(chanceBlockAttacks, -lessChanceBlock);
                chanceBlockSpells = IncreaseValueByPercentage(chanceBlockSpells, -lessChanceBlock);
                chanceBlockProjectiles = IncreaseValueByPercentage(chanceBlockProjectiles, -lessChanceBlock);
            }
            if (chanceBlockAttacks > 0)
                block.Add("Chance to Block Attacks: #%", new List<float>() { MaximumValue(RoundValue(chanceBlockAttacks, 0), maxChanceBlockAttacks), maxChanceBlockAttacks });
            if (chanceBlockSpells > 0)
                block.Add("Chance to Block Spells: #%", new List<float>() { MaximumValue(RoundValue(chanceBlockSpells, 0), maxChanceBlockSpells), maxChanceBlockSpells });
            if (chanceBlockProjectiles > 0)
                block.Add("Chance to Block Projectile Attacks: #%", new List<float>() { MaximumValue(RoundValue(chanceBlockProjectiles, 0), maxChanceBlockProjectiles), maxChanceBlockProjectiles });
            return block;
        }

        public AttributeSet HitAvoidance()
        {
            var def = new AttributeSet();
            // Elemental stataus ailments.
            float allAvoid = Global.GetOrDefault("#% chance to Avoid Elemental Status Ailments");
            float igniteAvoidance = Global.GetOrDefault("#% chance to Avoid being Ignited") + allAvoid;
            float chillAvoidance = Global.GetOrDefault("#% chance to Avoid being Chilled") + allAvoid;
            float freezeAvoidance = Global.GetOrDefault("#% chance to Avoid being Frozen") + allAvoid;
            float shockAvoidance = Global.GetOrDefault("#% chance to Avoid being Shocked") + allAvoid;

            if (Global.ContainsKey("Cannot be Ignited"))
                igniteAvoidance = 100;
            if (Global.ContainsKey("Cannot be Chilled"))
                chillAvoidance = 100;
            if (Global.ContainsKey("Cannot be Frozen"))
                freezeAvoidance = 100;
            if (Global.ContainsKey("Cannot be Shocked"))
                shockAvoidance = 100;

            if (igniteAvoidance > 0)
                def["Ignite Avoidance: #%"] = new List<float>() { igniteAvoidance };
            if (chillAvoidance > 0)
                def["Chill Avoidance: #%"] = new List<float>() { chillAvoidance };
            if (freezeAvoidance > 0)
                def["Freeze Avoidance: #%"] = new List<float>() { freezeAvoidance };
            if (shockAvoidance > 0)
                def["Shock Avoidance: #%"] = new List<float>() { shockAvoidance };
            return def;
        }

        // Initializes structures.
        public Computation() { }
        public Computation(SkillTree skillTree, ItemAttributes itemAttrs)
        {
            Items = itemAttrs.Equip.ToList();

            MainHand = new Weapon(WeaponHand.Main, itemAttrs.MainHand);
            OffHand = new Weapon(WeaponHand.Off, itemAttrs.OffHand);

            // If main hand weapon has Counts as Dual Wielding modifier, then clone weapon to off hand.
            // @see http://pathofexile.gamepedia.com/Wings_of_Entropy
            if (MainHand.Attributes.ContainsKey("Counts as Dual Wielding"))
                OffHand = MainHand.Clone(WeaponHand.Off);

            IsDualWielding = MainHand.IsWeapon() && OffHand.IsWeapon();
            if (IsDualWielding)
            {
                // Set dual wielded bit on weapons.
                MainHand.Hand |= WeaponHand.DualWielded;
                OffHand.Hand |= WeaponHand.DualWielded;
            }

            Level = skillTree.Level;
            if (Level < 1) Level = 1;
            else if (Level > 100) Level = 100;

            Tree = new AttributeSet(skillTree?.SelectedAttributesWithoutImplicit);
            Global.Add(Tree);

            // Keystones.
            Acrobatics = Tree.ContainsKey("#% Chance to Dodge Attacks. #% less Armour and Energy Shield, #% less Chance to Block Spells and Attacks");
            AvatarOfFire = Tree.ContainsKey("Deal no Non-Fire Damage");
            BloodMagic = Tree.ContainsKey("Removes all mana. Spend Life instead of Mana for Skills");
            ChaosInoculation = Tree.ContainsKey("Maximum Life becomes #, Immune to Chaos Damage");
            IronGrip = Tree.ContainsKey("The increase to Physical Damage from Strength applies to Projectile Attacks as well as Melee Attacks");
            IronReflexes = Tree.ContainsKey("Converts all Evasion Rating to Armour. Dexterity provides no bonus to Evasion Rating");
            NecromanticAegis = Tree.ContainsKey("All bonuses from an equipped Shield apply to your Minions instead of you");
            ResoluteTechnique = Tree.ContainsKey("Never deal Critical Strikes");
            VaalPact = Tree.ContainsKey("Life Leech applies instantly at #% effectiveness. Life Regeneration has no effect.");
            ZealotsOath = Tree.ContainsKey("Life Regeneration applies to Energy Shield instead of Life");

            foreach (var attr in itemAttrs.NonLocalMods)
                Equipment.Add(attr.Attribute, new List<float>(attr.Value));

            if (NecromanticAegis && OffHand.IsShield())
            {
                // Remove all bonuses of shield from equipment set.
                // @see http://pathofexile.gamepedia.com/Necromantic_Aegis
                foreach (var attr in OffHand.Attributes)
                    Equipment.Remove(attr);
                // Remove all bonuses from shield itself.
                OffHand.Attributes.Clear();
            }

            Global.Add(Equipment);

            if (Global.ContainsKey("Armour received from Body Armour is doubled")
                && itemAttrs.Armor != null)
            {
                var armorProp = itemAttrs.Armor.Properties.FirstOrDefault(m => m.Attribute == "Armour: #");
                if (armorProp != null && armorProp.Value.Any())
                {
                    Global["Armour: #"][0] += armorProp.Value[0];
                }
            }

            ComputeCoreAttributes();

            Implicit = new AttributeSet(SkillTree.ImplicitAttributes(Global, Level));
            Global.Add(Implicit);

            // Innate dual wielding bonuses.
            // @see http://pathofexile.gamepedia.com/Dual_wielding
            if (IsDualWielding)
            {
                Global["#% more Attack Speed"] = new List<float>() { 10 };
                Global["#% more Physical Damage with Weapons"] = new List<float>() { 20 };
            }
        }

        // Computes offensive attacks.
        public List<ListGroup> Offense()
        {
            List<ListGroup> groups = new List<ListGroup>();

            foreach (Item item in Items)
            {
                if (item.Gems == null)
                    continue;
                foreach (Item gem in item.Gems)
                {
                    if (AttackSkill.IsAttackSkill(gem))
                    {
                        // Skip gems linked to totems and Cast on gems for now.
                        if (item.GetLinkedGems(gem).Find(g => g.Name.Contains("Totem")) != null) continue;

                        AttackSkill attack = AttackSkill.Create(gem, this);

                        attack.Link(item.GetLinkedGems(gem), item);
                        attack.Apply(item);

                        groups.Add(attack.ToListGroup());
                    }
                }
            }

            return groups;
        }

    }
}
