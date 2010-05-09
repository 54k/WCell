using WCell.Constants.Misc;
using WCell.Constants.NPCs;
using WCell.Core.Initialization;
using WCell.RealmServer.AI.Brains;
using WCell.RealmServer.GameObjects;
using WCell.RealmServer.Instances;
using WCell.RealmServer.NPCs;
using WCell.RealmServer.Spells;
using WCell.RealmServer.Entities;
using WCell.Constants.Spells;
using WCell.Constants;
using WCell.Constants.GameObjects;
using WCell.RealmServer.AI.Actions.Combat;
using System;
using WCell.Util.Graphics;

namespace WCell.Addons.Default.Instances
{
	public class Deadmines : RaidInstance
	{
		#region Setup Content
		private static NPCEntry rhahkzorEntry;
		private static NPCEntry sneedEntry;
		private static NPCEntry sneedShredderEntry;
		private static NPCEntry gilnidEntry;
		private static NPCEntry smiteEntry;
		private static GOEntry defiasCannonEntry;

		[Initialization]
		[DependentInitialization(typeof(NPCMgr))]
		public static void InitNPCs()
		{
			// Rahkzor
			rhahkzorEntry = NPCMgr.GetEntry(NPCId.RhahkZor);
			rhahkzorEntry.BrainCreator = rhahkzor => new RhahkzorBrain(rhahkzor);

			rhahkzorEntry.AddSpell(SpellId.RhahkZorSlam);		// add Rhakzor's slam

			// Rhakzor's slam has a cooldown of about 12s
			SpellHandler.Apply(spell => { spell.CooldownTime = 12000; },
				SpellId.RhahkZorSlam);


			// Sneed
			sneedShredderEntry = NPCMgr.GetEntry(NPCId.SneedsShredder);
			sneedShredderEntry.Activated += sneedShredder =>
			{
				((BaseBrain)sneedShredder.Brain).DefaultCombatAction.Strategy = new SneedShredderAttackAction(sneedShredder);
			};
			sneedShredderEntry.Died += sneedShredder =>
			{
				// Cast the sneed ejection spell on the corpse after a short delay
				sneedShredder.CallDelayed(2500, (delayed) => { sneedShredder.SpellCast.TriggerSelf(SneedShredderAttackAction.ejectSneed); });
			};

			sneedEntry = NPCMgr.GetEntry(NPCId.Sneed);
			sneedEntry.Activated += sneed =>
			{
				((BaseBrain)sneed.Brain).DefaultCombatAction.Strategy = new SneedAttackAction(sneed);
			};
			sneedEntry.Died += sneed =>
			{
				var instance = sneed.Region as Deadmines;
				if (instance != null)
				{
					var door = instance.sneedDoor;
					if (door != null && door.IsInWorld)
					{
						// Open the door
						door.State = GameObjectState.Disabled;
					}
				}
			};


			// Gilnid
			gilnidEntry = NPCMgr.GetEntry(NPCId.Gilnid);

			gilnidEntry.AddSpell(SpellId.MoltenMetal);
			gilnidEntry.AddSpell(SpellId.MeltOre);

			SpellHandler.Apply(spell => spell.CooldownTime = 20000, SpellId.MoltenMetal);
			SpellHandler.Apply(spell => spell.CooldownTime = 25000, SpellId.MeltOre);

			gilnidEntry.Died += gilnid =>
			{
				var instance = gilnid.Region as Deadmines;
				if (instance != null)
				{
					var door = instance.gilnidDoor;

					if (door != null && door.IsInWorld)
					{
						// Open the door
						door.State = GameObjectState.Disabled;
					}
				}
			};

			gilnidEntry.Activated += gilnid =>
			{
				((BaseBrain)gilnid.Brain).DefaultCombatAction.Strategy = new GilnidAttackAction(gilnid);
			};


			// Mr Smite
			smiteEntry = NPCMgr.GetEntry(NPCId.MrSmite);
			smiteEntry.BrainCreator = smite => new SmiteBrain(smite);
			smiteEntry.Activated += smite =>
			{
				((BaseBrain)smite.Brain).DefaultCombatAction.Strategy = new SmiteAttackAction(smite);
			};
		}

		[Initialization]
		[DependentInitialization(typeof(GOMgr))]
		public static void InitGOs()
		{
			var rhahkzorDoorEntry = GOMgr.GetEntry(GOEntryId.FactoryDoor);		// 13965

			if (rhahkzorDoorEntry != null)
			{
				rhahkzorDoorEntry.Activated += go =>
				{
					var instance = go.Region as Deadmines;
					if (instance != null && instance.rhahkzorDoor == null)
					{
						// set the instance's Door object after the Door spawned
						instance.rhahkzorDoor = go;
					}
				};
			}

			var sneedDoorEntry = GOMgr.GetEntry(17153u);
			var sneedDoorCord = new Vector3(-290.294f, -536.96f, 49.4353f);
			if (sneedDoorEntry != null)
			{
				sneedDoorEntry.Activated += go =>
				{
					var instance = go.Region as Deadmines;
					if (instance != null)
					{
						// set the instance's Door object after the Door spawned
						instance.sneedDoor = go.Region.GetNearestGameObject(ref sneedDoorCord, (GOEntryId)sneedDoorEntry.Id);
					}
				};
			}

			var gilnidDoorEntry = GOMgr.GetEntry(17153u);
			var gilnidDoorCord = new Vector3(-168.514f, -579.861f, 19.3159f);
			if (gilnidDoorEntry != null)
			{
				gilnidDoorEntry.Activated += go =>
				{
					var instance = go.Region as Deadmines;
					if (instance != null)
					{
						// set the instance's Door object after the Door spawned
						instance.gilnidDoor = go.Region.GetNearestGameObject(ref gilnidDoorCord, (GOEntryId)gilnidEntry.Id);
					}
				};
			}

			// Cannon
			defiasCannonEntry = GOMgr.GetEntry(GOEntryId.DefiasCannon);

			defiasCannonEntry.Used += (cannon, user) =>
			{
				var door = cannon.GetNearbyGO(GOEntryId.IronCladDoor);
				if (door != null && door.IsInWorld)
				{
					// the cannon destroys the door
					door.State = GameObjectState.Destroyed;
					door.CallDelayed(5000, obj => obj.Delete());	// delete the actual object a little later
					return true;
				}
				return false;
			};
		}
		#endregion

		#region Fields
		public GameObject rhahkzorDoor;
		public GameObject sneedDoor; //16400
		public GameObject gilnidDoor; //16399
		#endregion
	}

	#region Rahkzor

	public class RhahkzorBrain : MobBrain
	{
		private GameObject m_Door;

		public RhahkzorBrain(NPC rhahkzor)
			: base(rhahkzor)
		{
		}

		public override void OnEnterCombat()
		{
			m_owner.PlaySound(5774);
			NPC.Yell("Van Cleef pay big for your heads!");
			base.OnEnterCombat();
		}

		public override void OnDeath()
		{
			var instance = m_owner.Region as Deadmines;

			if (instance != null)
			{
				m_Door = instance.rhahkzorDoor;

				if (m_Door != null)
				{
					// Open the door
					m_Door.State = GameObjectState.Disabled;
				}
			}
			base.OnDeath();
		}
	}
	#endregion

	#region Sneed
	public class SneedShredderAttackAction : AIAttackAction
	{
		internal static Spell terrify, distractingPain, ejectSneed;
		private DateTime timeSinceLastInterval;
		private const int interVal = 1;
		private int distractinPainTick;
		private int terrifyTick;

		[Initialization(InitializationPass.Second)]
		static void InitSneed()
		{
			// remember the Spells for later use
			terrify = SpellHandler.Get(SpellId.Terrify);
			distractingPain = SpellHandler.Get(SpellId.DistractingPain);
			ejectSneed = SpellHandler.Get(SpellId.EjectSneed);
		}

		public SneedShredderAttackAction(NPC sneed)
			: base(sneed)
		{
		}

		public override void Start()
		{
			terrifyTick = 0;
			distractinPainTick = 0;
			timeSinceLastInterval = DateTime.Now;
			base.Start();
		}

		public override void Update()
		{
			var timeNow = DateTime.Now;
			var timeBetween = timeNow - timeSinceLastInterval;

			if (timeBetween.TotalSeconds >= interVal)
			{
				timeSinceLastInterval = timeNow;
				if (CheckSpellCast())	// cast one of Sneed's special Spells
				{
					// idle a little after casting a spell
					m_owner.Idle(1000);
					return;
				}
			}

			base.Update();
		}

		private bool CheckSpellCast()
		{
			terrifyTick++;
			distractinPainTick++;

			if (distractinPainTick >= 12)
			{
				var chr = m_owner.GetNearbyRandomCharacter();
				if (chr != null)
				{
					distractinPainTick = 0;
					m_owner.SpellCast.Start(distractingPain, false, chr);
				}
				return true;
			}

			if (terrifyTick >= 21)
			{
				var chr = m_owner.GetNearbyRandomCharacter();
				if (chr != null)
				{
					terrifyTick = 0;
					m_owner.SpellCast.Start(terrify, false, chr);
				}
				return true;
			}
			return false;
		}
	}

	public class SneedAttackAction : AIAttackAction
	{
		private DateTime timeNow;
		private DateTime timeSinceLastInterval;
		private TimeSpan timeBetween;
		private const int interVal = 1;
		private int disarmTick;
		private static Spell disarm;

		[Initialization(InitializationPass.Second)]
		static void InitSneed()
		{
			disarm = SpellHandler.Get(6713);  //disarm
		}

		public SneedAttackAction(NPC sneed)
			: base(sneed)
		{
		}

		public override void Start()
		{
			disarmTick = 0;
			timeSinceLastInterval = DateTime.Now;
			base.Start();
		}

		public override void Update()
		{
			timeNow = DateTime.Now;
			timeBetween = timeNow - timeSinceLastInterval;

			if (timeBetween.TotalSeconds >= interVal)
			{
				timeSinceLastInterval = DateTime.Now;
				if (SpellCast())
				{
					return;
				}
			}

			base.Update();
		}

		private bool SpellCast()
		{
			disarmTick++;

			if (disarmTick >= 10)
			{
				disarmTick = 0;
				var chr = m_owner.GetNearbyRandomCharacter();
				if (chr != null)
				{
					m_owner.SpellCast.Start(disarm, false, chr);
					return true;
				}
			}
			return false;
		}

	}
	#endregion

	#region Gilnid
	public class GilnidAttackAction : AIAttackAction
	{
		public GilnidAttackAction(NPC gilnid)
			: base(gilnid)
		{
		}

		/*add his quotes somewhere dont know right now when he says it
		 * "Anyone want to take a break? Well too bad! Get to work you oafs!" 
		 * "Get those parts moving down to the ship!" 
		 * "The cannons must be finished soon." 
		 */
	}
	#endregion

	#region Mr Smite
	public class SmiteAttackAction : AIAttackAction
	{
		public static Vector3 ChestLocation = new Vector3(1.100060f, -780.026367f, 9.811194f);
		private static Spell smiteStomp;
		private static Spell smiteBuff;

		[Initialization(InitializationPass.Second)]
		static void InitSmite()
		{
			smiteStomp = SpellHandler.Get(SpellId.SmiteStomp);
			smiteBuff = SpellHandler.Get(SpellId.SmitesHammer);
		}

		private int phase;

		public SmiteAttackAction(NPC smite)
			: base(smite)
		{
		}

		public override void Update()
		{
			if (phase < 2)
			{
				var hpPct = m_owner.HealthPct;
				if (hpPct <= 33 && phase == 1)
				{
					// when below 33% health, do second special action
					m_owner.PlaySound(5779);
					m_owner.Yell("D'ah! Now you're making me angry!");
					m_owner.SpellCast.Trigger(smiteStomp);		// aoe spell finds targets automatically
					m_owner.Auras.AddSelf(smiteBuff, true);		// apply buff to self
					MoveToChest();
					phase = 2;
					return;
				}
				else if (hpPct <= 66 && phase == 0)
				{
					// when below 66% health, do first special action
					m_owner.PlaySound(5782);
					m_owner.Yell("You land lubbers are tougher than I thought! I'll have to improvise!");
					m_owner.SpellCast.Trigger(smiteStomp);
					MoveToChest();
					phase = 1;
					return;
				}
			}

			base.Update();
		}

		/// <summary>
		/// Approach the Chest and start using it
		/// </summary>
		public void MoveToChest()
		{
			m_owner.Target = null;				// deselect target so client wont display npc facing the last guy
			m_owner.MoveToThenExecute(ChestLocation, unit => StartUsingChest());
		}

		/// <summary>
		/// Use Chest and act busy
		/// </summary>
		private void StartUsingChest()
		{
			m_owner.Brain.StopCurrentAction();

			// look at chest and kneel in front of it
			m_owner.Face(5.47f);
			m_owner.StandState = StandState.Kneeling;
			m_owner.Emote(EmoteType.SimpleApplaud);

			m_owner.IdleThenExecute(2000, LeaveChest);	// wait 2 seconds
		}

		/// <summary>
		/// Go back to fighting
		/// </summary>
		private void LeaveChest()
		{
			m_owner.StandState = StandState.Stand;
			// from here on, Mr Smite will simply go back to doing what he did before (fighting)
		}
	}

	public class SmiteBrain : MobBrain
	{
		public SmiteBrain(NPC smite)
			: base(smite)
		{
		}
	}
	#endregion
}