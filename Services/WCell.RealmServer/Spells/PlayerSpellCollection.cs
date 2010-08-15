using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Castle.ActiveRecord;
using WCell.Constants.Spells;
using WCell.RealmServer.Entities;
using WCell.RealmServer.GameObjects;
using WCell.RealmServer.GameObjects.GOEntries;
using WCell.RealmServer.Items;
using WCell.RealmServer.Spells.Auras;
using WCell.RealmServer.Spells.Auras.Misc;
using WCell.RealmServer.Talents;
using WCell.Util.Threading;
using WCell.RealmServer.Database;
using WCell.Util;
using WCell.RealmServer.Spells.Auras.Handlers;

namespace WCell.RealmServer.Spells
{
	public class PlayerSpellCollection : SpellCollection
	{
		static readonly IEnumerable<ISpellIdCooldown> EmptyIdCooldownArray = new ISpellIdCooldown[0];
		static readonly IEnumerable<ISpellCategoryCooldown> EmptyCatCooldownArray = new ISpellCategoryCooldown[0];

		private Timer m_offlineCooldownTimer;
		private object m_lock;
		private uint m_ownerId;

		/// <summary>
		/// Whether to send Update Packets
		/// </summary>
		protected bool m_sendPackets;

		/// <summary>
		/// All current Spell-cooldowns. 
		/// Each SpellId has an expiry time associated with it
		/// </summary>
		protected Dictionary<uint, ISpellIdCooldown> m_idCooldowns;
		/// <summary>
		/// All current category-cooldowns. 
		/// Each category has an expiry time associated with it
		/// </summary>
		protected Dictionary<uint, ISpellCategoryCooldown> m_categoryCooldowns;

		/// <summary>
		/// The runes of this Player (if any)
		/// </summary>
		private readonly RuneSet m_runes;

		public PlayerSpellCollection(Character owner)
			: base(owner)
		{
			m_sendPackets = false;
			if (owner.Class == Constants.ClassId.DeathKnight)
			{
				m_runes = new RuneSet(owner);
			}
		}

		public IEnumerable<ISpellIdCooldown> IdCooldowns
		{
			get { return m_idCooldowns != null ? m_idCooldowns.Values : EmptyIdCooldownArray; }
		}

		public IEnumerable<ISpellCategoryCooldown> CategoryCooldowns
		{
			get { return m_categoryCooldowns != null ? m_categoryCooldowns.Values : EmptyCatCooldownArray; }
		}

		public int IdCooldownCount
		{
			get { return m_idCooldowns != null ? m_idCooldowns.Count : 0; }
		}

		public int CategoryCooldownCount
		{
			get { return m_categoryCooldowns != null ? m_categoryCooldowns.Count : 0; }
		}

		/// <summary>
		/// Owner as Character
		/// </summary>
		public Character OwnerChar
		{
			get { return (Character)Owner; }
		}

		/// <summary>
		/// The set of runes of this Character (if any)
		/// </summary>
		public RuneSet Runes
		{
			get { return m_runes; }
		}

		#region Add
		public void AddNew(Spell spell)
		{
			AddSpell(spell, true);
		}

		public override void AddSpell(Spell spell)
		{
			AddSpell(spell, true);
		}

		/// <summary>
		/// Adds the spell without doing any further checks nor adding any spell-related skills or showing animations (after load)
		/// </summary>
		internal void OnlyAdd(SpellRecord record)
		{
			var id = record.SpellId;
			if (!m_byId.ContainsKey(id))
			{
				//DeleteFromDB(id);
				var spell = SpellHandler.Get(id);
				m_byId[id] = spell;
			}
		}


		/// <summary>
		/// Teaches a new spell to the unit. Also sends the spell learning animation, if applicable.
		/// </summary>
		void AddSpell(Spell spell, bool sendPacket)
		{
			// make sure the char knows the skill that this spell belongs to
			if (spell.Ability != null)
			{
				var skill = OwnerChar.Skills[spell.Ability.Skill.Id];
				if (skill == null)
				{
					// learn new skill
					skill = OwnerChar.Skills.Add(spell.Ability.Skill, true);
				}

				if (skill.CurrentTierSpell == null || skill.CurrentTierSpell.SkillTier < spell.SkillTier)
				{
					// upgrade tier
					skill.CurrentTierSpell = spell;
				}
			}

			if (!m_byId.ContainsKey(spell.SpellId))
			{
				var owner = OwnerChar;
				if (m_sendPackets && sendPacket)
				{
					SpellHandler.SendLearnedSpell(owner.Client, spell.Id);
					if (!spell.IsPassive)
					{
						SpellHandler.SendVisual(owner, 362);	// ouchy: Unnamed constants 
					}
				}

				var specIndex = GetSpecIndex(spell);
				var spells = GetSpellList(spell);
				var newRecord = new SpellRecord(spell.SpellId, owner.EntityId.Low, specIndex);
				newRecord.SaveLater();
				spells.Add(newRecord);

				base.AddSpell(spell);
			}
		}
		#endregion

		#region Remove/Replace/Clear
		/// <summary>
		/// Replaces or (if newSpell == null) removes oldSpell.
		/// </summary>
		public override bool Replace(Spell oldSpell, Spell newSpell)
		{
			var hasOldSpell = oldSpell != null && m_byId.Remove(oldSpell.SpellId);
			if (hasOldSpell)
			{
				OnRemove(oldSpell);
				if (newSpell == null)
				{
					if (m_sendPackets)
					{
						SpellHandler.SendSpellRemoved(OwnerChar, oldSpell.Id);
					}
					return true;
				}
			}

			if (newSpell != null)
			{
				if (m_sendPackets && hasOldSpell)
				{
					SpellHandler.SendSpellSuperceded(OwnerChar.Client, oldSpell.Id, newSpell.Id);
				}

				AddSpell(newSpell, !hasOldSpell);
			}
			return hasOldSpell;
		}

		/// <summary>
		/// Enqueues a new task to remove that spell from DB
		/// </summary>
		private void OnRemove(Spell spell)
		{
			var chr = OwnerChar;
			if (spell.RepresentsSkillTier)
			{
				// TODO: Skill might now be represented by a lower tier, and only the MaxValue changes
				chr.Skills.Remove(spell.Ability.Skill.Id);
			}

			// figure out from where to remove and do it
			var spells = GetSpellList(spell);
			for (var i = 0; i < spells.Count; i++)
			{
				var record = spells[i];
				if (record.SpellId == spell.SpellId)
				{
					// delete and remove
					RealmServer.Instance.AddMessage(new Message(record.Delete));
					spells.RemoveAt(i);
					return;
				}
			}
		}

		int GetSpecIndex(Spell spell)
		{
			var chr = OwnerChar;
			return spell.IsTalent ? chr.Talents.CurrentSpecIndex : SpellRecord.NoSpecIndex;
		}

		List<SpellRecord> GetSpellList(Spell spell)
		{
			var chr = OwnerChar;
			if (spell.IsTalent)
			{
				return chr.CurrentSpecProfile.TalentSpells;
			}
			else
			{
				return chr.Record.Spells;
			}
		}

		public override void Clear()
		{
			foreach (var spell in m_byId.Values.ToArray())
			{
				OnRemove(spell);
				if (m_sendPackets)
				{
					SpellHandler.SendSpellRemoved(OwnerChar, spell.Id);
				}
			}

			base.Clear();
		}
		#endregion

		#region Init
		internal void PlayerInitialize()
		{
			// re-apply passive effects
			var chr = OwnerChar;
			foreach (var spell in m_byId.Values)
			{
				if (spell.IsPassive && !spell.HasHarmfulEffects)
				{
					chr.SpellCast.Start(spell, true, Owner);
				}
				if (spell.Talent != null)
				{
					chr.Talents.AddExisting(spell.Talent, spell.Rank);
				}
			}

			m_sendPackets = true;
		}

		public override void AddDefaultSpells()
		{
			// add the default Spells for the race/class
			for (var i = 0; i < OwnerChar.Archetype.Spells.Count; i++)
			{
				var spell = OwnerChar.Archetype.Spells[i];
				AddNew(spell);
			}

			// add all default Spells of all Skills the Char already has
			//foreach (var skill in OwnerChar.Skills)
			//{
			//    for (var i = 0; i < skill.SkillLine.InitialAbilities.Count; i++)
			//    {
			//        var ability = skill.SkillLine.InitialAbilities[i];
			//        AddNew(ability.Spell);
			//    }
			//}
		}
		#endregion

		#region Logout / Relog
		/// <summary>
		/// Called when the player logs out
		/// </summary>
		internal void OnOwnerLoggedOut()
		{
			m_ownerId = Owner.EntityId.Low;
			Owner = null;
			m_sendPackets = false;
			m_lock = new object();
			SpellHandler.PlayerSpellCollections[m_ownerId] = this;

			if (m_runes != null)
			{
				m_runes.OnOwnerLoggedOut();
			}

			m_offlineCooldownTimer = new Timer(FinalizeCooldowns);
			m_offlineCooldownTimer.Change(SpellHandler.DefaultCooldownSaveDelay, TimeSpan.Zero);
		}

		/// <summary>
		/// Called when the player logs back in
		/// </summary>
		internal void OnReconnectOwner(Character owner)
		{
			lock (m_lock)
			{
				if (m_offlineCooldownTimer != null)
				{
					m_offlineCooldownTimer.Change(Timeout.Infinite, Timeout.Infinite);
					m_offlineCooldownTimer = null;
				}
			}
			Owner = owner;
		}
		#endregion

		#region Spell Constraints
		/// <summary>
		/// Add everything to the caster that this spell requires
		/// </summary>
		public void SatisfyConstraintsFor(Spell spell)
		{
			var chr = OwnerChar;
			// add reagents
			foreach (var reagent in spell.Reagents)
			{
				var templ = reagent.Template;
				if (templ != null)
				{
					var amt = reagent.Amount * 10;
					chr.Inventory.Ensure(templ, amt);
				}
			}

			// add tools
			if (spell.RequiredTools != null)
			{
				foreach (var tool in spell.RequiredTools)
				{
					chr.Inventory.Ensure(tool.Template, 1);
				}
			}
			if (spell.RequiredTotemCategories != null)
			{
				foreach (var cat in spell.RequiredTotemCategories)
				{
					var tool = ItemMgr.GetFirstTotemCat(cat);
					if (tool != null)
					{
						chr.Inventory.Ensure(tool, 1);
					}
				}
			}

			// Profession
			if (spell.Ability.Skill != null)
			{
				chr.Skills.TryLearn(spell.Ability.Skill.Id);
			}


			// add spellfocus object (if not present)
			if (spell.RequiredSpellFocus != 0)
			{
				var range = Owner.GetSpellMaxRange(spell);
				var go = chr.Region.GetGOWithSpellFocus(chr.Position, spell.RequiredSpellFocus,
					range > 0 ? (range) : 5f, chr.Phase);

				if (go == null)
				{
					foreach (var entry in GOMgr.Entries.Values)
					{
						if (entry is GOSpellFocusEntry &&
							((GOSpellFocusEntry)entry).SpellFocus == spell.RequiredSpellFocus)
						{
							entry.Spawn(chr, chr);
							break;
						}
					}
				}
			}
		}
		#endregion

		#region Cooldowns
		/// <summary>
		/// Tries to add the given Spell to the cooldown List.
		/// Returns false if Spell is still cooling down.
		/// </summary>
		public bool CheckCooldown(Spell spell)
		{
			// check for individual cooldown
			ISpellIdCooldown idCooldown = null;
			if (m_idCooldowns != null)
			{
				if (m_idCooldowns.TryGetValue(spell.Id, out idCooldown))
				{
					if (idCooldown.Until > DateTime.Now)
					{
						return false;
					}
					m_idCooldowns.Remove(spell.Id);
				}
			}

			// check for category cooldown
			ISpellCategoryCooldown catCooldown = null;
			if (spell.CategoryCooldownTime > 0)
			{
				if (m_categoryCooldowns != null)
				{
					if (m_categoryCooldowns.TryGetValue(spell.Category, out catCooldown))
					{
						if (catCooldown.Until > DateTime.Now)
						{
							return false;
						}
						m_categoryCooldowns.Remove(spell.Category);
					}
				}
			}

			// enqueue delete task for consistent cooldowns
			if (idCooldown is ConsistentSpellIdCooldown || catCooldown is ConsistentSpellCategoryCooldown)
			{
				var removedId = idCooldown as ConsistentSpellIdCooldown;
				var removedCat = catCooldown as ConsistentSpellCategoryCooldown;
				RealmServer.Instance.AddMessage(new Message(() =>
				{
					if (removedId != null)
					{
						removedId.Delete();
					}
					if (removedCat != null)
					{
						removedCat.Delete();
					}
				}));
			}
			return true;
		}

		public override void AddCooldown(Spell spell, Item casterItem)
		{
			// TODO: Add cooldown mods
			var itemSpell = casterItem != null && casterItem.Template.UseSpell != null;

			var cd = 0;
			if (itemSpell)
			{
				cd = casterItem.Template.UseSpell.Cooldown;
			}
			if (cd == 0)
			{
				cd = spell.GetCooldown(Owner);
			}

			var catCd = 0;
			if (itemSpell)
			{
				catCd = casterItem.Template.UseSpell.CategoryCooldown;
			}
			if (catCd == 0)
			{
				catCd = spell.CategoryCooldownTime;
			}

			if (cd > 0)
			{
				if (m_idCooldowns == null)
				{
					m_idCooldowns = new Dictionary<uint, ISpellIdCooldown>();
				}
				var idCooldown = new SpellIdCooldown
				{
					SpellId = spell.Id,
					Until = (DateTime.Now + TimeSpan.FromMilliseconds(cd))
				};

				if (itemSpell)
				{
					idCooldown.ItemId = casterItem.Template.Id;
				}
				m_idCooldowns[spell.Id] = idCooldown;
			}

			if (spell.CategoryCooldownTime > 0)
			{
				if (m_categoryCooldowns == null)
				{
					m_categoryCooldowns = new Dictionary<uint, ISpellCategoryCooldown>();
				}
				var catCooldown = new SpellCategoryCooldown
				{
					SpellId = spell.Id,
					Until = DateTime.Now.AddMilliseconds(catCd)
				};

				if (itemSpell)
				{
					catCooldown.CategoryId = casterItem.Template.UseSpell.CategoryId;
					catCooldown.ItemId = casterItem.Template.Id;
				}
				else
				{
					catCooldown.CategoryId = spell.Category;
				}
				m_categoryCooldowns[spell.Category] = catCooldown;
			}

		}

		/// <summary>
		/// Returns whether the given spell is still cooling down
		/// </summary>
		public override bool IsReady(Spell spell)
		{
			ISpellCategoryCooldown catCooldown;
			if (m_categoryCooldowns != null)
			{
				if (m_categoryCooldowns.TryGetValue(spell.Category, out catCooldown))
				{
					if (catCooldown.Until > DateTime.Now)
					{
						return true;
					}

					m_categoryCooldowns.Remove(spell.Category);
					if (catCooldown is ActiveRecordBase)
					{
						RealmServer.Instance.AddMessage(new Message(() => ((ActiveRecordBase)catCooldown).Delete()));
					}
				}
			}

			ISpellIdCooldown idCooldown;
			if (m_idCooldowns != null)
			{
				if (m_idCooldowns.TryGetValue(spell.Id, out idCooldown))
				{
					if (idCooldown.Until > DateTime.Now)
					{
						return true;
					}

					m_idCooldowns.Remove(spell.Id);
					if (idCooldown is ActiveRecordBase)
					{
						RealmServer.Instance.AddMessage(() => ((ActiveRecordBase)idCooldown).Delete());
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Clears all pending spell cooldowns.
		/// </summary>
		/// <remarks>Requires IO-Context.</remarks>
		public override void ClearCooldowns()
		{
			// send cooldown updates to client
			if (m_idCooldowns != null)
			{
				foreach (var pair in m_idCooldowns)
				{
					SpellHandler.SendClearCoolDown(OwnerChar, (SpellId)pair.Key);
				}
				m_idCooldowns.Clear();
			}
			if (m_categoryCooldowns != null)
			{
				foreach (var spell in m_byId.Values)
				{
					if (m_categoryCooldowns.ContainsKey(spell.Category))
					{
						SpellHandler.SendClearCoolDown(OwnerChar, spell.SpellId);
					}
				}
			}

			// remove and delete all cooldowns
			var cds = m_idCooldowns;
			var catCds = m_categoryCooldowns;
			m_idCooldowns = null;
			m_categoryCooldowns = null;
			RealmServer.Instance.AddMessage(new Message(() =>
			{
				if (cds != null)
				{
					foreach (var cooldown in cds.Values)
					{
						if (cooldown is ActiveRecordBase)
						{
							((ActiveRecordBase)cooldown).Delete();
						}
					}
					cds.Clear();
				}
				if (catCds != null)
				{
					foreach (var cooldown in catCds.Values)
					{
						if (cooldown is ActiveRecordBase)
						{
							((ActiveRecordBase)cooldown).Delete();
						}
					}
					catCds.Clear();
				}
			}));

			// clear rune cooldowns
			if (m_runes != null)
			{
				// TODO: Clear rune cooldown
			}
		}

		/// <summary>
		/// Clears the cooldown for this spell
		/// </summary>
		public override void ClearCooldown(Spell cooldownSpell, bool alsoCategory = true)
		{
			var ownerChar = OwnerChar;
			if (ownerChar != null)
			{
				// send cooldown update to client
				SpellHandler.SendClearCoolDown(ownerChar, cooldownSpell.SpellId);
				if (alsoCategory && cooldownSpell.Category != 0)
				{
					foreach (var spell in m_byId.Values)
					{
						if (spell.Category == cooldownSpell.Category)
						{
							SpellHandler.SendClearCoolDown(ownerChar, spell.SpellId);
						}
					}
				}
			}

			// remove and delete
			ISpellIdCooldown idCooldown;
			ISpellCategoryCooldown catCooldown;
			if (m_idCooldowns != null)
			{
				if (m_idCooldowns.TryGetValue(cooldownSpell.Id, out idCooldown))
				{
					m_idCooldowns.Remove(cooldownSpell.Id);
				}
			}
			else
			{
				idCooldown = null;
			}

			if (alsoCategory && m_categoryCooldowns != null)
			{
				if (m_categoryCooldowns.TryGetValue(cooldownSpell.Category, out catCooldown))
				{
					m_categoryCooldowns.Remove(cooldownSpell.Id);
				}
			}
			else
			{
				catCooldown = null;
			}

			if (idCooldown is ActiveRecordBase || catCooldown is ActiveRecordBase)
			{
				RealmServer.Instance.AddMessage(new Message(() =>
				{
					if (idCooldown is ActiveRecordBase)
					{
						((ActiveRecordBase)idCooldown).Delete();
					}
					if (catCooldown is ActiveRecordBase)
					{
						((ActiveRecordBase)catCooldown).Delete();
					}
				}
				));
			}
		}

		private void FinalizeCooldowns(object sender)
		{
			lock (m_lock)
			{
				if (m_offlineCooldownTimer != null)
				{
					m_offlineCooldownTimer = null;
					FinalizeCooldowns(ref m_idCooldowns);
					FinalizeCooldowns(ref m_categoryCooldowns);
				}
			}
		}

		private void FinalizeCooldowns<T>(ref Dictionary<uint, T> cooldowns) where T : ICooldown
		{
			if (cooldowns == null)
				return;

			Dictionary<uint, T> newCooldowns = null;
			foreach (ICooldown cooldown in cooldowns.Values)
			{
				if (cooldown.Until < DateTime.Now + TimeSpan.FromMinutes(1))
				{
					// already expired or will expire very soon
					if (cooldown is ActiveRecordBase)
					{
						// delete
						((ActiveRecordBase)cooldown).Delete();
					}
				}
				else
				{
					if (newCooldowns == null)
					{
						newCooldowns = new Dictionary<uint, T>();
					}

					var cd = cooldown.AsConsistent();
					//if (cd.CharId != m_ownerId)
					cd.CharId = m_ownerId;
					cd.SaveAndFlush(); // update or create
					newCooldowns.Add(cd.Identifier, (T)cd);
				}
			}
			cooldowns = newCooldowns;
		}
		#endregion

		/// <summary>
		/// Called to save runes (cds & spells are saved in another way)
		/// </summary>
		internal void OnSave()
		{
			if (m_runes != null)
			{
				var record = OwnerChar.Record;
				record.RuneSetMask = m_runes.PackRuneSetMask();
				record.RuneCooldowns = m_runes.Cooldowns;
			}
		}
	}
}