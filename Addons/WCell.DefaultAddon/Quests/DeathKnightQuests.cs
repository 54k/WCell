﻿using System.Linq;
using WCell.Constants;
using WCell.Constants.Items;
using WCell.Constants.NPCs;
using WCell.Constants.Spells;
using WCell.Core.Initialization;
using WCell.RealmServer.Entities;
using WCell.RealmServer.NPCs;
using WCell.RealmServer.Quests;
using WCell.RealmServer.Spells;
using WCell.RealmServer.Spells.Auras;
using WCell.RealmServer.Spells.Targeting;
using WCell.Util.Graphics;

namespace WCell.Addons.Default.Quests
{
	public static class DeathKnightQuests
	{
		#region EmblazonRuneBlade

		private static SpellId _emblazonRunebladeId = SpellId.EmblazonRuneblade_3;
		private static SpellId[] _emblazonRunebladeLearnSpellIds = { 
																	   SpellId.ClassSkillRuneOfRazorice,
																	   SpellId.ClassSkillRuneOfCinderglacier,
																	   SpellId.ClassSkillRuneforging};

		/// <summary>
		/// 
		/// </summary>
		public static void IsRunebladeTriggerNPC(SpellEffectHandler effectHandler, WorldObject target, ref SpellFailedReason failedReason)
		{
			if(!(target is NPC))
			{
				failedReason =  SpellFailedReason.BadTargets;
				return;
			}

			var entryid = ((NPC) target).EntryId;
			if(entryid == (uint)NPCId.RuneforgeSE)
			{
				return;
			}

			if(entryid == (uint)NPCId.RuneforgeSW)
			{
				failedReason =  SpellFailedReason.Ok;
				return;
			}
			failedReason = SpellFailedReason.BadTargets;
		}

        [Initialization]
        [DependentInitialization(typeof(QuestMgr))]
        public static void FixIt()
        {
			//The Emblazoned Runeblade
        	var quest = QuestMgr.GetTemplate(12619);
        	quest.QuestFinished += EmblazonRuneBladeQuestFinished;
        }

    	private static void EmblazonRuneBladeQuestFinished(Quest obj)
    	{
    		var chr = obj.Owner;
			if(chr == null)
				return;

			foreach (var spell in _emblazonRunebladeLearnSpellIds.Select(SpellHandler.Get).Where(spell => spell != null))
			{
				chr.PlayerSpells.AddSpellRequirements(spell);
				chr.Spells.AddSpell(spell);
			}
    	}

    	[Initialization(InitializationPass.Second)]
		public static void FixSpells()
		{
			var emblazonRuneblade = SpellHandler.Get(_emblazonRunebladeId);
			//need to be able to add multiple targets by Id that are in range
			//emblazonRuneblade.RequiredTargetIds = {(uint)NPCId.RuneforgeSE, (uint)NPCId.RuneforgeSW };
			var effect = emblazonRuneblade.GetEffect(SpellEffectType.ApplyAura);
			effect.Radius = 8;
			effect.AuraEffectHandlerCreator = () => new EmblazonRuneBladeAuraHandler();
			emblazonRuneblade.RequiredTargetType = RequiredSpellTargetType.NPCAlive;
			emblazonRuneblade.OverrideCustomTargetDefinitions(
				DefaultTargetAdders.AddAreaSource,
				IsRunebladeTriggerNPC);
		}

		[Initialization]
		[DependentInitialization(typeof(NPCMgr))]
		public static void FixThem()
		{
		}
		#endregion
    }

	#region EmblazonRuneBladeAuraHandler
	public class EmblazonRuneBladeAuraHandler : AuraEffectHandler
	{
		protected override void Apply()
		{
			if (!(m_aura.CasterUnit is Character))
				return;

			var chr = m_aura.CasterUnit as Character;
			var npc = chr.ChannelObject;
			npc.SpellCast.TriggerSelf(SpellId.ShadowStorm_3);

			var runebladedSwordNPC = chr.GetNearbyNPC(NPCId.RunebladedSword);
			if (runebladedSwordNPC != null)
			{
				runebladedSwordNPC.MovementFlags |= MovementFlags.DisableGravity;

				var acherusDummy = chr.GetNearbyNPC(NPCId.AcherusDummy);
				if(acherusDummy != null)
					runebladedSwordNPC.SpellCast.Trigger(SpellId.Rotate_2, acherusDummy);

				runebladedSwordNPC.SpellCast.TriggerSelf(SpellId.ShadowStorm_3);
			}

			base.Apply();
		}

		protected override void Remove(bool cancelled)
		{
			if (!cancelled)
			{
				if (!(m_aura.CasterUnit is Character))
					return;

				var chr = m_aura.CasterUnit as Character;

				chr.SpellCast.TriggerSelf(SpellId.EmblazonRuneblade_4);
			}
			
			base.Remove(cancelled);
		}

	}
	#endregion
}
