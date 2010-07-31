﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Castle.ActiveRecord;
using NHibernate.Criterion;
using NLog;
using WCell.Constants.Achievements;
using WCell.Core.Database;
using WCell.RealmServer.Database;
using WCell.RealmServer.Entities;

namespace WCell.RealmServer.Achievement
{

	[ActiveRecord(Access = PropertyAccess.Property)]
	public class AchievementRecord : WCellRecord<AchievementRecord>
	{
		#region Static
		private static readonly Logger s_log = LogManager.GetCurrentClassLogger();
		private static readonly Order CreatedOrder = new Order("Created", true);

		/// <summary>
		/// Character will not have Ids below this threshold. 
		/// You can use those unused ids for self-implemented mechanisms, eg to fake participants in chat-channels etc.
		/// </summary>
		/// <remarks>
		/// Do not change this value once the first Character exists.
		/// If you want to change this value to reserve more (or less) ids for other use, make sure
		/// that none of the ids below this threshold are in the DB.
		/// </remarks>

		protected static readonly NHIdGenerator _idGenerator = new NHIdGenerator(typeof(AchievementRecord), "RecordId");

		/// <summary>
		/// Creates a new AchievementRecord row in the database with the given information.
		/// </summary>
		/// <param name="account">the account this character is on</param>
		/// <param name="name">the name of the new character</param>
		/// <returns>the <seealso cref="AchievementRecord"/> object</returns>
		public static AchievementRecord CreateNewAchievementRecord(Character chr, AchievementEntryId achievementEntryId)
		{
			AchievementRecord record;

			try
			{
				record = new AchievementRecord
				{
					RecordId = _idGenerator.Next(),
					_achievementEntryId = (int)achievementEntryId,
					_characterGuid = (int)chr.EntityId.Low,
					CompleteDate = DateTime.Now,
					New = true
				};
			}
			catch (Exception ex)
			{
				s_log.Error("AchievementRecord creation error (DBS: " + RealmServerConfiguration.DBType + "): ", ex);
				record = null;
			}


			return record;
		}

		#endregion

		[Field("CharacterId", NotNull = true, Access = PropertyAccess.FieldCamelcase)]
		private int _characterGuid;

		[Field("Achievement", NotNull = true, Access = PropertyAccess.FieldCamelcase)]
		private int _achievementEntryId;

		[Property]
		public DateTime CompleteDate { get; set; }

		/// <summary>
		/// Encode char id and achievement id into RecordId
		/// </summary>
		[PrimaryKey(PrimaryKeyType.Assigned)]
		public long RecordId
		{
			get
			{
				return _characterGuid | (_achievementEntryId << 32);
			}
			set
			{
				_characterGuid = (int)value;
				_achievementEntryId = (int)(value >> 32);
			}
		}

		public uint CharacterGuid
		{
			get { return (uint)_characterGuid; }
			set { _characterGuid = (int)value; }
		}

		public AchievementEntryId AchievementEntryId
		{
			get { return (AchievementEntryId)_achievementEntryId; }
			set { _achievementEntryId = (int)value; }
		}

		public static AchievementRecord[] Load(int chrId)
		{
			return FindAll(Restrictions.Eq("_characterGuid", chrId));
		}
	}
}