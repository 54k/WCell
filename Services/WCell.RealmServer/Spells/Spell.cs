/*************************************************************************
 *
 *   file		: Spell.cs
 *   copyright		: (C) The WCell Team
 *   email		: info@wcell.org
 *   last changed	: $LastChangedDate: 2010-04-23 15:13:50 +0200 (fr, 23 apr 2010) $

 *   revision		: $Rev: 1282 $
 *
 *   This program is free software; you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation; either version 2 of the License, or
 *   (at your option) any later version.
 *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WCell.Constants;
using WCell.Constants.Items;
using WCell.Constants.Skills;
using WCell.Constants.Spells;
using WCell.RealmServer.Content;
using WCell.RealmServer.Entities;
using WCell.RealmServer.Misc;
using WCell.RealmServer.Modifiers;
using WCell.RealmServer.Spells.Auras;
using WCell.RealmServer.Spells.Targeting;
using WCell.Util;
using WCell.Util.Data;
using WCell.Util.Graphics;

namespace WCell.RealmServer.Spells
{
	/// <summary>
	/// Represents any spell action or aura
	/// </summary>
	[DataHolder(RequirePersistantAttr = true)]
	public partial class Spell : IDataHolder, ISpellGroup
	{
		/// <summary>
		/// 
		/// </summary>
		/// <param name="spell">The special Spell being casted</param>
		/// <param name="caster">The caster casting the spell</param>
		/// <param name="target">The target that the Caster selected (or null)</param>
		/// <param name="targetPos">The targetPos that was selected (or 0,0,0)</param>
		public delegate void SpecialCastHandler(Spell spell, WorldObject caster, WorldObject target, ref Vector3 targetPos);

		/// <summary>
		/// This Range will be used for all Spells that have MaxRange = 0
		/// </summary>
		public static int DefaultSpellRange = 30;

		private static readonly Regex numberRegex = new Regex(@"\d+");

		public static readonly Spell[] EmptyArray = new Spell[0];

		public Spell()
		{
			AISettings = new AISpellSettings(this);
		}

		#region Harmful SpellEffects
		//public static readonly HashSet<SpellEffectType> HarmfulSpellEffects = new Func<HashSet<SpellEffectType>>(() => {
		//    var effects = new HashSet<SpellEffectType>();

		//    effects.Add(SpellEffectType.Attack);
		//    effects.Add(SpellEffectType.AttackMe);
		//    effects.Add(SpellEffectType.DestroyAllTotems);
		//    effects.Add(SpellEffectType.Dispel);
		//    effects.Add(SpellEffectType.Attack);

		//    return effects;
		//})();
		#endregion

		#region Trigger Spells
		/// <summary>
		/// Add Spells to be casted on the targets of this Spell
		/// </summary>
		public void AddTargetTriggerSpells(params SpellId[] spellIds)
		{
			var spells = new Spell[spellIds.Length];
			for (var i = 0; i < spellIds.Length; i++)
			{
				var id = spellIds[i];
				var spell = SpellHandler.Get(id);
				if (spell == null)
				{
					throw new InvalidSpellDataException("Invalid SpellId: " + id);
				}
				spells[i] = spell;
			}
			AddTargetTriggerSpells(spells);
		}

		/// <summary>
		/// Add Spells to be casted on the targets of this Spell
		/// </summary>
		public void AddTargetTriggerSpells(params Spell[] spells)
		{
			if (TargetTriggerSpells == null)
			{
				TargetTriggerSpells = spells;
			}
			else
			{
				var oldLen = TargetTriggerSpells.Length;
				Array.Resize(ref TargetTriggerSpells, oldLen + spells.Length);
				Array.Copy(spells, 0, TargetTriggerSpells, oldLen, spells.Length);
			}
		}

		/// <summary>
		/// Add Spells to be casted on the targets of this Spell
		/// </summary>
		public void AddCasterTriggerSpells(params SpellId[] spellIds)
		{
			var spells = new Spell[spellIds.Length];
			for (var i = 0; i < spellIds.Length; i++)
			{
				var id = spellIds[i];
				var spell = SpellHandler.Get(id);
				if (spell == null)
				{
					throw new InvalidSpellDataException("Invalid SpellId: " + id);
				}
				spells[i] = spell;
			}
			AddCasterTriggerSpells(spells);
		}

		/// <summary>
		/// Add Spells to be casted on the targets of this Spell
		/// </summary>
		public void AddCasterTriggerSpells(params Spell[] spells)
		{
			if (CasterTriggerSpells == null)
			{
				CasterTriggerSpells = spells;
			}
			else
			{
				var oldLen = CasterTriggerSpells.Length;
				Array.Resize(ref CasterTriggerSpells, oldLen + spells.Length);
				Array.Copy(spells, 0, CasterTriggerSpells, oldLen, spells.Length);
			}
		}
		#endregion

		#region Custom Proc Handlers
		/// <summary>
		/// Add Handler to be enabled when this aura spell is active
		/// </summary>
		public void AddProcHandler(ProcHandlerTemplate handler)
		{
			if (ProcHandlers == null)
			{
				ProcHandlers = new List<ProcHandlerTemplate>();
			}
			ProcHandlers.Add(handler);
			if (Effects.Length == 0)
			{
				// need at least one effect to make this work
				AddAuraEffect(AuraType.Dummy);
			}
		}
		#endregion

		/// <summary>
		/// List of Spells to be learnt when this Spell is learnt
		/// </summary>
		public readonly List<Spell> AdditionallyTaughtSpells = new List<Spell>(0);

		#region Field Generation (Generates the value of many fields, based on the 200+ original Spell properties)
		/// <summary>
		/// Sets all default variables
		/// </summary>
		internal void Initialize()
		{
			init1 = true;
			var learnSpellEffect = GetEffect(SpellEffectType.LearnSpell);
			if (learnSpellEffect == null)
			{
				learnSpellEffect = GetEffect(SpellEffectType.LearnPetSpell);
			}
			if (learnSpellEffect != null && learnSpellEffect.TriggerSpellId != 0)
			{
				IsTeachSpell = true;
			}

			// figure out Trigger spells
			for (var i = 0; i < Effects.Length; i++)
			{
				var effect = Effects[i];
				if (effect.TriggerSpellId != SpellId.None || effect.AuraType == AuraType.PeriodicTriggerSpell)
				{
					var triggeredSpell = SpellHandler.Get((uint)effect.TriggerSpellId);
					if (triggeredSpell != null)
					{
						if (!IsTeachSpell)
						{
							triggeredSpell.IsTriggeredSpell = true;
						}
						else
						{
							LearnSpell = triggeredSpell;
						}
						effect.TriggerSpell = triggeredSpell;
					}
					else
					{
						if (IsTeachSpell)
						{
							IsTeachSpell = GetEffect(SpellEffectType.LearnSpell) != null;
						}
					}
				}
			}

			foreach (var effect in Effects)
			{
				if (effect.EffectType == SpellEffectType.PersistantAreaAura// || effect.HasTarget(ImplicitTargetType.DynamicObject)
					)
				{
					DOEffect = effect;
					break;
				}
			}

			//foreach (var effect in Effects)
			//{
			//    effect.Initialize();
			//}
		}

		/// <summary>
		/// For all things that depend on info of all spells from first Init-round and other things
		/// </summary>
		internal void Init2()
		{
			if (init2)
			{
				return;
			}

			init2 = true;

			IsPassive = Attributes.HasFlag(SpellAttributes.Passive);

			IsChanneled = !IsPassive && AttributesEx.HasAnyFlag(SpellAttributesEx.Channeled_1 | SpellAttributesEx.Channeled_2) ||
				// don't use Enum.HasFlag!
			              (SpellInterrupts != null && SpellInterrupts.ChannelInterruptFlags > 0);

			foreach (var effect in Effects)
			{
				effect.Init2();
				if (effect.IsHealEffect)
				{
					IsHealSpell = true;
				}
				if (effect.EffectType == SpellEffectType.NormalizedWeaponDamagePlus)
				{
					IsDualWieldAbility = true;
				}
			}

			InitAura();

			if (IsChanneled)
			{
				if (Durations.Min == 0)
				{
					Durations.Min = Durations.Max = 1000;
				}

				foreach (var effect in Effects)
				{
					if (effect.IsPeriodic)
					{
						ChannelAuraPeriod = effect.AuraPeriod;
						break;
					}
				}
			}

			IsOnNextStrike = Attributes.HasAnyFlag(SpellAttributes.OnNextMelee | SpellAttributes.OnNextMelee_2);
			// don't use Enum.HasFlag!

			IsRanged = (Attributes.HasAnyFlag(SpellAttributes.Ranged) ||
						AttributesExC.HasFlag(SpellAttributesExC.ShootRangedWeapon));

			IsRangedAbility = IsRanged && !IsTriggeredSpell;

			IsStrikeSpell = HasEffectWith(effect => effect.IsStrikeEffect);

			IsPhysicalAbility = (IsRangedAbility || IsOnNextStrike || IsStrikeSpell) && !HasEffect(SpellEffectType.SchoolDamage);

			DamageIncreasedByAP = DamageIncreasedByAP || (PowerType == PowerType.Rage && SchoolMask == DamageSchoolMask.Physical);

			GeneratesComboPoints = HasEffectWith(effect => effect.EffectType == SpellEffectType.AddComboPoints);

			IsFinishingMove =
				AttributesEx.HasAnyFlag(SpellAttributesEx.FinishingMove) ||
				HasEffectWith(effect => effect.PointsPerComboPoint > 0 && effect.EffectType != SpellEffectType.Dummy);

			TotemEffect = GetFirstEffectWith(effect => effect.HasTarget(
				ImplicitSpellTargetType.TotemAir, ImplicitSpellTargetType.TotemEarth, ImplicitSpellTargetType.TotemFire,
				ImplicitSpellTargetType.TotemWater));

			IsEnchantment = HasEffectWith(effect => effect.IsEnchantmentEffect);

			if (!IsEnchantment && EquipmentSlot == EquipmentSlot.End)
			{

				// Required Item slot for weapon abilities
                if (SpellEquippedItems != null && SpellEquippedItems.RequiredItemClass == ItemClass.Armor && SpellEquippedItems.RequiredItemSubClassMask == ItemSubClassMask.Shield)
				{
					EquipmentSlot = EquipmentSlot.OffHand;
				}
				else if (AttributesExC.HasFlag(SpellAttributesExC.RequiresOffHandWeapon))
				{
					EquipmentSlot = EquipmentSlot.OffHand;
				}
				else if ((IsRangedAbility || AttributesExC.HasFlag(SpellAttributesExC.RequiresWand)))
				{
					EquipmentSlot = EquipmentSlot.ExtraWeapon;
				}
				else if (AttributesExC.HasFlag(SpellAttributesExC.RequiresMainHandWeapon))
				{
					EquipmentSlot = EquipmentSlot.MainHand;
				}
                else if (SpellEquippedItems != null && SpellEquippedItems.RequiredItemClass == ItemClass.Weapon)
				{
                    if (SpellEquippedItems.RequiredItemSubClassMask == ItemSubClassMask.AnyMeleeWeapon)
					{
						EquipmentSlot = EquipmentSlot.MainHand;
					}
					else if (SpellEquippedItems.RequiredItemSubClassMask.HasAnyFlag(ItemSubClassMask.AnyRangedAndThrownWeapon))
					{
						EquipmentSlot = EquipmentSlot.ExtraWeapon;
					}
				}
				else if (IsPhysicalAbility)
				{
					// OnNextMelee is set but no equipment slot -> select main hand
					EquipmentSlot = EquipmentSlot.MainHand;
				}
			}

            HasIndividualCooldown = (SpellCooldowns != null && SpellCooldowns.CooldownTime > 0) ||
									(IsPhysicalAbility && !IsOnNextStrike && EquipmentSlot != EquipmentSlot.End);

			HasCooldown = HasIndividualCooldown || (SpellCooldowns != null && SpellCooldowns.CategoryCooldownTime > 0);

			//IsAoe = HasEffectWith((effect) => {
			//    if (effect.ImplicitTargetA == ImplicitTargetType.)
			//        effect.ImplicitTargetA = ImplicitTargetType.None;
			//    if (effect.ImplicitTargetB == ImplicitTargetType.Unused_EnemiesInAreaChanneledWithExceptions)
			//        effect.ImplicitTargetB = ImplicitTargetType.None;
			//    return false;
			//});

			var profEffect = GetEffect(SpellEffectType.SkillStep);
			if (profEffect != null)
			{
				TeachesApprenticeAbility = profEffect.BasePoints == 0;
			}

			IsProfession = !IsRangedAbility && Ability != null && Ability.Skill.Category == SkillCategory.Profession;
			IsEnhancer = HasEffectWith(effect => effect.IsEnhancer);
			IsFishing = HasEffectWith(effect => effect.HasTarget(ImplicitSpellTargetType.SelfFishing));
			IsSkinning = HasEffectWith(effect => effect.EffectType == SpellEffectType.Skinning);
			IsTameEffect = HasEffectWith(effect => effect.EffectType == SpellEffectType.TameCreature);

            if (AttributesEx.HasAnyFlag(SpellAttributesEx.Negative) || IsPreventionDebuff || (SpellCategories != null && SpellCategories.Mechanic.IsNegative()))
			{
				HasHarmfulEffects = true;
				HasBeneficialEffects = false;
				HarmType = HarmType.Harmful;
			}
			else
			{
				HasHarmfulEffects = HasEffectWith(effect => effect.HarmType == HarmType.Harmful);
				HasBeneficialEffects = HasEffectWith(effect => effect.HarmType == HarmType.Beneficial);
				if (HasHarmfulEffects != HasBeneficialEffects && !HasEffectWith(effect => effect.HarmType == HarmType.Neutral))
				{
					HarmType = HasHarmfulEffects ? HarmType.Harmful : HarmType.Beneficial;
				}
				else
				{
					HarmType = HarmType.Neutral;
				}
			}

			RequiresDeadTarget = HasEffect(SpellEffectType.Resurrect) || HasEffect(SpellEffectType.ResurrectFlat) || HasEffect(SpellEffectType.SelfResurrect);
			// unreliable: TargetFlags.HasAnyFlag(SpellTargetFlags.Corpse | SpellTargetFlags.PvPCorpse | SpellTargetFlags.UnitCorpse);

            CostsPower = SpellPower != null && (SpellPower.PowerCost > 0 || SpellPower.PowerCostPercentage > 0);

			CostsRunes = RuneCostEntry != null && RuneCostEntry.CostsRunes;

			HasTargets = HasEffectWith(effect => effect.HasTargets);

			CasterIsTarget = HasTargets && HasEffectWith(effect => effect.HasTarget(ImplicitSpellTargetType.Self));

			//HasSingleNotSelfTarget = 

			IsAreaSpell = HasEffectWith(effect => effect.IsAreaEffect);

			IsDamageSpell = HasHarmfulEffects && !HasBeneficialEffects && HasEffectWith(effect =>
																						effect.EffectType ==
																						SpellEffectType.Attack ||
																						effect.EffectType ==
																						SpellEffectType.EnvironmentalDamage ||
																						effect.EffectType ==
																						SpellEffectType.InstantKill ||
																						effect.EffectType ==
																						SpellEffectType.SchoolDamage ||
																						effect.IsStrikeEffect);

		    ForeachEffect(effect =>
		                      {
		                          if (effect.ChainAmplitude <= 0)
		                          {
		                              effect.ChainAmplitude = 1;
		                          }
		                      });

            IsHearthStoneSpell = HasEffectWith(effect => effect.HasTarget(ImplicitSpellTargetType.HeartstoneLocation));

			// ResurrectFlat usually has no target type set
			ForeachEffect(effect =>
							{
								if (effect.ImplicitTargetA == ImplicitSpellTargetType.None &&
									effect.EffectType == SpellEffectType.ResurrectFlat)
								{
									effect.ImplicitTargetA = ImplicitSpellTargetType.SingleFriend;
								}
							});

			Schools = Utility.GetSetIndices<DamageSchool>((uint)SchoolMask);
			if (Schools.Length == 0)
			{
				Schools = new[] { DamageSchool.Physical };
			}

		    RequiresCasterOutOfCombat = !HasHarmfulEffects && CastDelay > 0 &&
		                                (Attributes.HasFlag(SpellAttributes.CannotBeCastInCombat) ||
		                                 AttributesEx.HasFlag(SpellAttributesEx.RemainOutOfCombat) ||
                                         (SpellInterrupts != null &&
                                         SpellInterrupts.AuraInterruptFlags.HasFlag(AuraInterruptFlags.OnStartAttack)));

			if (RequiresCasterOutOfCombat)
			{
				// We fail if being attacked (among others)
                if (SpellInterrupts == null)
                    SpellInterrupts = new SpellInterrupts();
				SpellInterrupts.InterruptFlags |= InterruptFlags.OnTakeDamage;
			}

			IsThrow = AttributesExC.HasFlag(SpellAttributesExC.ShootRangedWeapon) &&
					  Attributes.HasFlag(SpellAttributes.Ranged) && Ability != null && Ability.Skill.Id == SkillId.Thrown;

			HasModifierEffects = HasModifierEffects ||
								 HasEffectWith(
									effect =>
									effect.AuraType == AuraType.AddModifierFlat || effect.AuraType == AuraType.AddModifierPercent);

			// cannot taunt players
			CanCastOnPlayer = CanCastOnPlayer && !HasEffect(AuraType.ModTaunt);

			HasAuraDependentEffects = HasEffectWith(effect => effect.IsDependentOnOtherAuras);

			ForeachEffect(effect =>
							{
								for (var i = 0; i < 3; i++)
								{
									AllAffectingMasks[i] |= effect.AffectMask[i];
								}
							});

			if (Range.MaxDist == 0)
			{
				Range.MaxDist = 5;
			}

            if(SpellTotems == null)
            {
                SpellTotems = new SpellTotems();
            }
            else if (SpellTotems.RequiredToolIds == null)
			{
                SpellTotems.RequiredToolIds = new uint[0];
			}
			else
			{
                if (SpellTotems.RequiredToolIds.Length > 0 && (SpellTotems.RequiredToolIds[0] > 0 || SpellTotems.RequiredToolIds[1] > 0))
				{
					SpellHandler.SpellsRequiringTools.Add(this);
				}
                ArrayUtil.PruneVals(ref SpellTotems.RequiredToolIds);
			}

			var skillEffect = GetFirstEffectWith(effect =>
												 effect.EffectType == SpellEffectType.SkillStep ||
												 effect.EffectType == SpellEffectType.Skill);
			if (skillEffect != null)
			{
				SkillTier = (SkillTierId)skillEffect.BasePoints;
			}
			else
			{
				SkillTier = SkillTierId.End;
			}

			ArrayUtil.PruneVals(ref SpellTotems.RequiredToolCategories);

			ForeachEffect(effect =>
							{
								if (effect.SpellEffectHandlerCreator != null)
								{
									EffectHandlerCount++;
								}
							});
			//IsHealSpell = HasEffectWith((effect) => effect.IsHealEffect);

			if (GetEffect(SpellEffectType.QuestComplete) != null)
			{
				SpellHandler.QuestCompletors.Add(this);
			}

			AISettings.InitializeAfterLoad();
		}
		#endregion

		#region Targeting
		/// <summary>
		/// Sets the AITargetHandlerDefintion of all effects
		/// </summary>
		public void OverrideCustomTargetDefinitions(TargetAdder adder, params TargetFilter[] filters)
		{
			OverrideCustomTargetDefinitions(new TargetDefinition(adder, filters));
		}

		/// <summary>
		/// Sets the CustomTargetHandlerDefintion of all effects
		/// </summary>
		public void OverrideCustomTargetDefinitions(TargetAdder adder, TargetEvaluator evaluator = null,
			params TargetFilter[] filters)
		{
			OverrideCustomTargetDefinitions(new TargetDefinition(adder, filters), evaluator);
		}

		public void OverrideCustomTargetDefinitions(TargetDefinition def, TargetEvaluator evaluator = null)
		{
			ForeachEffect(
				effect => effect.CustomTargetHandlerDefintion = def);
			if (evaluator != null)
			{
				OverrideCustomTargetEvaluators(evaluator);
			}
		}

		/// <summary>
		/// Sets the AITargetHandlerDefintion of all effects
		/// </summary>
		public void OverrideAITargetDefinitions(TargetAdder adder, params TargetFilter[] filters)
		{
			OverrideAITargetDefinitions(new TargetDefinition(adder, filters));
		}

		/// <summary>
		/// Sets the AITargetHandlerDefintion of all effects
		/// </summary>
		public void OverrideAITargetDefinitions(TargetAdder adder, TargetEvaluator evaluator = null,
			params TargetFilter[] filters)
		{
			OverrideAITargetDefinitions(new TargetDefinition(adder, filters), evaluator);
		}

		public void OverrideAITargetDefinitions(TargetDefinition def, TargetEvaluator evaluator = null)
		{
			ForeachEffect(
				effect => effect.AITargetHandlerDefintion = def);
			if (evaluator != null)
			{
				OverrideCustomTargetEvaluators(evaluator);
			}
		}

		/// <summary>
		/// Sets the CustomTargetEvaluator of all effects
		/// </summary>
		public void OverrideCustomTargetEvaluators(TargetEvaluator eval)
		{
			ForeachEffect(
				effect => effect.CustomTargetEvaluator = eval);
		}

		/// <summary>
		/// Sets the AITargetEvaluator of all effects
		/// </summary>
		public void OverrideAITargetEvaluators(TargetEvaluator eval)
		{
			ForeachEffect(
				effect => effect.AITargetEvaluator = eval);
		}

		#endregion

		#region Manage Effects
		public void ForeachEffect(Action<SpellEffect> callback)
		{
			for (int i = 0; i < Effects.Length; i++)
			{
				var effect = Effects[i];
				callback(effect);
			}
		}

		public bool HasEffectWith(Predicate<SpellEffect> predicate)
		{
			for (var i = 0; i < Effects.Length; i++)
			{
				var effect = Effects[i];
				if (predicate(effect))
				{
					return true;
				}
			}
			return false;
		}

		public bool HasEffect(SpellEffectType type)
		{
			return GetEffect(type, false) != null;
		}

		public bool HasEffect(AuraType type)
		{
			return GetEffect(type, false) != null;
		}

		/// <summary>
		/// Returns the first SpellEffect of the given Type within this Spell
		/// </summary>
		public SpellEffect GetEffect(SpellEffectType type)
		{
			return GetEffect(type, true);
		}

		/// <summary>
		/// Returns the first SpellEffect of the given Type within this Spell
		/// </summary>
		public SpellEffect GetEffect(SpellEffectType type, bool force)
		{
			foreach (var effect in Effects)
			{
				if (effect.EffectType == type)
				{
					return effect;
				}
			}
			//ContentHandler.OnInvalidClientData("Spell {0} does not contain Effect of type {1}", this, type);
			//return null;
			if (!init1 && force)
			{
				throw new ContentException("Spell {0} does not contain Effect of type {1}", this, type);
			}
			return null;
		}

		/// <summary>
		/// Returns the first SpellEffect of the given Type within this Spell
		/// </summary>
		public SpellEffect GetEffect(AuraType type)
		{
			return GetEffect(type, ContentMgr.ForceDataPresence);
		}

		/// <summary>
		/// Returns the first SpellEffect of the given Type within this Spell
		/// </summary>
		public SpellEffect GetEffect(AuraType type, bool force)
		{
			foreach (var effect in Effects)
			{
				if (effect.AuraType == type)
				{
					return effect;
				}
			}
			//ContentHandler.OnInvalidClientData("Spell {0} does not contain Aura Effect of type {1}", this, type);
			//return null;
			if (!init1 && force)
			{
				throw new ContentException("Spell {0} does not contain Aura Effect of type {1}", this, type);
			}
			return null;
		}

		public SpellEffect GetFirstEffectWith(Predicate<SpellEffect> predicate)
		{
			foreach (var effect in Effects)
			{
				if (predicate(effect))
				{
					return effect;
				}
			}
			return null;
		}

		public SpellEffect[] GetEffectsWhere(Predicate<SpellEffect> predicate)
		{
			List<SpellEffect> effects = null;
			foreach (var effect in Effects)
			{
				if (predicate(effect))
				{
					if (effects == null)
					{
						effects = new List<SpellEffect>();
					}
					effects.Add(effect);
				}
			}
			return effects != null ? effects.ToArray() : null;
		}

		///// <summary>
		///// Removes the first Effect of the given Type and replace it with a new one which will be returned.
		///// Appends a new one if none of the given type was found.
		///// </summary>
		///// <param name="type"></param>
		///// <returns></returns>
		//public SpellEffect ReplaceEffect(SpellEffectType type, SpellEffectType newType, ImplicitTargetType target)
		//{
		//    for (var i = 0; i < Effects.Length; i++)
		//    {
		//        var effect = Effects[i];
		//        if (effect.EffectType == type)
		//        {
		//            return Effects[i] = new SpellEffect();
		//        }
		//    }
		//    return AddEffect(type, target);
		//}

		/// <summary>
		/// Adds a new Effect to this Spell
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		public SpellEffect AddEffect(SpellEffectHandlerCreator creator, ImplicitSpellTargetType target)
		{
			var effect = AddEffect(SpellEffectType.Dummy, target);
			effect.SpellEffectHandlerCreator = creator;
			return effect;
		}

		/// <summary>
		/// Adds a new Effect to this Spell
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		public SpellEffect AddEffect(SpellEffectType type, ImplicitSpellTargetType target)
		{
			var effect = new SpellEffect(this, EffectIndex.Custom) { EffectType = type };
			var effects = new SpellEffect[Effects.Length + 1];
			Array.Copy(Effects, effects, Effects.Length);
			Effects = effects;
			Effects[effects.Length - 1] = effect;

			effect.ImplicitTargetA = target;
			return effect;
		}

		/// <summary>
		/// Adds a SpellEffect that will trigger the given Spell on oneself
		/// </summary>
		public SpellEffect AddTriggerSpellEffect(SpellId triggerSpell)
		{
			return AddTriggerSpellEffect(triggerSpell, ImplicitSpellTargetType.Self);
		}

		/// <summary>
		/// Adds a SpellEffect that will trigger the given Spell on the given type of target
		/// </summary>
		public SpellEffect AddTriggerSpellEffect(SpellId triggerSpell, ImplicitSpellTargetType targetType)
		{
			var effect = AddEffect(SpellEffectType.TriggerSpell, targetType);
			effect.TriggerSpellId = triggerSpell;
			return effect;
		}

		/// <summary>
		/// Adds a SpellEffect that will trigger the given Spell on oneself
		/// </summary>
		public SpellEffect AddPeriodicTriggerSpellEffect(SpellId triggerSpell)
		{
			return AddPeriodicTriggerSpellEffect(triggerSpell, ImplicitSpellTargetType.Self);
		}

		/// <summary>
		/// Adds a SpellEffect that will trigger the given Spell on the given type of target
		/// </summary>
		public SpellEffect AddPeriodicTriggerSpellEffect(SpellId triggerSpell, ImplicitSpellTargetType targetType)
		{
			var effect = AddAuraEffect(AuraType.PeriodicTriggerSpell);
			effect.TriggerSpellId = triggerSpell;
			effect.ImplicitTargetA = targetType;
			return effect;
		}

		/// <summary>
		/// Adds a SpellEffect that will be applied to an Aura to be casted on oneself
		/// </summary>
		public SpellEffect AddAuraEffect(AuraType type)
		{
			return AddAuraEffect(type, ImplicitSpellTargetType.Self);
		}

		/// <summary>
		/// Adds a SpellEffect that will be applied to an Aura to be casted on the given type of target
		/// </summary>
		public SpellEffect AddAuraEffect(AuraType type, ImplicitSpellTargetType targetType)
		{
			var effect = AddEffect(SpellEffectType.ApplyAura, targetType);
			effect.AuraType = type;
			return effect;
		}

		/// <summary>
		/// Adds a SpellEffect that will be applied to an Aura to be casted on the given type of target
		/// </summary>
		public SpellEffect AddAuraEffect(AuraEffectHandlerCreator creator)
		{
			return AddAuraEffect(creator, ImplicitSpellTargetType.Self);
		}

		/// <summary>
		/// Adds a SpellEffect that will be applied to an Aura to be casted on the given type of target
		/// </summary>
		public SpellEffect AddAuraEffect(AuraEffectHandlerCreator creator, ImplicitSpellTargetType targetType)
		{
			var effect = AddEffect(SpellEffectType.ApplyAura, targetType);
			effect.AuraType = AuraType.Dummy;
			effect.AuraEffectHandlerCreator = creator;
			return effect;
		}

		public void ClearEffects()
		{
			Effects = new SpellEffect[0];
		}

		public SpellEffect RemoveEffect(AuraType type)
		{
			var effect = GetEffect(type);
			RemoveEffect(effect);
			return effect;
		}

		public SpellEffect RemoveEffect(SpellEffectType type)
		{
			var effect = GetEffect(type);
			RemoveEffect(effect);
			return effect;
		}

		public void RemoveEffect(SpellEffect toRemove)
		{
			var effects = new SpellEffect[Effects.Length - 1];
			var e = 0;
			foreach (var effct in Effects)
			{
				if (effct != toRemove)
				{
					effects[e++] = effct;
				}
			}
			Effects = effects;
		}

		public void RemoveEffect(Func<SpellEffect, bool> predicate)
		{
			foreach (var effct in Effects.ToArray())
			{
				if (predicate(effct))
				{
					RemoveEffect(effct);
				}
			}
		}
		#endregion

		#region Misc Methods & Props
		public bool IsAffectedBy(Spell spell)
		{
			return MatchesMask(spell.AllAffectingMasks);
		}

		public bool MatchesMask(uint[] masks)
		{
            if (SpellClassOptions == null)
                return false;

			for (var i = 0; i < SpellClassOptions.SpellClassMask.Length; i++)
			{
                if ((masks[i] & SpellClassOptions.SpellClassMask[i]) != 0)
				{
					return true;
				}
			}
			return false;
		}

		public int GetMaxLevelDiff(int casterLevel)
		{
            if (SpellLevels == null)
                return 0;

            if (SpellLevels.MaxLevel >= SpellLevels.BaseLevel && SpellLevels.MaxLevel < casterLevel)
			{
                return SpellLevels.MaxLevel - SpellLevels.BaseLevel;
			}
            return Math.Abs(casterLevel - SpellLevels.BaseLevel);
		}

		public int CalcBasePowerCost(Unit caster)
		{
            if (SpellPower == null)
                return GetMaxLevelDiff(caster.Level);

            var cost = SpellPower.PowerCost + (SpellPower.PowerCostPerlevel * GetMaxLevelDiff(caster.Level));
            if (SpellPower.PowerCostPercentage > 0)
			{
                cost += (SpellPower.PowerCostPercentage *
					((PowerType == PowerType.Health ? caster.BaseHealth : caster.BasePower))) / 100;
			}
			return cost;
		}

		public int CalcPowerCost(Unit caster, DamageSchool school)
		{
			return caster.GetPowerCost(school, this, CalcBasePowerCost(caster));
		}

		public bool ShouldShowToClient()
		{
			return IsRangedAbility || Visual != 0 || Visual2 != 0 ||
				   IsChanneled || CastDelay > 0 || HasCooldown;
			// || (!IsPassive && IsAura)
			;
		}

		public void SetDuration(int duration)
		{
			Durations.Min = Durations.Max = duration;
		}

		/// <summary>
		/// Returns the max duration for this Spell in milliseconds, 
		/// including all modifiers.
		/// </summary>
		public int GetDuration(ObjectReference caster)
		{
			return GetDuration(caster, null);
		}

		/// <summary>
		/// Returns the max duration for this Spell in milliseconds, 
		/// including all modifiers.
		/// </summary>
		public int GetDuration(ObjectReference caster, Unit target)
		{
			var millis = Durations.Min;
			//if (Durations.LevelDelta > 0)
			//{
			//	millis += (int)caster.Level * Durations.LevelDelta;
			//	if (Durations.Max > 0 && millis > Durations.Max)
			//	{
			//		millis = Durations.Max;
			//	}
			//}

			if (Durations.Max > Durations.Min && IsFinishingMove && caster.UnitMaster != null)
			{
				// For some finishing moves, Duration depends on Combopoints
				millis += caster.UnitMaster.ComboPoints * ((Durations.Max - Durations.Min) / 5);
			}

			if (target != null && SpellCategories.Mechanic != SpellMechanic.None)
			{
                var mod = target.GetMechanicDurationMod(SpellCategories.Mechanic);
				if (mod != 0)
				{
					millis = UnitUpdates.GetMultiMod(mod / 100f, millis);
				}
			}

			var unit = caster.UnitMaster;
			if (unit != null)
			{
				millis = unit.Auras.GetModifiedInt(SpellModifierType.Duration, this, millis);
			}
			return millis;
		}

		public bool IsAffectedByInvulnerability
		{
			get { return !Attributes.HasFlag(SpellAttributes.UnaffectedByInvulnerability); }
		}

		#endregion

		#region Verbose / Debug

		/// <summary>
		/// Fully qualified name
		/// </summary>
		public string FullName
		{
			get
			{
				// TODO: Item-spell?
				string fullName;

				bool isTalent = Talent != null;
				bool isSkill = Ability != null;

				if (isTalent)
				{
					fullName = Talent.FullName;
				}
				else
				{
					fullName = Name;
				}

				if (isSkill && !isTalent && Ability.Skill.Category != SkillCategory.Language &&
					Ability.Skill.Category != SkillCategory.Invalid)
				{
					fullName = Ability.Skill.Category + " " + fullName;
				}

				if (IsTeachSpell &&
					!Name.StartsWith("Learn", StringComparison.InvariantCultureIgnoreCase))
				{
					fullName = "Learn " + fullName;
				}
				else if (IsTriggeredSpell)
				{
					fullName = "Effect: " + fullName;
				}

				if (isSkill)
				{
				}
				else if (IsDeprecated)
				{
					fullName = "Unused " + fullName;
				}
				else if (Description != null && Description.Length == 0)
				{
					//fullName = "No Learn " + fullName;
				}


				return fullName;
			}
		}
		/// <summary>
		/// Spells that contain "zzOld", "test", "unused"
		/// </summary>
		public bool IsDeprecated
		{
			get
			{
				return IsDeprecatedSpellName(Name);
			}
		}

		public static bool IsDeprecatedSpellName(string name)
		{
			return name.IndexOf("test", StringComparison.InvariantCultureIgnoreCase) > -1 ||
					   name.StartsWith("zzold", StringComparison.InvariantCultureIgnoreCase) ||
					   name.IndexOf("unused", StringComparison.InvariantCultureIgnoreCase) > -1;
		}

		public override string ToString()
		{
			return FullName + (RankDesc != "" ? " " + RankDesc : "") + " (Id: " + Id + ")";
		}

		#endregion

		#region Dump
		public void Dump(TextWriter writer, string indent)
		{
			writer.WriteLine("Spell: " + this + " [" + SpellId + "]");

			if (SpellCategories.Category != 0)
			{
                writer.WriteLine(indent + "Category: " + SpellCategories.Category);
			}
			if (Line != null)
			{
				writer.WriteLine(indent + "Line: " + Line);
			}
			if (PreviousRank != null)
			{
				writer.WriteLine(indent + "Previous Rank: " + PreviousRank);
			}
			if (NextRank != null)
			{
				writer.WriteLine(indent + "Next Rank: " + NextRank);
			}
            if (SpellCategories.DispelType != 0)
			{
                writer.WriteLine(indent + "DispelType: " + SpellCategories.DispelType);
			}
            if (SpellCategories.Mechanic != SpellMechanic.None)
			{
                writer.WriteLine(indent + "Mechanic: " + SpellCategories.Mechanic);
			}
			if (Attributes != SpellAttributes.None)
			{
				writer.WriteLine(indent + "Attributes: " + Attributes);
			}
			if (AttributesEx != SpellAttributesEx.None)
			{
				writer.WriteLine(indent + "AttributesEx: " + AttributesEx);
			}
			if (AttributesExB != SpellAttributesExB.None)
			{
				writer.WriteLine(indent + "AttributesExB: " + AttributesExB);
			}
			if (AttributesExC != SpellAttributesExC.None)
			{
				writer.WriteLine(indent + "AttributesExC: " + AttributesExC);
			}
			if (AttributesExD != SpellAttributesExD.None)
			{
				writer.WriteLine(indent + "AttributesExD: " + AttributesExD);
			}
            if (AttributesExE != SpellAttributesExE.None)
            {
                writer.WriteLine(indent + "AttributesExE: " + AttributesExE);
            }
            if (AttributesExF != SpellAttributesExF.None)
            {
                writer.WriteLine(indent + "AttributesExF: " + AttributesExF);
            }
            if (AttributesExG != SpellAttributesExG.None)
            {
                writer.WriteLine(indent + "AttributesExG: " + AttributesExG);
            }
            if (AttributesExH != SpellAttributesExH.None)
            {
                writer.WriteLine(indent + "AttributesExH: " + AttributesExH);
            }
			if ((int)SpellShapeshift.RequiredShapeshiftMask != 0)
			{
                writer.WriteLine(indent + "ShapeshiftMask: " + SpellShapeshift.RequiredShapeshiftMask);
			}
            if ((int)SpellShapeshift.ExcludeShapeshiftMask != 0)
			{
                writer.WriteLine(indent + "ExcludeShapeshiftMask: " + SpellShapeshift.ExcludeShapeshiftMask);
			}
			if ((int)SpellTargetRestrictions.TargetFlags != 0)
			{
                writer.WriteLine(indent + "TargetType: " + SpellTargetRestrictions.TargetFlags);
			}
            if ((int)SpellTargetRestrictions.CreatureMask != 0)
			{
                writer.WriteLine(indent + "TargetUnitTypes: " + SpellTargetRestrictions.CreatureMask);
			}
            if ((int)SpellCastingRequirements.RequiredSpellFocus != 0)
			{
                writer.WriteLine(indent + "RequiredSpellFocus: " + SpellCastingRequirements.RequiredSpellFocus);
			}
            if (SpellCastingRequirements.FacingFlags != 0)
			{
                writer.WriteLine(indent + "FacingFlags: " + SpellCastingRequirements.FacingFlags);
			}
            if ((int)SpellAuraRestrictions.RequiredCasterAuraState != 0)
			{
                writer.WriteLine(indent + "RequiredCasterAuraState: " + SpellAuraRestrictions.RequiredCasterAuraState);
			}
            if ((int)SpellAuraRestrictions.RequiredTargetAuraState != 0)
			{
                writer.WriteLine(indent + "RequiredTargetAuraState: " + SpellAuraRestrictions.RequiredTargetAuraState);
			}
            if ((int)SpellAuraRestrictions.ExcludeCasterAuraState != 0)
			{
                writer.WriteLine(indent + "ExcludeCasterAuraState: " + SpellAuraRestrictions.ExcludeCasterAuraState);
			}
            if ((int)SpellAuraRestrictions.ExcludeTargetAuraState != 0)
			{
                writer.WriteLine(indent + "ExcludeTargetAuraState: " + SpellAuraRestrictions.ExcludeTargetAuraState);
			}

            if (SpellAuraRestrictions.RequiredCasterAuraId != 0)
			{
                writer.WriteLine(indent + "RequiredCasterAuraId: " + SpellAuraRestrictions.RequiredCasterAuraId);
			}
            if (SpellAuraRestrictions.RequiredTargetAuraId != 0)
			{
                writer.WriteLine(indent + "RequiredTargetAuraId: " + SpellAuraRestrictions.RequiredTargetAuraId);
			}
            if (SpellAuraRestrictions.ExcludeCasterAuraId != 0)
			{
                writer.WriteLine(indent + "ExcludeCasterAuraSpellId: " + SpellAuraRestrictions.ExcludeCasterAuraId);
			}
            if (SpellAuraRestrictions.ExcludeTargetAuraId != 0)
			{
                writer.WriteLine(indent + "ExcludeTargetAuraSpellId: " + SpellAuraRestrictions.ExcludeTargetAuraId);
			}


			if ((int)CastDelay != 0)
			{
				writer.WriteLine(indent + "StartTime: " + CastDelay);
			}
			if (SpellCooldowns.CooldownTime > 0)
			{
                writer.WriteLine(indent + "CooldownTime: " + SpellCooldowns.CooldownTime);
			}
            if (SpellCooldowns.CategoryCooldownTime > 0)
			{
                writer.WriteLine(indent + "CategoryCooldownTime: " + SpellCooldowns.CategoryCooldownTime);
			}

            if ((int)SpellInterrupts.InterruptFlags != 0)
			{
                writer.WriteLine(indent + "InterruptFlags: " + SpellInterrupts.InterruptFlags);
			}
            if ((int)SpellInterrupts.AuraInterruptFlags != 0)
			{
                writer.WriteLine(indent + "AuraInterruptFlags: " + SpellInterrupts.AuraInterruptFlags);
			}
            if ((int)SpellInterrupts.ChannelInterruptFlags != 0)
			{
                writer.WriteLine(indent + "ChannelInterruptFlags: " + SpellInterrupts.ChannelInterruptFlags);
			}
			if (SpellAuraOptions.ProcTriggerFlags != ProcTriggerFlags.None)
			{
                writer.WriteLine(indent + "ProcTriggerFlags: " + SpellAuraOptions.ProcTriggerFlags);

                if (SpellAuraOptions.ProcHitFlags != ProcHitFlags.None)
				{
                    writer.WriteLine(indent + "ProcHitFlags: " + SpellAuraOptions.ProcHitFlags);
				}
			}
            if ((int)SpellAuraOptions.ProcChance != 0)
			{
                writer.WriteLine(indent + "ProcChance: " + SpellAuraOptions.ProcChance);
			}


            if (SpellAuraOptions.ProcCharges != 0)
			{
                writer.WriteLine(indent + "ProcCharges: " + SpellAuraOptions.ProcCharges);
			}
            if (SpellLevels != null)
            {
                if (SpellLevels.MaxLevel != 0)
                {
                    writer.WriteLine(indent + "MaxLevel: " + SpellLevels.MaxLevel);
                }
                if (SpellLevels.BaseLevel != 0)
                {
                    writer.WriteLine(indent + "BaseLevel: " + SpellLevels.BaseLevel);
                }
                if (SpellLevels.Level != 0)
                {
                    writer.WriteLine(indent + "Level: " + SpellLevels.Level);
                }
            }
		    if (Durations.Max > 0)
			{
				writer.WriteLine(indent + "Duration: " + Durations.Min + " - " + Durations.Max + " (" + Durations.LevelDelta + ")");
			}
			if (Visual != 0u)
			{
				writer.WriteLine(indent + "Visual: " + Visual);
			}

			if ((int)PowerType != 0)
			{
				writer.WriteLine(indent + "PowerType: " + PowerType);
			}
			if (SpellPower.PowerCost != 0)
			{
                writer.WriteLine(indent + "PowerCost: " + SpellPower.PowerCost);
			}
            if (SpellPower.PowerCostPerlevel != 0)
			{
                writer.WriteLine(indent + "PowerCostPerlevel: " + SpellPower.PowerCostPerlevel);
			}
            if (SpellPower.PowerPerSecond != 0)
			{
                writer.WriteLine(indent + "PowerPerSecond: " + SpellPower.PowerPerSecond);
			}
            if (SpellPower.PowerCostPercentage != 0)
			{
                writer.WriteLine(indent + "PowerCostPercentage: " + SpellPower.PowerCostPercentage);
			}

			if (Range.MinDist != 0 || Range.MaxDist != DefaultSpellRange)
			{
				writer.WriteLine(indent + "Range: " + Range.MinDist + " - " + Range.MaxDist);
			}
			if ((int)ProjectileSpeed != 0)
			{
				writer.WriteLine(indent + "ProjectileSpeed: " + ProjectileSpeed);
			}
			if ((int)SpellClassOptions.ModalNextSpell != 0)
			{
                writer.WriteLine(indent + "ModalNextSpell: " + SpellClassOptions.ModalNextSpell);
			}
			if (SpellAuraOptions.MaxStackCount != 0)
			{
                writer.WriteLine(indent + "MaxStackCount: " + SpellAuraOptions.MaxStackCount);
			}

			if (RequiredTools != null)
			{
				writer.WriteLine(indent + "RequiredTools:");
				foreach (var tool in RequiredTools)
				{
					writer.WriteLine(indent + "\t" + tool);
				}
			}
			if (SpellEquippedItems.RequiredItemClass != ItemClass.None)
			{
                writer.WriteLine(indent + "RequiredItemClass: " + SpellEquippedItems.RequiredItemClass);
			}
            if ((int)SpellEquippedItems.RequiredItemInventorySlotMask != 0)
			{
                writer.WriteLine(indent + "RequiredItemInventorySlotMask: " + SpellEquippedItems.RequiredItemInventorySlotMask);
			}
            if ((int)SpellEquippedItems.RequiredItemSubClassMask != -1 && (int)SpellEquippedItems.RequiredItemSubClassMask != 0)
			{
                writer.WriteLine(indent + "RequiredItemSubClassMask: " + SpellEquippedItems.RequiredItemSubClassMask);
			}


			if ((int)Visual2 != 0)
			{
				writer.WriteLine(indent + "Visual2: " + Visual2);
			}

			if (SpellCategories.StartRecoveryCategory != 0)
			{
                writer.WriteLine(indent + "StartRecoveryCategory: " + SpellCategories.StartRecoveryCategory);
			}
			if (SpellCooldowns.StartRecoveryTime != 0)
			{
                writer.WriteLine(indent + "StartRecoveryTime: " + SpellCooldowns.StartRecoveryTime);
			}
            if (SpellTargetRestrictions.MaxTargetLevel != 0)
			{
                writer.WriteLine(indent + "MaxTargetLevel: " + SpellTargetRestrictions.MaxTargetLevel);
			}
			if ((int)SpellClassOptions.SpellClassSet != 0)
			{
                writer.WriteLine(indent + "SpellClassSet: " + SpellClassOptions.SpellClassSet);
			}

            if (SpellClassOptions.SpellClassMask[0] != 0 || SpellClassOptions.SpellClassMask[1] != 0 || SpellClassOptions.SpellClassMask[2] != 0)
			{
                writer.WriteLine(indent + "SpellClassMask: {0}{1}{2}", SpellClassOptions.SpellClassMask[0].ToString("X8"), SpellClassOptions.SpellClassMask[1].ToString("X8"), SpellClassOptions.SpellClassMask[2].ToString("X8"));
			}

			/*if ((int)FamilyFlags != 0)
			{
				writer.WriteLine(indent + "FamilyFlags: " + FamilyFlags);
			}*/
            if ((int)SpellTargetRestrictions.MaxTargets != 0)
			{
                writer.WriteLine(indent + "MaxTargets: " + SpellTargetRestrictions.MaxTargets);
			}

			if (SpellShapeshift.StanceBarOrder != 0)
			{
                writer.WriteLine(indent + "StanceBarOrder: " + SpellShapeshift.StanceBarOrder);
			}

			if ((int)SpellCategories.DefenseType != 0)
			{
                writer.WriteLine(indent + "DefenseType: " + SpellCategories.DefenseType);
			}

			if (HarmType != HarmType.Neutral)
			{
				writer.WriteLine(indent + "HarmType: " + HarmType);
			}

            if ((int)SpellCategories.PreventionType != 0)
			{
                writer.WriteLine(indent + "PreventionType: " + SpellCategories.PreventionType);
			}

            //TODO: Change to Spell.Effects.ChainAmplitude
            /*
			if (DamageMultipliers.Any(mult => mult != 1))
			{
				writer.WriteLine(indent + "DamageMultipliers: " + DamageMultipliers.ToString(", "));
			}
             */

			for (int i = 0; i < SpellTotems.RequiredToolCategories.Length; i++)
			{
                if (SpellTotems.RequiredToolCategories[i] != 0)
                    writer.WriteLine(indent + "RequiredTotemCategoryId[" + i + "]: " + SpellTotems.RequiredToolCategories[i]);
			}

			if ((int)SpellCastingRequirements.AreaGroupId != 0)
			{
                writer.WriteLine(indent + "AreaGroupId: " + SpellCastingRequirements.AreaGroupId);
			}

			if ((int)SchoolMask != 0)
			{
				writer.WriteLine(indent + "SchoolMask: " + SchoolMask);
			}

			if (RuneCostEntry != null)
			{
				writer.WriteLine(indent + "RuneCostId: " + RuneCostEntry.Id);
				var ind = indent + "\t";
				var rcosts = new List<String>(3);
				if (RuneCostEntry.CostPerType[(int)RuneType.Blood] != 0)
					rcosts.Add(string.Format("Blood: {0}", RuneCostEntry.CostPerType[(int)RuneType.Blood]));
				if (RuneCostEntry.CostPerType[(int)RuneType.Unholy] != 0)
					rcosts.Add(string.Format("Unholy: {0}", RuneCostEntry.CostPerType[(int)RuneType.Unholy]));
				if (RuneCostEntry.CostPerType[(int)RuneType.Frost] != 0)
					rcosts.Add(string.Format("Frost: {0}", RuneCostEntry.CostPerType[(int)RuneType.Frost]));
				writer.WriteLine(ind + "Runes - {0}", rcosts.Count == 0 ? "<None>" : rcosts.ToString(", "));
				writer.WriteLine(ind + "RunicPowerGain: {0}", RuneCostEntry.RunicPowerGain);
			}
			if (MissileId != 0)
			{
				writer.WriteLine(indent + "MissileId: " + MissileId);
			}


			if (!string.IsNullOrEmpty(Description))
			{
				writer.WriteLine(indent + "Desc: " + Description);
			}

		    if (SpellReagents != null)
		    {
		        if (SpellReagents.Reagents.Length > 0)
		        {
		            writer.WriteLine(indent + "Reagents: " + SpellReagents.Reagents.ToString(", "));
		        }
		    }

		    if (Ability != null)
			{
				writer.WriteLine(indent + string.Format("Skill: {0}", Ability.SkillInfo));
			}

			if (Talent != null)
			{
				writer.WriteLine(indent + string.Format("TalentTree: {0}", Talent.Tree));
			}

			writer.WriteLine();
			foreach (var effect in Effects)
			{
				effect.DumpInfo(writer, "\t\t");
			}
		}
		#endregion

		public bool IsBeneficialFor(ObjectReference casterReference, WorldObject target)
		{
			return IsBeneficial || (IsNeutral && (casterReference.Object == null || !casterReference.Object.MayAttack(target)));
		}

		public bool IsHarmfulFor(ObjectReference casterReference, WorldObject target)
		{
			return IsHarmful || (IsNeutral && casterReference.Object != null && casterReference.Object.MayAttack(target));
		}

		public bool IsBeneficial
		{
			get { return HarmType == HarmType.Beneficial; }
		}

		public bool IsHarmful
		{
			get { return HarmType == HarmType.Harmful; }
		}

		public bool IsNeutral
		{
			get { return HarmType == HarmType.Neutral; }
		}

		public override bool Equals(object obj)
		{
			return obj is Spell && ((Spell)obj).Id == Id;
		}

		public override int GetHashCode()
		{
			return (int)Id;
		}

		#region ISpellGroup
		public IEnumerator<Spell> GetEnumerator()
		{
			return new SingleEnumerator<Spell>(this);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
		#endregion

		#region Spell Alternatives

		#endregion

		protected Spell Clone()
		{
			return (Spell)MemberwiseClone();
		}
	}
}